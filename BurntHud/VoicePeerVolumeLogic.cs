using System.Security.Cryptography;
using System.Text;

namespace Isley;

internal sealed class VoicePeerVolumeEntry
{
    public string PeerKey { get; set; } = string.Empty;
    public int VolumePercent { get; set; } = 100;
    public long UpdatedAtUnixMs { get; set; }
}

internal static class VoicePeerVolumeLogic
{
    internal const int MaximumRememberedPeers = 64;
    internal const int PeerKeyLength = 16;

    // Voice peer ids are session-random 32-hex values, so cross-session volume
    // memory keys off a one-way hash of the self-reported display name instead.
    // Raw peer names are never persisted by this feature.
    internal static bool TryComputePeerKey(string? participantName, out string peerKey)
    {
        peerKey = string.Empty;
        var normalized = string.Join(' ', (participantName ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        normalized = new string(normalized.Where(character =>
            char.IsLetterOrDigit(character) || character is ' ' or '_' or '.' or '\'' or '-').ToArray());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        normalized = normalized[..Math.Min(32, normalized.Length)];
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"isley-voice-peer-volume-v1\n{normalized.ToLowerInvariant()}"));
        peerKey = Convert.ToHexString(hash)[..PeerKeyLength].ToLowerInvariant();
        return true;
    }

    internal static bool IsValidPeerKey(string? value) =>
        value is { Length: PeerKeyLength }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static int NormalizeVolumePercent(int value) => Math.Clamp(value, 0, 100);

    internal static List<VoicePeerVolumeEntry> NormalizeEntries(
        IEnumerable<VoicePeerVolumeEntry>? entries,
        DateTimeOffset now)
    {
        if (entries is null)
        {
            return [];
        }

        var minimumTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var maximumTime = now.AddDays(1).ToUnixTimeMilliseconds();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<VoicePeerVolumeEntry>();
        foreach (var source in entries
                     .Where(entry => entry is not null)
                     .OrderByDescending(entry => entry.UpdatedAtUnixMs))
        {
            if (!IsValidPeerKey(source.PeerKey) || !seen.Add(source.PeerKey))
            {
                continue;
            }

            normalized.Add(new VoicePeerVolumeEntry
            {
                PeerKey = source.PeerKey,
                VolumePercent = NormalizeVolumePercent(source.VolumePercent),
                UpdatedAtUnixMs = source.UpdatedAtUnixMs >= minimumTime
                                  && source.UpdatedAtUnixMs <= maximumTime
                    ? source.UpdatedAtUnixMs
                    : now.ToUnixTimeMilliseconds()
            });
            if (normalized.Count >= MaximumRememberedPeers)
            {
                break;
            }
        }

        return normalized;
    }

    internal static bool TryFindVolume(
        IEnumerable<VoicePeerVolumeEntry>? entries,
        string peerKey,
        out int volumePercent)
    {
        volumePercent = 100;
        if (!IsValidPeerKey(peerKey) || entries is null)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            if (string.Equals(entry.PeerKey, peerKey, StringComparison.Ordinal))
            {
                volumePercent = NormalizeVolumePercent(entry.VolumePercent);
                return true;
            }
        }

        return false;
    }

    // Most-recently-used first; the least-recently-used peer is pruned once the
    // bounded roster is full so persistence never grows past the cap.
    internal static List<VoicePeerVolumeEntry> Upsert(
        IEnumerable<VoicePeerVolumeEntry>? entries,
        string peerKey,
        int volumePercent,
        DateTimeOffset now)
    {
        var retained = NormalizeEntries(entries, now)
            .Where(entry => !string.Equals(entry.PeerKey, peerKey, StringComparison.Ordinal))
            .ToList();
        if (IsValidPeerKey(peerKey))
        {
            retained.Insert(0, new VoicePeerVolumeEntry
            {
                PeerKey = peerKey,
                VolumePercent = NormalizeVolumePercent(volumePercent),
                UpdatedAtUnixMs = now.ToUnixTimeMilliseconds()
            });
        }

        return retained.Count <= MaximumRememberedPeers
            ? retained
            : retained.Take(MaximumRememberedPeers).ToList();
    }
}
