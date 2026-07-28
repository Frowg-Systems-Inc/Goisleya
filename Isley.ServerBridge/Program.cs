using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Isley.ServerBridge;
using Isley.Telemetry;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddOptions<BridgeOptions>()
    .Bind(builder.Configuration.GetSection("Bridge"))
    .Validate(BridgeOptions.IsValid, "Bridge configuration contains an invalid value.")
    .ValidateOnStart();
builder.Services
    .AddOptions<RconOptions>()
    .Bind(builder.Configuration.GetSection("Rcon"))
    .Validate(RconOptions.IsValid, "RCON configuration contains an invalid value.")
    .ValidateOnStart();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddHealthChecks()
    .AddCheck<BridgeReadinessHealthCheck>("bridge-ready");
builder.Services.AddHttpClient<RelayPublisher>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<BridgeFrameQueue>();
builder.Services.AddSingleton<BridgeRuntimeStatus>();
builder.Services.AddSingleton<FrameFactory>();
builder.Services.AddSingleton<EvrimaRconClient>();
builder.Services.AddSingleton<RconPlayerDataParser>();
builder.Services.AddSingleton<MotionHeadingEstimator>();
builder.Services.AddHostedService<RelayPublishWorker>();
builder.Services.AddHostedService<RconPollingWorker>();

var app = builder.Build();
app.UseExceptionHandler();
app.MapHealthChecks("/health/live", new()
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new()
{
    Predicate = registration => registration.Name == "bridge-ready"
});
object BridgeStatusPayload(
    BridgeRuntimeStatus status,
    BridgeOptions bridge,
    RconOptions rcon) => new
{
    service = "Isley Server Bridge",
    protocolVersion = TelemetryProtocol.Version,
    status = status.Snapshot(),
    source = new
    {
        mode = bridge.SourceMode,
        rconEnabled = bridge.RconEnabled,
        rconConfigured = rcon.Configured,
        pluginEnabled = bridge.PluginEnabled,
        pluginCapable = bridge.PluginCapable,
        allowRemotePlugin = bridge.AllowRemotePlugin
    },
    relayConfigured = bridge.RelayConfigured,
    lastSuccessfulPublishAt = status.LastSuccessfulPublishAt,
    safety = "RCON stays operator-side; relay never receives the RCON password.",
    pluginIngress = "loopback authenticated"
};
app.MapGet("/", (
    BridgeRuntimeStatus status,
    IOptions<BridgeOptions> bridge,
    IOptions<RconOptions> rcon) =>
    TypedResults.Ok(BridgeStatusPayload(status, bridge.Value, rcon.Value)));
app.MapGet("/status", (
    BridgeRuntimeStatus status,
    IOptions<BridgeOptions> bridge,
    IOptions<RconOptions> rcon) =>
    TypedResults.Ok(BridgeStatusPayload(status, bridge.Value, rcon.Value)));
app.MapGet("/status/ui", (HttpContext context, BridgeRuntimeStatus status, IOptions<BridgeOptions> bridgeOptions) =>
{
    if (context.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var options = bridgeOptions.Value;
    var snapshot = status.Snapshot();
    var snapshotJson = JsonSerializer.Serialize(
        snapshot,
        new JsonSerializerOptions { WriteIndented = true });
    var sourceMode = WebUtility.HtmlEncode(options.SourceMode);
    var html = $$"""
        <!DOCTYPE html><html><head><meta charset="utf-8"><title>Isley Bridge Status</title>
        <style>body{font-family:Segoe UI,sans-serif;background:#071018;color:#e2e8f0;padding:24px}
        code,pre{color:#7dd3fc} .warn{color:#fbbf24}</style></head><body>
        <h1>Isley Server Bridge · Status</h1>
        <p>Loopback-only operator view. Secrets are never included.</p>
        <pre>{{snapshotJson}}</pre>
        <p>Source mode: <code>{{sourceMode}}</code> · Plugin enabled: <code>{{options.PluginEnabled}}</code> · Plugin capable: <code>{{options.PluginCapable}}</code></p>
        <p>Last successful relay publish: <code>{{status.LastSuccessfulPublishAt?.ToString("O") ?? "never"}}</code></p>
        <p class="warn">RCON cannot supply stationary facing, sickness/conditions, or AI animals. See RCON_TO_PLUGIN.md for the authorized plugin path.</p>
        <p><a href="/status">JSON status</a> · <a href="/">JSON root</a> · <a href="/health/ready">ready</a></p>
        </body></html>
        """;
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/plugin/v1/telemetry", async (
    HttpContext context,
    FrameFactory frames,
    BridgeFrameQueue queue,
    BridgeRuntimeStatus status,
    IOptions<BridgeOptions> bridgeOptions,
    CancellationToken cancellationToken) =>
{
    var options = bridgeOptions.Value;
    if (!options.PluginEnabled)
    {
        return Results.NotFound();
    }
    if (!options.AllowRemotePlugin
        && context.Connection.RemoteIpAddress is { } remote
        && !IPAddress.IsLoopback(remote))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var suppliedKey = context.Request.Headers["X-Isley-Plugin-Key"].ToString();
    if (!FixedTimeTextEquals(suppliedKey, options.PluginKey))
    {
        return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    byte[] body;
    try
    {
        body = await PluginRequestBodyReader.ReadAsync(
            context.Request,
            TelemetryProtocol.MaximumFrameBytes,
            cancellationToken);
    }
    catch (InvalidDataException)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    PluginTelemetryFrame? input;
    try
    {
        input = JsonSerializer.Deserialize<PluginTelemetryFrame>(
            body,
            BridgeJson.Options);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "invalid_json" });
    }
    if (input is null)
    {
        return Results.BadRequest(new { error = "invalid_json" });
    }

    var frame = frames.Create(
        input.SampledAt == default ? DateTimeOffset.UtcNow : input.SampledAt,
        string.IsNullOrWhiteSpace(input.Source) ? "plugin" : input.Source,
        input.Capabilities,
        input.Entities);
    var errors = TelemetryValidation.Validate(frame, DateTimeOffset.UtcNow);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new { error = "invalid_frame", details = errors });
    }
    await queue.EnqueueAsync(frame, cancellationToken);
    status.Sampled(frame);
    return Results.Accepted(value: new
    {
        frame.ServerId,
        frame.BridgeSessionId,
        frame.Sequence,
        entityCount = frame.Entities.Count
    });
});

app.Run();

static bool FixedTimeTextEquals(string supplied, string expected)
{
    var left = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
    var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    return CryptographicOperations.FixedTimeEquals(left, right)
           && !string.IsNullOrEmpty(expected);
}

public partial class Program;
