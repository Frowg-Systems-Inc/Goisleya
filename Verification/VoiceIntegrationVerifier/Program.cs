using Isley;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static int ReserveLoopbackPort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static async Task<string> ReceiveVoiceTextAsync(
    ClientWebSocket socket,
    CancellationToken cancellationToken)
{
    var buffer = new byte[8192];
    using var stream = new MemoryStream();
    WebSocketReceiveResult result;
    do
    {
        result = await socket.ReceiveAsync(buffer, cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            throw new InvalidOperationException("Voice socket closed before the expected message.");
        }
        stream.Write(buffer, 0, result.Count);
    } while (!result.EndOfMessage);
    return Encoding.UTF8.GetString(stream.ToArray());
}

static async Task<string> ReceiveVoiceTextMatchingAsync(
    ClientWebSocket socket,
    Func<string, bool> predicate,
    string expectation,
    CancellationToken cancellationToken,
    int maximumMessages = 8)
{
    var observed = new List<string>();
    for (var index = 0; index < maximumMessages; index++)
    {
        var message = await ReceiveVoiceTextAsync(socket, cancellationToken);
        observed.Add(message.Length <= 160 ? message : $"{message[..160]}...");
        if (predicate(message)) return message;
    }

    throw new InvalidOperationException(
        $"Voice socket did not receive {expectation} after {maximumMessages} messages. "
        + $"Observed: {string.Join(" | ", observed)}");
}

static ProcessStartInfo CreateVoiceServerStartInfo(
    string serverDirectory,
    bool redirectStandardOutput,
    bool redirectStandardError)
{
    var serverExecutable = Path.Combine(serverDirectory, "Isley.VoiceServer.exe");
    var serverDll = Path.Combine(serverDirectory, "Isley.VoiceServer.dll");
    Check(
        File.Exists(serverExecutable) || File.Exists(serverDll),
        "bundled voice server executable is missing");

    if (File.Exists(serverExecutable))
    {
        return new ProcessStartInfo(serverExecutable)
        {
            WorkingDirectory = serverDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = redirectStandardOutput,
            RedirectStandardError = redirectStandardError
        };
    }

    var startInfo = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = serverDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        RedirectStandardOutput = redirectStandardOutput,
        RedirectStandardError = redirectStandardError
    };
    startInfo.ArgumentList.Add(serverDll);
    return startInfo;
}

static async Task VerifyBundledVoiceServerAsync(string repositoryRoot)
{
    var configuration = AppContext.BaseDirectory.Contains(
        $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
        StringComparison.OrdinalIgnoreCase)
        ? "Debug"
        : "Release";
    var serverDirectory = Path.Combine(
        repositoryRoot,
        "Isley.VoiceServer", "bin", configuration, "net8.0");

    var rejectedPort = ReserveLoopbackPort();
    var rejectedStartInfo = CreateVoiceServerStartInfo(
        serverDirectory,
        redirectStandardOutput: false,
        redirectStandardError: true);
    rejectedStartInfo.ArgumentList.Add("--urls");
    rejectedStartInfo.ArgumentList.Add($"http://127.0.0.1:{rejectedPort}");
    rejectedStartInfo.Environment["Voice__AllowedOrigins__0"] = "http://unsafe.example.com";
    using (var rejectedHost = Process.Start(rejectedStartInfo)
               ?? throw new InvalidOperationException("Invalid voice server probe did not start."))
    using (var rejectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
    {
        try
        {
            await rejectedHost.WaitForExitAsync(rejectionTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            rejectedHost.Kill(entireProcessTree: true);
            await rejectedHost.WaitForExitAsync(CancellationToken.None);
            throw new InvalidOperationException(
                "Voice server accepted an unsafe plaintext origin instead of failing closed.");
        }
        var rejectedErrors = await rejectedHost.StandardError.ReadToEndAsync();
        Check(rejectedHost.ExitCode != 0
              && rejectedErrors.Contains("OptionsValidationException", StringComparison.Ordinal),
            "voice server did not fail closed on an unsafe allowed origin");
    }

    var port = ReserveLoopbackPort();
    var startInfo = CreateVoiceServerStartInfo(
        serverDirectory,
        redirectStandardOutput: true,
        redirectStandardError: true);
    startInfo.ArgumentList.Add("--urls");
    startInfo.ArgumentList.Add($"http://127.0.0.1:{port}");
    startInfo.Environment["Voice__MaxPeersPerRoom"] = "2";
    startInfo.Environment["Voice__MaxRooms"] = "2";
    startInfo.Environment["Voice__MaxTotalPeers"] = "2";
    using var host = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Bundled voice server did not start.");
    var hostOutput = host.StandardOutput.ReadToEndAsync();
    var hostErrors = host.StandardError.ReadToEndAsync();
    using var socketA = new ClientWebSocket();
    using var socketB = new ClientWebSocket();
    using var socketC = new ClientWebSocket();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    try
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        var ready = false;
        for (var attempt = 0; attempt < 50 && !ready; attempt++)
        {
            if (host.HasExited) break;
            try
            {
                using var response = await http.GetAsync(
                    $"http://127.0.0.1:{port}/health",
                    timeout.Token);
                ready = response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!timeout.IsCancellationRequested)
            {
            }
            if (!ready) await Task.Delay(100, timeout.Token);
        }
        if (!ready)
        {
            if (!host.HasExited)
            {
                host.Kill(entireProcessTree: true);
                await host.WaitForExitAsync(CancellationToken.None);
            }
            throw new InvalidOperationException(
                $"Bundled voice server health endpoint did not become ready (exit {host.ExitCode}). "
                + $"stdout: {await hostOutput} stderr: {await hostErrors}");
        }
        using var readinessResponse = await http.GetAsync(
            $"http://127.0.0.1:{port}/ready",
            timeout.Token);
        Check(readinessResponse.IsSuccessStatusCode
              && readinessResponse.Headers.CacheControl?.NoStore == true
              && readinessResponse.Headers.TryGetValues(
                  "X-Content-Type-Options",
                  out var contentTypeOptions)
              && contentTypeOptions.Contains("nosniff", StringComparer.OrdinalIgnoreCase),
            "bundled voice readiness response security headers failed");
        var initialReadiness = VoiceServerReadinessClient.Parse(
            await readinessResponse.Content.ReadAsStringAsync(timeout.Token),
            DateTimeOffset.UnixEpoch);
        Check(initialReadiness.ProtocolVersion == VoiceServerReadinessClient.ProtocolVersion
              && initialReadiness.ActiveRooms == 0
              && initialReadiness.ActivePeers == 0
              && initialReadiness.MaxPeersPerRoom == 2
              && initialReadiness.MaxTotalPeers == 2,
            "bundled voice server readiness snapshot failed");

        const string room = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string peerA = "11111111111111111111111111111111";
        const string peerB = "22222222222222222222222222222222";
        socketA.Options.SetRequestHeader("Origin", "https://isley.voice.local");
        socketB.Options.SetRequestHeader("Origin", "https://isley.voice.local");
        await socketA.ConnectAsync(
            new Uri($"ws://127.0.0.1:{port}/voice?room={room}&peer={peerA}"),
            timeout.Token);
        var firstWelcome = await ReceiveVoiceTextMatchingAsync(
            socketA,
            message => message.Contains("welcome", StringComparison.Ordinal),
            "the first peer welcome",
            timeout.Token);
        Check(!firstWelcome.Contains("\"name\"", StringComparison.Ordinal)
              && !firstWelcome.Contains("Alpha", StringComparison.Ordinal),
            "voice welcome exposed a display name");
        await socketB.ConnectAsync(
            new Uri($"ws://127.0.0.1:{port}/voice?room={room}&peer={peerB}"),
            timeout.Token);
        var secondWelcome = await ReceiveVoiceTextMatchingAsync(
            socketB,
            message => message.Contains("welcome", StringComparison.Ordinal)
                       && message.Contains(peerA, StringComparison.Ordinal),
            "the second peer welcome with the existing peer",
            timeout.Token);
        Check(!secondWelcome.Contains("\"name\"", StringComparison.Ordinal)
              && !secondWelcome.Contains("Bravo", StringComparison.Ordinal),
            "voice peer roster exposed a display name");
        var peerJoined = await ReceiveVoiceTextMatchingAsync(
            socketA,
            message => message.Contains("peer-joined", StringComparison.Ordinal)
                       && message.Contains(peerB, StringComparison.Ordinal),
            "the second peer join notification",
            timeout.Token);
        Check(!peerJoined.Contains("\"name\"", StringComparison.Ordinal),
            "voice peer join exposed a display name");
        var activeReadiness = await VoiceServerReadinessClient.FetchAsync(
            $"ws://127.0.0.1:{port}/voice",
            timeout.Token);
        Check(activeReadiness.ActiveRooms == 1
              && activeReadiness.ActivePeers == 2,
            "anonymous voice readiness capacity did not reflect the live room");
        var rawActiveReadiness = await http.GetStringAsync(
            $"http://127.0.0.1:{port}/ready",
            timeout.Token);
        Check(!rawActiveReadiness.Contains(room, StringComparison.Ordinal)
              && !rawActiveReadiness.Contains(peerA, StringComparison.Ordinal)
              && !rawActiveReadiness.Contains(peerB, StringComparison.Ordinal)
              && !rawActiveReadiness.Contains("Alpha", StringComparison.Ordinal)
              && !rawActiveReadiness.Contains("Bravo", StringComparison.Ordinal),
            "anonymous voice readiness leaked a room, peer, or display name");

        const string secondRoom = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string peerC = "33333333333333333333333333333333";
        socketC.Options.SetRequestHeader("Origin", "https://isley.voice.local");
        await socketC.ConnectAsync(
            new Uri($"ws://127.0.0.1:{port}/voice?room={secondRoom}&peer={peerC}"),
            timeout.Token);
        var rejectionBuffer = new byte[256];
        var capacityRejection = await socketC.ReceiveAsync(rejectionBuffer, timeout.Token);
        Check(capacityRejection.MessageType == WebSocketMessageType.Close
              && capacityRejection.CloseStatus == WebSocketCloseStatus.PolicyViolation,
            "voice server did not reject a peer after reaching global capacity");

        var signal = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "signal",
            to = peerB,
            @sealed = new
            {
                v = 1,
                iv = "AAAAAAAAAAAAAAAA",
                ciphertext = "BBBBBBBBBBBBBBBBBBBBBBBB"
            }
        });
        await socketA.SendAsync(
            signal,
            WebSocketMessageType.Text,
            true,
            timeout.Token);
        await ReceiveVoiceTextMatchingAsync(
            socketB,
            message => message.Contains(peerA, StringComparison.Ordinal)
                       && message.Contains("BBBBBBBBBBBBBBBBBBBBBBBB", StringComparison.Ordinal)
                       && !message.Contains("description", StringComparison.Ordinal)
                       && !message.Contains("offer", StringComparison.Ordinal),
            "the first peer opaque signaling envelope",
            timeout.Token);
    }
    finally
    {
        if (!host.HasExited)
        {
            host.Kill(entireProcessTree: true);
            await host.WaitForExitAsync(CancellationToken.None);
        }
        await Task.WhenAll(hostOutput, hostErrors);
    }
}

Check(VoiceIntegrationLogic.NormalizeKeyIndex(-4) == 0, "negative key index");
Check(VoiceIntegrationLogic.NormalizeKeyIndex(99) == 4, "high key index");
Check(VoiceIntegrationLogic.KeyLabel(0) == "V", "default key label");
Check(VoiceIntegrationLogic.KeyCode(0) == 0x56, "default key code");
Check(VoiceIntegrationLogic.NormalizeRangeIndex(-1) == 0, "negative voice range index");
Check(VoiceIntegrationLogic.NormalizeRangeIndex(99) == 2, "high voice range index");
Check(VoiceIntegrationLogic.Range(0).MaxDistance == 55, "close proximity range");
Check(VoiceIntegrationLogic.Range(1).Label == "NORMAL"
      && VoiceIntegrationLogic.Range(1).MaxDistance == 110, "normal proximity range");
Check(VoiceIntegrationLogic.Range(2).MaxDistance == 180, "far proximity range");
Check(VoiceIntegrationLogic.SpatialModeLabel(true) == "PROXIMITY", "proximity mode label");
Check(VoiceIntegrationLogic.SpatialModeLabel(false) == "ROOM RADIO", "private radio mode label");
Check(VoiceServerReadinessClient.TryCreateReadinessUri(
          "wss://voice.example.com/api/voice?ignored=yes",
          out var remoteReadyUri)
      && remoteReadyUri.AbsoluteUri == "https://voice.example.com/api/ready",
    "remote voice readiness URI construction");
Check(VoiceServerReadinessClient.TryCreateReadinessUri(
          "ws://127.0.0.1:5198/voice",
          out var localReadyUri)
      && localReadyUri.AbsoluteUri == "http://127.0.0.1:5198/ready",
    "local voice readiness URI construction");
Check(!VoiceServerReadinessClient.TryCreateReadinessUri(
        "ws://voice.example.com/voice",
        out _),
    "insecure remote voice readiness URI accepted");
var readinessJson = """
{
  "service": "Isley Voice Signaling",
  "status": "ready",
  "protocolVersion": 2,
  "mediaRelay": false,
  "roomIdsExposed": false,
  "positionDataReceived": false,
  "signalingPayloadsEncrypted": true,
  "displayNamesReceived": false,
  "webRtcCandidateDetailsReceived": false,
  "maxPeersPerRoom": 12,
  "maxMessageBytes": 65536,
  "maxRooms": 1024,
  "maxTotalPeers": 4096,
  "activeRooms": 3,
  "activePeers": 7
}
""";
var readiness = VoiceServerReadinessClient.Parse(readinessJson, DateTimeOffset.UnixEpoch);
Check(readiness.ActiveRooms == 3
      && readiness.ActivePeers == 7
      && readiness.CheckedAt == DateTimeOffset.UnixEpoch,
    "voice readiness parsing");
try
{
    VoiceServerReadinessClient.Parse(
        readinessJson.Replace("\"mediaRelay\": false", "\"mediaRelay\": true", StringComparison.Ordinal),
        DateTimeOffset.UnixEpoch);
    throw new InvalidOperationException("media-relaying endpoint passed the signaling-only readiness gate");
}
catch (InvalidDataException)
{
}
try
{
    VoiceServerReadinessClient.Parse(
        readinessJson.Replace("\"roomIdsExposed\": false", "\"roomIdsExposed\": true", StringComparison.Ordinal),
        DateTimeOffset.UnixEpoch);
    throw new InvalidOperationException("room-ID exposing endpoint passed the readiness gate");
}
catch (InvalidDataException)
{
}
try
{
    VoiceServerReadinessClient.Parse(
        readinessJson.Replace(
            "\"signalingPayloadsEncrypted\": true",
            "\"signalingPayloadsEncrypted\": false",
            StringComparison.Ordinal),
        DateTimeOffset.UnixEpoch);
    throw new InvalidOperationException("plaintext-signaling endpoint passed the readiness gate");
}
catch (InvalidDataException)
{
}
var readinessReady = VoiceServerReadinessClient.Present(
    VoiceServerCheckState.Ready,
    readiness);
Check(readinessReady.CanConnect
      && readinessReady.Label == "ISLEY VOICE V2 READY"
      && readinessReady.Detail.Contains("3 active rooms", StringComparison.Ordinal)
      && readinessReady.Detail.Contains("7 peers", StringComparison.Ordinal),
    "ready voice server presentation");
Check(!VoiceServerReadinessClient.Present(VoiceServerCheckState.Unchecked).CanConnect
      && VoiceServerReadinessClient.Present(VoiceServerCheckState.Checking).Detail.Contains(
          "Microphone remains off",
          StringComparison.Ordinal)
      && VoiceServerReadinessClient.Present(VoiceServerCheckState.Incompatible).Severity == 2
      && VoiceServerReadinessClient.Present(VoiceServerCheckState.Unavailable).Severity == 1,
    "fail-closed voice server presentation states");
Check(VoiceIntegrationLogic.NormalizeInputDeviceId("  device-123  ") == "device-123",
    "microphone device ID normalization");
Check(VoiceIntegrationLogic.NormalizeInputDeviceId("bad\ndevice") == string.Empty,
    "microphone device IDs reject control characters");
Check(VoiceIntegrationLogic.NormalizeInputDeviceId(new string('x', 513)) == string.Empty,
    "microphone device ID length bound");
Check(VoiceIntegrationLogic.NormalizeInputDeviceLabel("  USB   Mic  ", 0) == "USB Mic",
    "microphone label whitespace normalization");
Check(VoiceIntegrationLogic.NormalizeInputDeviceLabel(string.Empty, 2) == "Microphone 3",
    "anonymous microphone fallback label");
Check(VoiceIntegrationLogic.NormalizeOutputDeviceId("  speaker-123  ") == "speaker-123",
    "speaker device ID normalization");
Check(VoiceIntegrationLogic.NormalizeOutputDeviceId("bad\ndevice") == string.Empty,
    "speaker device IDs reject control characters");
Check(VoiceIntegrationLogic.NormalizeOutputDeviceLabel("  USB   Headset  ", 0) == "USB Headset",
    "speaker label whitespace normalization");
Check(VoiceIntegrationLogic.NormalizeOutputDeviceLabel(string.Empty, 2) == "Speaker 3",
    "anonymous speaker fallback label");
Check(VoiceIntegrationLogic.NormalizePeerId("ABCDEF0123456789ABCDEF0123456789")
      == "abcdef0123456789abcdef0123456789", "voice peer ID normalization");
Check(VoiceIntegrationLogic.NormalizePeerId("not-a-peer") == string.Empty,
    "invalid voice peer ID");
Check(VoiceIntegrationLogic.NormalizeParticipantName("  Raptor<script>  ", 0) == "Raptorscript",
    "participant name sanitization");
Check(VoiceIntegrationLogic.NormalizeParticipantName(string.Empty, 2) == "Player 3",
    "anonymous participant fallback");
Check(VoiceIntegrationLogic.NormalizeParticipantVolume(-10) == 0
      && VoiceIntegrationLogic.NormalizeParticipantVolume(140) == 100,
    "participant volume bounds");
Check(VoiceIntegrationLogic.NextParticipantVolume(100) == 75
      && VoiceIntegrationLogic.NextParticipantVolume(75) == 50
      && VoiceIntegrationLogic.NextParticipantVolume(50) == 25
      && VoiceIntegrationLogic.NextParticipantVolume(25) == 100,
    "participant volume cycle");
Check(VoiceIntegrationLogic.NormalizePeerConnectionState("connected") == "CONNECTED"
      && VoiceIntegrationLogic.NormalizePeerConnectionState("mystery") == "WAITING",
    "participant connection state normalization");
Check(VoiceIntegrationLogic.NormalizeParticipantDistance(42.4) == 40
      && VoiceIntegrationLogic.NormalizeParticipantDistance(42.6) == 45,
    "participant distance rounds to five map units");
Check(VoiceIntegrationLogic.NormalizeParticipantDistance(-1) is null
      && VoiceIntegrationLogic.NormalizeParticipantDistance(double.NaN) is null,
    "invalid participant distance is hidden");
var meterOff = VoiceIntegrationLogic.PresentMicMeter(false, true, 55, false, 0);
Check(meterOff.Label == "METER OFF" && !meterOff.Active && !meterOff.Fresh,
    "disabled microphone meter");
var meterDisconnected = VoiceIntegrationLogic.PresentMicMeter(true, false, 55, false, 0);
Check(meterDisconnected.Label == "CONNECT TO TEST" && meterDisconnected.Level == 0,
    "microphone meter requires an explicit voice connection");
var meterStale = VoiceIntegrationLogic.PresentMicMeter(true, true, 55, false, 1_201);
Check(meterStale.Label == "WAITING FOR SIGNAL" && !meterStale.Active && !meterStale.Fresh,
    "stale microphone samples must not look current");
var meterNoSignal = VoiceIntegrationLogic.PresentMicMeter(true, true, -20, false, 0);
Check(meterNoSignal.Label == "NO SIGNAL" && meterNoSignal.Level == 0 && meterNoSignal.Fresh,
    "microphone meter lower bound");
Check(VoiceIntegrationLogic.PresentMicMeter(true, true, 20, false, 0).Label == "QUIET",
    "quiet microphone band");
Check(VoiceIntegrationLogic.PresentMicMeter(true, true, 21, false, 0).Label == "CLEAR"
      && VoiceIntegrationLogic.PresentMicMeter(true, true, 72, false, 1_200).Label == "CLEAR",
    "clear microphone band and freshness boundary");
Check(VoiceIntegrationLogic.PresentMicMeter(true, true, 73, false, 0).Label == "LOUD",
    "loud microphone band");
var meterClipped = VoiceIntegrationLogic.PresentMicMeter(true, true, 140, false, 0);
Check(meterClipped.Label == "CLIPPING" && meterClipped.Level == 100 && meterClipped.Severity == 2,
    "clipping microphone band and upper bound");
Check(VoiceIntegrationLogic.PresentMicMeter(true, true, 55, true, 0).Label == "CLIPPING",
    "peak clipping overrides average level");
var qualityOff = VoiceIntegrationLogic.PresentQuality(
    false, true, 1, 1, 40, 4, 0, 0);
Check(qualityOff.Label == "OFF" && !qualityOff.Active && !qualityOff.Fresh,
    "disabled voice quality monitor");
var qualityDisconnected = VoiceIntegrationLogic.PresentQuality(
    true, false, 1, 1, 40, 4, 0, 0);
Check(qualityDisconnected.Label == "WAITING" && !qualityDisconnected.Active,
    "voice quality monitor requires a connection");
var qualitySolo = VoiceIntegrationLogic.PresentQuality(
    true, true, 0, 0, null, null, null, 0);
Check(qualitySolo.Label == "SOLO ROOM" && qualitySolo.Active && !qualitySolo.Fresh,
    "solo voice room state");
var qualityCalibrating = VoiceIntegrationLogic.PresentQuality(
    true, true, 2, 0, null, null, null, 0);
Check(qualityCalibrating.Label == "CALIBRATING"
      && qualityCalibrating.Detail.Contains("2 peers", StringComparison.Ordinal),
    "voice quality calibration state");
var qualityExcellent = VoiceIntegrationLogic.PresentQuality(
    true, true, 2, 2, 139.9, 19.9, 0.9, 8_000);
Check(qualityExcellent.Label == "EXCELLENT"
      && qualityExcellent.Severity == 0
      && qualityExcellent.Fresh
      && qualityExcellent.Detail.Contains("140 MS RTT", StringComparison.Ordinal)
      && qualityExcellent.Detail.Contains("0.9% LOSS", StringComparison.Ordinal),
    "excellent voice quality and detail formatting");
Check(VoiceIntegrationLogic.PresentQuality(
        true, true, 1, 1, 140, 20, 1, 0).Label == "GOOD",
    "good voice quality threshold");
var qualityWeak = VoiceIntegrationLogic.PresentQuality(
    true, true, 1, 1, 250, 40, 3, 0);
Check(qualityWeak.Label == "WEAK" && qualityWeak.Severity == 1,
    "weak voice quality threshold");
var qualityPoor = VoiceIntegrationLogic.PresentQuality(
    true, true, 1, 1, 500, 80, 8, 0);
Check(qualityPoor.Label == "POOR" && qualityPoor.Severity == 2,
    "poor voice quality threshold");
var qualityStale = VoiceIntegrationLogic.PresentQuality(
    true, true, 1, 1, 20, 2, 0, 8_001);
Check(qualityStale.Label == "CALIBRATING" && !qualityStale.Fresh,
    "stale voice quality cannot look current");
var qualityInvalid = VoiceIntegrationLogic.PresentQuality(
    true, true, 1, 1, double.NaN, -1, double.PositiveInfinity, 0);
Check(qualityInvalid.Label == "CALIBRATING" && !qualityInvalid.Fresh,
    "invalid voice quality metrics fail closed");
Check(VoiceIntegrationLogic.ResolveState(false, true, false, false) == VoiceBridgeState.Disabled, "disabled state");
Check(VoiceIntegrationLogic.ResolveState(true, false, false, false) == VoiceBridgeState.Ready, "ready state");
Check(VoiceIntegrationLogic.ResolveState(true, false, true, false) == VoiceBridgeState.Connecting, "connecting state");
Check(VoiceIntegrationLogic.ResolveState(true, true, false, false) == VoiceBridgeState.Connected, "connected state");
Check(VoiceIntegrationLogic.ResolveState(true, false, false, true) == VoiceBridgeState.Error, "error state");
Check(!VoiceIntegrationLogic.CanTransmit(true, true, false, true), "unrelated apps must not transmit");
Check(!VoiceIntegrationLogic.CanTransmit(true, false, true, true), "disconnected engine must not transmit");
Check(VoiceIntegrationLogic.CanTransmit(true, true, true, true), "allowed PTT state");
Check(!VoiceIntegrationLogic.HasPttIntent(false, true, true), "disabled PTT intent");
Check(!VoiceIntegrationLogic.HasPttIntent(true, false, true), "unrelated app PTT intent");
Check(VoiceIntegrationLogic.HasPttIntent(true, true, true), "built-in voice PTT intent");
var observedHeld = VoiceIntegrationLogic.ResolveObservedKeyState(false, keyDown: true, keyUp: false);
Check(observedHeld, "PTT key-down edge");
observedHeld = VoiceIntegrationLogic.ResolveObservedKeyState(observedHeld, keyDown: false, keyUp: true);
Check(!observedHeld, "rapid PTT key-up edge must clear the held state");

var ready = VoiceIntegrationLogic.Present(true, true, false, false, true, false, true, false, 0, 3);
Check(ready.Heading == "ISLEY VOICE READY", "ready heading");
Check(ready.Detail == "HOLD V · 3 IN ROOM", "ready detail");
Check(ready.ShowHud && !ready.Transmitting, "ready HUD state");

var transmitting = VoiceIntegrationLogic.Present(true, true, false, false, true, true, true, false, 0, 3);
Check(transmitting.Transmitting, "transmit state");
Check(transmitting.Heading == "PTT LIVE · ISLEY VOICE", "honest active PTT heading");

var privateView = VoiceIntegrationLogic.Present(true, true, false, false, true, true, true, true, 0, 3);
Check(!privateView.ShowHud, "streamer mode must hide voice HUD");

Check(VoiceRelayLogic.TryCreate(
        "turns:Relay.Example.com:5349?transport=tcp",
        "temporary-user",
        "temporary-credential",
        out var relay,
        out _),
    "valid session TURN relay configuration");
Check(relay.Url == "turns:relay.example.com:5349?transport=tcp", "TURN URL normalization");
Check(!VoiceRelayLogic.TryCreate("https://relay.example.com", "user", "credential", out _, out _),
    "relay URL must use TURN or TURNS");
Check(!VoiceRelayLogic.TryCreate("turn:relay.example.com:70000", "user", "credential", out _, out _),
    "relay port range");
Check(!VoiceRelayLogic.TryCreate("turn:relay.example.com", "user", "bad\ncredential", out _, out _),
    "relay credentials reject control characters");

Check(VoiceInviteLogic.TryCreate(
        "wss://voice.example.com/voice",
        "abcdef0123456789abcdef01",
        out var remoteInviteText,
        out var remoteInviteWarning),
    "remote voice invite creation");
Check(string.IsNullOrEmpty(remoteInviteWarning)
      && remoteInviteText.StartsWith($"{VoiceInviteLogic.Prefix}|", StringComparison.Ordinal),
    "remote voice invite format");
Check(VoiceInviteLogic.TryParse(
        remoteInviteText,
        "ws://127.0.0.1:5198/voice",
        out var remoteInvite,
        out _)
      && remoteInvite.ServerUrl == "wss://voice.example.com/voice"
      && remoteInvite.RoomSecret == "abcdef0123456789abcdef01"
      && !remoteInvite.LocalOnly
      && !remoteInvite.LegacyKeyOnly,
    "remote voice invite round trip");
Check(VoiceInviteLogic.TryCreate(
        "ws://127.0.0.1:5198/voice",
        "abcdef0123456789abcdef01",
        out var localInviteText,
        out var localInviteWarning)
      && localInviteWarning.Contains("SAME PC", StringComparison.Ordinal)
      && VoiceInviteLogic.TryParse(localInviteText, string.Empty, out var localInvite, out _)
      && localInvite.LocalOnly,
    "local-only voice invite warning");
Check(VoiceInviteLogic.TryParse(
        "ABCDEF0123456789ABCDEF01",
        "wss://voice.example.com/voice",
        out var legacyInvite,
        out _)
      && legacyInvite.LegacyKeyOnly
      && legacyInvite.RoomSecret == "abcdef0123456789abcdef01",
    "legacy copied room key compatibility");
Check(!VoiceInviteLogic.TryCreate(
        "ws://voice.example.com/voice",
        "abcdef0123456789abcdef01",
        out _, out _),
    "remote plaintext websocket refusal");
Check(!VoiceInviteLogic.TryCreate(
        "wss://user:secret@voice.example.com/voice",
        "abcdef0123456789abcdef01",
        out _, out _),
    "voice invite user-info refusal");
Check(!VoiceInviteLogic.TryParse(
        "ISLEY-VOICE/1|%%%|abcdef0123456789abcdef01",
        "wss://voice.example.com/voice",
        out _, out _),
    "malformed voice invite refusal");
Check(!VoiceInviteLogic.TryParse(
        new string('x', VoiceInviteLogic.MaximumInviteCharacters + 1),
        "wss://voice.example.com/voice",
        out _, out var oversizedInviteError)
      && oversizedInviteError == "VOICE INVITE TOO LARGE",
    "oversized voice invite refusal");

Check(VoiceRouteOfferLogic.TryParseRoute(
        "Isley route | -32157.28, -149546.43 > -32000, -149000 | 492.8 MU planned",
        out var sharedRoute,
        out _)
      && sharedRoute.Kind == "ROUTE"
      && sharedRoute.StopCount == 2
      && sharedRoute.PlannedDistance == 492.8,
    "voice route offer parsing");
Check(VoiceRouteOfferLogic.TryParseRoute(
        "Isley road/trail course | -100, 200 > -50, 250 > 0, 300",
        out var terrainSharedRoute,
        out _)
      && terrainSharedRoute.Kind == "ROAD / TRAIL"
      && terrainSharedRoute.StopCount == 3
      && terrainSharedRoute.PlannedDistance is null,
    "voice terrain route parsing");
Check(!VoiceRouteOfferLogic.TryParseRoute(
        "Isley route | -100, 200",
        out _,
        out _),
    "voice route requires at least two stops");
Check(!VoiceRouteOfferLogic.TryParseRoute(
        $"Isley route | {string.Join(" > ", Enumerable.Range(0, 13).Select(index => $"{index}, {index}"))}",
        out _,
        out _),
    "voice route stop bound");
Check(!VoiceRouteOfferLogic.TryParseRoute(
        "Isley route | -100, 200 > -50, 250\nhidden",
        out _,
        out _),
    "voice route control-character refusal");
Check(!VoiceRouteOfferLogic.TryParseRoute(
        "Isley route | 10000001, 0 > 0, 0",
        out _,
        out _),
    "voice route coordinate bound");
Check(VoiceRouteOfferLogic.TryCreateIncoming(
        "ABCDEF0123456789ABCDEF01",
        "ABCDEF0123456789ABCDEF0123456789",
        "  Pack   Lead  ",
        sharedRoute.Text,
        DateTimeOffset.UnixEpoch,
        out var routeOffer,
        out _)
      && routeOffer.OfferId == "abcdef0123456789abcdef01"
      && routeOffer.PeerId == "abcdef0123456789abcdef0123456789"
      && routeOffer.PeerName == "Pack Lead",
    "incoming voice route offer normalization");
Check(!VoiceRouteOfferLogic.TryCreateIncoming(
        "bad-offer",
        routeOffer.PeerId,
        "Pack Lead",
        sharedRoute.Text,
        DateTimeOffset.UnixEpoch,
        out _,
        out _),
    "voice route offer ID validation");
Check(!VoiceRouteOfferLogic.IsExpired(
          routeOffer,
          DateTimeOffset.UnixEpoch.Add(VoiceRouteOfferLogic.OfferLifetime).AddTicks(-1))
      && VoiceRouteOfferLogic.IsExpired(
          routeOffer,
          DateTimeOffset.UnixEpoch.Add(VoiceRouteOfferLogic.OfferLifetime)),
    "voice route offer expiry boundary");
Check(VoiceRouteOfferLogic.RemainingSeconds(
          routeOffer,
          DateTimeOffset.UnixEpoch.AddSeconds(1)) == 119
      && VoiceRouteOfferLogic.Summary(routeOffer, streamerMode: true).StartsWith(
          "PLAYER · ROUTE · 2 STOPS",
          StringComparison.Ordinal)
      && !VoiceRouteOfferLogic.Summary(routeOffer, streamerMode: true).Contains(
          routeOffer.PeerName,
          StringComparison.Ordinal),
    "voice route offer timer and streamer identity redaction");

var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var mainWindowXaml = File.ReadAllText(Path.Combine(repositoryRoot, "BurntHud", "MainWindow.xaml"));
var mainWindowSource = string.Join("\n", Directory.GetFiles(Path.Combine(repositoryRoot, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var voiceHtmlSource = File.ReadAllText(Path.Combine(repositoryRoot, "BurntHud", "Voice", "voice.html"));
var voiceClientSource = File.ReadAllText(Path.Combine(repositoryRoot, "BurntHud", "Voice", "voice.js"));
var voiceCryptoSource = File.ReadAllText(Path.Combine(repositoryRoot, "BurntHud", "Voice", "voice-crypto.js"));
var voiceInviteSource = File.ReadAllText(Path.Combine(repositoryRoot, "BurntHud", "VoiceInviteLogic.cs"));
var voiceRouteOfferSource = File.ReadAllText(Path.Combine(repositoryRoot, "BurntHud", "VoiceRouteOfferLogic.cs"));
var voiceReadinessSource = File.ReadAllText(Path.Combine(
    repositoryRoot,
    "BurntHud",
    "VoiceServerReadinessClient.cs"));
var voiceServerSource = File.ReadAllText(Path.Combine(repositoryRoot, "Isley.VoiceServer", "Program.cs"));
Check(!mainWindowXaml.Contains("IsleVOIP", StringComparison.OrdinalIgnoreCase)
      && !mainWindowXaml.Contains("Mumble", StringComparison.OrdinalIgnoreCase),
    "The active voice UI must not depend on the legacy external clients");
Check(mainWindowXaml.Contains("VoiceTurnRelayButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceTurnCredentialInputBox", StringComparison.Ordinal)
      && mainWindowXaml.Contains("PasswordBox", StringComparison.Ordinal),
    "The voice workspace must expose an optional masked relay configuration");
Check(mainWindowXaml.Contains("VoiceServerCheckButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceServerCheckStatusText", StringComparison.Ordinal)
      && mainWindowXaml.Contains("CHECK SERVER", StringComparison.Ordinal)
      && mainWindowXaml.Contains(
          "before Isley may request microphone permission",
          StringComparison.Ordinal),
    "The voice workspace must expose an accessible pre-microphone server readiness check");
Check(mainWindowXaml.Contains("VoiceSpatialModeButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceRangeButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceEchoCancellationButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceNoiseSuppressionButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceAutoGainButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Room radio sends no positions", StringComparison.Ordinal),
    "The voice workspace must expose clear spatial privacy, range, and microphone processing controls");
Check(mainWindowXaml.Contains("VoiceInputDeviceComboBox", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceInputDeviceRefreshButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("MICROPHONE INPUT", StringComparison.Ordinal)
      && mainWindowXaml.Contains("switching mutes PTT", StringComparison.Ordinal),
    "The voice workspace must expose a compact, clearly private microphone selector");
Check(mainWindowXaml.Contains("VoiceOutputDeviceComboBox", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceOutputDeviceRefreshButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("SPEAKER OUTPUT", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Session-only speaker selection", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Isley speaker output", StringComparison.Ordinal),
    "The voice workspace must expose a compact, accessible, session-only speaker selector");
Check(mainWindowXaml.Contains("VoiceMicMeterButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceMicLevelBar", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Local input only", StringComparison.Ordinal)
      && mainWindowXaml.Contains("no playback, recording, room, or server data", StringComparison.Ordinal),
    "The voice workspace must expose a slim, toggleable, plainly local microphone signal rail");
Check(mainWindowXaml.Contains("VoiceQualityStateText", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceQualityButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VOICE QUALITY", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Coarse quality from encrypted peer WebRTC statistics", StringComparison.Ordinal),
    "The voice workspace must expose one compact, accessible, toggleable quality row");
Check(mainWindowXaml.Contains("VoiceParticipantListPanel", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceParticipantEmptyText", StringComparison.Ordinal)
      && mainWindowXaml.Contains("TALKING follows peer PTT", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Proximity shows rounded distance", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Streamer Mode hides player names", StringComparison.Ordinal),
    "The voice workspace must expose a compact session-only participant roster with clear activity, distance, and privacy copy");
Check(mainWindowXaml.Contains("VoiceJoinInviteButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("COPY INVITE", StringComparison.Ordinal)
      && mainWindowXaml.Contains("PASTE INVITE TO JOIN", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceRoomInviteStatusText", StringComparison.Ordinal)
      && mainWindowXaml.Contains("IsReadOnly=\"True\"", StringComparison.Ordinal),
    "The voice workspace must expose a clear invite flow while keeping the room key read-only");
Check(mainWindowXaml.Contains("VoiceShareRouteButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceRouteOfferPanel", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceRouteOfferAcceptButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VoiceRouteOfferDeclineButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Recipients must accept explicitly", StringComparison.Ordinal)
      && mainWindowXaml.Contains("cannot auto-start navigation", StringComparison.Ordinal),
    "The voice workspace must expose an explicit sender action and explicit receiver consent without automatic navigation");
Check(mainWindowSource.Contains("PasteVoiceInviteFromClipboardAsync", StringComparison.Ordinal)
      && mainWindowSource.Contains("Clipboard.ContainsText(TextDataFormat.UnicodeText)", StringComparison.Ordinal)
      && mainWindowSource.Contains("clipboardText = string.Empty", StringComparison.Ordinal)
      && mainWindowSource.Contains("PrepareVoiceRoomChange", StringComparison.Ordinal)
      && mainWindowSource.Contains("MICROPHONE STILL OFF", StringComparison.Ordinal)
      && mainWindowSource.Contains("Paste Isley Voice invite", StringComparison.Ordinal),
    "Voice joining must be explicit, clipboard-bounded, state-clearing, microphone-off, and keyboard discoverable");
Check(voiceInviteSource.Contains("ISLEY-VOICE/1", StringComparison.Ordinal)
      && voiceInviteSource.Contains("MaximumInviteCharacters = 512", StringComparison.Ordinal)
      && voiceInviteSource.Contains("RoomSecretCharacters = 24", StringComparison.Ordinal)
      && voiceInviteSource.Contains("!string.IsNullOrEmpty(uri.UserInfo)", StringComparison.Ordinal)
      && voiceInviteSource.Contains("string.Equals(uri.Scheme, \"wss\"", StringComparison.Ordinal)
      && voiceInviteSource.Contains("LegacyKeyOnly", StringComparison.Ordinal),
    "Voice invites must be versioned, bounded, secret-validated, transport-restricted, and backward compatible");
Check(!Regex.IsMatch(mainWindowSource, @"public\s+(?:string|bool)\s+VoiceTurn(?:Url|Username|Credential|RelayEnabled)\s*\{"),
    "TURN relay configuration and credentials must never be persisted in MapperSettings");
Check(!Regex.IsMatch(mainWindowSource, @"public\s+string\s+Voice(?:Input)?DeviceId\s*\{"),
    "Microphone hardware identifiers must remain session-only and never enter MapperSettings");
Check(!Regex.IsMatch(mainWindowSource, @"public\s+string\s+VoiceOutputDeviceId\s*\{"),
    "Speaker hardware identifiers must remain session-only and never enter MapperSettings");
Check(!Regex.IsMatch(mainWindowSource, @"public\s+.*VoiceParticipant.*\s*\{"),
    "Voice participant identities and preferences must never enter MapperSettings");
Check(!Regex.IsMatch(mainWindowSource, @"public\s+.*VoiceRoute.*\s*\{"),
    "Voice route offers must remain session-only and never enter MapperSettings");
Check(!Regex.IsMatch(
        mainWindowSource,
        @"public\s+(?:double|float|decimal|int|long|string)\??\s+VoiceQuality",
        RegexOptions.IgnoreCase),
    "Voice quality measurements must remain session-only and never enter MapperSettings");
Check(voiceRouteOfferSource.Contains("MaximumRouteCharacters = 1600", StringComparison.Ordinal)
      && voiceRouteOfferSource.Contains("MaximumStopCount = 12", StringComparison.Ordinal)
      && voiceRouteOfferSource.Contains("TimeSpan.FromMinutes(2)", StringComparison.Ordinal)
      && voiceRouteOfferSource.Contains("routeText.Any(char.IsControl)", StringComparison.Ordinal)
      && voiceRouteOfferSource.Contains("Math.Abs(x) > 10_000_000", StringComparison.Ordinal),
    "Voice route offers must be bounded by size, stops, lifetime, control characters, and coordinate range");
var receiveRouteOfferStart = mainWindowSource.IndexOf(
    "private void ReadVoiceRouteOffer",
    StringComparison.Ordinal);
var receiveRouteOfferEnd = mainWindowSource.IndexOf(
    "private void ReadVoiceRouteSent",
    receiveRouteOfferStart,
    StringComparison.Ordinal);
Check(receiveRouteOfferStart >= 0
      && receiveRouteOfferEnd > receiveRouteOfferStart
      && !mainWindowSource[receiveRouteOfferStart..receiveRouteOfferEnd].Contains(
          "startSharedRouteText",
          StringComparison.Ordinal)
      && mainWindowSource.Contains("VoiceRouteOfferAcceptButton_Click", StringComparison.Ordinal)
      && mainWindowSource.Contains("startSharedRouteText", StringComparison.Ordinal)
      && mainWindowSource.Contains("ClearVoiceRouteOffer", StringComparison.Ordinal),
    "Receiving a voice route must only stage a session offer; the explicit Accept handler owns route activation");
Check(mainWindowSource.Contains("_voicePermissionArmed", StringComparison.Ordinal)
      && mainWindowSource.Contains("SavesInProfile = false", StringComparison.Ordinal)
      && mainWindowSource.Contains("CoreWebView2PermissionKind.Microphone", StringComparison.Ordinal)
      && mainWindowSource.Contains("ClearVoiceMicrophonePermissionArmAsync", StringComparison.Ordinal)
      && mainWindowSource.Contains("TimeSpan.FromSeconds(10)", StringComparison.Ordinal),
    "Microphone permission must be native-user-armed, non-persistent, and time-bounded");
var readinessGateStart = mainWindowSource.IndexOf(
    "var serverReady = IsBundledLocalVoiceServerUrl",
    StringComparison.Ordinal);
var microphoneArmAfterGate = mainWindowSource.IndexOf(
    "ArmVoiceMicrophonePermission();",
    readinessGateStart,
    StringComparison.Ordinal);
Check(readinessGateStart >= 0
      && microphoneArmAfterGate > readinessGateStart
      && mainWindowSource.Contains(
          "VOICE SERVER NOT READY · MICROPHONE KEPT OFF",
          StringComparison.Ordinal)
      && mainWindowSource.Contains(
          "VoiceServerReadinessClient.FetchAsync",
          StringComparison.Ordinal)
      && mainWindowSource.Contains(
          "_voiceServerReadinessCancellation?.Cancel()",
          StringComparison.Ordinal),
    "Voice connection must complete a cancellable readiness check before microphone permission");
Check(mainWindowSource.Contains(
          "EnsureBundledVoiceHostReadyAsync(showToast: false)",
          StringComparison.Ordinal)
      && mainWindowSource.Contains(
          "Process.Start(new ProcessStartInfo(hostExecutable)",
          StringComparison.Ordinal)
      && mainWindowSource.Contains(
          "CheckVoiceServerReadinessAsync(userInitiated: false, startupAttempts: 20)",
          StringComparison.Ordinal)
      && mainWindowXaml.Contains(
          "BUILT-IN HOST STARTS AUTOMATICALLY",
          StringComparison.Ordinal)
      && mainWindowXaml.Contains(
          "Content=\"Start voice\"",
          StringComparison.Ordinal),
    "Start Voice must automatically launch and verify the bundled local host when needed");
Check(mainWindowSource.Contains("public bool VoiceAutoOpen { get; set; } = true", StringComparison.Ordinal)
      && mainWindowSource.Contains("_voiceAutoOpen = settings.VoiceAutoOpen", StringComparison.Ordinal)
      && mainWindowSource.Contains("TryAutoConnectProximityVoiceAsync", StringComparison.Ordinal)
      && mainWindowSource.Contains("ConnectVoiceSessionAsync", StringComparison.Ordinal)
      && mainWindowSource.Contains("SyncProximityVoiceLobbyAsync", StringComparison.Ordinal)
      && mainWindowSource.Contains("VoiceProximityRoomLogic.TryResolveAutoRoomSecret", StringComparison.Ordinal)
      && mainWindowSource.Contains("PROXIMITY VOICE · AUTO CONNECTING", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"VoiceAutoOpenButton\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Auto proximity · On", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Proximity voice auto-connects", StringComparison.Ordinal),
    "Proximity voice must default to automatic connect with an explicit opt-out control");
Check(File.Exists(Path.Combine(repositoryRoot, "BurntHud", "VoiceProximityRoomLogic.cs")),
    "Server proximity lobby derivation must live in VoiceProximityRoomLogic");
var proximityRoomSource = File.ReadAllText(
    Path.Combine(repositoryRoot, "BurntHud", "VoiceProximityRoomLogic.cs"));
var derivedA = VoiceProximityRoomLogic.DeriveServerProximityRoomSecret("alpha-server");
var derivedB = VoiceProximityRoomLogic.DeriveServerProximityRoomSecret("alpha-server");
var derivedC = VoiceProximityRoomLogic.DeriveServerProximityRoomSecret("beta-server");
Check(derivedA.Length == VoiceInviteLogic.RoomSecretCharacters
      && string.Equals(derivedA, derivedB, StringComparison.Ordinal)
      && !string.Equals(derivedA, derivedC, StringComparison.Ordinal)
      && VoiceInviteLogic.TryNormalizeRoomSecret(derivedA, out _)
      && proximityRoomSource.Contains("isley-voice-proximity-v1|", StringComparison.Ordinal),
    "Live Network servers must map to a stable normalized proximity room secret");
Check(voiceReadinessSource.Contains("AllowAutoRedirect = false", StringComparison.Ordinal)
      && voiceReadinessSource.Contains("MaximumPayloadBytes = 32 * 1024", StringComparison.Ordinal)
      && voiceReadinessSource.Contains("roomIdsExposed", StringComparison.OrdinalIgnoreCase)
      && voiceReadinessSource.Contains("positionDataReceived", StringComparison.OrdinalIgnoreCase)
      && voiceReadinessSource.Contains("signalingPayloadsEncrypted", StringComparison.OrdinalIgnoreCase)
      && voiceReadinessSource.Contains("displayNamesReceived", StringComparison.OrdinalIgnoreCase)
      && voiceReadinessSource.Contains("webRtcCandidateDetailsReceived", StringComparison.OrdinalIgnoreCase)
      && voiceReadinessSource.Contains("microphone kept off", StringComparison.OrdinalIgnoreCase)
      && voiceReadinessSource.Contains("ISLEY VOICE V2 READY", StringComparison.Ordinal),
    "Voice readiness must be bounded, redirect-safe, privacy-declaring, and fail closed");
Check(mainWindowSource.Contains("VoiceProximityEnabled = _voiceProximityEnabled", StringComparison.Ordinal)
      && mainWindowSource.Contains("VoiceRangeIndex = _voiceRangeIndex", StringComparison.Ordinal)
      && mainWindowSource.Contains("VoiceMicMeterEnabled = _voiceMicMeterEnabled", StringComparison.Ordinal)
      && mainWindowSource.Contains("VoiceQualityMonitorEnabled = _voiceQualityMonitorEnabled", StringComparison.Ordinal)
      && mainWindowSource.Contains("proximityMaxDistance = VoiceIntegrationLogic.Range", StringComparison.Ordinal)
      && mainWindowSource.Contains("micMeterEnabled = _voiceMicMeterEnabled", StringComparison.Ordinal)
      && mainWindowSource.Contains("qualityMonitorEnabled = _voiceQualityMonitorEnabled", StringComparison.Ordinal)
      && mainWindowSource.Contains("type == \"voice-meter\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("type == \"voice-quality\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("type = \"preferences\"", StringComparison.Ordinal),
    "Spatial privacy, range, and non-secret microphone and quality preferences must flow through settings and the voice bridge");
Check(mainWindowSource.Contains("type = \"participant-settings\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("participant.VolumePercent / 100d", StringComparison.Ordinal)
      && mainWindowSource.Contains("_streamerMode ? $\"PLAYER {index + 1}\"", StringComparison.Ordinal),
    "Per-player mute and volume must remain local and participant names must anonymize in Streamer Mode");
Check(mainWindowSource.Contains("remoteSpeakerCount", StringComparison.Ordinal)
      && mainWindowSource.Contains("participant.Talking", StringComparison.Ordinal)
      && mainWindowSource.Contains("held = localTransmit", StringComparison.Ordinal)
      && !mainWindowSource.Contains("held = voiceActive", StringComparison.Ordinal),
    "Remote activity may wake the HUD but must never be mistaken for local microphone transmission");
var voiceConnectStart = voiceClientSource.IndexOf("const connect = async", StringComparison.Ordinal);
var encryptionBeforeMicrophone = voiceConnectStart >= 0
    ? voiceClientSource.IndexOf(
        "signalingKey = await voiceCrypto.deriveSignalKey",
        voiceConnectStart,
        StringComparison.Ordinal)
    : -1;
var microphoneAfterEncryption = encryptionBeforeMicrophone >= 0
    ? voiceClientSource.IndexOf(
        "navigator.mediaDevices.getUserMedia",
        encryptionBeforeMicrophone,
        StringComparison.Ordinal)
    : -1;
Check(voiceConnectStart >= 0
      && encryptionBeforeMicrophone > voiceConnectStart
      && microphoneAfterEncryption > encryptionBeforeMicrophone,
    "The room encryption key must be ready before Isley may request microphone access");
Check(voiceHtmlSource.IndexOf("voice-crypto.js", StringComparison.Ordinal)
          < voiceHtmlSource.IndexOf("voice.js", StringComparison.Ordinal)
      && voiceCryptoSource.Contains("MaximumPlaintextBytes = 32 * 1024", StringComparison.Ordinal)
      && voiceCryptoSource.Contains("name: 'AES-GCM'", StringComparison.Ordinal)
      && voiceCryptoSource.Contains("getRandomValues(new Uint8Array(12))", StringComparison.Ordinal)
      && voiceCryptoSource.Contains("additionalData", StringComparison.Ordinal)
      && voiceCryptoSource.Contains("Object.keys(envelope).sort()", StringComparison.Ordinal)
      && voiceCryptoSource.Contains("normalizeSignalPayload", StringComparison.Ordinal)
      && voiceClientSource.Contains("sendEncryptedSignal", StringComparison.Ordinal)
      && voiceClientSource.Contains("handleEncryptedSignal", StringComparison.Ordinal)
      && voiceClientSource.Contains("voiceCrypto.sealSignal", StringComparison.Ordinal)
      && voiceClientSource.Contains("voiceCrypto.openSignal", StringComparison.Ordinal)
      && voiceClientSource.Contains("type: 'profile', name: displayName", StringComparison.Ordinal)
      && !voiceClientSource.Contains("parsed.searchParams.set('name'", StringComparison.Ordinal)
      && !voiceClientSource.Contains(
          "send({ type: 'signal', to: id, data:",
          StringComparison.Ordinal),
    "Voice signaling must be bounded, room-key encrypted, tamper-evident, and display-name-free at the broker");
Check(voiceClientSource.Contains("getUserMedia", StringComparison.Ordinal)
      && voiceClientSource.Contains("track.enabled = false", StringComparison.Ordinal)
      && voiceClientSource.Contains("stun:stun.cloudflare.com:3478", StringComparison.Ordinal)
      && voiceClientSource.Contains("createDataChannel('isley-position'", StringComparison.Ordinal)
      && voiceClientSource.Contains("TURN RELAY", StringComparison.Ordinal)
      && voiceClientSource.Contains("selectedCandidatePairId", StringComparison.Ordinal)
      && voiceClientSource.Contains("pendingCandidates", StringComparison.Ordinal)
      && voiceClientSource.Contains("remoteDescription", StringComparison.Ordinal)
      && voiceClientSource.Contains("validTurnConfig", StringComparison.Ordinal)
      && voiceClientSource.Contains("iceServers.push(turnRelay)", StringComparison.Ordinal)
      && voiceClientSource.Contains("iceServers = [];", StringComparison.Ordinal)
      && voiceClientSource.Contains("if (!proximityEnabled)", StringComparison.Ordinal)
      && voiceClientSource.Contains("sharedPosition = proximityEnabled ? localPosition : null", StringComparison.Ordinal)
      && voiceClientSource.Contains("track.applyConstraints", StringComparison.Ordinal)
      && voiceClientSource.Contains("audioConstraints(selectedInputDeviceId)", StringComparison.Ordinal)
      && voiceClientSource.Contains("enumerateDevices", StringComparison.Ordinal)
      && voiceClientSource.Contains("sender.replaceTrack(nextTrack)", StringComparison.Ordinal)
      && voiceClientSource.Contains("nextTrack.enabled = false", StringComparison.Ordinal)
      && voiceClientSource.Contains("setPtt(false)", StringComparison.Ordinal)
      && voiceClientSource.Contains("'devicechange'", StringComparison.Ordinal)
      && voiceClientSource.Contains("post('voice-participants'", StringComparison.Ordinal)
      && voiceClientSource.Contains("peer.manualGain", StringComparison.Ordinal)
      && voiceClientSource.Contains("peer.muted", StringComparison.Ordinal)
      && voiceClientSource.Contains("attenuate(distance) * manualGain", StringComparison.Ordinal)
      && voiceClientSource.Contains("applyParticipantSettings", StringComparison.Ordinal)
      && voiceClientSource.Contains("type: 'ptt', transmitting", StringComparison.Ordinal)
      && voiceClientSource.Contains("peer.talking = Boolean(message.transmitting)", StringComparison.Ordinal)
      && voiceClientSource.Contains("Math.round(rawDistance / 5) * 5", StringComparison.Ordinal)
      && voiceClientSource.Contains("if (changed) broadcastPtt()", StringComparison.Ordinal)
      && voiceClientSource.Contains("sourceTrack.clone()", StringComparison.Ordinal)
      && voiceClientSource.Contains("createAnalyser()", StringComparison.Ordinal)
      && voiceClientSource.Contains("getByteTimeDomainData", StringComparison.Ordinal)
      && voiceClientSource.Contains("micMeterSource.connect(analyser)", StringComparison.Ordinal)
      && voiceClientSource.Contains("micMeterTrack?.stop", StringComparison.Ordinal)
      && voiceClientSource.Contains("post('voice-meter'", StringComparison.Ordinal)
      && voiceClientSource.Contains("}, 125);", StringComparison.Ordinal)
      && voiceClientSource.Contains("post('voice-quality'", StringComparison.Ordinal)
      && voiceClientSource.Contains("currentRoundTripTime", StringComparison.Ordinal)
      && voiceClientSource.Contains("report.jitter", StringComparison.Ordinal)
      && voiceClientSource.Contains("report.packetsLost", StringComparison.Ordinal)
      && voiceClientSource.Contains("qualityTimer = setInterval", StringComparison.Ordinal)
      && voiceClientSource.Contains("reportVoiceQuality(), 3000)", StringComparison.Ordinal)
      && voiceClientSource.Contains("Math.max(...roundTrips)", StringComparison.Ordinal)
      && !Regex.IsMatch(
          voiceClientSource,
          @"post\s*\(\s*['""]voice-quality['""]\s*,\s*\{[^}]*\bpeer\s*:",
          RegexOptions.IgnoreCase | RegexOptions.Singleline)
      && voiceClientSource.Contains("command.type === 'send-route-offer'", StringComparison.Ordinal)
      && voiceClientSource.Contains("message.type === 'route-offer'", StringComparison.Ordinal)
      && voiceClientSource.Contains("post('voice-route-offer'", StringComparison.Ordinal)
      && voiceClientSource.Contains("event.data.length > 2048", StringComparison.Ordinal)
      && voiceClientSource.Contains("now - Number(peerRouteOfferAt.get(id) || 0) < 3000", StringComparison.Ordinal)
      && voiceClientSource.Contains("now - seenAt > 120000", StringComparison.Ordinal)
      && !voiceClientSource.Contains("connect(micMeterContext.destination", StringComparison.Ordinal)
      && !Regex.IsMatch(
          voiceClientSource,
          @"send\s*\(\s*\{[^}]*type\s*:\s*['""]voice-meter",
          RegexOptions.IgnoreCase | RegexOptions.Singleline),
    "The built-in client must use muted-by-default WebRTC, optional NAT discovery and session relay, a peer-only position and PTT channel with a no-position radio mode, local per-peer controls, coarse distance, live microphone processing, aggregate privacy-safe path and quality diagnostics, and race-safe ICE queuing");
Check(mainWindowSource.Contains("outputDeviceId = _voiceSelectedOutputDeviceId", StringComparison.Ordinal)
      && mainWindowSource.Contains("type = \"switch-output\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("type == \"voice-output-device\"", StringComparison.Ordinal)
      && voiceClientSource.Contains("device.kind === 'audiooutput'", StringComparison.Ordinal)
      && voiceClientSource.Contains("typeof HTMLMediaElement.prototype.setSinkId", StringComparison.Ordinal)
      && voiceClientSource.Contains("await audio.setSinkId(normalized)", StringComparison.Ordinal)
      && voiceClientSource.Contains(".slice(0, 16)", StringComparison.Ordinal)
      && voiceClientSource.Contains("command.type === 'switch-output'", StringComparison.Ordinal)
      && voiceClientSource.Contains("outputDeviceSwitchRevision", StringComparison.Ordinal)
      && voiceClientSource.Contains("await applyOutputDevice(audio, previousDeviceId)", StringComparison.Ordinal)
      && voiceClientSource.Contains("outputSelectionSupported: supportsOutputSelection()", StringComparison.Ordinal),
    "Speaker output selection must be bounded, session-only, live-applied to peer audio, race-safe, rollback-safe, and honest when unsupported");
Check(voiceServerSource.Contains("type != \"signal\"", StringComparison.Ordinal)
      && voiceServerSource.Contains("MaxMessagesPerTenSeconds", StringComparison.Ordinal)
      && voiceServerSource.Contains("AllowedOrigins", StringComparison.Ordinal)
      && voiceServerSource.Contains("^[a-f0-9]{64}$", StringComparison.Ordinal)
      && voiceServerSource.Contains("ValidateOnStart", StringComparison.Ordinal)
      && voiceServerSource.Contains("MaxRooms", StringComparison.Ordinal)
      && voiceServerSource.Contains("MaxTotalPeers", StringComparison.Ordinal)
      && voiceServerSource.Contains("AllowedOrigins { get; init; } = []", StringComparison.Ordinal)
      && voiceServerSource.Contains("app.MapGet(\"/ready\"", StringComparison.Ordinal)
      && voiceServerSource.Contains("ProtocolVersion = 2", StringComparison.Ordinal)
      && voiceServerSource.Contains("RoomIdsExposed", StringComparison.Ordinal)
      && voiceServerSource.Contains("PositionDataReceived", StringComparison.Ordinal)
      && voiceServerSource.Contains("SignalingPayloadsEncrypted", StringComparison.Ordinal)
      && voiceServerSource.Contains("DisplayNamesReceived", StringComparison.Ordinal)
      && voiceServerSource.Contains("WebRtcCandidateDetailsReceived", StringComparison.Ordinal)
      && voiceServerSource.Contains("root.TryGetProperty(\"data\", out _)", StringComparison.Ordinal)
      && voiceServerSource.Contains("root.TryGetProperty(\"sealed\"", StringComparison.Ordinal)
      && !voiceServerSource.Contains("context.Request.Query[\"name\"]", StringComparison.Ordinal)
      && !voiceServerSource.Contains(".DisplayName", StringComparison.Ordinal)
      && !voiceServerSource.Contains("string displayName", StringComparison.Ordinal),
    "The signaling server must reject plaintext/location payloads, rate-limit peers, restrict origins, require opaque room IDs, cap global capacity, fail closed on configuration, omit display names, and expose anonymous encrypted-signaling readiness");

var audioOutputVerifierStartInfo = new ProcessStartInfo("node")
{
    WorkingDirectory = repositoryRoot,
    UseShellExecute = false,
    CreateNoWindow = true,
    WindowStyle = ProcessWindowStyle.Hidden,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};
audioOutputVerifierStartInfo.ArgumentList.Add(
    Path.Combine(repositoryRoot, "scripts", "verify-voice-audio-output.cjs"));
using (var audioOutputVerifier = Process.Start(audioOutputVerifierStartInfo)
           ?? throw new InvalidOperationException("Voice audio-output verifier did not start."))
{
    var audioOutputVerifierStandardOutput = audioOutputVerifier.StandardOutput.ReadToEndAsync();
    var audioOutputVerifierStandardError = audioOutputVerifier.StandardError.ReadToEndAsync();
    await audioOutputVerifier.WaitForExitAsync();
    var audioOutputOutput = await audioOutputVerifierStandardOutput;
    var audioOutputError = await audioOutputVerifierStandardError;
    Check(
        audioOutputVerifier.ExitCode == 0,
        $"Voice audio-output verifier failed: {audioOutputError}{audioOutputOutput}");
    Console.Write(audioOutputOutput);
}

await VerifyBundledVoiceServerAsync(repositoryRoot);

Console.WriteLine("Built-in voice integration passed (pre-microphone server readiness, room-key encrypted signaling, broker-blind display names, session-only speaker routing, anonymous capacity, local-only mic meter, aggregate quality monitor, invite security, consent-based peer route offers, and live two-peer opaque signaling).");
