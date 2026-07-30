using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Isley.Relay;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

const string ViewerSteamId = "76561198000000001";
var publicOrigin = new Uri("https://relay.example/");

// Shared data-protection key ring so the verifier can craft and tamper with
// payloads through the same protector purpose the relay uses.
var keyRoot = Path.Combine(Path.GetTempPath(), $"isley-device-auth-{Guid.NewGuid():N}");
Directory.CreateDirectory(keyRoot);
var dataProtection = DataProtectionProvider.Create(
    new DirectoryInfo(keyRoot),
    configuration => configuration.SetApplicationName("Isley.Relay"));
var accessTokens = new AccessTokenService(dataProtection);

// --- 1. DeviceAuthorizationStore: device-code flow state machine. ------------
{
    var store = new DeviceAuthorizationStore(accessTokens);
    var authorization = store.Create(publicOrigin);

    Check(Regex.IsMatch(authorization.DeviceCode, "^[a-f0-9]{64}$"),
        "The device code must be a 256-bit lowercase hex secret.");
    Check(Regex.IsMatch(authorization.UserCode, "^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$"),
        "The user code must use the unambiguous alphabet in XXXX-XXXX form.");
    Check(!authorization.UserCode.Contains('0') && !authorization.UserCode.Contains('O')
          && !authorization.UserCode.Contains('1') && !authorization.UserCode.Contains('I'),
        "The user-code alphabet must exclude lookalike characters 0/O/1/I.");
    Check(authorization.ExpiresIn == 600 && authorization.Interval == 2,
        "The device authorization must pin a 10-minute lifetime and 2-second poll interval.");
    Check(authorization.VerificationUri
          == $"https://relay.example/auth/steam/device/{authorization.UserCode}",
        "The verification URI must be rooted at the public origin under auth/steam/device.");

    // Exchange guard rails: nothing about an unknown or malformed code leaks.
    Check(store.Exchange(null).State == DeviceExchangeState.Invalid
          && store.Exchange("").State == DeviceExchangeState.Invalid
          && store.Exchange(new string('f', 64)).State == DeviceExchangeState.Invalid,
        "Unknown or malformed device codes must be invalid, never pending or approved.");

    // Pending is stable and non-consuming until the user approves.
    Check(store.Exchange(authorization.DeviceCode).State == DeviceExchangeState.Pending
          && store.Exchange(authorization.DeviceCode).State == DeviceExchangeState.Pending,
        "A pending device code must stay pending without being consumed.");
    Check(store.Exists(authorization.UserCode)
          && store.Exists(authorization.UserCode.ToLowerInvariant())
          && !store.Exists("AAAA-AAAA"),
        "User-code existence must be normalized and honest.");

    // Approval validation: bad Steam ids and unknown codes are refused.
    Check(!store.Approve(authorization.UserCode, "not-a-steam-id")
          && !store.Approve(authorization.UserCode, "7656119800000000")  // too short
          && !store.Approve("AAAA-AAAA", ViewerSteamId),
        "Approval must reject invalid Steam ids and unknown user codes.");
    Check(store.Exchange(authorization.DeviceCode).State == DeviceExchangeState.Pending,
        "A rejected approval must not accidentally authorize the device.");

    // Approve (with lowercase user code: normalization) then exchange once.
    Check(store.Approve(authorization.UserCode.ToLowerInvariant(), ViewerSteamId),
        "Approving with a normalized user code must succeed.");
    var approved = store.Exchange(authorization.DeviceCode);
    Check(approved.State == DeviceExchangeState.Approved
          && approved.SteamId == ViewerSteamId
          && approved.AccessToken.Length > 0,
        "An approved device must exchange for an access token and Steam id.");
    Check(accessTokens.TryRead(approved.AccessToken, out var approvedPayload)
          && approvedPayload.SteamId == ViewerSteamId,
        "The exchanged access token must round-trip to the approved Steam id.");

    // Replay guard: the approval is single-use; afterwards the code is gone.
    Check(store.Exchange(authorization.DeviceCode).State == DeviceExchangeState.Invalid,
        "A consumed device code must not exchange twice.");
    Check(!store.Exists(authorization.UserCode),
        "A consumed user code must be forgotten.");

    // Codes are unique across creations.
    var second = store.Create(publicOrigin);
    Check(second.DeviceCode != authorization.DeviceCode && second.UserCode != authorization.UserCode,
        "Each device authorization must mint fresh device and user codes.");
}

// --- 2. AccessTokenService: expiry and guard behavior. -----------------------
{
    Check(accessTokens.TryRead(accessTokens.Create(ViewerSteamId), out var payload)
          && payload.SteamId == ViewerSteamId
          && payload.ExpiresAt > DateTimeOffset.UtcNow.AddDays(29)
          && payload.IssuedAt <= DateTimeOffset.UtcNow,
        "A freshly minted access token must read back with a ~30 day expiry.");

    var protector = dataProtection.CreateProtector("Isley.Relay.AccessToken.v1");
    string Craft(AccessTokenPayload value) =>
        protector.Protect(JsonSerializer.Serialize(value, IsleyJson.Options));

    var now = DateTimeOffset.UtcNow;
    Check(!accessTokens.TryRead(Craft(new AccessTokenPayload(
              ViewerSteamId, "crafted-expired", now.AddDays(-31), now.AddDays(-1))), out _),
        "An expired token payload must be rejected.");
    Check(!accessTokens.TryRead(Craft(new AccessTokenPayload(
              ViewerSteamId, "crafted-future", now.AddMinutes(5), now.AddDays(30))), out _),
        "A token issued too far in the future must be rejected (clock-skew guard).");
    Check(accessTokens.TryRead(Craft(new AccessTokenPayload(
              ViewerSteamId, "crafted-skew", now.AddMinutes(1), now.AddDays(30))), out var skewed)
          && skewed.TokenId == "crafted-skew",
        "A token inside the two-minute issue-time tolerance must still be accepted.");
    Check(!accessTokens.TryRead(Craft(new AccessTokenPayload(
              "1234", "crafted-bad-id", now, now.AddDays(30))), out _),
        "A token carrying a malformed Steam id must be rejected.");
    Check(!accessTokens.TryRead(protector.Protect("{not json"), out _),
        "An undecipherable token payload must be rejected.");
    Check(!accessTokens.TryRead("garbage-token", out _),
        "A garbage token string must be rejected.");

    var wrongPurpose = dataProtection.CreateProtector("Isley.Relay.AccessToken.v2");
    Check(!accessTokens.TryRead(wrongPurpose.Protect(JsonSerializer.Serialize(
              new AccessTokenPayload(ViewerSteamId, "wrong-purpose", now, now.AddDays(30)),
              IsleyJson.Options)), out _),
        "A token protected for another purpose must fail closed.");

    var valid = accessTokens.Create(ViewerSteamId);
    var tampered = valid[..^1] + (valid[^1] == 'a' ? 'b' : 'a');
    Check(!accessTokens.TryRead(tampered, out _),
        "A tampered token must fail integrity verification.");
}

// --- 3. SteamOpenIdClient: login URI and callback validation. ----------------
{
    var steamOptions = Options.Create(new SteamOptions());
    const string userCode = "ABCD-2345";

    var handler = new StubHttpMessageHandler();
    var client = new SteamOpenIdClient(new HttpClient(handler), steamOptions);

    var loginUri = client.BuildLoginUri(userCode, publicOrigin);
    var loginQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(loginUri.Query);
    Check(loginUri.GetLeftPart(UriPartial.Path) == "https://steamcommunity.com/openid/login",
        "The Steam login must target the configured OpenID endpoint only.");
    Check(loginQuery["openid.ns"] == "http://specs.openid.net/auth/2.0"
          && loginQuery["openid.mode"] == "checkid_setup"
          && loginQuery["openid.return_to"]
             == $"https://relay.example/auth/steam/callback?device={userCode}"
          && loginQuery["openid.realm"] == "https://relay.example/"
          && loginQuery["openid.identity"] == "http://specs.openid.net/auth/2.0/identifier_select"
          && loginQuery["openid.claimed_id"] == "http://specs.openid.net/auth/2.0/identifier_select",
        "The Steam OpenID login parameters drifted from the pinned contract.");

    QueryCollection Callback(
        string mode,
        string endpoint,
        string returnTo,
        string claimedId) => new(new Dictionary<string, StringValues>
    {
        ["openid.mode"] = mode,
        ["openid.op_endpoint"] = endpoint,
        ["openid.return_to"] = returnTo,
        ["openid.claimed_id"] = claimedId
    });

    var validReturnTo = $"https://relay.example/auth/steam/callback?device={userCode}";
    const string validClaimed = "https://steamcommunity.com/openid/id/76561198000000001";

    // Guard failures must short-circuit before any HTTP assertion is made.
    Check(await client.ValidateCallbackAsync(
              Callback("checkid_setup", "https://steamcommunity.com/openid/login", validReturnTo, validClaimed),
              userCode, publicOrigin, CancellationToken.None) is null
          && handler.CallCount == 0,
        "A non id_res callback must be rejected without contacting Steam.");
    Check(await client.ValidateCallbackAsync(
              Callback("id_res", "https://evil.example/openid/login", validReturnTo, validClaimed),
              userCode, publicOrigin, CancellationToken.None) is null
          && handler.CallCount == 0,
        "A callback from a foreign OP endpoint must be rejected without contacting Steam.");
    Check(await client.ValidateCallbackAsync(
              Callback("id_res", "https://steamcommunity.com/openid/login",
                  "https://evil.example/auth/steam/callback?device=" + userCode, validClaimed),
              userCode, publicOrigin, CancellationToken.None) is null
          && handler.CallCount == 0,
        "A return_to pointing off-origin must be rejected without contacting Steam.");
    Check(await client.ValidateCallbackAsync(
              Callback("id_res", "https://steamcommunity.com/openid/login", validReturnTo,
                  "https://steamcommunity.com/openid/id/1234"),
              userCode, publicOrigin, CancellationToken.None) is null
          && handler.CallCount == 0,
        "A malformed claimed id must be rejected without contacting Steam.");
    Check(await client.ValidateCallbackAsync(
              Callback("id_res", "https://steamcommunity.com/openid/login", validReturnTo,
                  "http://steamcommunity.com/openid/id/76561198000000001"),
              userCode, publicOrigin, CancellationToken.None) is null
          && handler.CallCount == 0,
        "A claimed id must be HTTPS, never plain HTTP.");
    Check(await client.ValidateCallbackAsync(
              Callback("id_res", "https://steamcommunity.com/openid/login", validReturnTo,
                  "https://steamcommunity.com/openid/id/765611980000000011"),
              userCode, publicOrigin, CancellationToken.None) is null
          && handler.CallCount == 0,
        "An 18-digit claimed id must not prefix-match the SteamID64 shape.");

    // Steam-side assertion outcomes.
    handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n",
            Encoding.UTF8,
            "text/plain")
    });
    var steamId = await client.ValidateCallbackAsync(
        Callback("id_res", "https://steamcommunity.com/openid/login", validReturnTo, validClaimed),
        userCode, publicOrigin, CancellationToken.None);
    Check(steamId == ViewerSteamId,
        "A valid Steam assertion must yield the claimed SteamID64.");
    Check(handler.CallCount == 1 && handler.LastBody!.Contains("openid.mode=check_authentication", StringComparison.Ordinal),
        "The relay must verify the assertion back with Steam via check_authentication.");

    handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("ns:http://specs.openid.net/auth/2.0\nis_valid:false\n")
    });
    Check(await client.ValidateCallbackAsync(
              Callback("id_res", "https://steamcommunity.com/openid/login", validReturnTo, validClaimed),
              userCode, publicOrigin, CancellationToken.None) is null,
        "An is_valid:false assertion must be rejected.");

    handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
    Check(await client.ValidateCallbackAsync(
              Callback("id_res", "https://steamcommunity.com/openid/login", validReturnTo, validClaimed),
              userCode, publicOrigin, CancellationToken.None) is null,
        "A failed Steam verification call must fail closed.");
}

try
{
    Directory.Delete(keyRoot, recursive: true);
}
catch (IOException)
{
}

Console.WriteLine(
    "Steam device authorization verification passed: device/user-code minting, "
    + "pending-approved-consumed state machine, single-use exchange replay guard, "
    + "user-code normalization, token expiry/skew/tamper guards, OpenID login URI "
    + "pinning, and callback validation with HTTP-edge assertion outcomes.");

sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private Func<HttpRequestMessage, HttpResponseMessage> _responder =
        _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

    internal int CallCount { get; private set; }
    internal string? LastBody { get; private set; }

    internal void Respond(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return _responder(request);
    }
}
