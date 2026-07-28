using System.Net;
using System.Text.RegularExpressions;

namespace Isley;

internal readonly record struct CommunitySlotDecision(
    bool Alert,
    bool IsFull,
    int OpenSlots);

internal sealed class CommunityServerProfileSettings
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Any Isle server";
    public string Address { get; set; } = string.Empty;
    public bool WatchEnabled { get; set; }
    public bool SlotAlertEnabled { get; set; }
    public int GrowthMultiplierIndex { get; set; } = -1;
    /// <summary>Optional Isley Live Network join URL for this community profile (no tokens).</summary>
    public string IsleyJoinLink { get; set; } = string.Empty;
}

internal static partial class CommunityServerWatchLogic
{
    internal const int MaximumAddressLength = 96;
    internal const int MaximumProfiles = 6;

    internal static string SanitizeAddressInput(string? value)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();
        return sanitized.Length <= MaximumAddressLength
            ? sanitized
            : sanitized[..MaximumAddressLength];
    }

    internal static bool TryNormalizeAddress(string? value, out string normalized)
    {
        normalized = string.Empty;
        var input = SanitizeAddressInput(value);
        if (input.Length < 3
            || input.Any(char.IsWhiteSpace)
            || input.Contains('/')
            || input.Contains('\\')
            || input.Contains('?')
            || input.Contains('#')
            || input.Contains('@')
            || input.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        string host;
        string portText;
        if (input.StartsWith("[", StringComparison.Ordinal))
        {
            var closingBracket = input.IndexOf(']');
            if (closingBracket <= 1
                || closingBracket + 2 >= input.Length
                || input[closingBracket + 1] != ':')
            {
                return false;
            }
            host = input[1..closingBracket];
            portText = input[(closingBracket + 2)..];
            if (!IPAddress.TryParse(host, out var ipv6)
                || ipv6.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return false;
            }
            host = $"[{ipv6.ToString().ToLowerInvariant()}]";
        }
        else
        {
            var separator = input.LastIndexOf(':');
            if (separator <= 0
                || separator == input.Length - 1
                || input[..separator].Contains(':'))
            {
                return false;
            }
            host = input[..separator];
            portText = input[(separator + 1)..];
            if (IPAddress.TryParse(host, out var ipAddress))
            {
                if (ipAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return false;
                }
                host = ipAddress.ToString();
            }
            else
            {
                if (host.Length > 253
                    || !HostnameRegex().IsMatch(host)
                    || host.Split('.').Any(label => label.Length is < 1 or > 63
                                                    || label.StartsWith("-", StringComparison.Ordinal)
                                                    || label.EndsWith("-", StringComparison.Ordinal)))
                {
                    return false;
                }
                host = host.ToLowerInvariant();
            }
        }

        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            return false;
        }

        normalized = $"{host}:{port}";
        return normalized.Length <= MaximumAddressLength;
    }

    internal static CommunitySlotDecision EvaluateSlotTransition(
        bool? previousWasFull,
        bool alertEnabled,
        bool online,
        int players,
        int capacity)
    {
        if (capacity <= 0 || players < 0 || players > capacity)
        {
            return new CommunitySlotDecision(false, false, 0);
        }

        var isFull = online && players >= capacity;
        var openSlots = online ? Math.Max(0, capacity - players) : 0;
        var alert = alertEnabled
                    && previousWasFull is true
                    && online
                    && openSlots > 0;
        return new CommunitySlotDecision(alert, isFull, openSlots);
    }

    internal static List<CommunityServerProfileSettings> NormalizeProfiles(
        IEnumerable<CommunityServerProfileSettings>? profiles,
        string? legacyName,
        string? legacyAddress,
        bool legacyWatchEnabled,
        bool legacySlotAlertEnabled,
        int legacyGrowthMultiplierIndex)
    {
        var normalizedProfiles = new List<CommunityServerProfileSettings>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles ?? [])
        {
            if (normalizedProfiles.Count >= MaximumProfiles)
            {
                break;
            }

            var id = NormalizeProfileId(profile?.Id);
            if (string.IsNullOrEmpty(id) || !usedIds.Add(id))
            {
                id = NextAvailableId(usedIds);
                usedIds.Add(id);
            }
            normalizedProfiles.Add(NormalizeProfile(profile, id));
        }

        if (normalizedProfiles.Count == 0)
        {
            var id = NextAvailableId(usedIds);
            normalizedProfiles.Add(NormalizeProfile(
                new CommunityServerProfileSettings
                {
                    Id = id,
                    Name = legacyName ?? "Any Isle server",
                    Address = legacyAddress ?? string.Empty,
                    WatchEnabled = legacyWatchEnabled,
                    SlotAlertEnabled = legacySlotAlertEnabled,
                    GrowthMultiplierIndex = legacyGrowthMultiplierIndex
                },
                id));
        }

        return normalizedProfiles;
    }

    internal static int FindProfileIndex(
        IReadOnlyList<CommunityServerProfileSettings> profiles,
        string? selectedId)
    {
        if (profiles.Count == 0)
        {
            return 0;
        }
        var normalizedId = NormalizeProfileId(selectedId);
        var index = profiles
            .Select((profile, profileIndex) => (profile, profileIndex))
            .FirstOrDefault(candidate => string.Equals(
                candidate.profile.Id, normalizedId, StringComparison.Ordinal))
            .profileIndex;
        return index >= 0
               && index < profiles.Count
               && string.Equals(profiles[index].Id, normalizedId, StringComparison.Ordinal)
            ? index
            : 0;
    }

    internal static int MoveProfileIndex(int count, int currentIndex, int delta)
    {
        if (count <= 1)
        {
            return 0;
        }
        var normalizedCurrent = Math.Clamp(currentIndex, 0, count - 1);
        return (normalizedCurrent + delta % count + count) % count;
    }

    internal static CommunityServerProfileSettings CreateProfile(
        IReadOnlyList<CommunityServerProfileSettings> profiles)
    {
        var usedIds = profiles.Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);
        var id = NextAvailableId(usedIds);
        var suffix = int.TryParse(id.AsSpan("community-".Length), out var number)
            ? number
            : profiles.Count + 1;
        return new CommunityServerProfileSettings
        {
            Id = id,
            Name = $"Any Isle server {suffix}",
            GrowthMultiplierIndex = -1
        };
    }

    internal static (List<CommunityServerProfileSettings> Profiles, int SelectedIndex) RemoveProfileAt(
        IReadOnlyList<CommunityServerProfileSettings> profiles,
        int selectedIndex)
    {
        if (profiles.Count <= 1)
        {
            return (profiles.Select(CloneProfile).ToList(), 0);
        }

        var normalizedIndex = Math.Clamp(selectedIndex, 0, profiles.Count - 1);
        var remaining = profiles
            .Where((_, index) => index != normalizedIndex)
            .Select(CloneProfile)
            .ToList();
        return (remaining, Math.Min(normalizedIndex, remaining.Count - 1));
    }

    private static CommunityServerProfileSettings NormalizeProfile(
        CommunityServerProfileSettings? profile,
        string id)
    {
        var address = SanitizeAddressInput(profile?.Address);
        var validAddress = TryNormalizeAddress(address, out var normalizedAddress);
        var joinLink = new string((profile?.IsleyJoinLink ?? string.Empty)
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();
        if (joinLink.Length > 1024)
        {
            joinLink = string.Empty;
        }

        return new CommunityServerProfileSettings
        {
            Id = id,
            Name = ServerSessionLogic.NormalizeCustomServerName(profile?.Name),
            Address = validAddress ? normalizedAddress : address,
            WatchEnabled = profile?.WatchEnabled is true && validAddress,
            SlotAlertEnabled = profile?.SlotAlertEnabled is true,
            GrowthMultiplierIndex = Math.Clamp(profile?.GrowthMultiplierIndex ?? -1, -1, 4),
            IsleyJoinLink = joinLink
        };
    }

    private static CommunityServerProfileSettings CloneProfile(CommunityServerProfileSettings profile) =>
        new()
        {
            Id = profile.Id,
            Name = profile.Name,
            Address = profile.Address,
            WatchEnabled = profile.WatchEnabled,
            SlotAlertEnabled = profile.SlotAlertEnabled,
            GrowthMultiplierIndex = profile.GrowthMultiplierIndex,
            IsleyJoinLink = profile.IsleyJoinLink
        };

    private static string NormalizeProfileId(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Select(char.ToLowerInvariant)
            .ToArray())
            .Trim('-', '_');
        return normalized.Length <= 40 ? normalized : normalized[..40];
    }

    private static string NextAvailableId(ISet<string> usedIds)
    {
        for (var index = 1; index <= MaximumProfiles + 1; index++)
        {
            var candidate = $"community-{index}";
            if (!usedIds.Contains(candidate))
            {
                return candidate;
            }
        }
        return $"community-{usedIds.Count + 1}";
    }

    [GeneratedRegex("^[A-Za-z0-9.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HostnameRegex();
}
