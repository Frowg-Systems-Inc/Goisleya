namespace Isley;

internal sealed record CrashLogCandidate(string FileName, long SizeBytes, DateTime CreatedUtc);

internal static class DiagnosticsBundleLogic
{
    internal const int MaximumCrashLogFiles = 10;
    internal const long MaximumSingleLogBytes = 64 * 1024;
    internal const long MaximumTotalLogBytes = 256 * 1024;
    internal const long MaximumWhatsNewBytes = 16 * 1024;
    internal const int MaximumEntryNameLength = 64;
    internal const string BundleSchema = "isley-diagnostics-1";

    internal static IReadOnlyList<CrashLogCandidate> SelectCrashLogs(
        IEnumerable<CrashLogCandidate>? candidates)
    {
        var selected = new List<CrashLogCandidate>(MaximumCrashLogFiles);
        long totalBytes = 0;
        foreach (var candidate in (candidates ?? Enumerable.Empty<CrashLogCandidate>())
                     .Where(log => log.SizeBytes > 0 && log.SizeBytes <= MaximumSingleLogBytes)
                     .OrderByDescending(log => log.CreatedUtc))
        {
            if (selected.Count >= MaximumCrashLogFiles
                || totalBytes + candidate.SizeBytes > MaximumTotalLogBytes)
            {
                break;
            }

            selected.Add(candidate);
            totalBytes += candidate.SizeBytes;
        }

        return selected;
    }

    internal static string SanitizeEntryName(string? fileName)
    {
        var cleaned = new string((fileName ?? string.Empty)
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            .ToArray());
        if (cleaned.Length == 0)
        {
            return "log.txt";
        }

        return cleaned.Length <= MaximumEntryNameLength
            ? cleaned
            : cleaned[..MaximumEntryNameLength];
    }

    internal static string SuggestFileName(DateTimeOffset now) =>
        $"isley-diagnostics-{now:yyyyMMdd-HHmmss}.zip";
}
