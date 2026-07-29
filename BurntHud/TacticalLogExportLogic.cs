using System.Text;

using System.Text.Json;

using System.Text.RegularExpressions;



namespace Isley;



internal readonly record struct TacticalLogExportEvent(

    DateTimeOffset OccurredAt,

    string Category,

    string Title,

    string Detail,

    bool Warning);



internal readonly record struct TacticalLogExportResult(

    string Content,

    int ExportedEventCount,

    int TotalEventCount,

    bool TruncatedByCount,

    bool TruncatedBySize);



internal static class TacticalLogExportLogic

{

    internal const int MaxExportEntries = 500;

    internal const int MaxExportBytes = 256 * 1024;

    internal const int MaxFieldLength = 200;



    internal static TacticalLogExportResult BuildPlainText(

        IReadOnlyList<TacticalLogExportEvent> eventsNewestFirst,

        DateTimeOffset exportedAt)

    {

        var selected = SelectNewest(eventsNewestFirst, out var truncatedByCount);

        var header = new List<string>

        {

            $"ISLEY · TACTICAL LOG · {exportedAt:yyyy-MM-dd HH:mm:ss}",

            "Session-only local timeline"

        };

        var body = selected

            .Select(entry =>

                $"{entry.OccurredAt:HH:mm:ss} · {entry.Category} · {entry.Title} · {entry.Detail}")

            .ToList();

        var truncatedBySize = TrimToByteBudget(header, body, out var kept);

        var lines = new List<string>(header);

        lines.AddRange(kept);

        if (truncatedBySize)

        {

            lines.Add(

                $"··· oldest {selected.Count - kept.Count} event(s) omitted " +

                $"to stay under {MaxExportBytes / 1024} KB");

        }



        return new TacticalLogExportResult(

            string.Join(Environment.NewLine, lines),

            kept.Count,

            eventsNewestFirst.Count,

            truncatedByCount,

            truncatedBySize);

    }



    internal static TacticalLogExportResult BuildJson(

        IReadOnlyList<TacticalLogExportEvent> eventsNewestFirst,

        DateTimeOffset exportedAt)

    {

        var selected = SelectNewest(eventsNewestFirst, out var truncatedByCount);

        var truncatedBySize = false;

        var kept = selected;

        string content;

        while (true)

        {

            content = SerializeJson(kept, exportedAt, truncatedByCount || truncatedBySize);

            if (Encoding.UTF8.GetByteCount(content) <= MaxExportBytes || kept.Count == 0)

            {

                break;

            }



            truncatedBySize = true;

            var nextCount = Math.Max(0, kept.Count - Math.Max(1, kept.Count / 10));

            // kept always remains the newest tail of the chronological selection.

            kept = selected.Skip(selected.Count - nextCount).ToList();

        }



        return new TacticalLogExportResult(

            content,

            kept.Count,

            eventsNewestFirst.Count,

            truncatedByCount,

            truncatedBySize);

    }



    internal static string SuggestedFileName(DateTimeOffset now, bool json) =>

        $"isley-tactical-log-{now:yyyyMMdd-HHmmss}.{(json ? "json" : "txt")}";



    internal static string SanitizeField(string? value, string fallback)

    {

        var withoutControls = Regex.Replace(value ?? string.Empty, @"\p{C}+", " ");
        var normalized = Regex.Replace(withoutControls, @"\s+", " ").Trim();

        if (normalized.Length == 0)

        {

            return fallback;

        }



        return normalized.Length <= MaxFieldLength

            ? normalized

            : normalized[..MaxFieldLength];

    }



    private static List<TacticalLogExportEvent> SelectNewest(

        IReadOnlyList<TacticalLogExportEvent> eventsNewestFirst,

        out bool truncatedByCount)

    {

        truncatedByCount = eventsNewestFirst.Count > MaxExportEntries;

        return eventsNewestFirst

            .Take(MaxExportEntries)

            .Select(entry => new TacticalLogExportEvent(

                entry.OccurredAt,

                SanitizeField(entry.Category, "SYSTEM"),

                SanitizeField(entry.Title, "Update"),

                SanitizeField(entry.Detail, "No additional detail"),

                entry.Warning))

            .Reverse()

            .ToList();

    }



    private static bool TrimToByteBudget(

        IReadOnlyList<string> header,

        IReadOnlyList<string> body,

        out List<string> kept)

    {

        kept = body.ToList();

        var headerBytes = header.Sum(

            line => Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length);

        while (kept.Count > 0)

        {

            var bodyBytes = kept.Sum(

                line => Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length);

            if (headerBytes + bodyBytes + 96 <= MaxExportBytes)

            {

                break;

            }



            kept.RemoveAt(0);

        }



        return kept.Count < body.Count;

    }



    private static string SerializeJson(

        IReadOnlyList<TacticalLogExportEvent> chronologicalEvents,

        DateTimeOffset exportedAt,

        bool truncated)

    {

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))

        {

            writer.WriteStartObject();

            writer.WriteString("app", "Isley");

            writer.WriteString("kind", "tactical-log");

            writer.WriteNumber("schema", 1);

            writer.WriteString("exportedAt", exportedAt.ToString("yyyy-MM-ddTHH:mm:sszzz"));

            writer.WriteBoolean("sessionOnly", true);

            writer.WriteBoolean("truncated", truncated);

            writer.WriteNumber("eventCount", chronologicalEvents.Count);

            writer.WriteStartArray("events");

            foreach (var entry in chronologicalEvents)

            {

                writer.WriteStartObject();

                writer.WriteString("at", entry.OccurredAt.ToString("yyyy-MM-ddTHH:mm:sszzz"));

                writer.WriteString("category", entry.Category);

                writer.WriteString("title", entry.Title);

                writer.WriteString("detail", entry.Detail);

                writer.WriteBoolean("warning", entry.Warning);

                writer.WriteEndObject();

            }



            writer.WriteEndArray();

            writer.WriteEndObject();

        }



        return Encoding.UTF8.GetString(stream.ToArray());

    }

}

