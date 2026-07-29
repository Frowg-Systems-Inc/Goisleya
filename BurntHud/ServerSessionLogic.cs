using System.Text.RegularExpressions;

namespace Isley;

internal sealed record ServerSessionProfile(
    string Id,
    string DisplayName,
    string HeaderLabel,
    string ModeLabel,
    string Description,
    string SelectorLabel,
    string CompatibilityStatus,
    string CapabilitySummary,
    bool UniversalToolsAvailable,
    bool RequiresServerName,
    bool RequiresServerAddress,
    bool LiveMapServicesAvailable,
    int SuggestedGrowthMultiplierIndex);

internal static class ServerSessionLogic
{
    internal const string LiveMapId = "live-map";
    internal const string LiveMapDisplayName = "Live Map";
    internal const string NonAffiliationDisclosure =
        "Isley never uses the game server you play on for its map. Live Map uses an attributed public Gateway map feed; no server-operator account, private API, or private service is required.";
    internal const string OfficialId = "official";
    internal const string CommunityId = "community";

    internal static readonly ServerSessionProfile[] Profiles =
    [
        new(
            LiveMapId,
            LiveMapDisplayName,
            "LIVE MAP",
            "LIVE + UNIVERSAL",
            "Every all-server companion tool plus Isley's bundled map, terrain routes, local pins, opt-in coordinate capture, and an open live-data provider contract.",
            "LIVE MAP",
            "LOCAL MAP READY · NO SERVER-OPERATOR DEPENDENCY",
            "VOICE · VITALS · SURVIVAL · GUIDES · ROUTES · SELF / FRIEND PROVIDERS",
            true,
            false,
            false,
            true,
            -1),
        new(
            OfficialId,
            "Official server",
            "OFFICIAL",
            "UNIVERSAL",
            "Ready on every official server with local voice, vitals, survival, guide, lifecycle, combat, timer, and opt-in coordinate tools.",
            "OFFICIAL",
            "ALL-SERVER TOOLS READY · NO SERVER-FED PLAYER MAP",
            "VOICE · VITALS · SURVIVAL · GUIDE · LIFE RUN · PLAYER SYNC",
            true,
            false,
            false,
            false,
            0),
        new(
            CommunityId,
            "Any Isle server",
            "ANY SERVER",
            "UNIVERSAL",
            "Works on community, unofficial, private, passworded, and unlisted servers. A local name and public host:port are optional.",
            "ANY SERVER",
            "ALL-SERVER TOOLS READY · PRIVATE / UNLISTED SUPPORTED",
            "NO SERVER SETUP REQUIRED · OPTIONAL NAME, RATE, AND PUBLIC STATUS",
            true,
            false,
            false,
            false,
            -1)
    ];

    internal static ServerSessionProfile Find(string? id) =>
        Profiles.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.Ordinal))
        ?? Profiles[0];

    internal static string NormalizeProfileId(string? id) => Find(id).Id;

    internal static string NextProfileId(string? id)
    {
        var normalized = NormalizeProfileId(id);
        var index = Array.FindIndex(Profiles, profile => profile.Id == normalized);
        return Profiles[(index + 1) % Profiles.Length].Id;
    }

    internal static bool HasLiveMapServices(string? id) => Find(id).LiveMapServicesAvailable;

    internal static bool HasUniversalTools(string? id) => Find(id).UniversalToolsAvailable;

    internal static bool RequiresServerName(string? id) => Find(id).RequiresServerName;

    internal static bool RequiresServerAddress(string? id) => Find(id).RequiresServerAddress;

    internal static string NormalizeCustomServerName(string? value)
    {
        var withoutControls = Regex.Replace(value ?? string.Empty, @"[\u0000-\u001F\u007F]+", " ");
        var withoutBriefSeparators = Regex.Replace(withoutControls, @"[|]+", " ");
        var normalized = Regex.Replace(withoutBriefSeparators, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "Community server", StringComparison.OrdinalIgnoreCase))
        {
            return "Any Isle server";
        }

        return normalized.Length <= 28 ? normalized : normalized[..28];
    }

    internal static string DisplayName(string? profileId, string? customName)
    {
        var profile = Find(profileId);
        return profile.Id == CommunityId
            ? NormalizeCustomServerName(customName)
            : profile.DisplayName;
    }

    internal static string HeaderLabel(string? profileId, string? customName)
    {
        var profile = Find(profileId);
        if (profile.Id != CommunityId)
        {
            return profile.HeaderLabel;
        }

        var normalized = NormalizeCustomServerName(customName).ToUpperInvariant();
        return normalized.Length <= 14 ? normalized : $"{normalized[..13]}…";
    }

    internal static string BriefLabel(string? profileId, string? customName) =>
        DisplayName(profileId, customName).ToUpperInvariant();

    // Named server-rate preset suggested by each session profile. Planners resolve the
    // returned id through ServerRatePresetLogic; an empty id means the profile leaves
    // the rate to the player. Kept dependency-free so this file still compiles alone.
    internal static string SuggestedRatePresetId(string? id) => NormalizeProfileId(id) switch
    {
        OfficialId => "official-1x",
        _ => string.Empty
    };
}
