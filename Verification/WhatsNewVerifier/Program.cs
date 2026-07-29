using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static bool IsFallback(WhatsNewPresentation presentation, string version) =>
    presentation.Version == version
    && presentation.Title == $"Isley {version}"
    && presentation.Body.Contains("Live Map with Gateway basemap")
    && presentation.Body.Contains("Open Tools for Live Network, Voice, and Patch Watch.");

Check(IsFallback(WhatsNewLogic.FromJson(null, "1.3.0"), "1.3.0"), "null JSON falls back");
Check(IsFallback(WhatsNewLogic.FromJson("  ", "1.3.0"), "1.3.0"), "blank JSON falls back");
Check(IsFallback(WhatsNewLogic.FromJson("not json", "1.3.0"), "1.3.0"), "malformed JSON falls back");
Check(IsFallback(WhatsNewLogic.FromJson("""{"title":"","body":"x"}""", "1.3.0"), "1.3.0"),
    "missing title falls back");
Check(IsFallback(WhatsNewLogic.FromJson("""{"title":"x","body":"  "}""", "1.3.0"), "1.3.0"),
    "missing body falls back");
Check(IsFallback(WhatsNewLogic.FromJson(null, " "), "1.3.0"),
    "blank current version defaults to 1.3.0");

var parsed = WhatsNewLogic.FromJson(
    """{"version":"1.4.0","title":"  What's new  ","body":"• Added things"}""",
    "1.3.0");
Check(parsed.Version == "1.4.0", "release version honored");
Check(parsed.Title == "What's new", "title trimmed");
Check(parsed.Body == "• Added things", "body trimmed");

var noVersion = WhatsNewLogic.FromJson("""{"title":"t","body":"b"}""", "1.3.2");
Check(noVersion.Version == "1.3.2", "missing version uses current version");

var oversized = WhatsNewLogic.FromJson(
    $"{{\"title\":\"t\",\"body\":\"{new string('x', WhatsNewLogic.MaximumBodyCharacters + 500)}\"}}",
    "1.3.0");
Check(oversized.Body.Length == WhatsNewLogic.MaximumBodyCharacters, "body bounded to the cap");

Check(!WhatsNewLogic.ShouldHighlight("1.3.0", "1.3.0"), "same version stays quiet");
Check(!WhatsNewLogic.ShouldHighlight(" 1.3.0 ", "1.3.0"), "whitespace-insensitive compare");
Check(!WhatsNewLogic.ShouldHighlight("1.3.0", "1.3.0".ToUpperInvariant()), "case-insensitive compare");
Check(WhatsNewLogic.ShouldHighlight("1.2.0", "1.3.0"), "new version highlights");
Check(WhatsNewLogic.ShouldHighlight(null!, "1.3.0"), "never-seen version highlights");
Check(!WhatsNewLogic.ShouldHighlight("1.2.0", " "), "blank current version never highlights");

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var shipped = File.ReadAllText(Path.Combine(root, "BurntHud", "whats-new.json"));
var shippedPresentation = WhatsNewLogic.FromJson(shipped, "0.0.0");
Check(shippedPresentation.Title.Length > 0 && !IsFallback(shippedPresentation, "0.0.0"),
    "shipped whats-new.json parses as real release notes");
Check(shippedPresentation.Body.Length <= WhatsNewLogic.MaximumBodyCharacters,
    "shipped whats-new.json respects the body cap");

Console.WriteLine(
    "What's-new verification passed (fallback honesty, trimming, version handling, body cap, highlight gating, and shipped notes parse).");
