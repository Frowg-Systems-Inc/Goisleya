using System.Text;
using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var now = DateTimeOffset.Parse("2026-07-28T20:00:00Z");
var timerId = "0123456789abcdef0123456789abcdef";
var otherTimerId = "fedcba9876543210fedcba9876543210";

Check(TimerJournalLogic.NormalizeEvent(" START ") == TimerJournalLogic.StartEvent
      && TimerJournalLogic.NormalizeEvent("explode") == string.Empty,
    "journal event whitelist failed");
Check(TimerJournalLogic.NormalizeTimerId(timerId.ToUpperInvariant()) == timerId
      && TimerJournalLogic.NormalizeTimerId("not-a-guid") == string.Empty,
    "journal timer id validation failed");
Check(TimerJournalLogic.NormalizeLabel("  nest   watch  ") == "nest watch"
      && TimerJournalLogic.NormalizeLabel(new string('x', 40)).Length == 28,
    "journal label normalization failed");
Check(TimerJournalLogic.NormalizeDurationSeconds(5) == 60
      && TimerJournalLogic.NormalizeDurationSeconds(999999) == 21600,
    "journal duration bounds failed");
Check(TimerJournalLogic.IsTerminal(TimerJournalLogic.ElapseEvent)
      && TimerJournalLogic.IsTerminal(TimerJournalLogic.CancelEvent)
      && TimerJournalLogic.IsTerminal(TimerJournalLogic.ExpiredAwayEvent)
      && !TimerJournalLogic.IsTerminal(TimerJournalLogic.StartEvent),
    "journal terminal-event classification failed");

var created = TimerJournalLogic.Create("start", now, timerId, "Nest watch", 900);
Check(created.Event == TimerJournalLogic.StartEvent
      && created.TimerId == timerId
      && created.TimestampUnixMs == now.ToUnixTimeMilliseconds(),
    "journal entry creation failed");
var fallbackEvent = TimerJournalLogic.Create("invented", now, timerId, "x", 60);
Check(fallbackEvent.Event == TimerJournalLogic.CancelEvent,
    "journal unknown event must fall back to a terminal-safe kind");

var journal = new List<TimerJournalEntry>
{
    TimerJournalLogic.Create(TimerJournalLogic.StartEvent, now.AddMinutes(-20), timerId, "Nest", 900),
    TimerJournalLogic.Create(TimerJournalLogic.StartEvent, now.AddMinutes(-10), otherTimerId, "Patrol", 300),
    TimerJournalLogic.Create(TimerJournalLogic.ElapseEvent, now.AddMinutes(-5), otherTimerId, "Patrol", 300)
};

// Tracked start, no terminal event afterwards → expired while away.
var expired = TimerJournalLogic.FindExpiredWhileAway(journal, [timerId, otherTimerId]);
Check(expired.Count == 1 && expired[0] == timerId,
    "journal expired-while-away detection failed");

// Terminal event before the newest start does not cover a later cycle.
var restarted = new List<TimerJournalEntry>(journal)
{
    TimerJournalLogic.Create(TimerJournalLogic.ElapseEvent, now.AddMinutes(-25), timerId, "Nest", 900)
};
Check(TimerJournalLogic.FindExpiredWhileAway(restarted, [timerId]).Count == 1,
    "journal must only honor terminal events from the current timer cycle");

// Unknown provenance (no tracked start) is never reported.
Check(TimerJournalLogic.FindExpiredWhileAway(journal, ["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]).Count == 0,
    "journal must not flag timers it never tracked");

// An expired-away record is terminal for the next launch.
var reconciled = new List<TimerJournalEntry>(journal)
{
    TimerJournalLogic.Create(TimerJournalLogic.ExpiredAwayEvent, now, timerId, "Nest", 900)
};
Check(TimerJournalLogic.FindExpiredWhileAway(reconciled, [timerId]).Count == 0,
    "journal expired-away entries must be terminal");

var serialized = TimerJournalLogic.Serialize(journal);
Check(TimerJournalLogic.TryDeserialize(serialized, out var roundTrip)
      && roundTrip.Count == journal.Count
      && roundTrip[0].Event == TimerJournalLogic.StartEvent
      && roundTrip[1].TimerId == otherTimerId,
    "journal round-trip failed");
Check(!TimerJournalLogic.TryDeserialize("{\"kind\":\"settings\",\"schema\":1}", out _)
      && !TimerJournalLogic.TryDeserialize("not json", out _)
      && !TimerJournalLogic.TryDeserialize(null, out _)
      && !TimerJournalLogic.TryDeserialize(new string('x', TimerJournalLogic.MaxReadBytes + 1), out _),
    "journal must reject foreign, malformed, missing, and oversized payloads");
Check(TimerJournalLogic.TryDeserialize(
        "{\"kind\":\"timer-journal\",\"schema\":1,\"entries\":[" +
        "{\"at\":1700000000000,\"event\":\"explode\",\"timer\":\"" + timerId + "\",\"label\":\"x\",\"durationSeconds\":60}," +
        "{\"at\":1700000000000,\"event\":\"start\",\"timer\":\"bad\",\"label\":\"x\",\"durationSeconds\":60}," +
        "{\"at\":1700000000000,\"event\":\"start\",\"timer\":\"" + timerId + "\",\"label\":\"ok\",\"durationSeconds\":60}" +
        "]}",
        out var filtered)
      && filtered.Count == 1
      && filtered[0].Label == "ok",
    "journal must drop hostile or invalid entries while keeping valid ones");

var oversized = Enumerable.Range(0, TimerJournalLogic.MaxEntries + 50)
    .Select(index => TimerJournalLogic.Create(
        TimerJournalLogic.StartEvent,
        now.AddSeconds(index),
        timerId,
        $"Timer {index}",
        60))
    .ToList();
var pruned = TimerJournalLogic.Prune(oversized);
Check(pruned.Count <= TimerJournalLogic.MaxEntries
      && pruned[^1].Label == $"Timer {TimerJournalLogic.MaxEntries + 49}"
      && Encoding.UTF8.GetByteCount(TimerJournalLogic.Serialize(pruned)) <= TimerJournalLogic.MaxBytes,
    "journal pruning must keep the newest entries within count and byte budgets");

Console.WriteLine(
    "Timer journal: PASS (whitelist events, bounded entries, cycle-aware expired-while-away, honest unknown provenance, round-trip, hostile-input refusal, pruning)");
