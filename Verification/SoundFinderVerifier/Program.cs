using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var now = DateTimeOffset.UtcNow;
Check(TrackFinderModeLogic.Next(TrackFinderMode.Sound) == TrackFinderMode.Scent
      && TrackFinderModeLogic.Next(TrackFinderMode.Scent) == TrackFinderMode.Sound,
    "sound and scent mode cycle");
var scentCycle = new[]
{
    ScentTargetKind.Water,
    ScentTargetKind.Food,
    ScentTargetKind.Trail,
    ScentTargetKind.Carcass,
    ScentTargetKind.Water
};
for (var index = 0; index < scentCycle.Length - 1; index++)
{
    Check(TrackFinderModeLogic.Next(scentCycle[index]) == scentCycle[index + 1],
        $"scent target cycle {index}");
}
Check(TrackFinderModeLogic.ModeId(TrackFinderMode.Sound) == "sound"
      && TrackFinderModeLogic.ModeId(TrackFinderMode.Scent) == "scent"
      && TrackFinderModeLogic.TargetId(ScentTargetKind.Trail) == "trail",
    "controller mode IDs");
Check(TrackFinderModeLogic.CueLabel(TrackFinderMode.Scent, ScentTargetKind.Carcass)
          == "carcass scent clue"
      && TrackFinderModeLogic.VerificationPhrase(TrackFinderMode.Sound) == "verify by sound"
      && TrackFinderModeLogic.VerificationPhrase(TrackFinderMode.Scent) == "verify with scent in game",
    "truthful mode copy");
Check(SoundFinderLogic.Analyze(null, null, now).Status == SoundFinderStatus.WaitingFirst,
    "empty state");

var east = new SoundBearingReading(100, 100, 90, now.AddSeconds(-20));
Check(SoundFinderLogic.Analyze(east, null, now).Status == SoundFinderStatus.WaitingSecond,
    "first reading state");
Check(SoundFinderLogic.Analyze(east with { CapturedAt = now.AddSeconds(-121) }, null, now).Status
      == SoundFinderStatus.FirstExpired, "stale first reading");

var north = new SoundBearingReading(120, 120, 0, now.AddSeconds(-5));
var crossing = SoundFinderLogic.Analyze(east, north, now);
Check(crossing.Status == SoundFinderStatus.Ready
      && Math.Abs(crossing.EstimateX!.Value - 120) < 0.0001
      && Math.Abs(crossing.EstimateY!.Value - 100) < 0.0001,
    "orthogonal forward intersection");
Check(crossing.BaselineDistance > 28
      && crossing.DistanceFromFirst is > 19.9 and < 20.1
      && crossing.DistanceFromSecond is > 19.9 and < 20.1,
    "intersection distances");
Check(crossing.IntersectionAngleDegrees is > 89.9 and < 90.1
      && crossing.UncertaintyRadius is >= 8 and <= 120
      && crossing.Confidence == "HIGH",
    "confidence and uncertainty");

var tooClose = SoundFinderLogic.Analyze(
    east,
    new SoundBearingReading(103, 100, 0, now),
    now);
Check(tooClose.Status == SoundFinderStatus.TooClose, "minimum movement baseline");

var parallel = SoundFinderLogic.Analyze(
    east,
    new SoundBearingReading(100, 130, 91, now),
    now);
Check(parallel.Status == SoundFinderStatus.Parallel, "near-parallel refusal");

var diverging = SoundFinderLogic.Analyze(
    east,
    new SoundBearingReading(120, 80, 0, now),
    now);
Check(diverging.Status == SoundFinderStatus.Diverging, "behind-ray refusal");

var tooDistant = SoundFinderLogic.Analyze(
    new SoundBearingReading(0, 0, 90, now.AddSeconds(-20)),
    new SoundBearingReading(999, 999, 0, now),
    now);
Check(tooDistant.Status == SoundFinderStatus.Ready, "bounded long-range map intersection");
var outsideMap = SoundFinderLogic.Analyze(
    new SoundBearingReading(0, 900, 90, now.AddSeconds(-20)),
    new SoundBearingReading(900, 0, 0, now),
    now);
Check(outsideMap.Status is SoundFinderStatus.Diverging or SoundFinderStatus.TooDistant,
    "off-map or behind estimate refusal");

var normalized = SoundFinderLogic.Normalize(new SoundBearingReading(-5, 1100, -45, now));
Check(normalized.X == 0 && normalized.Y == 1000 && normalized.BearingDegrees == 315,
    "coordinate and bearing normalization");

Console.WriteLine(
    "Track Finder verification passed (sound/scent modes, target cycle, capture states, freshness, movement baseline, ray geometry, uncertainty, and safe refusal)." );
