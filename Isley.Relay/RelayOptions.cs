using System.Net;

namespace Isley.Relay;

public sealed class RelayOptions
{
    public string PublicBaseUrl { get; init; } = string.Empty;
    public string DataProtectionKeysPath { get; init; } = "data/keys";
    public string StatePath { get; init; } = "data/state";
    public int FrameFreshnessSeconds { get; init; } = 10;
    public int AllowedClockSkewSeconds { get; init; } = 30;
    public BridgeRegistration[] Bridges { get; init; } = [];

    public static bool IsValid(RelayOptions value) =>
        value.FrameFreshnessSeconds is >= 2 and <= 60
        && value.AllowedClockSkewSeconds is >= 5 and <= 120
        && value.Bridges.Length <= 10_000
        && value.Bridges.Select(bridge => bridge.ServerId)
            .Distinct(StringComparer.Ordinal)
            .Count() == value.Bridges.Length
        && value.Bridges.All(BridgeRegistration.IsValid)
        && ValidPublicBaseUrl(value.PublicBaseUrl);

    private static bool ValidPublicBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            return false;
        }
        return uri.Scheme == Uri.UriSchemeHttps
               || (uri.Scheme == Uri.UriSchemeHttp
                   && (IPAddress.TryParse(uri.Host, out var address)
                       && IPAddress.IsLoopback(address)
                       || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class BridgeRegistration
{
    public string ServerId { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;

    internal static bool IsValid(BridgeRegistration value) =>
        Isley.Telemetry.TelemetryValidation.Validate(new()
        {
            ServerId = value.ServerId,
            BridgeSessionId = new string('a', 32),
            Sequence = 1,
            SampledAt = DateTimeOffset.UtcNow
        }, DateTimeOffset.UtcNow).All(error => !error.StartsWith("ServerId", StringComparison.Ordinal))
        && value.Secret.Length >= 32;
}

public sealed class SteamOptions
{
    public string OpenIdEndpoint { get; init; } = "https://steamcommunity.com/openid/login";
    public string WebApiKey { get; init; } = string.Empty;
    public int FriendCacheSeconds { get; init; } = 300;

    public static bool IsValid(SteamOptions value) =>
        Uri.TryCreate(value.OpenIdEndpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "steamcommunity.com", StringComparison.OrdinalIgnoreCase)
        && value.FriendCacheSeconds is >= 30 and <= 3600;
}

internal static class RelayUris
{
    internal static bool TryResolvePublicOrigin(
        HttpRequest request,
        RelayOptions options,
        out Uri origin)
    {
        if (Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var configured))
        {
            origin = configured;
            return true;
        }

        // Host-header fallback is loopback-only so reverse proxies cannot mint
        // Steam return URLs from an unconfigured PublicBaseUrl.
        var host = request.Host.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address))
        {
            origin = new Uri($"{request.Scheme}://{request.Host}/");
            return true;
        }

        origin = null!;
        return false;
    }

    internal static Uri ResolvePublicOrigin(HttpRequest request, RelayOptions options)
    {
        if (TryResolvePublicOrigin(request, options, out var origin))
        {
            return origin;
        }

        throw new InvalidOperationException(
            "Relay__PublicBaseUrl must be configured for non-loopback Steam sign-in.");
    }
}
