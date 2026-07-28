using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Isley.Relay;

internal sealed record BridgeAuthorizationResult(
    bool Accepted,
    int StatusCode,
    string Error,
    string ServerId)
{
    internal static BridgeAuthorizationResult Allow(string serverId) =>
        new(true, StatusCodes.Status200OK, string.Empty, serverId);

    internal static BridgeAuthorizationResult Deny(int statusCode, string error) =>
        new(false, statusCode, error, string.Empty);
}

internal sealed partial class BridgeSignatureVerifier(
    IOptions<RelayOptions> options,
    BridgeReplayGuard replayGuard)
{
    internal const string ServerHeader = "X-Isley-Server";
    internal const string TimestampHeader = "X-Isley-Timestamp";
    internal const string NonceHeader = "X-Isley-Nonce";
    internal const string SignatureHeader = "X-Isley-Signature";

    private readonly RelayOptions _options = options.Value;

    internal BridgeAuthorizationResult Verify(HttpRequest request, ReadOnlySpan<byte> body)
    {
        var serverId = request.Headers[ServerHeader].ToString();
        var timestampText = request.Headers[TimestampHeader].ToString();
        var nonce = request.Headers[NonceHeader].ToString();
        var suppliedSignature = request.Headers[SignatureHeader].ToString();
        var registration = _options.Bridges.FirstOrDefault(candidate =>
            string.Equals(candidate.ServerId, serverId, StringComparison.Ordinal));
        if (registration is null)
        {
            return BridgeAuthorizationResult.Deny(
                StatusCodes.Status401Unauthorized,
                "unknown_bridge");
        }
        if (!long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || !NonceRegex().IsMatch(nonce)
            || !SignatureRegex().IsMatch(suppliedSignature))
        {
            return BridgeAuthorizationResult.Deny(
                StatusCodes.Status401Unauthorized,
                "invalid_signature_headers");
        }

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset signedAt;
        try
        {
            signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return BridgeAuthorizationResult.Deny(
                StatusCodes.Status401Unauthorized,
                "invalid_timestamp");
        }
        if ((now - signedAt).Duration() > TimeSpan.FromSeconds(_options.AllowedClockSkewSeconds))
        {
            return BridgeAuthorizationResult.Deny(
                StatusCodes.Status401Unauthorized,
                "expired_signature");
        }

        var bodyHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var canonical = $"{serverId}\n{timestampText}\n{nonce}\n{bodyHash}";
        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(registration.Secret),
            Encoding.UTF8.GetBytes(canonical));
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(suppliedSignature);
        }
        catch (FormatException)
        {
            return BridgeAuthorizationResult.Deny(
                StatusCodes.Status401Unauthorized,
                "invalid_signature");
        }
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
        {
            return BridgeAuthorizationResult.Deny(
                StatusCodes.Status401Unauthorized,
                "invalid_signature");
        }
        // Consume the nonce only after the HMAC passes so unauthenticated
        // callers cannot fill the replay map with forged requests.
        if (!replayGuard.TryUse(serverId, nonce, now.AddMinutes(2)))
        {
            return BridgeAuthorizationResult.Deny(
                StatusCodes.Status409Conflict,
                "replayed_signature");
        }
        return BridgeAuthorizationResult.Allow(serverId);
    }

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex NonceRegex();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SignatureRegex();
}

internal sealed class BridgeReplayGuard
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new(StringComparer.Ordinal);
    private long _requests;

    internal bool TryUse(string serverId, string nonce, DateTimeOffset expiresAt)
    {
        if (Interlocked.Increment(ref _requests) % 256 == 0)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var candidate in _nonces.Where(item => item.Value <= now))
            {
                _nonces.TryRemove(candidate.Key, out _);
            }
        }
        return _nonces.TryAdd($"{serverId}:{nonce}", expiresAt);
    }
}
