using System.Globalization;
using System.Text.RegularExpressions;

namespace Isley;

internal sealed record VisibleHudTextReadout(
    UniversalCoordinatePoint? Position,
    int? HealthPercent,
    int? FoodPercent,
    int? WaterPercent,
    int? StaminaPercent,
    int? GrowthPercent)
{
    internal int FieldCount =>
        (Position is null ? 0 : 1)
        + (HealthPercent is null ? 0 : 1)
        + (FoodPercent is null ? 0 : 1)
        + (WaterPercent is null ? 0 : 1)
        + (StaminaPercent is null ? 0 : 1)
        + (GrowthPercent is null ? 0 : 1);

    internal string Summary
    {
        get
        {
            var values = new List<string>();
            if (Position is not null) values.Add("LOCATION");
            if (HealthPercent is { } health) values.Add($"HP {health}%");
            if (FoodPercent is { } food) values.Add($"FOOD {food}%");
            if (WaterPercent is { } water) values.Add($"WATER {water}%");
            if (StaminaPercent is { } stamina) values.Add($"STAMINA {stamina}%");
            if (GrowthPercent is { } growth) values.Add($"GROWTH {growth}%");
            return values.Count == 0 ? "NO SUPPORTED FIELDS" : string.Join(" · ", values);
        }
    }
}

internal static partial class VisibleHudTextLogic
{
    private const int MaximumTextLength = 6_000;

    [GeneratedRegex(
        @"(?<label>HEALTH|HP|FOOD|HUNGER|WATER|THIRST|STAMINA|STAM|GROWTH)" +
        @"\s*[:=\-]?\s*(?<value>\d{1,3})(?:\s*%|\s*/\s*(?<maximum>\d{1,3}))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VitalPattern();

    [GeneratedRegex(
        @"[+-]?\d+(?:[.,]\d+)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    internal static VisibleHudTextReadout Parse(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new VisibleHudTextReadout(null, null, null, null, null, null);
        }

        var text = Regex.Replace(
            rawText[..Math.Min(rawText.Length, MaximumTextLength)],
            @"[\u0000-\u001F\u007F]+",
            "\n");
        UniversalCoordinatePoint? position = null;
        int? health = null;
        int? food = null;
        int? water = null;
        int? stamina = null;
        int? growth = null;

        foreach (var line in text.Split(
                     '\n',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (position is null && LooksLikeLocationLine(line))
            {
                var numbers = NumberPattern().Matches(line)
                    .Select(match => match.Value)
                    .ToArray();
                if (numbers.Length >= 3)
                {
                    var candidate = string.Join(", ", numbers[^3..]);
                    if (UniversalCoordinateLogic.TryParseClipboard(candidate, out var parsed))
                    {
                        position = parsed;
                    }
                }
            }

            foreach (Match match in VitalPattern().Matches(line))
            {
                if (!TryReadPercent(match, out var percent)) continue;
                switch (match.Groups["label"].Value.ToUpperInvariant())
                {
                    case "HEALTH":
                    case "HP":
                        health ??= percent;
                        break;
                    case "FOOD":
                    case "HUNGER":
                        food ??= percent;
                        break;
                    case "WATER":
                    case "THIRST":
                        water ??= percent;
                        break;
                    case "STAMINA":
                    case "STAM":
                        stamina ??= percent;
                        break;
                    case "GROWTH":
                        growth ??= percent;
                        break;
                }
            }
        }

        return new VisibleHudTextReadout(position, health, food, water, stamina, growth);
    }

    private static bool LooksLikeLocationLine(string line) =>
        line.Contains("ASSET LOCATION", StringComparison.OrdinalIgnoreCase)
        || (line.Contains('X', StringComparison.OrdinalIgnoreCase)
            && line.Contains('Y', StringComparison.OrdinalIgnoreCase)
            && line.Contains('Z', StringComparison.OrdinalIgnoreCase));

    private static bool TryReadPercent(Match match, out int percent)
    {
        percent = 0;
        if (!int.TryParse(
                match.Groups["value"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return false;
        }

        if (match.Groups["maximum"].Success)
        {
            if (!int.TryParse(
                    match.Groups["maximum"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var maximum)
                || maximum <= 0)
            {
                return false;
            }
            value = (int)Math.Round(value / (double)maximum * 100);
        }

        if (value is < 0 or > 100) return false;
        percent = value;
        return true;
    }
}
