using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var now = DateTimeOffset.UnixEpoch.AddHours(4);

Check(FieldConditionsLogic.NextWeather(FieldWeather.Unknown) == FieldWeather.Clear
      && FieldConditionsLogic.NextWeather(FieldWeather.Clear) == FieldWeather.Rain
      && FieldConditionsLogic.NextWeather(FieldWeather.Rain) == FieldWeather.Storm
      && FieldConditionsLogic.NextWeather(FieldWeather.Storm) == FieldWeather.Fog
      && FieldConditionsLogic.NextWeather(FieldWeather.Fog) == FieldWeather.Unknown,
    "weather cycle failed");
Check(FieldConditionsLogic.NextLight(FieldLight.Unknown) == FieldLight.Day
      && FieldConditionsLogic.NextLight(FieldLight.Day) == FieldLight.Dusk
      && FieldConditionsLogic.NextLight(FieldLight.Dusk) == FieldLight.Night
      && FieldConditionsLogic.NextLight(FieldLight.Night) == FieldLight.Dawn
      && FieldConditionsLogic.NextLight(FieldLight.Dawn) == FieldLight.Unknown,
    "light cycle failed");

var storm = FieldConditionsLogic.Evaluate(new FieldConditionsSnapshot(
    FieldWeather.Storm,
    now.AddSeconds(-90),
    FieldLight.Night,
    now.AddSeconds(-30),
    now,
    ["barometric-sensitivity", "nocturnal"],
    "allosaurus"));
Check(storm.WeatherFresh && storm.LightFresh
      && storm.Warning && storm.ShowHud
      && storm.Heading == "STORM REPORTED"
      && storm.Action == "HOLD COVER / MOVE BY COMPASS"
      && storm.MutationWindow.Contains("BAROMETRIC SENSITIVITY", StringComparison.Ordinal)
      && storm.CompactLabel == "ENV STORM / NIGHT - 1M",
    "storm priority or compact guidance failed");

var rain = FieldConditionsLogic.Evaluate(new FieldConditionsSnapshot(
    FieldWeather.Rain,
    now.AddSeconds(-5),
    FieldLight.Day,
    now.AddSeconds(-20),
    now,
    ["reabsorption", "hydro-regenerative", "photosynthetic-tissue"],
    "maiasaura"));
Check(rain.ShowHud && !rain.Warning
      && rain.Action == "USE THE MUTATION WINDOW"
      && rain.MutationWindow.Contains("REABSORPTION", StringComparison.Ordinal)
      && rain.MutationWindow.Contains("HYDRO-REGENERATIVE", StringComparison.Ordinal)
      && rain.MutationWindow.Contains("PHOTOSYNTHETIC TISSUE", StringComparison.Ordinal),
    "rain/day mutation window failed");

var nightHunter = FieldConditionsLogic.Evaluate(new FieldConditionsSnapshot(
    FieldWeather.Clear,
    now.AddMinutes(-2),
    FieldLight.Night,
    now.AddSeconds(-15),
    now,
    [],
    "dilophosaurus"));
Check(nightHunter.Heading == "NIGHT REPORTED"
      && nightHunter.Action == "USE THE DARKNESS"
      && !nightHunter.Warning
      && nightHunter.ShowHud,
    "night species context failed");

var stale = FieldConditionsLogic.Evaluate(new FieldConditionsSnapshot(
    FieldWeather.Fog,
    now.AddSeconds(-FieldConditionsLogic.FreshnessSeconds),
    FieldLight.Dusk,
    now.AddSeconds(-FieldConditionsLogic.FreshnessSeconds - 1),
    now,
    ["nocturnal"],
    "troodon"));
Check(!stale.HasFreshReport
      && stale.Weather == FieldWeather.Unknown
      && stale.Light == FieldLight.Unknown
      && !stale.ShowHud
      && stale.CompactLabel == "ENV - CHECK IN GAME"
      && stale.Freshness.Contains("stale", StringComparison.OrdinalIgnoreCase),
    "stale report expiry failed");

var futureClock = FieldConditionsLogic.Evaluate(new FieldConditionsSnapshot(
    FieldWeather.Clear,
    now.AddMinutes(1),
    FieldLight.Day,
    now.AddMinutes(1),
    now,
    null,
    string.Empty));
Check(futureClock.WeatherAgeSeconds == 0 && futureClock.LightAgeSeconds == 0,
    "future clock clamping failed");
Check(FieldConditionsLogic.FormatAge(0) == "0s"
      && FieldConditionsLogic.FormatAge(59) == "59s"
      && FieldConditionsLogic.FormatAge(60) == "1m"
      && FieldConditionsLogic.FormatAge(599) == "9m",
    "age formatting failed");

Console.WriteLine("Field conditions: PASS (cycles, freshness, storm/fog risk, species context, and mutation windows)");
