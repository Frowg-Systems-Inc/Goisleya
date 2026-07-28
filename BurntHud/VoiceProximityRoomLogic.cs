using System.Security.Cryptography;
using System.Text;

namespace Isley;

internal static class VoiceProximityRoomLogic
{
    private const string DerivationPrefix = "isley-voice-proximity-v1|";

    /// <summary>
    /// Derives a stable private room key so every Isley client on the same
    /// Live Network server lands in one proximity voice lobby.
    /// </summary>
    internal static string DeriveServerProximityRoomSecret(string? serverId)
    {
        var normalized = (serverId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is 0 or > 64
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character)
                                           && character is not ('-' or '_')))
        {
            return string.Empty;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(DerivationPrefix + normalized));
        return Convert.ToHexString(hash)[..VoiceInviteLogic.RoomSecretCharacters]
            .ToLowerInvariant();
    }

    internal static bool TryResolveAutoRoomSecret(
        string? liveNetworkServerId,
        string? currentRoomSecret,
        out string roomSecret)
    {
        var derived = DeriveServerProximityRoomSecret(liveNetworkServerId);
        if (!string.IsNullOrEmpty(derived))
        {
            roomSecret = derived;
            return true;
        }

        return VoiceInviteLogic.TryNormalizeRoomSecret(currentRoomSecret, out roomSecret);
    }
}
