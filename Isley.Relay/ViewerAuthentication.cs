using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Isley.Relay;

internal static class IsleyClaimTypes
{
    internal const string SteamId = "urn:isley:steamid";
}

internal sealed record AccessTokenPayload(
    string SteamId,
    string TokenId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

internal sealed class AccessTokenService(IDataProtectionProvider dataProtection)
{
    private readonly IDataProtector _protector =
        dataProtection.CreateProtector("Isley.Relay.AccessToken.v1");

    internal string Create(string steamId)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new AccessTokenPayload(
            steamId,
            Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
                .ToLowerInvariant(),
            now,
            now.AddDays(30));
        return _protector.Protect(JsonSerializer.Serialize(payload, IsleyJson.Options));
    }

    internal bool TryRead(string protectedToken, out AccessTokenPayload payload)
    {
        payload = null!;
        try
        {
            var json = _protector.Unprotect(protectedToken);
            var candidate = JsonSerializer.Deserialize<AccessTokenPayload>(json, IsleyJson.Options);
            if (candidate is null
                || !Isley.Telemetry.TelemetryValidation.IsSteamId(candidate.SteamId)
                || candidate.ExpiresAt <= DateTimeOffset.UtcNow
                || candidate.IssuedAt > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return false;
            }
            payload = candidate;
            return true;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return false;
        }
    }
}

internal sealed class IsleyBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AccessTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "IsleyBearer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        var token = authorization["Bearer ".Length..].Trim();
        if (!tokens.TryRead(token, out var payload))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired Isley access token."));
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, payload.SteamId),
            new Claim(IsleyClaimTypes.SteamId, payload.SteamId),
            new Claim("urn:isley:token-id", payload.TokenId)
        ], SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
