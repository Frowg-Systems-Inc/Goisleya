using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var unsafeEnvironmentOrigin = Enumerable.Range(0, 17)
    .Select(index => Environment.GetEnvironmentVariable(
        $"Voice__AllowedOrigins__{index}"))
    .FirstOrDefault(origin =>
        origin is not null && !VoiceServerOptions.IsValidOrigin(origin));
if (unsafeEnvironmentOrigin is not null)
{
    Console.Error.WriteLine(
        "OptionsValidationException: Isley Voice allowed origins must use HTTPS.");
    Environment.ExitCode = 78;
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddOptions<VoiceServerOptions>()
    .Bind(builder.Configuration.GetSection("Voice"))
    .Validate(options => options.IsValid(), "Isley Voice server limits or allowed origins are invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton<VoiceRoomBroker>();

var app = builder.Build();
var configured = app.Services
    .GetRequiredService<Microsoft.Extensions.Options.IOptions<VoiceServerOptions>>()
    .Value;
if (!configured.IsValid())
{
    throw new Microsoft.Extensions.Options.OptionsValidationException(
        Microsoft.Extensions.Options.Options.DefaultName,
        typeof(VoiceServerOptions),
        ["Isley Voice server limits or allowed origins are invalid."]);
}
var allowedOrigins = configured.AllowedOrigins
    .Select(origin => origin.TrimEnd('/'))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
};
foreach (var origin in allowedOrigins) webSocketOptions.AllowedOrigins.Add(origin);
app.UseWebSockets(webSocketOptions);

app.MapHealthChecks("/health");
app.MapGet("/ready", (HttpContext context, VoiceRoomBroker broker) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    return Results.Json(broker.Readiness());
});
app.MapGet("/", () => TypedResults.Ok(new
{
    service = "Isley Voice Signaling",
    protocolVersion = VoiceRoomBroker.ProtocolVersion,
    transport = "WebRTC signaling only",
    mediaRelay = false,
    roomIdsExposed = false,
    positionDataReceived = false,
    signalingPayloadsEncrypted = true,
    displayNamesReceived = false,
    webRtcCandidateDetailsReceived = false
}));

app.Map("/voice", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        return;
    }

    var origin = context.Request.Headers.Origin.ToString();
    if (allowedOrigins.Count > 0 && !allowedOrigins.Contains(origin))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    var roomId = context.Request.Query["room"].ToString().Trim().ToLowerInvariant();
    var peerId = context.Request.Query["peer"].ToString().Trim().ToLowerInvariant();
    if (!VoiceRoomBroker.IsOpaqueId(roomId) || !VoiceRoomBroker.IsPeerId(peerId))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var broker = context.RequestServices.GetRequiredService<VoiceRoomBroker>();
    await broker.RunPeerAsync(roomId, peerId, socket, context.RequestAborted);
});

app.Run();

internal sealed class VoiceServerOptions
{
    public int MaxPeersPerRoom { get; init; } = 12;
    public int MaxMessageBytes { get; init; } = 65_536;
    public int MaxMessagesPerTenSeconds { get; init; } = 240;
    public int MaxRooms { get; init; } = 1024;
    public int MaxTotalPeers { get; init; } = 4096;
    public string[] AllowedOrigins { get; init; } = [];

    internal bool IsValid()
    {
        if (MaxPeersPerRoom is < 2 or > 32
            || MaxMessageBytes is < 4096 or > 262_144
            || MaxMessagesPerTenSeconds is < 20 or > 1000
            || MaxRooms is < 1 or > 10_000
            || MaxTotalPeers < MaxPeersPerRoom
            || MaxTotalPeers > 100_000
            || AllowedOrigins is not { Length: > 0 and <= 16 })
        {
            return false;
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in AllowedOrigins)
        {
            var candidate = (value ?? string.Empty).Trim().TrimEnd('/');
            if (!IsValidOrigin(candidate)
                || !normalized.Add(candidate))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsValidOrigin(string? value)
    {
        var candidate = (value ?? string.Empty).Trim().TrimEnd('/');
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps
               && !string.IsNullOrWhiteSpace(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo)
               && string.IsNullOrEmpty(uri.Query)
               && string.IsNullOrEmpty(uri.Fragment)
               && uri.AbsolutePath == "/";
    }
}

internal sealed class VoiceRoomBroker(
    Microsoft.Extensions.Options.IOptions<VoiceServerOptions> options,
    ILogger<VoiceRoomBroker> logger)
{
    internal const int ProtocolVersion = 2;
    private static readonly Regex OpaqueId = new("^[a-f0-9]{64}$", RegexOptions.Compiled);
    private static readonly Regex PeerId = new("^[a-f0-9]{32}$", RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, VoicePeer>> _rooms = new();
    private readonly VoiceServerOptions _options = options.Value;
    private readonly object _admissionGate = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private int _activePeers;

    internal static bool IsOpaqueId(string value) => OpaqueId.IsMatch(value);
    internal static bool IsPeerId(string value) => PeerId.IsMatch(value);

    internal async Task RunPeerAsync(
        string roomId,
        string peerId,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        if (!TryAdmit(roomId, peerId, socket, out var room))
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation,
                "Voice capacity reached or peer already connected",
                CancellationToken.None);
            return;
        }

        try
        {
            var existing = room.Values
                .Where(peer => peer.Id != peerId)
                .Select(peer => new { id = peer.Id })
                .ToArray();
            await SendAsync(socket, new { type = "welcome", self = peerId, peers = existing }, cancellationToken);
            await BroadcastAsync(room, peerId, null, new
            {
                type = "peer-joined",
                peer = new { id = peerId }
            }, cancellationToken);

            await ReceiveLoopAsync(room, peerId, socket, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "Voice peer {PeerId} disconnected from room {RoomId}", peerId, roomId);
        }
        finally
        {
            if (RemovePeer(roomId, peerId, room))
            {
                await BroadcastAsync(
                    room,
                    peerId,
                    null,
                    new { type = "peer-left", peer = peerId },
                    CancellationToken.None);
            }
        }
    }

    internal VoiceServiceReadiness Readiness()
    {
        lock (_admissionGate)
        {
            return new VoiceServiceReadiness(
                "Isley Voice Signaling",
                "ready",
                ProtocolVersion,
                false,
                false,
                false,
                true,
                false,
                false,
                _options.MaxPeersPerRoom,
                _options.MaxMessageBytes,
                _options.MaxRooms,
                _options.MaxTotalPeers,
                _rooms.Count,
                _activePeers,
                _startedAt);
        }
    }

    private bool TryAdmit(
        string roomId,
        string peerId,
        WebSocket socket,
        out ConcurrentDictionary<string, VoicePeer> room)
    {
        lock (_admissionGate)
        {
            room = null!;
            if (_activePeers >= _options.MaxTotalPeers)
            {
                return false;
            }

            if (_rooms.TryGetValue(roomId, out var existingRoom))
            {
                room = existingRoom;
            }
            else
            {
                if (_rooms.Count >= _options.MaxRooms)
                {
                    return false;
                }
                room = new ConcurrentDictionary<string, VoicePeer>();
                if (!_rooms.TryAdd(roomId, room))
                {
                    return false;
                }
            }

            if (room.Count >= _options.MaxPeersPerRoom
                || !room.TryAdd(peerId, new VoicePeer(peerId, socket)))
            {
                if (room.IsEmpty)
                {
                    _rooms.TryRemove(roomId, out _);
                }
                return false;
            }

            _activePeers++;
            return true;
        }
    }

    private bool RemovePeer(
        string roomId,
        string peerId,
        ConcurrentDictionary<string, VoicePeer> room)
    {
        lock (_admissionGate)
        {
            if (!room.TryRemove(peerId, out _))
            {
                return false;
            }
            _activePeers = Math.Max(0, _activePeers - 1);
            if (room.IsEmpty)
            {
                _rooms.TryRemove(roomId, out _);
            }
            return true;
        }
    }

    private async Task ReceiveLoopAsync(
        ConcurrentDictionary<string, VoicePeer> room,
        string peerId,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var maxBytes = Math.Clamp(_options.MaxMessageBytes, 4096, 262_144);
        var buffer = new byte[Math.Min(maxBytes, 65_536)];
        var windowStarted = Environment.TickCount64;
        var messageCount = 0;
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var payload = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (result.MessageType != WebSocketMessageType.Text
                    || payload.Length + result.Count > maxBytes)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Invalid message", CancellationToken.None);
                    return;
                }
                payload.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var now = Environment.TickCount64;
            if (now - windowStarted >= 10_000)
            {
                windowStarted = now;
                messageCount = 0;
            }
            if (++messageCount > Math.Clamp(_options.MaxMessagesPerTenSeconds, 20, 1000))
            {
                await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Rate limit exceeded", CancellationToken.None);
                return;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payload.ToArray());
            }
            catch (JsonException)
            {
                await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid message", CancellationToken.None);
                return;
            }
            using var parsedDocument = document;
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement)) continue;
            var type = typeElement.GetString();
            if (type != "signal") continue;
            var target = root.TryGetProperty("to", out var targetElement)
                ? targetElement.GetString()?.Trim().ToLowerInvariant()
                : null;
            if (target is null
                || !IsPeerId(target)
                || root.EnumerateObject().Count() != 3
                || root.TryGetProperty("data", out _)
                || !root.TryGetProperty("sealed", out var sealedElement)
                || sealedElement.ValueKind != JsonValueKind.Object
                || sealedElement.EnumerateObject().Count() != 3
                || !sealedElement.TryGetProperty("v", out var versionElement)
                || !versionElement.TryGetInt32(out var envelopeVersion)
                || envelopeVersion != 1
                || !sealedElement.TryGetProperty("iv", out var ivElement)
                || ivElement.GetString() is not { Length: 16 } iv
                || !Regex.IsMatch(iv, "^[A-Za-z0-9_-]{16}$")
                || !sealedElement.TryGetProperty("ciphertext", out var ciphertextElement)
                || ciphertextElement.GetString() is not { Length: >= 24 and <= 45_056 } ciphertext
                || !Regex.IsMatch(ciphertext, "^[A-Za-z0-9_-]+$"))
            {
                continue;
            }
            var outbound = new
            {
                type = "signal",
                to = target,
                @sealed = sealedElement.Clone()
            };
            await BroadcastAsync(room, peerId, target, outbound, cancellationToken);
        }
    }

    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private static async Task BroadcastAsync<T>(
        ConcurrentDictionary<string, VoicePeer> room,
        string sender,
        string? target,
        T message,
        CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new { from = sender, message });
        var peers = target is null
            ? room.Values.Where(peer => peer.Id != sender).ToArray()
            : room.TryGetValue(target, out var peer) ? [peer] : [];
        foreach (var recipient in peers)
        {
            if (recipient.Socket.State != WebSocketState.Open) continue;
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendTimeout.CancelAfter(SendTimeout);
            try
            {
                await recipient.SendGate.WaitAsync(sendTimeout.Token);
                try
                {
                    await recipient.Socket.SendAsync(
                        envelope,
                        WebSocketMessageType.Text,
                        true,
                        sendTimeout.Token);
                }
                finally
                {
                    recipient.SendGate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A stalled or broken recipient must not block the room or
                // tear down the sender; abort it so its own loop cleans up.
                recipient.Socket.Abort();
            }
        }
    }

    private static Task SendAsync<T>(WebSocket socket, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        return socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
    }

    private sealed record VoicePeer(string Id, WebSocket Socket)
    {
        internal SemaphoreSlim SendGate { get; } = new(1, 1);
    }
}

internal sealed record VoiceServiceReadiness(
    string Service,
    string Status,
    int ProtocolVersion,
    bool MediaRelay,
    bool RoomIdsExposed,
    bool PositionDataReceived,
    bool SignalingPayloadsEncrypted,
    bool DisplayNamesReceived,
    bool WebRtcCandidateDetailsReceived,
    int MaxPeersPerRoom,
    int MaxMessageBytes,
    int MaxRooms,
    int MaxTotalPeers,
    int ActiveRooms,
    int ActivePeers,
    DateTimeOffset StartedAt);

public partial class Program;
