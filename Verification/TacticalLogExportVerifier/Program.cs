using System.Text;
using System.Text.Json;
using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var now = DateTimeOffset.Parse("2026-07-28T20:00:00Z");

static TacticalLogExportEvent Event(
    int minutesAgo,
    string category,
    string title,
    string detail,
    bool warning = false) =>
    new(
        DateTimeOffset.Parse("2026-07-28T20:00:00Z").AddMinutes(-minutesAgo),
        category,
        title,
        detail,
        warning);

// Newest-first input, like the in-memory tactical log.
var events = new List<TacticalLogExportEvent>
{
    Event(0, "TIMER", "Timer complete", "Nest watch"),
    Event(3, "SYSTEM", "Live map connected", "Authorized feed active"),
    Event(9, "LOGOUT", "Safe Logout Guard started", "90s countdown", warning: true)
};

var text = TacticalLogExportLogic.BuildPlainText(events, now);
Check(text.ExportedEventCount == 3
      && text.TotalEventCount == 3
      && !text.TruncatedByCount
      && !text.TruncatedBySize
      && text.Content.StartsWith("ISLEY · TACTICAL LOG · 2026-07-28 20:00:00", StringComparison.Ordinal)
      && text.Content.Contains("Session-only local timeline", StringComparison.Ordinal),
    "plain-text export header failed");
var textLines = text.Content.Split(Environment.NewLine);
Check(textLines[2].StartsWith("19:51:00 · LOGOUT", StringComparison.Ordinal)
      && textLines[4].StartsWith("20:00:00 · TIMER", StringComparison.Ordinal),
    "plain-text export must be chronological with the existing log line shape");

var json = TacticalLogExportLogic.BuildJson(events, now);
using (var document = JsonDocument.Parse(json.Content))
{
    var root = document.RootElement;
    Check(root.GetProperty("app").GetString() == "Isley"
          && root.GetProperty("kind").GetString() == "tactical-log"
          && root.GetProperty("schema").GetInt32() == 1
          && root.GetProperty("sessionOnly").GetBoolean()
          && !root.GetProperty("truncated").GetBoolean()
          && root.GetProperty("eventCount").GetInt32() == 3,
        "json export envelope failed");
    var items = root.GetProperty("events");
    Check(items.GetArrayLength() == 3
          && items[0].GetProperty("category").GetString() == "LOGOUT"
          && items[0].GetProperty("warning").GetBoolean()
          && items[2].GetProperty("category").GetString() == "TIMER",
        "json export events failed");
}

var hostile = new List<TacticalLogExportEvent>
{
    Event(0, "TI\nMER", "  ", new string('x', 500))
};
var sanitized = TacticalLogExportLogic.BuildPlainText(hostile, now);
Check(!sanitized.Content.Contains("TI\nMER", StringComparison.Ordinal)
      && sanitized.Content.Contains(" · Update · ", StringComparison.Ordinal)
      && sanitized.Content.Contains(new string('x', 200), StringComparison.Ordinal)
      && !sanitized.Content.Contains(new string('x', 201), StringComparison.Ordinal),
    "export must strip control characters, fall back on empty fields, and bound field length");

Check(TacticalLogExportLogic.SuggestedFileName(now, json: false)
        == "isley-tactical-log-20260728-200000.txt"
      && TacticalLogExportLogic.SuggestedFileName(now, json: true)
        == "isley-tactical-log-20260728-200000.json",
    "export file-name suggestion failed");

var flood = Enumerable.Range(0, TacticalLogExportLogic.MaxExportEntries + 120)
    .Select(index => Event(index, "SYSTEM", $"Event {index}", new string('y', 180)))
    .ToList();
var cappedText = TacticalLogExportLogic.BuildPlainText(flood, now);
Check(cappedText.TruncatedByCount
      && cappedText.ExportedEventCount <= TacticalLogExportLogic.MaxExportEntries
      && cappedText.Content.Contains("Event 0", StringComparison.Ordinal)
      && Encoding.UTF8.GetByteCount(cappedText.Content) <= TacticalLogExportLogic.MaxExportBytes + 512,
    "export must keep only the newest events within the entry cap and byte budget");
var cappedJson = TacticalLogExportLogic.BuildJson(flood, now);
Check(cappedJson.ExportedEventCount <= TacticalLogExportLogic.MaxExportEntries
      && Encoding.UTF8.GetByteCount(cappedJson.Content) <= TacticalLogExportLogic.MaxExportBytes,
    "json export must stay under the byte budget");
using (var document = JsonDocument.Parse(cappedJson.Content))
{
    Check(document.RootElement.GetProperty("truncated").GetBoolean(),
        "truncated json export must say so");
}

var empty = TacticalLogExportLogic.BuildPlainText([], now);
Check(empty.ExportedEventCount == 0 && empty.Content.Contains("TACTICAL LOG", StringComparison.Ordinal),
    "empty export still produces an honest header");

Console.WriteLine(
    "Tactical log export: PASS (existing log shape, chronological order, JSON envelope, sanitization, 500-entry and 256 KB bounds, honest truncation)");
