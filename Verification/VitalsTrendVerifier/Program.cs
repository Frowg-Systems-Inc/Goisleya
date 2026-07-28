using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var now = DateTimeOffset.UnixEpoch.AddHours(20);
VitalsTrendSample Sample(
    int secondsAgo,
    double food,
    double water,
    double growth = 50,
    double health = 100) =>
    new(now.AddSeconds(-secondsAgo), health, food, water, growth);

var empty = VitalsTrendLogic.Analyze([], now);
Check(!empty.Active && empty.CompactLabel == "TREND · WAITING FOR LIVE SAMPLES",
    "empty state failed");

var learning = VitalsTrendLogic.Analyze([
    Sample(30, 60, 70),
    Sample(0, 57, 70)
], now);
Check(learning.Active
      && learning.Fresh
      && learning.SampleCount == 2
      && learning.Food.Direction == VitalTrendDirection.Learning
      && learning.CompactLabel == "TREND · LEARNING 2/3"
      && !learning.Warning,
    "minimum evidence gate failed");

var foodWarning = VitalsTrendLogic.Analyze([
    Sample(60, 60, 70),
    Sample(30, 57, 70),
    Sample(0, 54, 70)
], now);
Check(foodWarning.Warning
      && foodWarning.WarningHeading == "FOOD LOW IN ABOUT 4M"
      && foodWarning.Food.Direction == VitalTrendDirection.Falling
      && foodWarning.Food.RatePerMinute == -6
      && foodWarning.Food.MinutesToBoundary == 4
      && foodWarning.Health.Direction == VitalTrendDirection.Stable
      && foodWarning.Water.Direction == VitalTrendDirection.Stable
      && VitalsTrendLogic.FooterGlyph(foodWarning.Health) == "→"
      && VitalsTrendLogic.FooterGlyph(foodWarning.Food) == "↓"
      && VitalsTrendLogic.FooterGlyph(foodWarning.Water) == "→"
      && foodWarning.CompactLabel == "HP FULL · FOOD ↓ 4M TO LOW · WATER →",
    "steady food decline warning failed");

var healing = VitalsTrendLogic.Analyze([
    Sample(60, 60, 70, health: 70),
    Sample(30, 59, 69, health: 72),
    Sample(0, 58, 68, health: 74)
], now);
Check(healing.Health.Direction == VitalTrendDirection.Rising
      && healing.Health.RatePerMinute == 4
      && healing.Health.MinutesToBoundary == 7
      && healing.Health.BoundaryPercent == 100
      && healing.Health.BoundaryLabel == "FULL"
      && VitalsTrendLogic.FooterGlyph(healing.Health) == "↑"
      && healing.CompactLabel.StartsWith("HP ↑ 7M TO FULL", StringComparison.Ordinal)
      && VitalsTrendLogic.HealthRecoveryDetail(healing.Health)
          .Contains("Damage resets", StringComparison.Ordinal),
    "live healing rate and ETA failed");

var damageReset = VitalsTrendLogic.Analyze([
    Sample(90, 60, 70, health: 70),
    Sample(60, 59, 69, health: 72),
    Sample(30, 58, 68, health: 74),
    Sample(0, 57, 67, health: 70)
], now);
Check(damageReset.Health.Direction == VitalTrendDirection.Learning
      && damageReset.Health.SampleCount == 1
      && damageReset.Health.MinutesToBoundary is null,
    "damage should reset healing evidence");

var waterFirst = VitalsTrendLogic.Analyze([
    Sample(60, 60, 46),
    Sample(30, 58, 43),
    Sample(0, 56, 40)
], now);
Check(waterFirst.Warning
      && waterFirst.WarningHeading == "WATER LOW IN ABOUT 1M",
    "nearest resource boundary priority failed");

var refillReset = VitalsTrendLogic.Analyze([
    Sample(90, 60, 70),
    Sample(60, 57, 67),
    Sample(30, 54, 64),
    Sample(0, 80, 90)
], now);
Check(!refillReset.Warning
      && refillReset.Food.Direction == VitalTrendDirection.Learning
      && refillReset.Food.SampleCount == 1
      && refillReset.Water.Direction == VitalTrendDirection.Learning,
    "refill reset failed");

var stale = VitalsTrendLogic.Analyze([
    Sample(160, 60, 70),
    Sample(130, 57, 67),
    Sample(100, 54, 64)
], now);
Check(!stale.Active
      && !stale.Warning
      && stale.CompactLabel == "TREND PAUSED · SNAPSHOT STALE",
    "stale trend failed closed");

var invalidIgnored = VitalsTrendLogic.Analyze([
    Sample(60, 60, 70),
    new VitalsTrendSample(now.AddSeconds(-30), 100, double.NaN, 65, 50),
    Sample(0, 54, 64)
], now);
Check(invalidIgnored.Food.Direction == VitalTrendDirection.Learning
      && !invalidIgnored.Warning,
    "invalid sample filtering failed");

Console.WriteLine("Vitals trend: PASS (evidence gate, adaptive HP recovery ETA, damage reset, resource refill reset, ETA priority, stale closure, and invalid filtering)");
