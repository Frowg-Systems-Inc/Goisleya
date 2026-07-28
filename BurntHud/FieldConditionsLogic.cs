namespace Isley;

internal enum FieldWeather
{
    Unknown,
    Clear,
    Rain,
    Storm,
    Fog
}

internal enum FieldLight
{
    Unknown,
    Day,
    Dusk,
    Night,
    Dawn
}

internal readonly record struct FieldConditionsSnapshot(
    FieldWeather Weather,
    DateTimeOffset WeatherReportedAt,
    FieldLight Light,
    DateTimeOffset LightReportedAt,
    DateTimeOffset Now,
    IReadOnlyCollection<string>? ActiveMutationIds,
    string SpeciesId);

internal readonly record struct FieldConditionsGuidance(
    FieldWeather Weather,
    FieldLight Light,
    bool WeatherFresh,
    bool LightFresh,
    int WeatherAgeSeconds,
    int LightAgeSeconds,
    string Heading,
    string Action,
    string Detail,
    string MutationWindow,
    string Freshness,
    string CompactLabel,
    string BriefLabel,
    bool Warning,
    bool ShowHud)
{
    internal bool HasFreshReport => WeatherFresh || LightFresh;
}

internal static class FieldConditionsLogic
{
    internal const int FreshnessSeconds = 600;

    internal static FieldWeather NextWeather(FieldWeather value) => value switch
    {
        FieldWeather.Unknown => FieldWeather.Clear,
        FieldWeather.Clear => FieldWeather.Rain,
        FieldWeather.Rain => FieldWeather.Storm,
        FieldWeather.Storm => FieldWeather.Fog,
        _ => FieldWeather.Unknown
    };

    internal static FieldLight NextLight(FieldLight value) => value switch
    {
        FieldLight.Unknown => FieldLight.Day,
        FieldLight.Day => FieldLight.Dusk,
        FieldLight.Dusk => FieldLight.Night,
        FieldLight.Night => FieldLight.Dawn,
        _ => FieldLight.Unknown
    };

    internal static string WeatherLabel(FieldWeather value) => value switch
    {
        FieldWeather.Clear => "CLEAR",
        FieldWeather.Rain => "RAIN",
        FieldWeather.Storm => "STORM",
        FieldWeather.Fog => "FOG",
        _ => "?"
    };

    internal static string LightLabel(FieldLight value) => value switch
    {
        FieldLight.Day => "DAY",
        FieldLight.Dusk => "DUSK",
        FieldLight.Night => "NIGHT",
        FieldLight.Dawn => "DAWN",
        _ => "?"
    };

    internal static FieldConditionsGuidance Evaluate(FieldConditionsSnapshot raw)
    {
        var weatherAge = AgeSeconds(raw.WeatherReportedAt, raw.Now);
        var lightAge = AgeSeconds(raw.LightReportedAt, raw.Now);
        var weatherFresh = raw.Weather != FieldWeather.Unknown && weatherAge < FreshnessSeconds;
        var lightFresh = raw.Light != FieldLight.Unknown && lightAge < FreshnessSeconds;
        var weather = weatherFresh ? raw.Weather : FieldWeather.Unknown;
        var light = lightFresh ? raw.Light : FieldLight.Unknown;
        var mutations = (raw.ActiveMutationIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var species = (raw.SpeciesId ?? string.Empty).Trim().ToLowerInvariant();
        var mutationWindow = BuildMutationWindow(weather, light, mutations);

        var heading = "FIELD CONDITIONS";
        var action = "REPORT WHAT YOU SEE";
        var detail = "The authorized map does not expose weather or world time. Reports expire after 10 minutes.";
        var warning = false;
        var showHud = false;

        if (weather == FieldWeather.Storm)
        {
            heading = "STORM REPORTED";
            action = "HOLD COVER / MOVE BY COMPASS";
            detail = "Sound and visibility can be unreliable. Avoid exposed crossings and refresh when the storm changes.";
            warning = true;
            showHud = true;
        }
        else if (weather == FieldWeather.Fog)
        {
            heading = "FOG REPORTED";
            action = "SHORTEN THE ROUTE";
            detail = "Use the map, compass, and close landmarks. Avoid committing beyond your visible escape lane.";
            warning = true;
            showHud = true;
        }
        else if (light == FieldLight.Night)
        {
            heading = "NIGHT REPORTED";
            action = species is "dilophosaurus" or "troodon"
                ? "USE THE DARKNESS"
                : "USE NIGHT VISION / COVER";
            detail = species is "dilophosaurus" or "troodon"
                ? "Your selected species is strongest when it controls sightlines and disengages cleanly."
                : "Reduce exposed travel and keep a known retreat while visibility is limited.";
            warning = species is not "dilophosaurus" and not "troodon";
            showHud = true;
        }
        else if (weather == FieldWeather.Rain)
        {
            heading = "RAIN REPORTED";
            action = string.IsNullOrEmpty(mutationWindow)
                ? "USE COVER / RECHECK"
                : "USE THE MUTATION WINDOW";
            detail = "Rain is player-reported, not a forecast. Recheck the condition before relying on it for recovery.";
            showHud = !string.IsNullOrEmpty(mutationWindow);
        }
        else if (light == FieldLight.Dusk)
        {
            heading = "DUSK REPORTED";
            action = "PREPARE FOR LOW LIGHT";
            detail = "Choose a retreat, check night vision, and finish exposed crossings before visibility falls.";
            showHud = true;
        }
        else if (light == FieldLight.Dawn)
        {
            heading = "DAWN REPORTED";
            action = "RECHECK SIGHTLINES";
            detail = "Daylight is returning. Reassess open routes and any night-focused mutation plan.";
        }
        else if (light == FieldLight.Day && !string.IsNullOrEmpty(mutationWindow))
        {
            heading = "DAY REPORTED";
            action = "USE THE MUTATION WINDOW";
            detail = "The light phase is player-reported. Refresh before relying on a day-only recovery effect.";
            showHud = true;
        }
        else if (weather == FieldWeather.Clear || light == FieldLight.Day)
        {
            heading = weather == FieldWeather.Clear && light == FieldLight.Day
                ? "CLEAR / DAY REPORTED"
                : weather == FieldWeather.Clear ? "CLEAR REPORTED" : "DAY REPORTED";
            action = "NORMAL FIELD READ";
            detail = "No severe field condition is reported. Refresh either control when the environment changes.";
        }

        var freshness = BuildFreshness(weatherFresh, weatherAge, lightFresh, lightAge,
            raw.Weather != FieldWeather.Unknown, raw.Light != FieldLight.Unknown);
        var compactLabel = BuildCompactLabel(weather, light, weatherAge, lightAge);
        var briefLabel = showHud ? compactLabel : string.Empty;
        return new FieldConditionsGuidance(
            weather,
            light,
            weatherFresh,
            lightFresh,
            weatherAge,
            lightAge,
            heading,
            action,
            detail,
            mutationWindow,
            freshness,
            compactLabel,
            briefLabel,
            warning,
            showHud);
    }

    private static string BuildMutationWindow(
        FieldWeather weather,
        FieldLight light,
        HashSet<string> mutations)
    {
        var active = new List<string>();
        if (weather == FieldWeather.Rain)
        {
            AddIfActive(active, mutations, "reabsorption", "REABSORPTION");
            AddIfActive(active, mutations, "hydro-regenerative", "HYDRO-REGENERATIVE");
        }
        if (weather == FieldWeather.Storm)
        {
            AddIfActive(active, mutations, "barometric-sensitivity", "BAROMETRIC SENSITIVITY");
        }
        if (light == FieldLight.Day)
        {
            AddIfActive(active, mutations, "photosynthetic-regeneration", "PHOTOSYNTHETIC REGEN");
            AddIfActive(active, mutations, "photosynthetic-tissue", "PHOTOSYNTHETIC TISSUE");
        }
        if (light == FieldLight.Night)
        {
            AddIfActive(active, mutations, "nocturnal", "NOCTURNAL");
            AddIfActive(active, mutations, "augmented-tapetum", "AUGMENTED TAPETUM");
        }
        return active.Count == 0 ? string.Empty : $"ACTIVE WINDOW - {string.Join(" + ", active)}";
    }

    private static void AddIfActive(
        ICollection<string> target,
        IReadOnlySet<string> mutations,
        string id,
        string label)
    {
        if (mutations.Contains(id)) target.Add(label);
    }

    private static int AgeSeconds(DateTimeOffset reportedAt, DateTimeOffset now)
    {
        if (reportedAt == default) return int.MaxValue;
        var seconds = Math.Floor((now - reportedAt).TotalSeconds);
        return (int)Math.Clamp(seconds, 0, int.MaxValue);
    }

    private static string BuildFreshness(
        bool weatherFresh,
        int weatherAge,
        bool lightFresh,
        int lightAge,
        bool weatherWasReported,
        bool lightWasReported)
    {
        var parts = new List<string>();
        if (weatherFresh) parts.Add($"weather {FormatAge(weatherAge)}");
        else if (weatherWasReported) parts.Add("weather stale");
        if (lightFresh) parts.Add($"light {FormatAge(lightAge)}");
        else if (lightWasReported) parts.Add("light stale");
        if (parts.Count == 0) return "No report - manual / session-only";
        return $"{string.Join(" - ", parts)} - expires at 10m";
    }

    private static string BuildCompactLabel(
        FieldWeather weather,
        FieldLight light,
        int weatherAge,
        int lightAge)
    {
        var states = new List<string>();
        if (weather != FieldWeather.Unknown) states.Add(WeatherLabel(weather));
        if (light != FieldLight.Unknown) states.Add(LightLabel(light));
        if (states.Count == 0) return "ENV - CHECK IN GAME";
        var ages = new List<int>();
        if (weather != FieldWeather.Unknown) ages.Add(weatherAge);
        if (light != FieldLight.Unknown) ages.Add(lightAge);
        var oldestAge = ages.Count == 0 ? 0 : ages.Max();
        return $"ENV {string.Join(" / ", states)} - {FormatAge(oldestAge).ToUpperInvariant()}";
    }

    internal static string FormatAge(int seconds)
    {
        if (seconds == int.MaxValue) return "stale";
        if (seconds < 60) return $"{Math.Max(0, seconds)}s";
        return $"{seconds / 60}m";
    }
}
