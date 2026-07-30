using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Isley.ServerBridge;
using Isley.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

const string Password = "verify-password";
const string PlayerDataPayload =
    "[2026.07.28] PlayerDataName: Alpha, PlayerID: 76561198000000001, " +
    "Location: X=100 Y=200 Z=5, Class: BP_Triceratops_C, Growth: 0.82, " +
    "Health: 0.94, Stamina: 0.70, Hunger: 0.60, Thirst: 0.50\n";

static EvrimaRconClient CreateClient(int port, bool allowUnsafeRemote = false) => new(
    Options.Create(new RconOptions
    {
        Host = "127.0.0.1",
        Port = port,
        Password = Password,
        AllowUnsafeRemoteRcon = allowUnsafeRemote
    }),
    NullLogger<EvrimaRconClient>.Instance);

static RconPollingWorker CreateWorker(
    int port,
    BridgeFrameQueue queue,
    BridgeRuntimeStatus status,
    string sourceMode = "Rcon",
    string password = Password,
    int pollMilliseconds = 200)
{
    var bridgeOptions = Options.Create(new BridgeOptions
    {
        ServerId = "verify-bridge",
        ServerName = "Verify Bridge",
        RelayUrl = "http://127.0.0.1:1/",
        RelaySecret = new string('s', 48),
        SourceMode = sourceMode
    });
    var rconOptions = Options.Create(new RconOptions
    {
        Host = "127.0.0.1",
        Port = port,
        Password = password,
        PollIntervalMilliseconds = pollMilliseconds
    });
    return new RconPollingWorker(
        CreateClient(port),
        new RconPlayerDataParser(rconOptions, new MotionHeadingEstimator()),
        new FrameFactory(bridgeOptions),
        queue,
        status,
        bridgeOptions,
        rconOptions,
        NullLogger<RconPollingWorker>.Instance);
}

// --- 1. Wire codec round trip and connection reuse. --------------------------
{
    using var server = new FakeRconServer();
    server.OnConnection = async connection =>
    {
        await connection.WriteAsync("Password Accepted\n");
        while (await connection.ReadCommandAsync())
        {
            await connection.WriteAsync(PlayerDataPayload);
        }
    };
    server.Start();

    await using var client = CreateClient(server.Port);
    var first = await client.GetPlayerDataAsync(CancellationToken.None);
    Check(first == PlayerDataPayload,
        "The RCON player-data response must round trip exactly.");

    var expectedAuth = new byte[Password.Length + 2];
    expectedAuth[0] = 0x01;
    Encoding.UTF8.GetBytes(Password).CopyTo(expectedAuth, 1);
    expectedAuth[^1] = 0x00;
    Check(server.AuthPackets.Count == 1
          && server.AuthPackets[0].SequenceEqual(expectedAuth),
        "The auth packet must be 0x01, the UTF-8 password, then the 0x00 terminator.");
    Check(server.CommandPackets.Count == 1
          && server.CommandPackets[0].SequenceEqual(new byte[] { 0x02, 0x77, 0x00 }),
        "The player-data command must be the pinned 0x02 0x77 0x00 sequence.");

    // The idle-terminated response read leaves the socket closed, so the next
    // call must transparently reconnect and re-authenticate with the same codec.
    var second = await client.GetPlayerDataAsync(CancellationToken.None);
    Check(second == first
          && server.AcceptCount == 2
          && server.AuthPackets.Count == 2
          && server.AuthPackets[1].SequenceEqual(expectedAuth)
          && server.CommandPackets.Count == 2
          && server.CommandPackets[1].SequenceEqual(new byte[] { 0x02, 0x77, 0x00 }),
        "A follow-up call must transparently reconnect with the identical auth/command codec.");

    var clientSource = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Isley.ServerBridge", "EvrimaRcon.cs"));
    Check(clientSource.Contains("_client?.Connected != true", StringComparison.Ordinal),
        "The client must reuse a live connection and only reconnect when it dropped.");
}

// --- 2. Authentication rejection and reconnect. ------------------------------
{
    using var server = new FakeRconServer();
    server.OnConnection = async connection =>
    {
        await connection.WriteAsync("Password Failed\n");
    };
    server.Start();

    await using var client = CreateClient(server.Port);
    var rejected = await AssertThrowsAsync<UnauthorizedAccessException>(
        () => client.GetPlayerDataAsync(CancellationToken.None));
    Check(rejected.Message!.Contains("rejected the configured password", StringComparison.Ordinal),
        "A rejected RCON password must surface the honest reason.");

    await AssertThrowsAsync<UnauthorizedAccessException>(
        () => client.GetPlayerDataAsync(CancellationToken.None));
    Check(server.AcceptCount == 2 && server.AuthPackets.Count == 2,
        "After an auth failure the client must disconnect and re-authenticate next call.");
}

// --- 3. Malformed responses: EOF silence, oversize, NUL padding. -------------
{
    using var server = new FakeRconServer();
    server.OnConnection = async connection =>
    {
        await connection.WriteAsync("Password Accepted\n");
        await connection.ReadCommandAsync();
        // Close without answering the command.
    };
    server.Start();

    await using var client = CreateClient(server.Port);
    var silent = await AssertThrowsAsync<IOException>(
        () => client.GetPlayerDataAsync(CancellationToken.None));
    Check(silent.Message!.Contains("did not answer", StringComparison.Ordinal),
        "A silent RCON peer must raise the honest timeout/EOF error.");
}

{
    using var server = new FakeRconServer();
    server.OnConnection = async connection =>
    {
        await connection.WriteAsync("Password Accepted\n");
        await connection.ReadCommandAsync();
        await connection.WriteBytesAsync(new byte[1024 * 1024 + 64]);
    };
    server.Start();

    await using var client = CreateClient(server.Port);
    var oversized = await AssertThrowsAsync<InvalidDataException>(
        () => client.GetPlayerDataAsync(CancellationToken.None));
    Check(oversized.Message!.Contains("exceeded the bridge limit", StringComparison.Ordinal),
        "A response beyond the 1 MiB bridge limit must be refused.");
}

{
    using var server = new FakeRconServer();
    server.OnConnection = async connection =>
    {
        await connection.WriteAsync("Password Accepted\n");
        await connection.ReadCommandAsync();
        await connection.WriteBytesAsync(Encoding.UTF8.GetBytes("player-data\0\0\0"));
    };
    server.Start();

    await using var client = CreateClient(server.Port);
    var padded = await client.GetPlayerDataAsync(CancellationToken.None);
    Check(padded == "player-data",
        "Trailing NUL padding must be trimmed from RCON responses.");
}

// --- 4. Private/loopback address guarding. -----------------------------------
{
    await using var publicClient = new EvrimaRconClient(
        Options.Create(new RconOptions
        {
            Host = "8.8.8.8",
            Port = 8888,
            Password = Password
        }),
        NullLogger<EvrimaRconClient>.Instance);
    var guard = await AssertThrowsAsync<InvalidOperationException>(
        () => publicClient.GetPlayerDataAsync(CancellationToken.None));
    Check(guard.Message!.Contains("RCON resolved to a public address", StringComparison.Ordinal),
        "RCON to a public address must be refused with the operator guidance.");

    var guardMethod = typeof(EvrimaRconClient).GetMethod(
        "IsPrivateOrLoopback",
        BindingFlags.NonPublic | BindingFlags.Static);
    Check(guardMethod is not null, "The IsPrivateOrLoopback guard must stay in place.");
    bool Allows(string address) =>
        (bool)guardMethod!.Invoke(null, [IPAddress.Parse(address)])!;
    Check(Allows("127.0.0.1") && Allows("127.34.0.9")
          && Allows("10.0.0.8") && Allows("10.255.255.255")
          && Allows("169.254.1.1")
          && Allows("172.16.0.1") && Allows("172.31.255.255")
          && Allows("192.168.1.1")
          && Allows("::1") && Allows("fe80::1"),
        "Loopback and RFC1918/link-local addresses must be allowed for RCON.");
    Check(!Allows("8.8.8.8") && !Allows("1.1.1.1")
          && !Allows("172.15.0.1") && !Allows("172.32.0.1")
          && !Allows("192.167.1.1") && !Allows("11.0.0.1"),
        "Public addresses and range-edge lookalikes must stay refused.");
}

// --- 5. Worker guard rails. ---------------------------------------------------
{
    var queue = new BridgeFrameQueue();
    var status = new BridgeRuntimeStatus();
    using var pluginOnly = CreateWorker(1, queue, status, sourceMode: "Plugin");
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await pluginOnly.StartAsync(cts.Token);
    await pluginOnly.ExecuteTask!;
    using var idleSnapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot()));
    Check(idleSnapshot.RootElement.GetProperty("source").GetString() == "waiting",
        "A bridge with RCON disabled must leave the worker idle.");

    using var unconfigured = CreateWorker(1, queue, status, password: "");
    await unconfigured.StartAsync(cts.Token);
    await unconfigured.ExecuteTask!;
    using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot()));
    Check(snapshot.RootElement.GetProperty("source").GetString() == "error"
          && snapshot.RootElement.GetProperty("detail").GetString()!
              .Contains("configuration is incomplete", StringComparison.Ordinal),
        "An unconfigured RCON source must report the honest configuration error.");
}

// --- 6. Worker success path: RCON → parse → validate → frame. ----------------
{
    using var server = new FakeRconServer();
    server.OnConnection = async connection =>
    {
        await connection.WriteAsync("Password Accepted\n");
        while (await connection.ReadCommandAsync())
        {
            await connection.WriteAsync(PlayerDataPayload);
        }
    };
    server.Start();

    var queue = new BridgeFrameQueue();
    var status = new BridgeRuntimeStatus();
    using var worker = CreateWorker(server.Port, queue, status);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await worker.StartAsync(cts.Token);

    var frame = queue.TryDequeueNewest(out var dequeued)
        ? dequeued
        : await WaitForFrameAsync(queue, cts.Token);
    Check(frame is not null,
        "The worker must deliver a frame from live RCON data.");
    Check(frame!.ServerId == "verify-bridge"
          && frame.ServerName == "Verify Bridge"
          && frame.Source == "evrima-rcon"
          && frame.Sequence >= 1
          && frame.VisibilityPolicy == TelemetryVisibilityPolicy.PrivacyFiltered,
        "The worker frame must carry the configured bridge identity and honest source.");
    var capabilities = frame.Capabilities;
    Check(capabilities.Position && capabilities.Health && capabilities.Growth
          && capabilities.Stamina && capabilities.Food && capabilities.Water
          && !capabilities.AuthoritativeDirection && !capabilities.Conditions
          && !capabilities.AiAnimals,
        "The RCON capability contract must match what the protocol can actually report.");
    var entity = frame.Entities.Single();
    Check(entity.SteamId == "76561198000000001"
          && entity.SpeciesId == "triceratops"
          && entity.HealthPercent == 94
          && entity.GrowthPercent == 82
          && entity.ShareScope == TelemetryShareScope.Self,
        "RCON entities must parse with normalized species, vitals, and the default Self scope.");

    using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot()));
    Check(snapshot.RootElement.GetProperty("source").GetString() == "live"
          && snapshot.RootElement.GetProperty("lastSequence").GetInt64() >= 1,
        "A successful sample must mark the source live with the frame sequence.");

    await worker.StopAsync(CancellationToken.None);
}

// --- 7. Worker reconnect backoff and recovery. --------------------------------
{
    using var server = new FakeRconServer();
    server.OnConnection = async connection =>
    {
        if (connection.Attempt <= 3)
        {
            await connection.WriteAsync("Password Failed\n");
            return;
        }
        await connection.WriteAsync("Password Accepted\n");
        while (await connection.ReadCommandAsync())
        {
            await connection.WriteAsync(PlayerDataPayload);
        }
    };
    server.Start();

    var queue = new BridgeFrameQueue();
    var status = new BridgeRuntimeStatus();
    using var worker = CreateWorker(server.Port, queue, status);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await worker.StartAsync(cts.Token);

    var frame = await WaitForFrameAsync(queue, cts.Token);
    Check(frame is not null,
        "The worker must recover and deliver frames once RCON becomes healthy.");
    Check(server.AcceptCount >= 4,
        "The worker must keep reconnecting through repeated auth failures.");
    using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot()));
    Check(snapshot.RootElement.GetProperty("source").GetString() == "live",
        "Recovery must restore the live source state.");

    // Backoff proof: with 200 ms doubling, attempts land at ~0, 200, 600 ms;
    // a flat 200 ms retry would produce ~6 attempts inside the same window.
    var first = server.AcceptTimestamps[0];
    var withinWindow = server.AcceptTimestamps.Count(at => at - first < TimeSpan.FromMilliseconds(1100));
    Check(withinWindow <= 4,
        "Reconnects must back off (200→400→800 ms), not spin at the poll interval.");
    Check(server.AcceptTimestamps.Count >= 4
          && server.AcceptTimestamps[3] - server.AcceptTimestamps[1] > TimeSpan.FromMilliseconds(300),
        "Later reconnect gaps must exceed the first gap: the delay doubles per failure.");

    var workerSource = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Isley.ServerBridge", "RconPollingWorker.cs"));
    Check(workerSource.Contains("Math.Min(15_000, failureDelay.TotalMilliseconds * 2)", StringComparison.Ordinal)
          && workerSource.Contains("status.SourceError(\"RCON unavailable; reconnecting with backoff.\")", StringComparison.Ordinal),
        "The backoff cap and honest error surface must stay pinned.");

    await worker.StopAsync(CancellationToken.None);
}

Console.WriteLine(
    "Evrima RCON protocol verification passed: auth/command wire codec, "
    + "connection reuse, auth rejection and reconnect, EOF/oversize/NUL handling, "
    + "private/loopback address guarding, worker guard rails, live sampling "
    + "pipeline, and reconnect backoff with recovery.");

static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException ex)
    {
        return ex;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
}

static async Task<TelemetryFrame?> WaitForFrameAsync(
    BridgeFrameQueue queue,
    CancellationToken cancellationToken)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
    {
        if (queue.TryDequeueNewest(out var frame))
        {
            return frame;
        }
        await Task.Delay(50, CancellationToken.None);
    }
    return null;
}

// A loopback fake-RCON TCP server: captures every auth and command packet and
// delegates each connection's behavior to the test.
sealed class FakeRconServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _connections = [];
    private Task _acceptLoop = Task.CompletedTask;

    internal Func<ConnectionContext, Task> OnConnection = _ => Task.CompletedTask;
    internal readonly List<byte[]> AuthPackets = [];
    internal readonly List<byte[]> CommandPackets = [];
    internal readonly List<DateTimeOffset> AcceptTimestamps = [];
    internal readonly List<int> CompletedConnections = [];

    internal int Port { get; private set; }
    internal int AcceptCount => AcceptTimestamps.Count;

    internal void Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                lock (AcceptTimestamps)
                {
                    AcceptTimestamps.Add(DateTimeOffset.UtcNow);
                }
                var attempt = AcceptCount;
                var task = Task.Run(() => HandleConnectionAsync(client, attempt));
                lock (_connections)
                {
                    _connections.Add(task);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, int attempt)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var authPacket = await ReadUntilTerminatorAsync(stream, _cts.Token);
                if (authPacket.Length == 0)
                {
                    return;
                }
                lock (AuthPackets)
                {
                    AuthPackets.Add(authPacket);
                }
                await OnConnection(new ConnectionContext(stream, this, attempt));
            }
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (CompletedConnections)
            {
                CompletedConnections.Add(attempt);
            }
        }
    }

    private static async Task<byte[]> ReadUntilTerminatorAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream();
        var buffer = new byte[1024];
        while (payload.Length == 0 || payload.GetBuffer()[payload.Length - 1] != 0x00)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return payload.ToArray();
            }
            await payload.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return payload.ToArray();
    }

    internal void RecordCommand(byte[] command)
    {
        lock (CommandPackets)
        {
            CommandPackets.Add(command);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        try
        {
            _acceptLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _cts.Dispose();
    }

    internal sealed class ConnectionContext(
        NetworkStream stream,
        FakeRconServer server,
        int attempt)
    {
        internal int Attempt { get; } = attempt;

        internal async Task WriteAsync(string text) =>
            await stream.WriteAsync(Encoding.UTF8.GetBytes(text));

        internal async Task WriteBytesAsync(byte[] bytes) =>
            await stream.WriteAsync(bytes);

        internal async Task<bool> ReadCommandAsync()
        {
            var command = new byte[3];
            var offset = 0;
            while (offset < command.Length)
            {
                var read = await stream.ReadAsync(
                    command.AsMemory(offset),
                    server._cts.Token);
                if (read == 0)
                {
                    return false;
                }
                offset += read;
            }
            server.RecordCommand(command);
            return true;
        }
    }
}
