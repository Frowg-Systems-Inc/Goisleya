namespace Isley;

internal readonly record struct QuickKeyEntry(string Keys, string Action);

internal readonly record struct QuickKeysPresentation(
    int ModeIndex,
    string ModeId,
    string ModeLabel,
    IReadOnlyList<QuickKeyEntry> Entries,
    bool IsCompact);

internal static class QuickKeysLogic
{
    internal const int ModeCount = 3;
    internal const string ReferenceSnapshot = "2026-05-29";

    private static readonly QuickKeyEntry[][] Modes =
    [
        [
            new("Q HOLD", "SCENT"),
            new("Q TAP", "COMPASS"),
            new("E", "USE"),
            new("H / HOLD", "REST / LOGOUT"),
            new("TAB", "STATUS")
        ],
        [
            new("LMB", "PRIMARY"),
            new("RMB", "SECONDARY"),
            new("ALT + CLICK", "SPECIES ACTION"),
            new("Z", "TIGHTER TURN"),
            new("SHIFT", "SPRINT")
        ],
        [
            new("1", "BROADCAST"),
            new("2", "FRIENDLY"),
            new("3", "THREAT"),
            new("4", "HELP"),
            new("F", "VOCAL")
        ]
    ];

    internal static int NormalizeModeIndex(int requested) =>
        requested < 0 || requested >= ModeCount ? 0 : requested;

    internal static QuickKeysPresentation Present(int requestedModeIndex, double viewportWidth)
    {
        var modeIndex = NormalizeModeIndex(requestedModeIndex);
        var (modeId, modeLabel) = modeIndex switch
        {
            1 => ("combat", "COMBAT"),
            2 => ("calls", "CALLS"),
            _ => ("survival", "SURVIVAL")
        };
        var safeWidth = double.IsFinite(viewportWidth) && viewportWidth > 0
            ? viewportWidth
            : 472;
        var limit = safeWidth < 330 ? 3 : safeWidth < 430 ? 4 : 5;
        var entries = Modes[modeIndex].Take(limit).ToArray();

        return new(modeIndex, modeId, modeLabel, entries, limit < Modes[modeIndex].Length);
    }
}
