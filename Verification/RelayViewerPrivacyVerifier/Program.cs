using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Isley.Relay;
using Isley.Telemetry;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static JsonDocument Parse(byte[] payload) => JsonDocument.Parse(payload);

static string ExpectedPseudonym(string serverId, string viewerSteamId, string entityId)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{serverId}\n{viewerSteamId}\n{entityId}"));
    return $"entity-{Convert.ToHexString(hash.AsSpan(0, 10)).ToLowerInvariant()}";
}

const string ServerId = "verify-srv1";
const string ViewerA = "76561198000000001";
const string ViewerB = "76561198000000009";
const string FriendId = "76561198000000002";
const string StrangerId = "76561198000000003";

static TelemetryFrame CreateFrame(long sequence, DateTimeOffset sampledAt) => new()
{
    ServerId = ServerId,
    ServerName = "Verifier Relay",
    BridgeSessionId = new string('a', 32),
    Sequence = sequence,
    SampledAt = sampledAt,
    Source = "verifier",
    VisibilityPolicy = TelemetryVisibilityPolicy.PrivacyFiltered,
    Entities =
    [
        new TelemetryEntity
        {
            EntityId = "self-entity",
            SteamId = ViewerA,
            DisplayName = "Self Name",
            Kind = TelemetryEntityKind.Player,
            SpeciesId = "triceratops",
            X = 10,
            Y = 20,
            Z = 5,
            Yaw = 45,
            DirectionQuality = TelemetryDirectionQuality.ServerAuthoritative,
            HealthPercent = 94,
            GrowthPercent = 82,
            StaminaPercent = 73,
            FoodPercent = 64,
            WaterPercent = 55,
            Conditions = ["vomit-sickness"],
            ShareScope = TelemetryShareScope.Self
        },
        new TelemetryEntity
        {
            EntityId = "friend-entity",
            SteamId = FriendId,
            DisplayName = "Pack Friend",
            Kind = TelemetryEntityKind.Player,
            SpeciesId = "stegosaurus",
            X = 30,
            Y = 40,
            Z = 5,
            HealthPercent = 88,
            Conditions = ["fracture"],
            ShareScope = TelemetryShareScope.Friends
        },
        new TelemetryEntity
        {
            EntityId = "stranger-entity",
            SteamId = StrangerId,
            DisplayName = "Private Player",
            Kind = TelemetryEntityKind.Player,
            X = 50,
            Y = 60,
            Z = 5,
            ShareScope = TelemetryShareScope.Self
        },
        new TelemetryEntity
        {
            EntityId = "deer-entity",
            DisplayName = "Deer",
            Kind = TelemetryEntityKind.AiAnimal,
            SpeciesId = "deer",
            X = 70,
            Y = 80,
            Z = 5,
            ShareScope = TelemetryShareScope.Server
        }
    ]
};

static (TelemetryFrameStore Store, TelemetryBroker Broker, RelayMetrics Metrics) CreateBroker(
    IFriendResolver friends)
{
    var options = Options.Create(new RelayOptions());
    var store = new TelemetryFrameStore(options);
    var metrics = new RelayMetrics(options);
    var broker = new TelemetryBroker(
        store,
        friends,
        metrics,
        options,
        NullLogger<TelemetryBroker>.Instance);
    return (store, broker, metrics);
}

// --- 1. Viewer pseudonymization and vitals redaction ------------------------
{
    var friends = new StubFriendResolver(new Dictionary<string, FriendDecision>
    {
        [FriendId] = new FriendDecision(true, true, "bridge-grant")
    });
    var (store, broker, _) = CreateBroker(friends);
    var frame = CreateFrame(1, DateTimeOffset.UtcNow);
    Check(store.TryAccept(frame, out _), "The fresh frame was not accepted by the store.");

    using var socket = new FakeWebSocket();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var viewerTask = broker.RunViewerAsync(ServerId, ViewerA, socket, cts.Token);
    var firstPayload = await socket.NextSentAsync(TimeSpan.FromSeconds(10));
    socket.QueueClose();
    await viewerTask;

    Check(socket.SentCount == 1, "A viewer with a fresh frame must receive exactly one snapshot.");
    using var message = Parse(firstPayload);
    var root = message.RootElement;
    Check(root.GetProperty("type").GetString() == "snapshot", "First frame was not a snapshot.");
    var snapshot = root.GetProperty("snapshot");
    Check(snapshot.GetProperty("serverId").GetString() == ServerId
          && snapshot.GetProperty("sequence").GetInt64() == 1
          && snapshot.GetProperty("connectedPlayerNodes").GetInt32() == 1
          && snapshot.GetProperty("visibleEntityCount").GetInt32() == 3
          && snapshot.GetProperty("relayAgeMilliseconds").GetDouble() >= 0,
        "Snapshot envelope metadata was wrong.");

    var self = snapshot.GetProperty("self");
    Check(self.GetProperty("id").GetString() == "self"
          && self.GetProperty("label").GetString() == "You"
          && self.GetProperty("self").GetBoolean()
          && self.GetProperty("speciesId").GetString() == "triceratops"
          && self.GetProperty("healthPercent").GetDouble() == 94
          && self.GetProperty("growthPercent").GetDouble() == 82
          && self.GetProperty("staminaPercent").GetDouble() == 73
          && self.GetProperty("foodPercent").GetDouble() == 64
          && self.GetProperty("waterPercent").GetDouble() == 55
          && self.GetProperty("conditions").GetArrayLength() == 1,
        "The viewer's own vitals, species, or conditions were not delivered in full.");

    var players = snapshot.GetProperty("players");
    Check(players.GetArrayLength() == 2,
        "The stranger with a Self share scope must be filtered out entirely.");
    var friend = players.EnumerateArray().Single(p => p.GetProperty("friend").GetBoolean());
    Check(friend.GetProperty("label").GetString() == "Pack Friend",
        "A consented friend must keep their display name.");
    var ai = players.EnumerateArray().Single(p => !p.GetProperty("friend").GetBoolean());
    Check(ai.GetProperty("label").GetString() == "Animal"
          && ai.GetProperty("kind").GetInt32() == (int)TelemetryEntityKind.AiAnimal,
        "A server-visible AI must stay visible with the honest Animal label.");

    // Non-self entities must never carry vitals, conditions, or species.
    foreach (var player in players.EnumerateArray())
    {
        Check(!player.TryGetProperty("healthPercent", out _)
              && !player.TryGetProperty("growthPercent", out _)
              && !player.TryGetProperty("staminaPercent", out _)
              && !player.TryGetProperty("foodPercent", out _)
              && !player.TryGetProperty("waterPercent", out _)
              && !player.TryGetProperty("speciesId", out _)
              && player.GetProperty("conditions").GetArrayLength() == 0,
            "Another entity's private vitals, species, or conditions leaked to the viewer.");
    }

    // Pseudonymization: ids are per-viewer SHA-256 pseudonyms, never raw ids.
    var payloadText = Encoding.UTF8.GetString(socket.SentAt(0));
    Check(!payloadText.Contains(FriendId, StringComparison.Ordinal)
          && !payloadText.Contains(StrangerId, StringComparison.Ordinal)
          && !payloadText.Contains("self-entity", StringComparison.Ordinal)
          && !payloadText.Contains("friend-entity", StringComparison.Ordinal)
          && !payloadText.Contains("deer-entity", StringComparison.Ordinal),
        "A raw Steam ID or source entity ID leaked into the viewer payload.");
    Check(friend.GetProperty("id").GetString() == ExpectedPseudonym(ServerId, ViewerA, "friend-entity")
          && ai.GetProperty("id").GetString() == ExpectedPseudonym(ServerId, ViewerA, "deer-entity"),
        "Viewer pseudonyms do not match the pinned SHA-256 derivation.");
    Check(friends.Calls == 2,
        "The resolver must be consulted exactly once per non-self, non-AI, Steam-identified entity.");
}

// --- 1b. Pseudonyms are viewer-scoped: a different viewer sees different ids.
{
    var friends = new StubFriendResolver(new Dictionary<string, FriendDecision>
    {
        [FriendId] = new FriendDecision(true, true, "bridge-grant")
    });
    var (store, broker, _) = CreateBroker(friends);
    Check(store.TryAccept(CreateFrame(1, DateTimeOffset.UtcNow), out _), "Frame was not accepted.");

    using var socket = new FakeWebSocket();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var viewerTask = broker.RunViewerAsync(ServerId, ViewerB, socket, cts.Token);
    var payload = await socket.NextSentAsync(TimeSpan.FromSeconds(10));
    socket.QueueClose();
    await viewerTask;
    using var message = Parse(payload);
    var players = message.RootElement.GetProperty("snapshot").GetProperty("players");
    var friend = players.EnumerateArray().Single(p => p.GetProperty("friend").GetBoolean());
    Check(friend.GetProperty("id").GetString() == ExpectedPseudonym(ServerId, ViewerB, "friend-entity")
          && friend.GetProperty("id").GetString() != ExpectedPseudonym(ServerId, ViewerA, "friend-entity"),
        "Pseudonyms must be re-derived per viewer so ids cannot be correlated across viewers.");
}

// --- 2. Fanout: every viewer of the same server is served; others are not. --
{
    var friends = new StubFriendResolver([]);
    var (store, broker, metrics) = CreateBroker(friends);
    Check(store.TryAccept(CreateFrame(1, DateTimeOffset.UtcNow), out _), "Frame was not accepted.");

    using var first = new FakeWebSocket();
    using var second = new FakeWebSocket();
    using var otherServer = new FakeWebSocket();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var firstTask = broker.RunViewerAsync(ServerId, ViewerA, first, cts.Token);
    var secondTask = broker.RunViewerAsync(ServerId, ViewerB, second, cts.Token);
    var otherTask = broker.RunViewerAsync("verify-srv2", StrangerId, otherServer, cts.Token);
    await first.NextSentAsync(TimeSpan.FromSeconds(10));
    await second.NextSentAsync(TimeSpan.FromSeconds(10));
    var otherPayload = await otherServer.NextSentAsync(TimeSpan.FromSeconds(10));
    first.QueueClose();
    second.QueueClose();
    otherServer.QueueClose();
    await Task.WhenAll(firstTask, secondTask, otherTask);

    Check(first.SentCount == 1 && second.SentCount == 1,
        "Every viewer of the same server must receive the fanned-out snapshot.");
    using var otherMessage = JsonDocument.Parse(otherPayload);
    Check(otherServer.SentCount == 1
          && otherMessage.RootElement.GetProperty("state").GetString() == "waiting",
        "A viewer of a different server must not receive another server's frames.");
    Check(broker.ActiveViewerCount == 0,
        "Disconnected viewers must be removed from the active viewer count.");
    var metricsJson = JsonSerializer.Serialize(metrics.Snapshot(store.CountFresh(), broker.ActiveViewerCount));
    Check(JsonDocument.Parse(metricsJson).RootElement.GetProperty("viewerConnectionsTotal").GetInt64() == 3,
        "Viewer connections must be counted in aggregate.");
    Check(!metricsJson.Contains(ViewerA, StringComparison.Ordinal)
          && !metricsJson.Contains(ServerId, StringComparison.Ordinal),
        "Operational metrics must stay aggregate-only: no Steam IDs or server IDs.");

    // PublishAsync fans out to live viewers of the matching server only.
    using var live = new FakeWebSocket();
    using var liveCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var liveTask = broker.RunViewerAsync(ServerId, ViewerA, live, liveCts.Token);
    await live.NextSentAsync(TimeSpan.FromSeconds(10)); // initial snapshot
    Check(broker.ActiveViewerCount == 1, "The live viewer was not tracked.");
    var published = CreateFrame(2, DateTimeOffset.UtcNow);
    await broker.PublishAsync(published, liveCts.Token);
    using var second2 = Parse(await live.NextSentAsync(TimeSpan.FromSeconds(10)));
    Check(second2.RootElement.GetProperty("snapshot").GetProperty("sequence").GetInt64() == 2,
        "PublishAsync did not fan the new frame out to the live viewer.");
    Check(JsonDocument.Parse(JsonSerializer.Serialize(metrics.Snapshot(0, 0)))
              .RootElement.GetProperty("framesRelayed").GetInt64() == 1,
        "Each relayed frame must be counted exactly once.");
    live.QueueClose();
    await liveTask;
    Check(broker.ActiveViewerCount == 0, "The viewer count did not drop after disconnect.");
}

// --- 3. One-frame queue coalescing: a slow viewer skips stale frames. -------
{
    var friends = new StubFriendResolver([]);
    var (store, broker, _) = CreateBroker(friends);
    using var socket = new FakeWebSocket { GateSends = true };
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var task = broker.RunViewerAsync(ServerId, ViewerA, socket, cts.Token);

    // The "waiting" status send is in progress and holds the connection send
    // gate; nothing else can be sent until we release it.
    Check(await socket.SendCallSignal.WaitAsync(TimeSpan.FromSeconds(10)),
        "The waiting-status send never started.");
    await broker.PublishAsync(CreateFrame(1, DateTimeOffset.UtcNow), cts.Token);
    await Task.Delay(50);
    await broker.PublishAsync(CreateFrame(2, DateTimeOffset.UtcNow), cts.Token);
    await broker.PublishAsync(CreateFrame(3, DateTimeOffset.UtcNow), cts.Token);
    await Task.Delay(50);
    socket.SendGate.Release(2); // status + first snapshot
    await socket.NextSentAsync(TimeSpan.FromSeconds(10)); // status
    using var firstSnapshot = Parse(await socket.NextSentAsync(TimeSpan.FromSeconds(10)));
    Check(firstSnapshot.RootElement.GetProperty("snapshot").GetProperty("sequence").GetInt64() == 1,
        "The oldest queued frame must still be delivered first.");
    socket.SendGate.Release(1);
    using var latestSnapshot = Parse(await socket.NextSentAsync(TimeSpan.FromSeconds(10)));
    Check(latestSnapshot.RootElement.GetProperty("snapshot").GetProperty("sequence").GetInt64() == 3,
        "The bounded one-frame queue must coalesce: sequence 2 was stale before it was sent.");
    Check(socket.SentCount == 3,
        "Exactly one frame must be coalesced away, never batched or duplicated.");
    socket.QueueClose();
    await task;
}

// --- 4. Stream negotiation and control-frame bounds. ------------------------
{
    var friends = new StubFriendResolver([]);
    var (store, broker, _) = CreateBroker(friends);

    using var helloSocket = new FakeWebSocket();
    helloSocket.QueueText("{\"type\":\"hello\",\"maxStreamVersion\":2}");
    using var helloCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var helloTask = broker.RunViewerAsync(ServerId, ViewerA, helloSocket, helloCts.Token);
    var statusPayload = await helloSocket.NextSentAsync(TimeSpan.FromSeconds(10));
    var helloPayload = await helloSocket.NextSentAsync(TimeSpan.FromSeconds(10));
    helloSocket.QueueClose();
    await helloTask;
    Check(helloSocket.SentCount == 2, "The hello handshake must answer with exactly one control frame.");
    using (var status = Parse(statusPayload))
    {
        Check(status.RootElement.GetProperty("state").GetString() == "waiting",
            "An empty store must produce the honest waiting status first.");
    }
    using (var hello = Parse(helloPayload))
    {
        Check(hello.RootElement.GetProperty("type").GetString() == "hello"
              && hello.RootElement.GetProperty("streamVersion").GetInt32()
                 == TelemetryProtocol.ViewerStreamVersion
              && hello.RootElement.GetProperty("keyframeIntervalFrames").GetInt32() == 240
              && hello.RootElement.GetProperty("deltaEncoding").GetBoolean(),
            "The negotiated stream hello did not pin version, keyframe cadence, and delta support.");
    }

    // Malformed control frames are ignored without tearing the stream down.
    using var junkSocket = new FakeWebSocket();
    junkSocket.QueueText("{not-json");
    junkSocket.QueueText("{\"type\":\"unrelated\"}");
    using var junkCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var junkTask = broker.RunViewerAsync(ServerId, ViewerA, junkSocket, junkCts.Token);
    await junkSocket.NextSentAsync(TimeSpan.FromSeconds(10)); // waiting status
    await Task.Delay(300);
    Check(junkSocket.SentCount == 1,
        "Malformed control frames must be ignored, never answered.");
    junkSocket.QueueClose();
    await junkTask;
    Check(junkSocket.ClosedWith == WebSocketCloseStatus.NormalClosure,
        "Malformed control frames must never tear the stream down.");

    // Oversized (fragmented) client messages are rejected with MessageTooBig.
    using var bigSocket = new FakeWebSocket();
    bigSocket.QueueText(new string('x', 512), endOfMessage: false);
    using var bigCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await broker.RunViewerAsync(ServerId, ViewerA, bigSocket, bigCts.Token);
    Check(bigSocket.ClosedWith == WebSocketCloseStatus.MessageTooBig,
        "A fragmented client message must be closed as too big.");

    // Invalid server ids are refused with a policy violation before any send.
    using var badServerSocket = new FakeWebSocket();
    using var badServerCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await broker.RunViewerAsync("Bad_Server", ViewerA, badServerSocket, badServerCts.Token);
    Check(badServerSocket.ClosedWith == WebSocketCloseStatus.PolicyViolation
          && badServerSocket.SentCount == 0,
        "An invalid server id must be refused before any frame is sent.");
}

// --- 5. Negotiated v2 delivery: keyframe anchor then delta frames. ----------
{
    var friends = new StubFriendResolver([]);
    var (store, broker, _) = CreateBroker(friends);
    Check(store.TryAccept(CreateFrame(1, DateTimeOffset.UtcNow), out _), "Frame was not accepted.");

    using var socket = new FakeWebSocket();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var task = broker.RunViewerAsync(ServerId, ViewerA, socket, cts.Token);
    // The initial frame is published before any negotiation: legacy v1 shape.
    using var legacy = Parse(await socket.NextSentAsync(TimeSpan.FromSeconds(10)));
    Check(legacy.RootElement.GetProperty("type").GetString() == "snapshot"
          && !legacy.RootElement.TryGetProperty("streamVersion", out _),
        "A viewer must receive the pre-negotiation frame in the legacy v1 shape.");
    socket.QueueText("{\"type\":\"hello\",\"maxStreamVersion\":2}");
    using var hello = Parse(await socket.NextSentAsync(TimeSpan.FromSeconds(10)));
    Check(hello.RootElement.GetProperty("type").GetString() == "hello",
        "The relay must answer the negotiated hello.");

    // After negotiation the next send must re-anchor with a v2 keyframe.
    await broker.PublishAsync(CreateFrame(2, DateTimeOffset.UtcNow), cts.Token);
    using var keyframe = Parse(await socket.NextSentAsync(TimeSpan.FromSeconds(10)));
    Check(keyframe.RootElement.GetProperty("type").GetString() == "snapshot"
          && keyframe.RootElement.GetProperty("keyframe").GetBoolean()
          && keyframe.RootElement.GetProperty("streamVersion").GetInt32()
             == TelemetryProtocol.ViewerStreamVersion,
        "A negotiated v2 stream must re-anchor with a keyframe snapshot.");

    await broker.PublishAsync(CreateFrame(3, DateTimeOffset.UtcNow), cts.Token);
    using var delta = Parse(await socket.NextSentAsync(TimeSpan.FromSeconds(10)));
    Check(delta.RootElement.GetProperty("type").GetString() == "delta"
          && delta.RootElement.GetProperty("delta").GetProperty("baseSequence").GetInt64() == 2
          && delta.RootElement.GetProperty("delta").GetProperty("sequence").GetInt64() == 3,
        "The next v2 frame must be a delta against the keyframe base.");
    var deltaText = delta.RootElement.GetRawText();
    Check(!deltaText.Contains(FriendId, StringComparison.Ordinal)
          && !deltaText.Contains("friend-entity", StringComparison.Ordinal),
        "Delta frames must carry the same pseudonymized ids as snapshots.");
    socket.QueueClose();
    await task;
}

// --- 6. Rate-limit rejection accounting and readiness health. ---------------
{
    var metrics = new RelayMetrics(Options.Create(new RelayOptions()));
    metrics.RateLimitRejected();
    metrics.RateLimitRejected();
    using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(metrics.Snapshot(0, 0)));
    Check(snapshot.RootElement.GetProperty("rateLimitRejections").GetInt64() == 2,
        "Rate-limit rejections must be counted in aggregate metrics.");
    Check(snapshot.RootElement.GetProperty("visibility").GetString() == "loopback-only",
        "The metrics endpoint must default to the loopback-only posture.");

    var relayProgram = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Isley.Relay", "Program.cs"));
    Check(relayProgram.Contains("RejectionStatusCode = StatusCodes.Status429TooManyRequests", StringComparison.Ordinal)
          && relayProgram.Contains("?.RateLimitRejected();", StringComparison.Ordinal)
          && relayProgram.Contains("AddFixedWindowLimiter(\"device\"", StringComparison.Ordinal)
          && relayProgram.Contains("limiter.PermitLimit = 20;", StringComparison.Ordinal)
          && relayProgram.Contains("AddFixedWindowLimiter(\"ingest\"", StringComparison.Ordinal)
          && relayProgram.Contains("limiter.QueueLimit = 0;", StringComparison.Ordinal),
        "The relay must reject over-limit requests with 429, no queueing, and metrics accounting.");

    var readiness = new RelayReadinessHealthCheck(
        Options.Create(new RelayOptions
        {
            PublicBaseUrl = "https://relay.example/",
            Bridges = [new BridgeRegistration { ServerId = "verify-srv1", Secret = new string('s', 48) }]
        }),
        Options.Create(new SteamOptions { WebApiKey = "key" }));
    Check((await readiness.CheckHealthAsync(new HealthCheckContext())).Status == HealthStatus.Healthy,
        "A fully configured relay must report healthy.");
    var degraded = new RelayReadinessHealthCheck(
        Options.Create(new RelayOptions()),
        Options.Create(new SteamOptions()));
    var degradedResult = await degraded.CheckHealthAsync(new HealthCheckContext());
    Check(degradedResult.Status == HealthStatus.Degraded
          && degradedResult.Description!.Contains("no bridge registrations", StringComparison.Ordinal)
          && degradedResult.Description.Contains("no public base URL", StringComparison.Ordinal)
          && degradedResult.Description.Contains("Steam friend lookup disabled", StringComparison.Ordinal),
        "An unconfigured relay must degrade with every missing piece named honestly.");
}

Console.WriteLine(
    "Relay viewer privacy verification passed: per-viewer SHA-256 pseudonyms, "
    + "self-only vitals/species/conditions, stranger filtering, friend labels, "
    + "AI honesty, same-server fanout, one-frame queue coalescing, hello "
    + "negotiation and control-frame bounds, v2 keyframe/delta delivery, "
    + "rate-limit rejection accounting, and readiness health.");

// A controllable in-memory WebSocket: the test queues inbound frames and
// captures every outbound payload, so the real TelemetryBroker send/receive
// loops run end to end without any network or process spawning.
sealed class FakeWebSocket : WebSocket
{
    private readonly ConcurrentQueue<(byte[] Data, WebSocketMessageType Type, bool EndOfMessage)> _inbound = new();
    private readonly SemaphoreSlim _inboundSignal = new(0);
    private WebSocketState _state = WebSocketState.Open;

    private readonly List<byte[]> _sent = new();
    private int _consumed;
    internal readonly SemaphoreSlim SentSignal = new(0);
    internal readonly SemaphoreSlim SendCallSignal = new(0);
    internal readonly SemaphoreSlim SendGate = new(0);
    internal bool GateSends;
    internal WebSocketCloseStatus? ClosedWith { get; private set; }

    internal void QueueText(string text, bool endOfMessage = true)
    {
        _inbound.Enqueue((Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage));
        _inboundSignal.Release();
    }

    internal void QueueClose()
    {
        _inbound.Enqueue(([], WebSocketMessageType.Close, true));
        _inboundSignal.Release();
    }

    internal async Task<byte[]> NextSentAsync(TimeSpan timeout)
    {
        if (!await SentSignal.WaitAsync(timeout))
        {
            throw new InvalidOperationException("Timed out waiting for a relay send.");
        }
        lock (_sent)
        {
            return _sent[_consumed++];
        }
    }

    internal int SentCount
    {
        get
        {
            lock (_sent)
            {
                return _sent.Count;
            }
        }
    }

    internal byte[] SentAt(int index)
    {
        lock (_sent)
        {
            return _sent[index];
        }
    }

    public override WebSocketState State => _state;
    public override WebSocketCloseStatus? CloseStatus => ClosedWith;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;

    public override async Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken)
    {
        await _inboundSignal.WaitAsync(cancellationToken);
        if (!_inbound.TryDequeue(out var item))
        {
            throw new InvalidOperationException("Inbound queue/signaling desynchronized.");
        }
        if (item.Type == WebSocketMessageType.Close)
        {
            _state = WebSocketState.CloseReceived;
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }
        item.Data.CopyTo(buffer.Array!, buffer.Offset);
        return new WebSocketReceiveResult(item.Data.Length, item.Type, item.EndOfMessage);
    }

    public override async Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        SendCallSignal.Release();
        if (GateSends)
        {
            await SendGate.WaitAsync(cancellationToken);
        }
        lock (_sent)
        {
            _sent.Add(buffer.ToArray());
        }
        SentSignal.Release();
    }

    public override Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        ClosedWith ??= closeStatus;
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken) =>
        CloseAsync(closeStatus, statusDescription, cancellationToken);

    public override void Abort() => _state = WebSocketState.Aborted;

    public override void Dispose()
    {
    }
}

sealed class StubFriendResolver(Dictionary<string, FriendDecision> decisions) : IFriendResolver
{
    internal int Calls;

    public Task<FriendDecision> EvaluateAsync(
        string targetSteamId,
        string viewerSteamId,
        TelemetryShareScope sourceScope,
        IReadOnlyList<string> sourceGrants,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(decisions.TryGetValue(targetSteamId, out var decision)
            ? decision
            : new FriendDecision(false, false, "not-shared"));
    }
}
