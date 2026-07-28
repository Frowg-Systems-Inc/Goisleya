using System.Text;

namespace Isley;

internal readonly record struct VoiceInvite(
    string ServerUrl,
    string RoomSecret,
    bool LocalOnly,
    bool LegacyKeyOnly);

internal static class VoiceInviteLogic
{
    internal const string Prefix = "ISLEY-VOICE/1";
    internal const int MaximumInviteCharacters = 512;
    internal const int RoomSecretCharacters = 24;
    internal const int MaximumServerUrlCharacters = 180;

    internal static bool TryCreate(
        string? serverUrl,
        string? roomSecret,
        out string invite,
        out string error)
    {
        invite = string.Empty;
        if (!TryNormalizeServerUrl(serverUrl, out var normalizedServer, out var localOnly))
        {
            error = "VOICE SERVER MUST USE WSS OR LOCALHOST WS";
            return false;
        }
        if (!TryNormalizeRoomSecret(roomSecret, out var normalizedSecret))
        {
            error = "ROOM KEY INVALID · CREATE A NEW ROOM";
            return false;
        }

        invite = $"{Prefix}|{Encode(normalizedServer)}|{normalizedSecret}";
        if (invite.Length > MaximumInviteCharacters)
        {
            invite = string.Empty;
            error = "VOICE INVITE TOO LARGE";
            return false;
        }

        error = localOnly
            ? "LOCAL INVITE · SAME PC ONLY"
            : string.Empty;
        return true;
    }

    internal static bool TryParse(
        string? value,
        string? fallbackServerUrl,
        out VoiceInvite invite,
        out string error)
    {
        invite = default;
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is 0 or > MaximumInviteCharacters
            || normalized.Any(char.IsControl))
        {
            error = normalized.Length > MaximumInviteCharacters
                ? "VOICE INVITE TOO LARGE"
                : "VOICE INVITE FORMAT INVALID";
            return false;
        }

        if (TryNormalizeRoomSecret(normalized, out var legacySecret))
        {
            if (!TryNormalizeServerUrl(fallbackServerUrl, out var fallbackServer, out var fallbackLocalOnly))
            {
                error = "CURRENT VOICE SERVER IS INVALID";
                return false;
            }
            invite = new VoiceInvite(fallbackServer, legacySecret, fallbackLocalOnly, true);
            error = string.Empty;
            return true;
        }

        var parts = normalized.Split('|', StringSplitOptions.None);
        if (parts.Length != 3
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !TryDecode(parts[1], out var decodedServer)
            || !TryNormalizeServerUrl(decodedServer, out var server, out var localOnly)
            || !TryNormalizeRoomSecret(parts[2], out var roomSecret))
        {
            error = "VOICE INVITE FORMAT INVALID";
            return false;
        }

        invite = new VoiceInvite(server, roomSecret, localOnly, false);
        error = string.Empty;
        return true;
    }

    internal static bool TryNormalizeServerUrl(
        string? value,
        out string normalized,
        out bool localOnly)
    {
        normalized = string.Empty;
        localOnly = false;
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length is 0 or > MaximumServerUrlCharacters
            || candidate.Any(char.IsControl)
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var local = uri.Host is "localhost" or "127.0.0.1" or "::1";
        if (!string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)
            && !(string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) && local))
        {
            return false;
        }

        normalized = uri.AbsoluteUri;
        localOnly = local;
        return normalized.Length <= MaximumServerUrlCharacters;
    }

    internal static bool TryNormalizeRoomSecret(string? value, out string normalized)
    {
        normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length != RoomSecretCharacters
            || normalized.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            normalized = string.Empty;
            return false;
        }
        return true;
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryDecode(string value, out string decoded)
    {
        decoded = string.Empty;
        if (value.Length is 0 or > 256
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            || value.Length % 4 == 1)
        {
            return false;
        }

        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(padded));
            return true;
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            decoded = string.Empty;
            return false;
        }
    }
}
