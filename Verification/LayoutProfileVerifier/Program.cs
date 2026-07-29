using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static HudLayoutProfile Profile(
    string name,
    bool mirrored = false,
    double width = 472,
    double height = 560,
    int detail = 0,
    int quickKeysMode = 0,
    long savedAt = 0,
    bool nav = true,
    bool vitals = true,
    bool survival = true,
    bool alert = true,
    bool quickKeys = false) =>
    new(name, mirrored, false, width, height, detail, nav, vitals, survival, alert, quickKeys,
        quickKeysMode, savedAt);

Check(LayoutProfileLogic.MaximumProfiles == 8, "the profile roster stays bounded at eight");
Check(LayoutProfileLogic.MaximumNameLength == 24, "profile names stay short");
Check(LayoutProfileLogic.HudDetailModeCount == 3, "three HUD detail modes");
Check(LayoutProfileLogic.MinimumSize == 240, "overlay width floor");
Check(LayoutProfileLogic.MaximumSize == 3840, "overlay size ceiling");
Check(LayoutProfileLogic.FallbackWidth == 472 && LayoutProfileLogic.FallbackHeight == 560,
    "fallback geometry stays 472x560");

Check(LayoutProfileLogic.NormalizeName(null) == string.Empty, "null names normalize to empty");
Check(LayoutProfileLogic.NormalizeName("  My   Layout  ") == "My Layout", "names trim and collapse");
Check(LayoutProfileLogic.NormalizeName("badname") == "badname", "control characters are stripped");
Check(LayoutProfileLogic.NormalizeName(new string('x', 30)).Length == 24, "names cap at 24 characters");
Check(LayoutProfileLogic.FallbackName(0) == "Layout 1", "first fallback name");
Check(LayoutProfileLogic.FallbackName(3) == "Layout 4", "later fallback names count up");
Check(LayoutProfileLogic.FallbackName(-2) == "Layout 1", "negative indexes fall back safely");

Check(LayoutProfileLogic.UniqueName("Main", new[] { "Other" }, 0) == "Main", "free names pass through");
Check(LayoutProfileLogic.UniqueName("  ", new[] { "Other" }, 2) == "Layout 3",
    "blank requests use the fallback name");
Check(LayoutProfileLogic.UniqueName("Main", new[] { "main" }, 0) == "Main 2",
    "taken names gain a numeric suffix case-insensitively");
Check(LayoutProfileLogic.UniqueName("Main", new[] { "Main", "Main 2" }, 0) == "Main 3",
    "suffixes count upward");
Check(LayoutProfileLogic.UniqueName(new string('x', 24), new[] { new string('x', 24) }, 0)
        == new string('x', 22) + " 2",
    "suffixed names still fit the cap");

var fallback = LayoutProfileLogic.Normalize(null, 0);
Check(fallback.Name == "Layout 1"
      && fallback.Width == 472
      && fallback.Height == 560
      && fallback is { NavigationHudVisible: true, VitalsHudVisible: true, SurvivalHudVisible: true,
          AlertHudVisible: true, QuickKeysHudVisible: false }
      && fallback.HudDetailModeIndex == 0
      && fallback.QuickKeysModeIndex == 0
      && fallback.SavedAtUnixMs == 0,
    "a null profile normalizes to the honest fallback");

var clamped = LayoutProfileLogic.Normalize(
    Profile("Custom", width: double.NaN, height: 100, detail: 9, quickKeysMode: 99, savedAt: -5), 0);
Check(clamped.Width == 472, "non-finite width falls back");
Check(clamped.Height == 240, "undersized height clamps to the floor");
Check(clamped.HudDetailModeIndex == 2, "detail mode clamps to the known modes");
Check(clamped.QuickKeysModeIndex == 0, "unknown Quick Keys modes fall back to the first mode");
Check(clamped.SavedAtUnixMs == 0, "negative timestamps clamp to zero");
Check(LayoutProfileLogic.Normalize(Profile("Wide", width: 99_999), 0).Width == 3840,
    "oversized width clamps to the ceiling");

Check(LayoutProfileLogic.NormalizeProfiles(null).Count == 0, "null rosters normalize to empty");
var crowded = LayoutProfileLogic.NormalizeProfiles(
    Enumerable.Range(0, 12).Select(index => Profile($"Layout {index + 1}")));
Check(crowded.Count == 8, "the eight-profile cap holds");
var deduped = LayoutProfileLogic.NormalizeProfiles(new[] { Profile("Main"), Profile("MAIN") });
Check(deduped.Count == 2
      && deduped[0].Name == "Main"
      && deduped[1].Name == "Layout 2",
    "duplicate names fall back to the slot name case-insensitively");
var dropped = LayoutProfileLogic.NormalizeProfiles(new[] { Profile("Layout 2"), Profile("Layout 2") });
Check(dropped.Count == 1 && dropped[0].Name == "Layout 2",
    "duplicates that collide with their own fallback name drop out");

Check(LayoutProfileLogic.VisibleSurfaceCount(Profile("A")) == 4, "the default profile shows four surfaces");
Check(LayoutProfileLogic.VisibleSurfaceCount(Profile("A", nav: false, alert: false)) == 2,
    "hidden surfaces are not counted");
Check(LayoutProfileLogic.Summary(Profile("A")) == "DOCK RIGHT · 4/5 HUDS · 472×560",
    "summary composes dock side, surface count, and geometry");
Check(LayoutProfileLogic.Summary(Profile("A", mirrored: true, width: 600, quickKeys: true))
        == "DOCK LEFT · 5/5 HUDS · 600×560",
    "mirrored docks and visible Quick Keys surface in the summary");

Console.WriteLine(
    "Layout profile verification passed (name normalization and suffixing, size and mode clamping, roster caps and dedup, and summary composition).");
