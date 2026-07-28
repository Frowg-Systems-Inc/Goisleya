using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Isley.Relay;

internal sealed record DeviceAuthorizationResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval);

internal enum DeviceExchangeState
{
    Invalid,
    Pending,
    Approved,
    Expired
}

internal sealed record DeviceExchangeResult(
    DeviceExchangeState State,
    string AccessToken = "",
    string SteamId = "");

internal sealed class DeviceAuthorizationStore(AccessTokenService accessTokens)
{
    private static readonly char[] UserCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
    private readonly ConcurrentDictionary<string, PendingDevice> _byDeviceCode = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _deviceCodeByUserCode = new(StringComparer.Ordinal);

    internal DeviceAuthorizationResponse Create(Uri publicOrigin)
    {
        Cleanup();
        string deviceCode;
        string userCode;
        do
        {
            deviceCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            userCode = CreateUserCode();
        } while (!_deviceCodeByUserCode.TryAdd(userCode, deviceCode));

        var pending = new PendingDevice(deviceCode, userCode, DateTimeOffset.UtcNow.AddMinutes(10));
        _byDeviceCode[deviceCode] = pending;
        var verification = new Uri(publicOrigin, $"auth/steam/device/{userCode}");
        return new DeviceAuthorizationResponse(deviceCode, userCode, verification.AbsoluteUri, 600, 2);
    }

    internal bool Exists(string userCode) =>
        TryFindByUserCode(NormalizeUserCode(userCode), out var device)
        && device.ExpiresAt > DateTimeOffset.UtcNow;

    internal bool Approve(string userCode, string steamId)
    {
        if (!Isley.Telemetry.TelemetryValidation.IsSteamId(steamId)
            || !TryFindByUserCode(NormalizeUserCode(userCode), out var pending)
            || pending.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }
        pending.Approve(steamId, accessTokens.Create(steamId));
        return true;
    }

    internal DeviceExchangeResult Exchange(string? deviceCode)
    {
        Cleanup();
        if (string.IsNullOrWhiteSpace(deviceCode)
            || !_byDeviceCode.TryGetValue(deviceCode, out var pending))
        {
            return new DeviceExchangeResult(DeviceExchangeState.Invalid);
        }
        if (pending.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            Remove(pending);
            return new DeviceExchangeResult(DeviceExchangeState.Expired);
        }
        if (!pending.TryTakeApproval(out var steamId, out var accessToken))
        {
            return new DeviceExchangeResult(DeviceExchangeState.Pending);
        }
        Remove(pending);
        return new DeviceExchangeResult(DeviceExchangeState.Approved, accessToken, steamId);
    }

    private bool TryFindByUserCode(string userCode, out PendingDevice pending)
    {
        pending = null!;
        if (!_deviceCodeByUserCode.TryGetValue(userCode, out var deviceCode)
            || !_byDeviceCode.TryGetValue(deviceCode, out var found))
        {
            return false;
        }
        pending = found;
        return true;
    }

    private void Cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pending in _byDeviceCode.Values.Where(value => value.ExpiresAt <= now))
        {
            Remove(pending);
        }
    }

    private void Remove(PendingDevice pending)
    {
        _byDeviceCode.TryRemove(pending.DeviceCode, out _);
        _deviceCodeByUserCode.TryRemove(pending.UserCode, out _);
    }

    private static string CreateUserCode()
    {
        Span<byte> random = stackalloc byte[8];
        RandomNumberGenerator.Fill(random);
        Span<char> characters = stackalloc char[9];
        for (var index = 0; index < 8; index++)
        {
            characters[index + (index >= 4 ? 1 : 0)] =
                UserCodeAlphabet[random[index] % UserCodeAlphabet.Length];
        }
        characters[4] = '-';
        return new string(characters);
    }

    private static string NormalizeUserCode(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    private sealed class PendingDevice(
        string deviceCode,
        string userCode,
        DateTimeOffset expiresAt)
    {
        private readonly object _gate = new();
        private string _steamId = string.Empty;
        private string _accessToken = string.Empty;

        internal string DeviceCode { get; } = deviceCode;
        internal string UserCode { get; } = userCode;
        internal DateTimeOffset ExpiresAt { get; } = expiresAt;

        internal void Approve(string steamId, string accessToken)
        {
            lock (_gate)
            {
                _steamId = steamId;
                _accessToken = accessToken;
            }
        }

        internal bool TryTakeApproval(out string steamId, out string accessToken)
        {
            lock (_gate)
            {
                steamId = _steamId;
                accessToken = _accessToken;
                return !string.IsNullOrEmpty(_steamId) && !string.IsNullOrEmpty(_accessToken);
            }
        }
    }
}

internal sealed partial class SteamOpenIdClient(
    HttpClient httpClient,
    IOptions<SteamOptions> options)
{
    private readonly SteamOptions _options = options.Value;

    internal Uri BuildLoginUri(string userCode, Uri publicOrigin)
    {
        var returnTo = new Uri(
            publicOrigin,
            $"auth/steam/callback?device={Uri.EscapeDataString(userCode)}");
        var parameters = new Dictionary<string, string?>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnTo.AbsoluteUri,
            ["openid.realm"] = publicOrigin.AbsoluteUri,
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select"
        };
        return new Uri(QueryHelpers.AddQueryString(_options.OpenIdEndpoint, parameters));
    }

    internal async Task<string?> ValidateCallbackAsync(
        IQueryCollection query,
        string userCode,
        Uri publicOrigin,
        CancellationToken cancellationToken)
    {
        var expectedReturnTo = new Uri(
            publicOrigin,
            $"auth/steam/callback?device={Uri.EscapeDataString(userCode)}").AbsoluteUri;
        if (!string.Equals(query["openid.mode"], "id_res", StringComparison.Ordinal)
            || !string.Equals(query["openid.op_endpoint"], _options.OpenIdEndpoint, StringComparison.Ordinal)
            || !string.Equals(query["openid.return_to"], expectedReturnTo, StringComparison.Ordinal))
        {
            return null;
        }

        var claimedId = query["openid.claimed_id"].ToString();
        var match = ClaimedIdRegex().Match(claimedId);
        if (!match.Success)
        {
            return null;
        }

        var fields = query
            .Where(item => item.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(item => item.Key, item => item.Value.ToString(), StringComparer.Ordinal);
        fields["openid.mode"] = "check_authentication";
        using var response = await httpClient.PostAsync(
            _options.OpenIdEndpoint,
            new FormUrlEncodedContent(fields),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(line => string.Equals(line.Trim(), "is_valid:true", StringComparison.Ordinal))
            ? match.Groups["steamId"].Value
            : null;
    }

    [GeneratedRegex(
        "^https://steamcommunity\\.com/openid/id/(?<steamId>7656119[0-9]{10})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ClaimedIdRegex();
}
