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
    ILogger<TelemetryBroker> logger)
{
    private readonly ConcurrentDictionary<Guid, ViewerConnection> _connections = new();

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
        foreach (var connection in _connections.Values.Where(connection => string.Equals(
                connection.ServerId,
                frame.ServerId,
                StringComparison.Ordinal)))
        {
            connection.TryPublish(frame);
        }
        return Task.CompletedTask;
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
        await connection.SendAsync(new { type = "snapshot", snapshot }, cancellationToken);
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
