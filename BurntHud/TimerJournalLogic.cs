using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Isley;

internal readonly record struct TimerJournalEntry(
    long TimestampUnixMs,
    string Event,
    string TimerId,
    string Label,
    int DurationSeconds);

internal static class TimerJournalLogic
{
    internal const int MaxEntries = 200;
    internal const int MaxBytes = 256 * 1024;
    internal const int MaxReadBytes = 1024 * 1024;
    internal const string StartEvent = "start";
    internal const string ElapseEvent = "elapse";
    internal const string CancelEvent = "cancel";
    internal const string ExpiredAwayEvent = "expired-away";

    private static readonly string[] KnownEvents =
    [
        StartEvent,
        ElapseEvent,
        CancelEvent,
        ExpiredAwayEvent
    ];

    internal static string NormalizeEvent(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return KnownEvents.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : string.Empty;
    }

    internal static string NormalizeTimerId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return Regex.IsMatch(normalized, "^[a-f0-9]{32}$", RegexOptions.CultureInvariant)
            ? normalized
            : string.Empty;
    }

    internal static string NormalizeLabel(string? value)
    {
        var withoutControls = Regex.Replace(value ?? string.Empty, @"\p{C}+", " ");
        var normalized = Regex.Replace(withoutControls, @"\s+", " ").Trim();
        return normalized.Length <= 28 ? normalized : normalized[..28];
    }

    internal static int NormalizeDurationSeconds(int value) =>
        Math.Clamp(value, 60, 21600);

    internal static TimerJournalEntry Create(
        string eventKind,
        DateTimeOffset at,
        string timerId,
        string label,
        int durationSeconds)
    {
        var normalizedEvent = NormalizeEvent(eventKind);
        return new TimerJournalEntry(
            at.ToUnixTimeMilliseconds(),
            normalizedEvent.Length == 0 ? CancelEvent : normalizedEvent,
            NormalizeTimerId(timerId),
            NormalizeLabel(label),
            NormalizeDurationSeconds(durationSeconds));
    }

    internal static bool IsTerminal(string eventKind) =>
        string.Equals(eventKind, ElapseEvent, StringComparison.Ordinal)
        || string.Equals(eventKind, CancelEvent, StringComparison.Ordinal)
        || string.Equals(eventKind, ExpiredAwayEvent, StringComparison.Ordinal);

    /// <summary>
    /// Returns the ids of restored, already-completed timers whose journal shows a
    /// tracked start but no terminal event afterwards — meaning they elapsed while
    /// Isley was closed. Timers with no tracked start (pre-journal provenance) are
    /// never reported, because their history is unknown rather than missed.
    /// </summary>
    internal static IReadOnlyList<string> FindExpiredWhileAway(
        IReadOnlyList<TimerJournalEntry> entries,
        IEnumerable<string> restoredCompletedTimerIds)
    {
        var expired = new List<string>();
        foreach (var rawId in restoredCompletedTimerIds)
        {
            var id = NormalizeTimerId(rawId);
            if (id.Length == 0 || expired.Contains(id, StringComparer.Ordinal))
            {
                continue;
            }

            var lastStart = entries
                .Where(entry =>
                    string.Equals(entry.TimerId, id, StringComparison.Ordinal)
                    && string.Equals(entry.Event, StartEvent, StringComparison.Ordinal))
                .Select(entry => (long?)entry.TimestampUnixMs)
                .Max();
            if (lastStart is null)
            {
                continue;
            }

            var terminated = entries.Any(entry =>
                string.Equals(entry.TimerId, id, StringComparison.Ordinal)
                && IsTerminal(entry.Event)
                && entry.TimestampUnixMs >= lastStart.Value);
            if (!terminated)
            {
                expired.Add(id);
            }
        }

        return expired;
    }

    internal static IReadOnlyList<TimerJournalEntry> Prune(
        IReadOnlyList<TimerJournalEntry> entries)
    {
        var kept = entries.Count <= MaxEntries
            ? entries.ToList()
            : entries.Skip(entries.Count - MaxEntries).ToList();
        // Bound the serialized size by shedding the oldest entries in steps.
        for (var attempt = 0; attempt < 24 && kept.Count > 0; attempt++)
        {
            if (Encoding.UTF8.GetByteCount(Serialize(kept)) <= MaxBytes)
            {
                break;
            }

            var nextCount = Math.Max(0, kept.Count - Math.Max(1, kept.Count / 8));
            kept = kept.Skip(kept.Count - nextCount).ToList();
        }

        return kept;
    }

    internal static string Serialize(IReadOnlyList<TimerJournalEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("app", "Isley");
            writer.WriteString("kind", "timer-journal");
            writer.WriteNumber("schema", 1);
            writer.WriteStartArray("entries");
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteNumber("at", entry.TimestampUnixMs);
                writer.WriteString("event", entry.Event);
                writer.WriteString("timer", entry.TimerId);
                writer.WriteString("label", entry.Label);
                writer.WriteNumber("durationSeconds", entry.DurationSeconds);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static bool TryDeserialize(string? json, out List<TimerJournalEntry> entries)
    {
        entries = [];
        if (string.IsNullOrWhiteSpace(json)
            || Encoding.UTF8.GetByteCount(json) > MaxReadBytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind)
                || !string.Equals(kind.GetString(), "timer-journal", StringComparison.Ordinal)
                || !root.TryGetProperty("schema", out var schema)
                || schema.ValueKind != JsonValueKind.Number
                || schema.GetInt32() != 1
                || !root.TryGetProperty("entries", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsed = new List<TimerJournalEntry>();
            foreach (var item in items.EnumerateArray())
            {
                if (parsed.Count >= MaxEntries
                    || item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("at", out var at)
                    || at.ValueKind != JsonValueKind.Number
                    || !at.TryGetInt64(out var timestamp)
                    || timestamp is < 0 or > 4_102_444_800_000
                    || !item.TryGetProperty("event", out var eventProperty)
                    || !item.TryGetProperty("timer", out var timerProperty)
                    || !item.TryGetProperty("label", out var labelProperty)
                    || !item.TryGetProperty("durationSeconds", out var durationProperty)
                    || durationProperty.ValueKind != JsonValueKind.Number
                    || !durationProperty.TryGetInt32(out var durationSeconds))
                {
                    continue;
                }

                var eventKind = NormalizeEvent(eventProperty.GetString());
                var timerId = NormalizeTimerId(timerProperty.GetString());
                if (eventKind.Length == 0 || timerId.Length == 0)
                {
                    continue;
                }

                parsed.Add(new TimerJournalEntry(
                    timestamp,
                    eventKind,
                    timerId,
                    NormalizeLabel(labelProperty.GetString()),
                    NormalizeDurationSeconds(durationSeconds)));
            }

            entries = parsed;
            return true;
        }
        catch (JsonException)
        {
            entries = [];
            return false;
        }
    }
}
