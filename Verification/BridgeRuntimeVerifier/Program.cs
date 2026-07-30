using System.Text;
using System.Text.Json;
using Isley.ServerBridge;
using Isley.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

// --- 1. BridgeOptions.PluginCapable / RelayConfigured derivation matrix. -----
{
    // PluginCapable = PluginEnabled AND key length >= 32 (the plugin endpoint
    // must not become reachable on a short/empty key).
    Check(!new BridgeOptions { PluginEnabled = false, PluginKey = new string('k', 64) }.PluginCapable,
        "A disabled plugin must never be capable, even with a strong key.");
    Check(!new BridgeOptions { PluginEnabled = true }.PluginCapable,
        "An enabled plugin with an empty key must not be capable.");
    Check(!new BridgeOptions { PluginEnabled = true, PluginKey = new string('k', 31) }.PluginCapable,
        "A 31-character plugin key must not be capable (length floor is 32).");
    Check(new BridgeOptions { PluginEnabled = true, PluginKey = new string('k', 32) }.PluginCapable
          && new BridgeOptions { PluginEnabled = true, PluginKey = new string('k', 64) }.PluginCapable,
        "A plugin key of 32+ characters with the plugin enabled must be capable.");

    // RelayConfigured = server id + relay URL + secret length >= 32.
    Check(!new BridgeOptions().RelayConfigured,
        "An empty bridge must not be relay-configured.");
    Check(!new BridgeOptions
        {
            ServerId = "verify-bridge",
            RelayUrl = "https://relay.example/",
            RelaySecret = new string('s', 31)
        }.RelayConfigured,
        "A 31-character relay secret must not count as configured.");
    Check(new BridgeOptions
        {
            ServerId = "verify-bridge",
            RelayUrl = "https://relay.example/",
            RelaySecret = new string('s', 32)
        }.RelayConfigured,
        "Server id + URL + 32-character secret must be relay-configured.");
    Check(!new BridgeOptions
        {
            ServerId = " ",
            RelayUrl = "https://relay.example/",
            RelaySecret = new string('s', 32)
        }.RelayConfigured,
        "A whitespace server id must not count as configured.");
}

// --- 2. BridgeRuntimeStatus snapshot shape and transitions. ------------------
{
    var status = new BridgeRuntimeStatus();
    using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot())))
    {
        var root = snapshot.RootElement;
        foreach (var field in new[]
                 {
                     "source", "relay", "detail", "lastSampledAt", "lastPublishedAt",
                     "lastSuccessfulPublishAt", "lastSequence", "lastEntityCount"
                 })
        {
            Check(root.TryGetProperty(field, out _),
                $"The status snapshot must always carry the '{field}' field.");
        }
        Check(root.GetProperty("source").GetString() == "waiting"
              && root.GetProperty("relay").GetString() == "waiting"
              && root.GetProperty("detail").GetString() == "Waiting for configuration."
              && root.GetProperty("lastSampledAt").ValueKind == JsonValueKind.Null
              && root.GetProperty("lastPublishedAt").ValueKind == JsonValueKind.Null
              && root.GetProperty("lastSuccessfulPublishAt").ValueKind == JsonValueKind.Null
              && root.GetProperty("lastSequence").GetInt64() == 0
              && root.GetProperty("lastEntityCount").GetInt64() == 0,
            "A fresh status must snapshot the honest waiting state with null timestamps.");
    }

    var frame = new TelemetryFrame
    {
        ServerId = "verify-bridge",
        BridgeSessionId = new string('a', 32),
        Sequence = 41,
        SampledAt = DateTimeOffset.UtcNow.AddSeconds(-2),
        Entities =
        [
            new TelemetryEntity { EntityId = "self" },
            new TelemetryEntity { EntityId = "ai-1", Kind = TelemetryEntityKind.AiAnimal }
        ]
    };
    status.Sampled(frame);
    using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot())))
    {
        var root = snapshot.RootElement;
        Check(root.GetProperty("source").GetString() == "live"
              && root.GetProperty("lastSequence").GetInt64() == 41
              && root.GetProperty("lastEntityCount").GetInt64() == 2,
            "Sampling must mark the source live with the frame sequence and entity count.");
        Check(DateTimeOffset.TryParse(
                  root.GetProperty("lastSampledAt").GetString(), out var sampled)
              && sampled == frame.SampledAt,
            "lastSampledAt must serialize as an ISO-8601 timestamp equal to the frame's.");
        Check(root.GetProperty("lastSuccessfulPublishAt").ValueKind == JsonValueKind.Null,
            "Sampling alone must never fabricate a successful publish timestamp.");
    }

    status.Published(frame);
    DateTimeOffset publishedAt = default;
    using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot())))
    {
        var root = snapshot.RootElement;
        Check(root.GetProperty("relay").GetString() == "live",
            "A successful publish must mark the relay live.");
        Check(root.GetProperty("lastSuccessfulPublishAt").ValueKind == JsonValueKind.String
              && DateTimeOffset.TryParse(
                  root.GetProperty("lastSuccessfulPublishAt").GetString(), out publishedAt)
              && publishedAt <= DateTimeOffset.UtcNow
              && publishedAt > DateTimeOffset.UtcNow.AddMinutes(-1),
            "lastSuccessfulPublishAt must serialize as a parseable, current timestamp.");
        Check(root.GetProperty("lastPublishedAt").GetString()
              == root.GetProperty("lastSuccessfulPublishAt").GetString(),
            "lastPublishedAt and lastSuccessfulPublishAt must stay consistent.");
    }

    // Failure must never rewrite the last successful publish timestamp.
    status.RelayError("Relay unavailable; retrying the newest frame.");
    status.SourceError("RCON unavailable; reconnecting with backoff.");
    using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot())))
    {
        var root = snapshot.RootElement;
        Check(root.GetProperty("relay").GetString() == "error"
              && root.GetProperty("source").GetString() == "error"
              && root.GetProperty("detail").GetString()!
                  .Contains("backoff", StringComparison.Ordinal),
            "Errors must surface honestly in the snapshot.");
        Check(DateTimeOffset.TryParse(
                  root.GetProperty("lastSuccessfulPublishAt").GetString(), out var afterError)
              && afterError == publishedAt,
            "A relay failure must leave lastSuccessfulPublishAt untouched.");
    }
}

// --- 3. BridgeJson serializer: round trips and hostile inputs. ---------------
{
    var input = new PluginTelemetryFrame
    {
        SampledAt = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero),
        Source = "plugin",
        Capabilities = new TelemetryCapabilities { Position = true, Health = true },
        Entities =
        [
            new TelemetryEntity
            {
                EntityId = "self",
                SteamId = "76561198000000001",
                SpeciesId = "triceratops",
                X = 12.5,
                Y = -4,
                ShareScope = TelemetryShareScope.Friends,
                Conditions = ["sick"]
            }
        ]
    };
    var json = JsonSerializer.Serialize(input, BridgeJson.Options);
    Check(json.Contains("\"shareScope\":\"Friends\"", StringComparison.Ordinal),
        "Enums must serialize as strings (JsonStringEnumConverter).");
    var roundTripped = JsonSerializer.Deserialize<PluginTelemetryFrame>(json, BridgeJson.Options);
    Check(roundTripped is not null
          && roundTripped.SampledAt == input.SampledAt
          && roundTripped.Source == "plugin"
          && roundTripped.Capabilities.Position
          && roundTripped.Entities.Count == 1
          && roundTripped.Entities[0].EntityId == "self"
          && roundTripped.Entities[0].ShareScope == TelemetryShareScope.Friends
          && roundTripped.Entities[0].Conditions.Count == 1,
        "A PluginTelemetryFrame must round-trip through BridgeJson intact.");

    // Missing fields fall back to the documented defaults the endpoint relies on.
    var defaults = JsonSerializer.Deserialize<PluginTelemetryFrame>("{}", BridgeJson.Options);
    Check(defaults is not null
          && defaults.SampledAt == default
          && defaults.Source == "plugin"
          && defaults.Capabilities.Position && defaults.Capabilities.AuthoritativeDirection
          && defaults.Capabilities.AiAnimals
          && defaults.Entities.Count == 0,
        "An empty body must deserialize to the honest plugin defaults.");

    // A JSON null is a null result (the endpoint's documented guard), not a throw.
    Check(JsonSerializer.Deserialize<PluginTelemetryFrame>("null", BridgeJson.Options) is null,
        "A 'null' body must deserialize to null for the endpoint's guard.");

    // Case sensitivity is deliberate: wrong-case property names are ignored.
    var wrongCase = JsonSerializer.Deserialize<PluginTelemetryFrame>(
        "{\"SOURCE\":\"forged\"}", BridgeJson.Options);
    Check(wrongCase is not null && wrongCase.Source == "plugin",
        "BridgeJson must stay case-sensitive; wrong-case properties are ignored.");

    // Unknown properties are ignored rather than failing.
    var extra = JsonSerializer.Deserialize<PluginTelemetryFrame>(
        "{\"source\":\"plugin\",\"unexpected\":123}", BridgeJson.Options);
    Check(extra is not null && extra.Source == "plugin",
        "Unknown properties must be ignored.");

    // Wrong types fail bounded: JsonException, never anything past the guard.
    foreach (var hostile in new[]
             {
                 "{\"sampledAt\":123}",
                 "{\"entities\":\"nope\"}",
                 "{\"capabilities\":{\"position\":\"yes\"}}",
                 "{\"entities\":[{\"entityId\":\"e\",\"shareScope\":\"Nope\"}]}",
                 "{not json",
                 "[1,2,3]"
             })
    {
        AssertThrows<JsonException>(
            () => JsonSerializer.Deserialize<PluginTelemetryFrame>(hostile, BridgeJson.Options),
            $"Hostile body must fail with a bounded JsonException: {hostile}");
    }

    // Depth is capped at 12: a 13-deep document fails, a 12-deep one parses.
    string NestedJson(int depth)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            builder.Append("{\"child\":");
        }
        builder.Append("null");
        builder.Append('}', depth);
        return builder.ToString();
    }

    Check(JsonSerializer.Deserialize<NestedProbe>(NestedJson(12), BridgeJson.Options) is not null,
        "A document inside the depth cap must deserialize.");
    AssertThrows<JsonException>(
        () => JsonSerializer.Deserialize<NestedProbe>(NestedJson(13), BridgeJson.Options),
        "A document deeper than MaxDepth 12 must fail bounded.");
}

// --- 4. PluginRequestBodyReader: bounded reads, honest 413 guard. ------------
{
    // Declared-oversized body: refused before a single byte is read.
    var declared = new DefaultHttpContext();
    declared.Request.ContentLength = 4097;
    var tracking = new TrackingStream(new byte[4097]);
    declared.Request.Body = tracking;
    await AssertThrowsAsync<InvalidDataException>(
        () => PluginRequestBodyReader.ReadAsync(declared.Request, 4096, CancellationToken.None));
    Check(tracking.BytesRead == 0,
        "A ContentLength over the cap must be refused without reading the body.");

    // Streaming past the cap without a declared length: still bounded.
    var streaming = new DefaultHttpContext();
    streaming.Request.Body = new MemoryStream(new byte[4097]);
    await AssertThrowsAsync<InvalidDataException>(
        () => PluginRequestBodyReader.ReadAsync(streaming.Request, 4096, CancellationToken.None));

    // Exactly at the cap succeeds; a normal body round-trips.
    var atCap = new DefaultHttpContext();
    atCap.Request.Body = new MemoryStream(new byte[4096]);
    Check((await PluginRequestBodyReader.ReadAsync(atCap.Request, 4096, CancellationToken.None))
          .Length == 4096,
        "A body exactly at the cap must be accepted.");

    var normal = new DefaultHttpContext();
    var payload = Encoding.UTF8.GetBytes("{\"source\":\"plugin\"}");
    normal.Request.Body = new MemoryStream(payload);
    var read = await PluginRequestBodyReader.ReadAsync(normal.Request, 4096, CancellationToken.None);
    Check(read.AsSpan().SequenceEqual(payload),
        "A normal body must round-trip byte-for-byte.");
}

// --- 5. BridgeReadinessHealthCheck matrix. -----------------------------------
{
    static BridgeOptions ConfiguredBridge() => new()
    {
        ServerId = "verify-bridge",
        RelayUrl = "https://relay.example/",
        RelaySecret = new string('s', 32),
        SourceMode = "Plugin",
        PluginEnabled = true,
        PluginKey = new string('k', 32)
    };

    var status = new BridgeRuntimeStatus();
    var health = new BridgeReadinessHealthCheck(
        Options.Create(ConfiguredBridge()),
        Options.Create(new RconOptions()),
        status);
    Check((await health.CheckHealthAsync(new HealthCheckContext())).Status
          == HealthStatus.Degraded,
        "A configured bridge that has never published must report Degraded.");

    var frame = new TelemetryFrame
    {
        ServerId = "verify-bridge",
        BridgeSessionId = new string('a', 32),
        Sequence = 1,
        SampledAt = DateTimeOffset.UtcNow
    };
    status.Published(frame);
    Check((await health.CheckHealthAsync(new HealthCheckContext())).Status
          == HealthStatus.Healthy,
        "A configured bridge with a live relay must report Healthy.");

    var unconfigured = new BridgeReadinessHealthCheck(
        Options.Create(new BridgeOptions()),
        Options.Create(new RconOptions()),
        status);
    Check((await unconfigured.CheckHealthAsync(new HealthCheckContext())).Status
          == HealthStatus.Degraded,
        "An unconfigured relay must report Degraded even after publishes.");
}

Console.WriteLine(
    "Bridge runtime verification passed: PluginCapable/RelayConfigured derivation "
    + "matrix, status snapshot shape with ISO lastSuccessfulPublishAt that failures "
    + "never rewrite, BridgeJson round trips with hostile-input guards (case "
    + "sensitivity, wrong types, depth cap), bounded plugin body reads, and the "
    + "readiness health-check matrix.");

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static async Task AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name} but nothing was thrown.");
}

sealed class NestedProbe
{
    public NestedProbe? Child { get; init; }
}

sealed class TrackingStream(byte[] backing) : MemoryStream(backing)
{
    internal int BytesRead { get; private set; }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = base.ReadAsync(buffer, cancellationToken);
        BytesRead += read.AsTask().Result;
        return read;
    }
}
