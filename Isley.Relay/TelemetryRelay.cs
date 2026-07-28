using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Isley.Telemetry;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Isley.Relay;

internal sealed class TelemetryFrameStore(IOptions<RelayOptions> options)
{
    private readonly ConcurrentDictionary<string, StoredFrame> _frames =
        new(StringComparer.Ordinal);
    private readonly RelayOptions _options = options.Value;

    internal bool TryAccept(TelemetryFrame frame, out string state)
    {
        while (true)
        {
            if (!_frames.TryGetValue(frame.ServerId, out var current))
            {
                if (_frames.TryAdd(
                        frame.ServerId,
                        new StoredFrame(frame, DateTimeOffset.UtcNow, null)))
                {
                    state = "accepted";
                    return true;
                }
                continue;
            }

            var sameSession = string.Equals(
                current.Frame.BridgeSessionId,
                frame.BridgeSessionId,
                StringComparison.Ordinal);
            if (sameSession && frame.Sequence <= current.Frame.Sequence)
            {
                state = "sequence_not_newer";
                return false;
            }
            if (!sameSession
                && frame.SampledAt < current.Frame.SampledAt.Subtract(TimeSpan.FromSeconds(2)))
            {
                state = "older_bridge_session";
                return false;
            }

            var updateRate = CalculateUpdateRate(current, frame);
            if (_frames.TryUpdate(
                    frame.ServerId,
                    new StoredFrame(frame, DateTimeOffset.UtcNow, updateRate),
                    current))
            {
                state = "accepted";
                return true;
            }
        }
    }

    internal bool TryGetFresh(string serverId, out StoredFrame stored)
    {
        if (_frames.TryGetValue(serverId, out stored!)
            && DateTimeOffset.UtcNow - stored.ReceivedAt
            <= TimeSpan.FromSeconds(_options.FrameFreshnessSeconds))
        {
            return true;
        }
        stored = null!;
        return false;
    }

    internal int CountFresh()
    {
        var now = DateTimeOffset.UtcNow;
        var freshness = TimeSpan.FromSeconds(_options.FrameFreshnessSeconds);
        var count = 0;
        foreach (var stored in _frames.Values)
        {
            if (now - stored.ReceivedAt <= freshness)
            {
                count++;
            }
        }
        return count;
    }

    internal IReadOnlyList<object> ListForViewer(string steamId)
    {
        var now = DateTimeOffset.UtcNow;
        return _frames.Values
            .Where(stored => stored.Frame.Entities.Any(entity =>
                string.Equals(entity.SteamId, steamId, StringComparison.Ordinal)))
            .Select(stored => (object)new
            {
                stored.Frame.ServerId,
                stored.Frame.ServerName,
                state = now - stored.ReceivedAt
                        <= TimeSpan.FromSeconds(_options.FrameFreshnessSeconds)
                    ? "live"
                    : "stale",
                sampledAt = stored.Frame.SampledAt,
                receivedAt = stored.ReceivedAt,
                updateRateHz = stored.UpdateRateHz,
                source = stored.Frame.Source,
                capabilities = stored.Frame.Capabilities
            })
            .OrderBy(value => JsonSerializer.Serialize(value))
            .ToArray();
    }

    private static double? CalculateUpdateRate(StoredFrame current, TelemetryFrame next)
    {
        var interval = (next.SampledAt - current.Frame.SampledAt).TotalMilliseconds;
        if (interval is <= 0 or > 60_000)
        {
            return current.UpdateRateHz;
        }

        var observed = Math.Clamp(1000d / interval, 0.01, 100);
        return current.UpdateRateHz is double previous
            ? previous * 0.7 + observed * 0.3
            : observed;
    }
}

internal sealed record StoredFrame(
    TelemetryFrame Frame,
    DateTimeOffset ReceivedAt,
    double? UpdateRateHz);

internal sealed partial class TelemetryBroker(
    TelemetryFrameStore frames,
    IFriendResolver friends,
    RelayMetrics metrics,
    IOptions<RelayOptions> options,
    ILogger<TelemetryBroker> logger)
{
    private readonly ConcurrentDictionary<Guid, ViewerConnection> _connections = new();
    private readonly RelayOptions _options = options.Value;

    internal int ActiveViewerCount => _connections.Count;

    internal async Task RunViewerAsync(
        string serverId,
        string steamId,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        if (!ServerIdRegex().IsMatch(serverId))
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation,
                "Invalid server",
                CancellationToken.None);
            return;
        }

        var connection = new ViewerConnection(Guid.NewGuid(), serverId, steamId, socket);
        _connections[connection.Id] = connection;
        metrics.ViewerConnected();
        var sendTask = connection.RunSendLoopAsync(
            (frame, token) => SendSnapshotAsync(connection, frame, token),
            cancellationToken);
        try
        {
            if (frames.TryGetFresh(serverId, out var initial))
            {
                connection.TryPublish(initial.Frame);
            }
            else
            {
                await connection.SendAsync(new
                {
                    type = "status",
                    state = "waiting",
                    serverId,
                    detail = "Waiting for a fresh authorized server frame."
                }, cancellationToken);
            }

            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                if (!result.EndOfMessage || result.Count > buffer.Length)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        "Client messages are limited to small control frames.",
                        CancellationToken.None);
                    return;
                }
                if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                {
                    await TryHandleHelloAsync(
                        connection,
                        buffer.AsMemory(0, result.Count),
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "An Isley live viewer disconnected.");
        }
        finally
        {
            _connections.TryRemove(connection.Id, out _);
            connection.Complete();
            try
            {
                await sendTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (WebSocketException ex)
            {
                logger.LogDebug(ex, "An Isley live viewer send loop ended.");
            }
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Isley live stream closed",
                    CancellationToken.None);
            }
        }
    }

    internal Task PublishAsync(
        TelemetryFrame frame,
        CancellationToken cancellationToken)
    {
        metrics.FrameRelayed();
        foreach (var connection in _connections.Values.Where(connection => string.Equals(
                connection.ServerId,
                frame.ServerId,
                StringComparison.Ordinal)))
        {
            connection.TryPublish(frame);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Optional stream-version negotiation. A viewer may send
    /// {"type":"hello","maxStreamVersion":2} on a small text frame; the relay
    /// answers with the negotiated version and starts delta delivery. Malformed
    /// control frames are ignored — they never tear down a viewer stream.
    /// </summary>
    private async Task TryHandleHelloAsync(
        ViewerConnection connection,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        int requested;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.GetString() is not "hello")
            {
                return;
            }
            if (!root.TryGetProperty("maxStreamVersion", out var versionElement)
                || !versionElement.TryGetInt32(out requested))
            {
                requested = 1;
            }
        }
        catch (JsonException)
        {
            return;
        }

        connection.NegotiateStreamVersion(requested, TelemetryProtocol.ViewerStreamVersion);
        await connection.SendAsync(new
        {
            type = "hello",
            streamVersion = connection.MaxStreamVersion,
            keyframeIntervalFrames = _options.ViewerKeyframeIntervalFrames,
            deltaEncoding = connection.MaxStreamVersion >= 2
                            && _options.ViewerDeltaEncodingEnabled
        }, cancellationToken);
    }

    private async Task SendSnapshotAsync(
        ViewerConnection connection,
        TelemetryFrame frame,
        CancellationToken cancellationToken)
    {
        if (connection.Socket.State != WebSocketState.Open)
        {
            return;
        }
        var snapshot = await CreateViewerSnapshotAsync(
            frame,
            connection.SteamId,
            _connections.Values.Count(value => string.Equals(
                value.ServerId,
                frame.ServerId,
                StringComparison.Ordinal)),
            cancellationToken);

        // Stream version 1 (default): full snapshots in the original shape.
        // Stream version 2 (negotiated): deltas against the last sent snapshot
        // with periodic keyframe snapshots as the resync anchor.
        if (connection.MaxStreamVersion >= 2)
        {
            var deltasEnabled = _options.ViewerDeltaEncodingEnabled;
            if (deltasEnabled
                && connection.LastSent is { } previous
                && connection.FramesSinceKeyframe < _options.ViewerKeyframeIntervalFrames
                && ViewerTelemetryDeltaBuilder.TryCreate(previous, snapshot, out var delta))
            {
                await connection.SendAsync(new
                {
                    type = "delta",
                    streamVersion = TelemetryProtocol.ViewerStreamVersion,
                    delta
                }, cancellationToken);
                connection.RecordSent(snapshot, keyframe: false);
                return;
            }

            await connection.SendAsync(new
            {
                type = "snapshot",
                streamVersion = TelemetryProtocol.ViewerStreamVersion,
                keyframe = true,
                snapshot
            }, cancellationToken);
            connection.RecordSent(snapshot, keyframe: true);
            return;
        }

        await connection.SendAsync(new { type = "snapshot", snapshot }, cancellationToken);
        connection.RecordSent(snapshot, keyframe: true);
    }

    private async Task<ViewerTelemetrySnapshot> CreateViewerSnapshotAsync(
        TelemetryFrame frame,
        string viewerSteamId,
        int connectedPlayerNodes,
        CancellationToken cancellationToken)
    {
        ViewerTelemetryEntity? self = null;
        var visible = new List<ViewerTelemetryEntity>();
        foreach (var entity in frame.Entities)
        {
            var isSelf = string.Equals(entity.SteamId, viewerSteamId, StringComparison.Ordinal);
            FriendDecision decision;
            if (isSelf)
            {
                decision = new FriendDecision(true, false, "self");
            }
            else if (entity.Kind == TelemetryEntityKind.AiAnimal)
            {
                decision = new FriendDecision(
                    entity.ShareScope == TelemetryShareScope.Server,
                    false,
                    "server-ai");
            }
            else if (entity.SteamId is not null)
            {
                decision = await friends.EvaluateAsync(
                    entity.SteamId,
                    viewerSteamId,
                    entity.ShareScope,
                    entity.AllowedViewerSteamIds,
                    cancellationToken);
            }
            else
            {
                decision = new FriendDecision(
                    entity.ShareScope == TelemetryShareScope.Server,
                    false,
                    "anonymous-server-entity");
            }
            if (!decision.Visible)
            {
                continue;
            }

            var viewerEntity = new ViewerTelemetryEntity
            {
                Id = isSelf
                    ? "self"
                    : Pseudonym(frame.ServerId, viewerSteamId, entity.EntityId),
                Label = isSelf
                    ? "You"
                    : decision.Friend
                        ? TelemetryValidation.CleanLabel(entity.DisplayName, "Friend", 32)
                        : "Animal",
                Self = isSelf,
                Friend = decision.Friend,
                Kind = entity.Kind,
                SpeciesId = isSelf ? entity.SpeciesId : null,
                X = entity.X,
                Y = entity.Y,
                Z = entity.Z,
                Yaw = entity.Yaw,
                DirectionQuality = entity.DirectionQuality,
                HealthPercent = isSelf ? entity.HealthPercent : null,
                GrowthPercent = isSelf ? entity.GrowthPercent : null,
                StaminaPercent = isSelf ? entity.StaminaPercent : null,
                FoodPercent = isSelf ? entity.FoodPercent : null,
                WaterPercent = isSelf ? entity.WaterPercent : null,
                Conditions = isSelf ? entity.Conditions : []
            };
            if (isSelf)
            {
                self = viewerEntity;
            }
            else
            {
                visible.Add(viewerEntity);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var updateRate = frames.TryGetFresh(frame.ServerId, out var stored)
                         && stored.Frame.Sequence == frame.Sequence
            ? stored.UpdateRateHz
            : null;
        return new ViewerTelemetrySnapshot
        {
            ServerId = frame.ServerId,
            ServerName = TelemetryValidation.CleanLabel(
                frame.ServerName,
                "The Isle server",
                80),
            Sequence = frame.Sequence,
            SampledAt = frame.SampledAt,
            RelayedAt = now,
            RelayAgeMilliseconds = Math.Max(0, (now - frame.SampledAt).TotalMilliseconds),
            UpdateRateHz = updateRate,
            ConnectedPlayerNodes = connectedPlayerNodes,
            VisibleEntityCount = visible.Count + (self is null ? 0 : 1),
            Source = frame.Source,
            VisibilityPolicy = frame.VisibilityPolicy,
            Capabilities = frame.Capabilities,
            Self = self,
            Players = visible
        };
    }

    private static string Pseudonym(string serverId, string viewerSteamId, string entityId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{serverId}\n{viewerSteamId}\n{entityId}"));
        return $"entity-{Convert.ToHexString(hash.AsSpan(0, 10)).ToLowerInvariant()}";
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerIdRegex();

    private sealed class ViewerConnection(
        Guid id,
        string serverId,
        string steamId,
        WebSocket socket)
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private readonly object _streamGate = new();
        private readonly Channel<TelemetryFrame> _latestFrames =
            Channel.CreateBounded<TelemetryFrame>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        internal Guid Id { get; } = id;
        internal string ServerId { get; } = serverId;
        internal string SteamId { get; } = steamId;
        internal WebSocket Socket { get; } = socket;
        internal int MaxStreamVersion { get; private set; } = 1;
        internal ViewerTelemetrySnapshot? LastSent { get; private set; }
        internal int FramesSinceKeyframe { get; private set; }

        internal void NegotiateStreamVersion(int requested, int newestSupported)
        {
            lock (_streamGate)
            {
                MaxStreamVersion = Math.Clamp(requested, 1, newestSupported);
                // The next send must be a keyframe so the viewer has a base
                // state for any following deltas.
                LastSent = null;
                FramesSinceKeyframe = 0;
            }
        }

        internal void RecordSent(ViewerTelemetrySnapshot snapshot, bool keyframe)
        {
            lock (_streamGate)
            {
                LastSent = snapshot;
                FramesSinceKeyframe = keyframe ? 0 : FramesSinceKeyframe + 1;
            }
        }

        internal bool TryPublish(TelemetryFrame frame) =>
            _latestFrames.Writer.TryWrite(frame);

        internal void Complete() =>
            _latestFrames.Writer.TryComplete();

        internal async Task RunSendLoopAsync(
            Func<TelemetryFrame, CancellationToken, Task> send,
            CancellationToken cancellationToken)
        {
            await foreach (var frame in _latestFrames.Reader.ReadAllAsync(cancellationToken))
            {
                await send(frame, cancellationToken);
            }
        }

        internal async Task SendAsync<T>(T value, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, IsleyJson.Options);
            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    await Socket.SendAsync(
                        payload,
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken);
                }
            }
            finally
            {
                _sendGate.Release();
            }
        }
    }
}

internal sealed class RelayReadinessHealthCheck(
    IOptions<RelayOptions> relay,
    IOptions<SteamOptions> steam)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        if (relay.Value.Bridges.Length == 0)
        {
            failures.Add("no bridge registrations");
        }
        if (string.IsNullOrWhiteSpace(relay.Value.PublicBaseUrl))
        {
            failures.Add("no public base URL");
        }
        if (string.IsNullOrWhiteSpace(steam.Value.WebApiKey))
        {
            failures.Add("Steam friend lookup disabled");
        }
        return Task.FromResult(failures.Count == 0
            ? HealthCheckResult.Healthy("Isley relay is configured.")
            : HealthCheckResult.Degraded(string.Join("; ", failures)));
    }
}
