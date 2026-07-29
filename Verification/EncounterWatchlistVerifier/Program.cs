using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
var nowMs = now.ToUnixTimeMilliseconds();

Check(EncounterWatchlistLogic.MaximumWatchedPlayers == 32, "the watchlist stays bounded at 32 players");

foreach (var raw in new[] { " Rex   Runner ", "badname", null, new string('x', 80) })
{
    Check(EncounterWatchlistLogic.NormalizeName(raw) == SteamFriendLogic.NormalizeMapName(raw),
        $"watch names reuse Steam friend map-name normalization ({raw ?? "null"})");
}

Check(EncounterWatchlistLogic.NormalizeCardinal("n") == "N", "cardinals uppercase");
Check(EncounterWatchlistLogic.NormalizeCardinal(" ne ") == "NE", "cardinals trim");
Check(EncounterWatchlistLogic.NormalizeCardinal("NW") == "NW", "cardinals pass through");
Check(EncounterWatchlistLogic.NormalizeCardinal("NORTH") == string.Empty, "unknown cardinals are dropped");
Check(EncounterWatchlistLogic.NormalizeCardinal(null) == string.Empty, "null cardinals are dropped");

Check(EncounterWatchlistLogic.NormalizeDistanceMu(null) is null, "null distance stays unknown");
Check(EncounterWatchlistLogic.NormalizeDistanceMu(double.NaN) is null, "NaN distance stays unknown");
Check(EncounterWatchlistLogic.NormalizeDistanceMu(double.PositiveInfinity) is null,
    "infinite distance stays unknown");
Check(EncounterWatchlistLogic.NormalizeDistanceMu(-3) is null, "negative distance stays unknown");
Check(EncounterWatchlistLogic.NormalizeDistanceMu(0) == 0, "zero distance is kept");
Check(EncounterWatchlistLogic.NormalizeDistanceMu(12.4) == 10, "distance snaps to five-map-unit steps");
Check(EncounterWatchlistLogic.NormalizeDistanceMu(13) == 15, "distance rounds to the nearest step");
Check(EncounterWatchlistLogic.NormalizeDistanceMu(2_000_000) == 1_000_000, "distance is clamped");

var first = EncounterWatchlistLogic.Upsert(null, "Rex Runner", 125, "ne", now);
Check(first.Count == 1
      && first[0].Name == "Rex Runner"
      && first[0].DistanceMu == 125
      && first[0].Cardinal == "NE"
      && first[0].AddedAtUnixMs == nowMs,
    "a fresh watch lands at the front with a normalized snapshot");

var second = EncounterWatchlistLogic.Upsert(first, "Trike Tracker", null, null, now.AddMinutes(1));
Check(second.Count == 2
      && second[0].Name == "Trike Tracker"
      && second[0].DistanceMu is null
      && second[0].Cardinal.Length == 0,
    "unknown distance and cardinal stay honestly empty");

var refreshed = EncounterWatchlistLogic.Upsert(second, "rex  RUNNER", 240, "s", now.AddMinutes(2));
Check(refreshed.Count == 2
      && refreshed[0].Name == "rex RUNNER"
      && refreshed[0].DistanceMu == 240
      && refreshed[0].Cardinal == "S"
      && refreshed[0].AddedAtUnixMs == now.AddMinutes(2).ToUnixTimeMilliseconds(),
    "re-watching refreshes the snapshot and moves it to the front without duplicating");

var dropped = EncounterWatchlistLogic.Upsert(second, "   ", 10, "N", now.AddMinutes(3));
Check(dropped.Count == 2 && dropped.All(entry => entry.Name.Length > 0),
    "a blank watch inserts nothing but still normalizes the list");

var withBlank = second.Concat(new[] { new EncounterWatchEntry("  ", 5, "N", nowMs) });
var cleaned = EncounterWatchlistLogic.Upsert(withBlank, "Stego Scout", 5, "W", now.AddMinutes(4));
Check(cleaned.Count == 3 && cleaned.All(entry => entry.Name.Trim().Length > 0),
    "pre-existing blank entries are filtered out");

var full = Enumerable.Range(0, 32)
    .Select(index => new EncounterWatchEntry($"Player {index:00}", 100, "N", index))
    .ToList();
var pruned = EncounterWatchlistLogic.Upsert(full, "New Player", 50, "E", now);
Check(pruned.Count == 32
      && pruned[0].Name == "New Player"
      && pruned.Any(entry => entry.Name == "Player 00")
      && !pruned.Any(entry => entry.Name == "Player 31"),
    "a full watchlist drops the oldest entry first");

Console.WriteLine(
    "Encounter watchlist verification passed (shared name normalization, cardinal and distance honesty, refresh-on-rewatch, and bounded pruning).");
