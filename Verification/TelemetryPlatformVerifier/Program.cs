using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Isley;
using Isley.Relay;
using Isley.ServerBridge;
using Isley.Telemetry;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static int FreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static Process StartService(
    string assemblyPath,
    IReadOnlyDictionary<string, string> environment,
    List<string> output)
{
    var start = new ProcessStartInfo("dotnet", $"\"{assemblyPath}\"")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = Path.GetDirectoryName(assemblyPath)!
    };
    foreach (var item in environment)
    {
        start.Environment[item.Key] = item.Value;
    }
    var process = Process.Start(start)
                  ?? throw new InvalidOperationException($"Could not start {assemblyPath}.");
    process.OutputDataReceived += (_, eventArgs) =>
    {
        if (eventArgs.Data is not null)
        {
            lock (output) output.Add(eventArgs.Data);
        }
    };
    process.ErrorDataReceived += (_, eventArgs) =>
    {
        if (eventArgs.Data is not null)
        {
            lock (output) output.Add(eventArgs.Data);
        }
    };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

static async Task WaitForServiceAsync(Uri health, Process process, List<string> output)
{
    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(1)
    };
    var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (process.HasExited)
        {
            string log;
            lock (output) log = string.Join(Environment.NewLine, output);
            throw new InvalidOperationException(
                $"Service exited with {process.ExitCode}:{Environment.NewLine}{log}");
        }
        try
        {
            using var response = await http.GetAsync(health);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }
        await Task.Delay(100);
    }
    string captured;
    lock (output) captured = string.Join(Environment.NewLine, output);
    throw new TimeoutException(
        $"Service did not become healthy: {health}{Environment.NewLine}{captured}");
}

static async Task<JsonDocument> ReceiveSnapshotAsync(
    ClientWebSocket socket,
    CancellationToken cancellationToken)
{
    var buffer = new byte[16 * 1024];
    while (true)
    {
        using var payload = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            Check(result.MessageType == WebSocketMessageType.Text, "Relay closed before a snapshot.");
            await payload.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
        } while (!result.EndOfMessage);
        var document = JsonDocument.Parse(payload.ToArray());
        if (document.RootElement.GetProperty("type").GetString() == "snapshot")
        {
            return document;
        }
        document.Dispose();
    }
}

static string SignBody(
    DefaultHttpContext context,
    byte[] body,
    string serverId,
    string secret,
    string nonce)
{
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        .ToString(CultureInfo.InvariantCulture);
    var bodyHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
    var canonical = $"{serverId}\n{timestamp}\n{nonce}\n{bodyHash}";
    var signature = Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(secret),
        Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    context.Request.Headers[BridgeSignatureVerifier.ServerHeader] = serverId;
    context.Request.Headers[BridgeSignatureVerifier.TimestampHeader] = timestamp;
    context.Request.Headers[BridgeSignatureVerifier.NonceHeader] = nonce;
    context.Request.Headers[BridgeSignatureVerifier.SignatureHeader] = signature;
    return signature;
}

var now = DateTimeOffset.UtcNow;
var validFrame = new TelemetryFrame
{
    ServerId = "verification-server",
    ServerName = "Verification",
    BridgeSessionId = new string('a', 32),
    Sequence = 1,
    SampledAt = now,
    Entities =
    [
        new TelemetryEntity
        {
            EntityId = "self",
            SteamId = "76561198000000001",
            X = 1,
            Y = 2,
            Z = 3,
            HealthPercent = 100,
            GrowthPercent = 50
        }
    ]
};
Check(TelemetryValidation.Validate(validFrame, now).Count == 0,
    "A valid shared telemetry frame was rejected.");
Check(TelemetryValidation.Validate(validFrame with
{
    SampledAt = now.Subtract(TelemetryProtocol.MaximumFrameAge).AddSeconds(-1)
}, now).Any(error => error.Contains("SampledAt", StringComparison.Ordinal)),
    "A stale SampledAt frame was accepted beyond MaximumFrameAge.");
Check(TelemetryValidation.Validate(validFrame with
{
    Entities = [validFrame.Entities[0] with { HealthPercent = 101 }]
}, now).Any(error => error.Contains("percentage", StringComparison.Ordinal)),
    "Out-of-range vitals did not fail closed.");
var streamStore = new TelemetryFrameStore(Options.Create(new RelayOptions()));
Check(streamStore.TryAccept(validFrame, out _)
      && streamStore.TryAccept(validFrame with
      {
          Sequence = 2,
          SampledAt = now.AddMilliseconds(100)
      }, out _)
      && streamStore.TryGetFresh(validFrame.ServerId, out var measuredStream)
      && measuredStream.UpdateRateHz is > 9.9 and < 10.1,
    "The relay did not measure the live network's update rate.");
var streamNow = DateTimeOffset.UtcNow;
Check(TelemetryStreamHealthLogic.Assess(
          streamNow,
          streamNow.AddMilliseconds(100),
          10,
          80).State == TelemetryStreamState.Live,
    "A current high-cadence stream was not classified as live.");
Check(TelemetryStreamHealthLogic.Assess(
          streamNow,
          streamNow.AddMilliseconds(1_100),
          1,
          1_100).State == TelemetryStreamState.Delayed,
    "A slow telemetry stream was not classified as delayed.");
Check(TelemetryStreamHealthLogic.Assess(
          streamNow,
          streamNow.AddMilliseconds(3_100),
          5,
          100).State == TelemetryStreamState.Stalled,
    "A silent telemetry stream was not classified as stalled.");

var rconOptions = Options.Create(new RconOptions
{
    Password = "verification-password",
    DefaultShareScope = "Friends"
});
var headingEstimator = new MotionHeadingEstimator();
var rconParser = new RconPlayerDataParser(rconOptions, headingEstimator);
const string firstRcon =
    "[2026.07.23] PlayerDataName: Alpha, PlayerID: 76561198000000001, " +
    "Location: X=100 Y=200 Z=5, Class: BP_Triceratops_C, Growth: 0.82, " +
    "Health: 0.94, Stamina: 0.70, Hunger: 0.60, Thirst: 0.50";
var firstPlayers = rconParser.Parse(firstRcon, now);
Check(firstPlayers.Count == 1
      && firstPlayers[0].SpeciesId == "triceratops"
      && firstPlayers[0].GrowthPercent == 82
      && firstPlayers[0].HealthPercent == 94
      && firstPlayers[0].Yaw is null
      && firstPlayers[0].ShareScope == TelemetryShareScope.Friends,
    "RCON player data parsing failed.");
var secondPlayers = rconParser.Parse(
    firstRcon.Replace("X=100 Y=200", "X=200 Y=300", StringComparison.Ordinal),
    now.AddMilliseconds(500));
Check(secondPlayers[0].Yaw is > 44.9 and < 45.1
      && secondPlayers[0].DirectionQuality == TelemetryDirectionQuality.MotionInferred,
    "RCON movement-heading inference failed.");
var networkFactory = new FrameFactory(Options.Create(new BridgeOptions
{
    ServerId = "verification-server",
    ServerName = "Verification",
    ServerWideAwareness = true
}));
var networkFrame = networkFactory.Create(
    now,
    "verification",
    new TelemetryCapabilities(),
    firstPlayers);
Check(networkFrame.VisibilityPolicy == TelemetryVisibilityPolicy.ServerWide
      && networkFrame.Entities.All(entity =>
          entity.ShareScope == TelemetryShareScope.Server),
    "The explicit server-wide awareness policy did not publish every authorized entity.");
var privacyFactory = new FrameFactory(Options.Create(new BridgeOptions
{
    ServerId = "verification-server",
    ServerName = "Verification",
    ServerWideAwareness = false
}));
var privacyFrame = privacyFactory.Create(
    now,
    "verification",
    new TelemetryCapabilities(),
    [
        new TelemetryEntity
        {
            EntityId = "forced-player",
            SteamId = "76561198000000009",
            Kind = TelemetryEntityKind.Player,
            X = 1,
            Y = 2,
            Z = 3,
            ShareScope = TelemetryShareScope.Server
        },
        new TelemetryEntity
        {
            EntityId = "wildlife",
            Kind = TelemetryEntityKind.AiAnimal,
            SpeciesId = "deer",
            X = 4,
            Y = 5,
            Z = 6,
            ShareScope = TelemetryShareScope.Server
        }
    ]);
Check(privacyFrame.VisibilityPolicy == TelemetryVisibilityPolicy.PrivacyFiltered
      && privacyFrame.Entities.Single(entity => entity.EntityId == "forced-player")
          .ShareScope == TelemetryShareScope.Self
      && privacyFrame.Entities.Single(entity => entity.EntityId == "wildlife")
          .ShareScope == TelemetryShareScope.Server,
    "Privacy-filtered bridges must clamp player Server scopes without hiding authorized AI.");
Check(new RconOptions().PollIntervalMilliseconds == 200,
    "The server bridge does not default to the 5 Hz continuous RCON cadence.");

var privacyRoot = Path.Combine(Path.GetTempPath(), $"isley-privacy-{Guid.NewGuid():N}");
Directory.CreateDirectory(privacyRoot);
try
{
    var privacyStore = new PrivacyStore(
        Options.Create(new RelayOptions { StatePath = privacyRoot }),
        NullLogger<PrivacyStore>.Instance);
    using var friendHttp = new HttpClient();
    var friendResolver = new SteamFriendResolver(
        friendHttp,
        Options.Create(new SteamOptions { WebApiKey = "verification-key" }),
        privacyStore,
        NullLogger<SteamFriendResolver>.Instance);
    var friendDecision = await friendResolver.EvaluateAsync(
        "76561198000000002",
        "76561198000000001",
        TelemetryShareScope.Friends,
        [],
        CancellationToken.None);
    Check(!friendDecision.Visible && friendDecision.Reason == "not-shared",
        "Bridge Friends scope must not bypass the player's ShareWithSteamFriends opt-out.");
}
finally
{
    try { Directory.Delete(privacyRoot, recursive: true); } catch { }
}

Check(IsleyRelayJoinLogic.TryParse(
        "https://relay.example/join/verification-server",
        out var parsedJoin)
      && parsedJoin.ServerId == "verification-server"
      && parsedJoin.RelayOrigin.AbsoluteUri == "https://relay.example/",
    "Friendly participating-server link parsing failed.");
Check(!IsleyRelayJoinLogic.TryParse(
        "http://public.example/join/verification-server",
        out _),
    "An unencrypted public relay link was accepted.");

var bridgeSecret = new string('s', 48);
var signatureOptions = Options.Create(new RelayOptions
{
    Bridges =
    [
        new BridgeRegistration
        {
            ServerId = "verification-server",
            Secret = bridgeSecret
        }
    ]
});
var replayGuard = new BridgeReplayGuard();
var signatureVerifier = new BridgeSignatureVerifier(
    signatureOptions,
    replayGuard);
var signedBody = Encoding.UTF8.GetBytes("{}");
var forgedNonce = new string('a', 32);
var forgedContext = new DefaultHttpContext();
SignBody(
    forgedContext,
    signedBody,
    "verification-server",
    new string('x', 48),
    forgedNonce);
Check(!signatureVerifier.Verify(forgedContext.Request, signedBody).Accepted,
    "A forged bridge HMAC signature was accepted.");
var recoveredContext = new DefaultHttpContext();
SignBody(
    recoveredContext,
    signedBody,
    "verification-server",
    bridgeSecret,
    forgedNonce);
Check(signatureVerifier.Verify(recoveredContext.Request, signedBody).Accepted,
    "Invalid signatures must not consume nonce slots.");
var signedContext = new DefaultHttpContext();
SignBody(
    signedContext,
    signedBody,
    "verification-server",
    bridgeSecret,
    new string('b', 32));
Check(signatureVerifier.Verify(signedContext.Request, signedBody).Accepted,
    "A valid bridge HMAC signature was rejected.");
var replay = signatureVerifier.Verify(signedContext.Request, signedBody);
Check(!replay.Accepted && replay.Error == "replayed_signature",
    "Bridge nonce replay was not rejected.");

var root = Directory.GetCurrentDirectory();
var relayAssembly = Path.Combine(root, "Isley.Relay", "bin", "Release", "net8.0", "Isley.Relay.dll");
var bridgeAssembly = Path.Combine(root, "Isley.ServerBridge", "bin", "Release", "net8.0", "Isley.ServerBridge.dll");
Check(File.Exists(relayAssembly) && File.Exists(bridgeAssembly),
    "Build Release relay and bridge before running the platform verifier.");

var relayPort = FreePort();
var bridgePort = FreePort();
var relayOrigin = new Uri($"http://127.0.0.1:{relayPort}/");
var bridgeOrigin = new Uri($"http://127.0.0.1:{bridgePort}/");
var temporaryRoot = Path.Combine(
    root,
    "Verification",
    "TelemetryPlatformVerifier",
    $".runtime-{Guid.NewGuid():N}");
var keyPath = Path.Combine(temporaryRoot, "keys");
var statePath = Path.Combine(temporaryRoot, "state");
Directory.CreateDirectory(keyPath);
var dataProtection = DataProtectionProvider.Create(
    new DirectoryInfo(keyPath),
    configuration => configuration.SetApplicationName("Isley.Relay"));
var viewerSteamId = "76561198000000001";
var accessToken = new AccessTokenService(dataProtection).Create(viewerSteamId);
var pluginKey = new string('p', 48);
var relayOutput = new List<string>();
var bridgeOutput = new List<string>();
Process? relayProcess = null;
Process? bridgeProcess = null;

try
{
    relayProcess = StartService(relayAssembly, new Dictionary<string, string>
    {
        ["ASPNETCORE_URLS"] = relayOrigin.AbsoluteUri,
        ["Urls"] = relayOrigin.AbsoluteUri,
        ["Relay__PublicBaseUrl"] = relayOrigin.AbsoluteUri,
        ["Relay__DataProtectionKeysPath"] = keyPath,
        ["Relay__StatePath"] = statePath,
        ["Relay__Bridges__0__ServerId"] = "verification-server",
        ["Relay__Bridges__0__Secret"] = bridgeSecret
    }, relayOutput);
    await WaitForServiceAsync(
        new Uri(relayOrigin, "health/live"),
        relayProcess,
        relayOutput);

    bridgeProcess = StartService(bridgeAssembly, new Dictionary<string, string>
    {
        ["ASPNETCORE_URLS"] = bridgeOrigin.AbsoluteUri,
        ["Urls"] = bridgeOrigin.AbsoluteUri,
        ["Bridge__ServerId"] = "verification-server",
        ["Bridge__ServerName"] = "Verification Server",
        ["Bridge__RelayUrl"] = relayOrigin.AbsoluteUri,
        ["Bridge__RelaySecret"] = bridgeSecret,
        ["Bridge__SourceMode"] = "Plugin",
        ["Bridge__PluginEnabled"] = "true",
        ["Bridge__PluginKey"] = pluginKey
    }, bridgeOutput);
    await WaitForServiceAsync(
        new Uri(bridgeOrigin, "health/live"),
        bridgeProcess,
        bridgeOutput);

    using var bridgeHttp = new HttpClient(new SocketsHttpHandler { UseProxy = false });
    using (var unauthorizedPlugin = new HttpRequestMessage(
               HttpMethod.Post,
               new Uri(bridgeOrigin, "plugin/v1/telemetry"))
           {
               Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
           })
    using (var unauthorizedResponse = await bridgeHttp.SendAsync(unauthorizedPlugin))
    {
        Check(unauthorizedResponse.StatusCode == HttpStatusCode.Unauthorized,
            "Plugin authentication did not happen before telemetry parsing.");
    }
    using (var oversizedPlugin = new HttpRequestMessage(
               HttpMethod.Post,
               new Uri(bridgeOrigin, "plugin/v1/telemetry"))
           {
               Content = new StringContent(
                   new string('x', TelemetryProtocol.MaximumFrameBytes + 1),
                   Encoding.UTF8,
                   "application/json")
           })
    {
        oversizedPlugin.Headers.Add("X-Isley-Plugin-Key", pluginKey);
        using var oversizedResponse = await bridgeHttp.SendAsync(oversizedPlugin);
        Check(oversizedResponse.StatusCode == HttpStatusCode.RequestEntityTooLarge,
            "Oversized plugin telemetry was not rejected before JSON parsing.");
    }

    using var socket = new ClientWebSocket();
    socket.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
    using var streamTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await socket.ConnectAsync(
        new Uri($"ws://127.0.0.1:{relayPort}/api/v1/live/verification-server"),
        streamTimeout.Token);

    var pluginFrame = new PluginTelemetryFrame
    {
        SampledAt = DateTimeOffset.UtcNow,
        Source = "verification-plugin",
        Entities =
        [
            new TelemetryEntity
            {
                EntityId = "player-self",
                SteamId = viewerSteamId,
                DisplayName = "Self Name",
                Kind = TelemetryEntityKind.Player,
                SpeciesId = "triceratops",
                X = 10,
                Y = 20,
                Z = 5,
                Yaw = 123,
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
                EntityId = "player-friend",
                SteamId = "76561198000000002",
                DisplayName = "Pack Friend",
                Kind = TelemetryEntityKind.Player,
                X = 30,
                Y = 40,
                Z = 5,
                Yaw = 90,
                DirectionQuality = TelemetryDirectionQuality.ServerAuthoritative,
                ShareScope = TelemetryShareScope.Self,
                AllowedViewerSteamIds = [viewerSteamId]
            },
            new TelemetryEntity
            {
                EntityId = "player-private",
                SteamId = "76561198000000003",
                DisplayName = "Private Player",
                Kind = TelemetryEntityKind.Player,
                X = 50,
                Y = 60,
                Z = 5,
                ShareScope = TelemetryShareScope.Self
            },
            new TelemetryEntity
            {
                EntityId = "ai-deer",
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
    var pluginJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    pluginJson.Converters.Add(new JsonStringEnumConverter());
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        new Uri(bridgeOrigin, "plugin/v1/telemetry"))
    {
        Content = JsonContent.Create(pluginFrame, options: pluginJson)
    };
    request.Headers.Add("X-Isley-Plugin-Key", pluginKey);
    using var pluginResponse = await bridgeHttp.SendAsync(request, streamTimeout.Token);
    Check(pluginResponse.StatusCode == HttpStatusCode.Accepted,
        $"Bridge plugin ingestion returned {(int)pluginResponse.StatusCode}.");
    using (var statusResponse = await bridgeHttp.GetAsync(
               new Uri(bridgeOrigin, "status"),
               streamTimeout.Token))
    {
        statusResponse.EnsureSuccessStatusCode();
        await using var statusStream =
            await statusResponse.Content.ReadAsStreamAsync(streamTimeout.Token);
        using var statusDocument = await JsonDocument.ParseAsync(
            statusStream,
            cancellationToken: streamTimeout.Token);
        var status = statusDocument.RootElement.GetProperty("status");
        Check(status.GetProperty("source").GetString() == "live",
            "Plugin ingestion did not mark the bridge source as live.");
    }

    using var envelope = await ReceiveSnapshotAsync(socket, streamTimeout.Token);
    var snapshot = envelope.RootElement.GetProperty("snapshot")
        .Deserialize<ViewerTelemetrySnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    Check(snapshot is not null
          && snapshot.ServerId == "verification-server"
          && snapshot.Source == "verification-plugin"
          && snapshot.Self is
          {
              Id: "self",
              Yaw: 123,
              HealthPercent: 94,
              GrowthPercent: 82,
              StaminaPercent: 73
          }
          && snapshot.Self.Conditions.SequenceEqual(["vomit-sickness"]),
        "End-to-end self position, direction, vitals, or conditions were lost.");
    Check(snapshot!.Players.Count == 2,
        "Privacy filtering did not return exactly the explicit friend and server-visible AI.");
    Check(snapshot.ConnectedPlayerNodes == 1
          && snapshot.VisibleEntityCount == 3
          && snapshot.VisibilityPolicy == TelemetryVisibilityPolicy.PrivacyFiltered,
        "The live awareness-node metadata was not delivered accurately.");
    Check(snapshot.Players.Any(player =>
              player.Friend && player.Label == "Pack Friend")
          && snapshot.Players.Any(player =>
              player.Kind == TelemetryEntityKind.AiAnimal
              && !player.Friend
              && player.Label == "Animal")
          && snapshot.Players.All(player => player.Label != "Private Player"),
        "Friend consent, stranger redaction, or AI visibility failed.");
    Check(snapshot.Players.All(player =>
              player.HealthPercent is null
              && player.Conditions.Count == 0),
        "Another entity's private vitals or conditions leaked to the viewer.");

    await socket.CloseAsync(
        WebSocketCloseStatus.NormalClosure,
        "verification complete",
        CancellationToken.None);
}
finally
{
    foreach (var process in new[] { bridgeProcess, relayProcess })
    {
        if (process is null)
        {
            continue;
        }
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        process.Dispose();
    }
    if (Directory.Exists(temporaryRoot)
        && Path.GetFullPath(temporaryRoot).StartsWith(
            Path.GetFullPath(Path.Combine(root, "Verification", "TelemetryPlatformVerifier")),
            StringComparison.OrdinalIgnoreCase))
    {
        Directory.Delete(temporaryRoot, recursive: true);
    }
}

var guidedLauncher = File.ReadAllText(Path.Combine(
    root,
    "scripts",
    "Start-IsleyServerBridge.ps1"));
Check(guidedLauncher.Contains("Read-Host $Prompt -AsSecureString", StringComparison.Ordinal)
      && guidedLauncher.Contains(
          "EnvironmentVariables[\"Bridge__RelaySecret\"]",
          StringComparison.Ordinal)
      && guidedLauncher.Contains(
          "EnvironmentVariables[\"Rcon__Password\"]",
          StringComparison.Ordinal)
      && guidedLauncher.Contains("ZeroFreeBSTR", StringComparison.Ordinal)
      && guidedLauncher.Contains(
          "ValidateSet(\"Self\", \"Friends\", \"Server\")",
          StringComparison.Ordinal)
      && !guidedLauncher.Contains("Explicit", StringComparison.Ordinal)
      && !guidedLauncher.Contains("Set-Content", StringComparison.OrdinalIgnoreCase)
      && !guidedLauncher.Contains("Out-File", StringComparison.OrdinalIgnoreCase),
    "The guided server launcher must prompt securely and avoid secret files.");
var relayProgram = File.ReadAllText(Path.Combine(root, "Isley.Relay", "Program.cs"));
Check(relayProgram.Contains("app.MapGet(\"/join/{serverId}\"", StringComparison.Ordinal)
      && relayProgram.Contains("public_base_url_required", StringComparison.Ordinal),
    "The relay must serve join pages and refuse unconfigured public Steam origins.");
var relayClient = File.ReadAllText(Path.Combine(root, "BurntHud", "IsleyRelayClient.cs"));
Check(relayClient.Contains("AllowAutoRedirect = false", StringComparison.Ordinal)
      && relayClient.Contains("ReadTrustedVerificationUri", StringComparison.Ordinal)
      && relayClient.Contains("/auth/steam/", StringComparison.Ordinal),
    "The desktop relay client must pin Steam verification URIs to the joined origin.");
Check(File.Exists(Path.Combine(
          root,
          "docs",
          "THE_ISLE_TELEMETRY_INTERFACE_REQUEST.md")),
    "The sanctioned telemetry-interface request is missing.");

Console.WriteLine(
    "Isley telemetry platform verification passed: contract bounds, RCON parsing, "
    + "motion heading, safe join links, HMAC replay defense, bridge-to-relay delivery, "
    + "Steam-token WebSocket auth, privacy filtering, server-wide awareness, node counts, "
    + "stream rate, delayed/stalled honesty, vitals, facing, sickness, friends, AI, "
    + "and secure guided setup.");
