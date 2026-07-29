using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static CrashLogCandidate Log(string name, long bytes, int minutesAgo) =>
    new(name, bytes, DateTime.UtcNow.AddMinutes(-minutesAgo));

Check(DiagnosticsBundleLogic.MaximumCrashLogFiles == 10, "bundle ships at most ten crash logs");
Check(DiagnosticsBundleLogic.MaximumSingleLogBytes == 64 * 1024, "single logs stay under 64 KB");
Check(DiagnosticsBundleLogic.MaximumTotalLogBytes == 256 * 1024, "the log payload stays under 256 KB");
Check(DiagnosticsBundleLogic.MaximumWhatsNewBytes == 16 * 1024, "release notes stay bounded");
Check(DiagnosticsBundleLogic.MaximumEntryNameLength == 64, "zip entry names stay short");
Check(DiagnosticsBundleLogic.BundleSchema == "isley-diagnostics-1", "bundle schema tag is stable");

Check(DiagnosticsBundleLogic.SelectCrashLogs(null).Count == 0, "no candidates selects nothing");
var rejected = DiagnosticsBundleLogic.SelectCrashLogs(new[]
{
    Log("empty.log", 0, 1),
    Log("negative.log", -5, 2),
    Log("oversized.log", 64 * 1024 + 1, 3)
});
Check(rejected.Count == 0, "empty, negative, and oversized logs are all refused");

var ordered = DiagnosticsBundleLogic.SelectCrashLogs(new[]
{
    Log("oldest.log", 1024, 30),
    Log("newest.log", 1024, 1),
    Log("middle.log", 1024, 10)
});
Check(ordered.Count == 3
      && ordered[0].FileName == "newest.log"
      && ordered[1].FileName == "middle.log"
      && ordered[2].FileName == "oldest.log",
    "newest logs are bundled first");

var crowded = DiagnosticsBundleLogic.SelectCrashLogs(
    Enumerable.Range(0, 12).Select(index => Log($"crash-{index}.log", 1024, index)));
Check(crowded.Count == 10, "the ten-file cap holds");

var heavy = DiagnosticsBundleLogic.SelectCrashLogs(
    Enumerable.Range(0, 5).Select(index => Log($"heavy-{index}.log", 60 * 1024, index)));
Check(heavy.Count == 4 && heavy.Sum(log => log.SizeBytes) == 240 * 1024,
    "the 256 KB total cap stops before the payload overflows");

Check(DiagnosticsBundleLogic.SanitizeEntryName(null) == "log.txt", "null names fall back to log.txt");
Check(DiagnosticsBundleLogic.SanitizeEntryName("   ") == "log.txt", "blank names fall back to log.txt");
Check(DiagnosticsBundleLogic.SanitizeEntryName("crash log 01.txt") == "crashlog01.txt",
    "unsafe characters are stripped from entry names");
Check(DiagnosticsBundleLogic.SanitizeEntryName("../evil/path.txt") == "..evilpath.txt",
    "path separators cannot survive into the zip");
Check(DiagnosticsBundleLogic.SanitizeEntryName(new string('a', 100) + ".txt").Length == 64,
    "entry names are truncated to the cap");

var stamp = new DateTimeOffset(2026, 7, 28, 13, 45, 9, TimeSpan.Zero);
Check(DiagnosticsBundleLogic.SuggestFileName(stamp) == "isley-diagnostics-20260728-134509.zip",
    "suggested bundle names carry a sortable timestamp");

Console.WriteLine(
    "Diagnostics bundle verification passed (newest-first selection, per-file and total byte caps, entry-name sanitization, and schema stability).");
