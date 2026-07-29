using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Isley.Relay;
using Isley.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddOptions<RelayOptions>()
    .Bind(builder.Configuration.GetSection("Relay"))
    .Validate(RelayOptions.IsValid, "Relay configuration contains an invalid value.")
    .ValidateOnStart();
builder.Services
    .AddOptions<SteamOptions>()
    .Bind(builder.Configuration.GetSection("Steam"))
    .Validate(SteamOptions.IsValid, "Steam configuration contains an invalid value.")
    .ValidateOnStart();

var configuredKeyPath = builder.Configuration["Relay:DataProtectionKeysPath"];
if (!string.IsNullOrWhiteSpace(configuredKeyPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("Isley.Relay")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(configuredKeyPath)));
}
else
{
    builder.Services.AddDataProtection()
        .SetApplicationName("Isley.Relay");
}

builder.Services
    .AddAuthentication(IsleyBearerHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, IsleyBearerHandler>(
        IsleyBearerHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<RelayReadinessHealthCheck>("relay-ready");
builder.Services.AddHttpClient<SteamOpenIdClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<SteamFriendResolver>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddSingleton<AccessTokenService>();
builder.Services.AddSingleton<DeviceAuthorizationStore>();
builder.Services.AddSingleton<BridgeReplayGuard>();
builder.Services.AddSingleton<BridgeSignatureVerifier>();
builder.Services.AddSingleton<PrivacyStore>();
builder.Services.AddSingleton<TelemetryFrameStore>();
builder.Services.AddSingleton<RelayMetrics>();
builder.Services.AddSingleton<TelemetryBroker>();
builder.Services.AddSingleton<IFriendResolver>(provider =>
    provider.GetRequiredService<SteamFriendResolver>());
builder.Services.AddSingleton<SteamFriendResolver>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.RequestServices
            .GetService<RelayMetrics>()
            ?.RateLimitRejected();
        return ValueTask.CompletedTask;
    };
    options.AddFixedWindowLimiter("device", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("ingest", limiter =>
    {
        limiter.PermitLimit = 2400;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Name == "relay-ready"
});
app.MapGet("/metrics", (
    HttpContext context,
    RelayMetrics metrics,
    TelemetryFrameStore frames,
    TelemetryBroker broker,
    IOptions<RelayOptions> relayOptions) =>
{
    // Loopback-only by default, matching the bridge status UI posture; an
    // operator may widen it explicitly. The payload is aggregate counters
    // only — no bridge server IDs, Steam IDs, or entity data.
    if (!relayOptions.Value.MetricsPubliclyVisible
        && context.Connection.RemoteIpAddress is { } remote
        && !IPAddress.IsLoopback(remote))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    return Results.Json(metrics.Snapshot(frames.CountFresh(), broker.ActiveViewerCount));
});
app.MapGet("/", () => TypedResults.Ok(new
{
    service = "Isley Relay",
    protocolVersion = TelemetryProtocol.Version,
    authentication = "Steam OpenID device authorization",
    transport = "HTTPS ingest and authenticated WebSocket delivery",
    privacy = "self plus consented friends and server-authorized entities",
    metrics = "aggregate counters on loopback-gated /metrics",
    rconExposed = false
}));
app.MapGet("/join/{serverId}", (
    string serverId,
    HttpContext context,
    IOptions<RelayOptions> relayOptions) =>
{
    if (!IsSafeJoinServerId(serverId))
    {
        return Results.BadRequest(new { error = "invalid_server_id" });
    }

    var origin = RelayUris.ResolvePublicOrigin(context.Request, relayOptions.Value)
        .AbsoluteUri
        .TrimEnd('/');
    var joinUrl = System.Net.WebUtility.HtmlEncode($"{origin}/join/{serverId}");
    var safeServer = System.Net.WebUtility.HtmlEncode(serverId);
    var html =
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
        "<title>Join Isley Live Network</title>" +
        "<style>body{font-family:Segoe UI,sans-serif;background:#071018;color:#e2e8f0;padding:28px;max-width:640px}" +
        "code{color:#7dd3fc} button{margin-top:12px;padding:8px 12px;border:0;border-radius:8px;background:#0ea5e9;color:#041016;font-weight:700}</style></head><body>" +
        "<h1>Join with Isley</h1>" +
        "<p>Paste this link into Isley → Tools → Isley Live Network, then connect with Steam.</p>" +
        "<p>Server ID: <strong>" + safeServer + "</strong></p>" +
        "<p><code id=\"join\">" + joinUrl + "</code></p>" +
        "<button type=\"button\" onclick=\"navigator.clipboard.writeText(document.getElementById('join').textContent)\">Copy join link</button>" +
        "<p>Isley is an external companion. This page never asks for a Steam password or RCON secret.</p>" +
        "</body></html>";
    return Results.Content(html, "text/html; charset=utf-8");
});

static bool IsSafeJoinServerId(string? serverId)
{
    if (string.IsNullOrWhiteSpace(serverId) || serverId.Length is < 2 or > 64)
    {
        return false;
    }

    foreach (var ch in serverId)
    {
        if (!char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_')
        {
            return false;
        }
    }

    return true;
}

var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("device");
auth.MapPost("/device", (
    HttpContext context,
    DeviceAuthorizationStore devices,
    IOptions<RelayOptions> relayOptions) =>
{
    if (!RelayUris.TryResolvePublicOrigin(
            context.Request,
            relayOptions.Value,
            out var origin))
    {
        return Results.Json(
            new { error = "public_base_url_required" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    var authorization = devices.Create(origin);
    return TypedResults.Ok(authorization);
});
auth.MapPost("/device/token", (
    DeviceTokenRequest request,
    DeviceAuthorizationStore devices) =>
{
    var result = devices.Exchange(request.DeviceCode);
    return result.State switch
    {
        DeviceExchangeState.Approved => Results.Ok(new
        {
            accessToken = result.AccessToken,
            tokenType = "Bearer",
            expiresIn = (int)TimeSpan.FromDays(30).TotalSeconds,
            steamId = result.SteamId
        }),
        DeviceExchangeState.Pending => Results.Json(
            new { error = "authorization_pending" },
            statusCode: StatusCodes.Status428PreconditionRequired),
        DeviceExchangeState.Expired => Results.BadRequest(new { error = "expired_token" }),
        _ => Results.BadRequest(new { error = "invalid_device_code" })
    };
});

app.MapGet("/auth/steam/device/{userCode}", (
    string userCode,
    HttpContext context,
    DeviceAuthorizationStore devices,
    SteamOpenIdClient steam,
    IOptions<RelayOptions> relayOptions) =>
{
    if (!devices.Exists(userCode))
    {
        return Results.BadRequest("This Isley sign-in code is invalid or expired.");
    }
    if (!RelayUris.TryResolvePublicOrigin(
            context.Request,
            relayOptions.Value,
            out var origin))
    {
        return Results.BadRequest(
            "This Isley relay is missing PublicBaseUrl and cannot start Steam sign-in.");
    }
    return Results.Redirect(steam.BuildLoginUri(userCode, origin).AbsoluteUri);
});
app.MapGet("/auth/steam/callback", async (
    HttpContext context,
    DeviceAuthorizationStore devices,
    SteamOpenIdClient steam,
    IOptions<RelayOptions> relayOptions,
    CancellationToken cancellationToken) =>
{
    var userCode = context.Request.Query["device"].ToString();
    if (!devices.Exists(userCode))
    {
        return Results.BadRequest("This Isley sign-in request is invalid or expired.");
    }

    if (!RelayUris.TryResolvePublicOrigin(
            context.Request,
            relayOptions.Value,
            out var origin))
    {
        return Results.BadRequest(
            "This Isley relay is missing PublicBaseUrl and cannot finish Steam sign-in.");
    }
    var steamId = await steam.ValidateCallbackAsync(
        context.Request.Query,
        userCode,
        origin,
        cancellationToken);
    if (steamId is null || !devices.Approve(userCode, steamId))
    {
        return Results.BadRequest("Steam could not verify this Isley sign-in.");
    }

    const string html = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
        <title>Isley connected</title>
        <style>body{font-family:system-ui;background:#071019;color:#eef7ff;display:grid;place-items:center;min-height:100vh;margin:0}
        main{max-width:32rem;padding:2rem;border:1px solid #2dd4bf55;border-radius:1rem;background:#0c1722}
        h1{color:#5eead4}p{line-height:1.5;color:#cbd5e1}</style></head>
        <body><main><h1>Steam connected to Isley</h1>
        <p>You can close this window. Isley will finish connecting automatically.</p></main></body></html>
        """;
    return Results.Content(html, "text/html");
});

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapGet("/me", (ClaimsPrincipal user, PrivacyStore privacy) =>
{
    var steamId = user.FindFirstValue(IsleyClaimTypes.SteamId)!;
    return TypedResults.Ok(new
    {
        steamId,
        privacy = privacy.Get(steamId)
    });
});
api.MapPut("/privacy", async (
    ClaimsPrincipal user,
    PrivacyUpdateRequest request,
    PrivacyStore privacy,
    CancellationToken cancellationToken) =>
{
    var steamId = user.FindFirstValue(IsleyClaimTypes.SteamId)!;
    var result = await privacy.UpdateAsync(
        steamId,
        request.ShareWithSteamFriends,
        cancellationToken);
    return TypedResults.Ok(result);
});
api.MapPut("/privacy/grants/{viewerSteamId}", async (
    string viewerSteamId,
    ClaimsPrincipal user,
    PrivacyStore privacy,
    CancellationToken cancellationToken) =>
{
    if (!TelemetryValidation.IsSteamId(viewerSteamId))
    {
        return Results.BadRequest(new { error = "invalid_steam_id" });
    }
    var steamId = user.FindFirstValue(IsleyClaimTypes.SteamId)!;
    var result = await privacy.GrantAsync(steamId, viewerSteamId, cancellationToken);
    return Results.Ok(result);
});
api.MapDelete("/privacy/grants/{viewerSteamId}", async (
    string viewerSteamId,
    ClaimsPrincipal user,
    PrivacyStore privacy,
    CancellationToken cancellationToken) =>
{
    var steamId = user.FindFirstValue(IsleyClaimTypes.SteamId)!;
    var result = await privacy.RevokeAsync(steamId, viewerSteamId, cancellationToken);
    return Results.Ok(result);
});
api.MapGet("/servers", (
    ClaimsPrincipal user,
    TelemetryFrameStore frames) =>
{
    var steamId = user.FindFirstValue(IsleyClaimTypes.SteamId)!;
    return TypedResults.Ok(frames.ListForViewer(steamId));
});
api.Map("/live/{serverId}", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        return;
    }

    var serverId = context.Request.RouteValues["serverId"]?.ToString() ?? string.Empty;
    var steamId = context.User.FindFirstValue(IsleyClaimTypes.SteamId);
    if (steamId is null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var broker = context.RequestServices.GetRequiredService<TelemetryBroker>();
    await broker.RunViewerAsync(serverId, steamId, socket, context.RequestAborted);
});

app.MapPost("/api/v1/ingest", async (
    HttpContext context,
    BridgeSignatureVerifier signatures,
    TelemetryFrameStore frames,
    TelemetryBroker broker,
    CancellationToken cancellationToken) =>
{
    var maxBody = TelemetryProtocol.MaximumFrameBytes;
    context.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = maxBody;
    var body = await RequestBodyReader.ReadAsync(context.Request, maxBody, cancellationToken);
    var authorization = signatures.Verify(context.Request, body);
    if (!authorization.Accepted)
    {
        return Results.Json(
            new { error = authorization.Error },
            statusCode: authorization.StatusCode);
    }

    TelemetryFrame? frame;
    try
    {
        frame = JsonSerializer.Deserialize<TelemetryFrame>(
            body,
            IsleyJson.Options);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "invalid_json" });
    }
    if (frame is null || !string.Equals(
            frame.ServerId,
            authorization.ServerId,
            StringComparison.Ordinal))
    {
        return Results.BadRequest(new { error = "server_id_mismatch" });
    }

    var errors = TelemetryValidation.Validate(frame, DateTimeOffset.UtcNow);
    if (errors.Count > 0)
    {
        return Results.BadRequest(new { error = "invalid_frame", details = errors });
    }
    if (!frames.TryAccept(frame, out var state))
    {
        return Results.Conflict(new { error = state });
    }

    await broker.PublishAsync(frame, cancellationToken);
    return Results.Accepted(value: new
    {
        frame.ServerId,
        frame.BridgeSessionId,
        frame.Sequence,
        receivedAt = DateTimeOffset.UtcNow
    });
}).RequireRateLimiting("ingest");

app.Run();

namespace Isley.Relay
{
    public sealed record DeviceTokenRequest(string DeviceCode);
    public sealed record PrivacyUpdateRequest(bool ShareWithSteamFriends);
}

public partial class Program;
