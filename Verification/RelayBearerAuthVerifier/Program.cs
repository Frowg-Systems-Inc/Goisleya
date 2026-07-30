using System.Text.Encodings.Web;
using System.Text.Json;
using Isley.Relay;
using Microsoft.AspNetCore.Authentication;
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

const string ViewerSteamId = "76561198000000001";

var keyRoot = Path.Combine(Path.GetTempPath(), $"isley-bearer-auth-{Guid.NewGuid():N}");
Directory.CreateDirectory(keyRoot);
var dataProtection = DataProtectionProvider.Create(
    new DirectoryInfo(keyRoot),
    configuration => configuration.SetApplicationName("Isley.Relay"));
var tokens = new AccessTokenService(dataProtection);

IsleyBearerHandler CreateHandler() => new(
    new StubOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
    NullLoggerFactory.Instance,
    UrlEncoder.Default,
    tokens);

async Task<AuthenticateResult> AuthenticateAsync(string? authorizationHeader)
{
    var handler = CreateHandler();
    var context = new DefaultHttpContext();
    if (authorizationHeader is not null)
    {
        context.Request.Headers.Authorization = authorizationHeader;
    }
    var scheme = new AuthenticationScheme(
        IsleyBearerHandler.SchemeName,
        null,
        typeof(IsleyBearerHandler));
    await handler.InitializeAsync(scheme, context);
    return await handler.AuthenticateAsync();
}

Check(IsleyBearerHandler.SchemeName == "IsleyBearer",
    "The bearer scheme name is part of the relay's auth contract.");

var validToken = tokens.Create(ViewerSteamId);

// --- Accept path -------------------------------------------------------------
{
    var result = await AuthenticateAsync($"Bearer {validToken}");
    Check(result.Succeeded, "A valid bearer token was not accepted.");
    var principal = result.Ticket!.Principal;
    Check(principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value == ViewerSteamId
          && principal.FindFirst(IsleyClaimTypes.SteamId)?.Value == ViewerSteamId
          && !string.IsNullOrEmpty(principal.FindFirst("urn:isley:token-id")?.Value)
          && principal.Identity!.AuthenticationType == IsleyBearerHandler.SchemeName,
        "The accepted identity must carry the Steam id and token id claims under the IsleyBearer scheme.");

    // Case-insensitive scheme keyword and tolerant token trimming.
    Check((await AuthenticateAsync($"bearer {validToken}")).Succeeded,
        "The Bearer keyword must be case-insensitive.");
    Check((await AuthenticateAsync($"BEARER {validToken}")).Succeeded,
        "The Bearer keyword must be case-insensitive.");
    Check((await AuthenticateAsync($"Bearer   {validToken}  ")).Succeeded,
        "Surrounding whitespace around the token must be trimmed.");

    // A second viewer's token resolves to a different identity.
    var otherToken = tokens.Create("76561198000000002");
    var other = await AuthenticateAsync($"Bearer {otherToken}");
    Check(other.Succeeded
          && other.Ticket!.Principal.FindFirst(IsleyClaimTypes.SteamId)?.Value == "76561198000000002",
        "Each token must resolve to its own Steam identity.");
}

// --- NoResult path: not a bearer attempt at all ------------------------------
{
    Check((await AuthenticateAsync(null)).None,
        "A missing Authorization header must yield NoResult.");
    Check((await AuthenticateAsync("Basic dXNlcjpwYXNz")).None,
        "A non-Bearer scheme must yield NoResult, not a failure.");
    Check((await AuthenticateAsync("Bearer")).None,
        "A bare keyword without the separator space is not a bearer attempt.");
}

// --- Reject matrix: presented bearer credentials that must fail --------------
{
    Check((await AuthenticateAsync("Bearer ")).Failure is not null,
        "An empty bearer token must fail.");
    Check((await AuthenticateAsync("Bearer   ")).Failure is not null,
        "A whitespace-only bearer token must fail.");
    var garbage = await AuthenticateAsync("Bearer garbage-token");
    Check(garbage.Failure?.Message == "Invalid or expired Isley access token.",
        "A garbage token must fail with the honest invalid-or-expired message.");

    var tampered = validToken[..^1] + (validToken[^1] == 'a' ? 'b' : 'a');
    Check((await AuthenticateAsync($"Bearer {tampered}")).Failure is not null,
        "A token with a flipped character must fail integrity verification.");

    var protector = dataProtection.CreateProtector("Isley.Relay.AccessToken.v1");
    string Craft(AccessTokenPayload value) =>
        protector.Protect(JsonSerializer.Serialize(value, IsleyJson.Options));
    var now = DateTimeOffset.UtcNow;

    Check((await AuthenticateAsync($"Bearer {Craft(new AccessTokenPayload(
              ViewerSteamId, "expired", now.AddDays(-31), now.AddDays(-1)))}")).Failure is not null,
        "An expired token must be rejected.");
    Check((await AuthenticateAsync($"Bearer {Craft(new AccessTokenPayload(
              ViewerSteamId, "future", now.AddMinutes(5), now.AddDays(30)))}")).Failure is not null,
        "A token issued beyond the clock-skew tolerance must be rejected.");
    Check((await AuthenticateAsync($"Bearer {Craft(new AccessTokenPayload(
              "not-a-steam-id", "bad-id", now, now.AddDays(30)))}")).Failure is not null,
        "A token with a malformed Steam id must be rejected.");

    var wrongPurpose = dataProtection.CreateProtector("Isley.Relay.AccessToken.v2");
    var foreign = wrongPurpose.Protect(JsonSerializer.Serialize(
        new AccessTokenPayload(ViewerSteamId, "foreign", now, now.AddDays(30)),
        IsleyJson.Options));
    Check((await AuthenticateAsync($"Bearer {foreign}")).Failure is not null,
        "A token protected for another purpose must fail closed.");
}

// --- Cryptographic posture of the auth edge (contract greps). ----------------
{
    var root = Directory.GetCurrentDirectory();
    var viewerAuth = File.ReadAllText(Path.Combine(root, "Isley.Relay", "ViewerAuthentication.cs"));
    Check(viewerAuth.Contains("_protector.Unprotect(protectedToken)", StringComparison.Ordinal)
          && !viewerAuth.Contains("== token", StringComparison.Ordinal)
          && !viewerAuth.Contains("Equals(token", StringComparison.Ordinal),
        "Bearer tokens must be authenticated by AEAD unprotect, never by string comparison.");
    var bridgeAuth = File.ReadAllText(Path.Combine(root, "Isley.Relay", "BridgeAuthentication.cs"));
    Check(bridgeAuth.Contains("CryptographicOperations.FixedTimeEquals(expected, supplied)", StringComparison.Ordinal),
        "The bridge signature comparison must stay fixed-time.");
    var relayProgram = File.ReadAllText(Path.Combine(root, "Isley.Relay", "Program.cs"));
    Check(relayProgram.Contains(".AddScheme<AuthenticationSchemeOptions, IsleyBearerHandler>(", StringComparison.Ordinal)
          && relayProgram.Contains("app.UseAuthentication();", StringComparison.Ordinal)
          && relayProgram.Contains("app.UseAuthorization();", StringComparison.Ordinal)
          && relayProgram.Contains("RequireAuthorization()", StringComparison.Ordinal),
        "The relay must wire the bearer handler and require authorization on the API group.");
}

try
{
    Directory.Delete(keyRoot, recursive: true);
}
catch (IOException)
{
}

Console.WriteLine(
    "Relay bearer authentication verification passed: accept matrix with claims "
    + "and trimming, NoResult for non-bearer attempts, reject matrix for empty, "
    + "garbage, tampered, expired, future-issued, malformed-id and foreign-purpose "
    + "tokens, and fixed-time/AEAD posture at the auth edge.");

sealed class StubOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
