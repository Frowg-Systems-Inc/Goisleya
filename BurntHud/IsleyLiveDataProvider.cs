using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Isley;

internal sealed record IsleyLivePlayer(
    string Id,
    string Label,
    double X,
    double Y,
    double Z,
    double Yaw,
    bool Self,
    bool Friend);

internal sealed record IsleyLiveVitals(
    string? SpeciesId,
    double GrowthPercent,
    double HealthCurrent,
    double HealthMaximum,
    double FoodCurrent,
    double FoodMaximum,
    double WaterCurrent,
    double WaterMaximum);

internal sealed record IsleyLiveDataSnapshot(
    DateTimeOffset UpdatedAt,
    IsleyLivePlayer? Self,
    IReadOnlyList<IsleyLivePlayer> Players,
    IsleyLiveVitals? Vitals)
{
    internal string ToMapJson() => JsonSerializer.Serialize(new
    {
        updatedAt = UpdatedAt.ToUnixTimeMilliseconds(),
        self = Self is null ? null : PlayerPayload(Self),
        players = Players.Select(PlayerPayload),
        vitals = Vitals is null ? null : new
        {
            speciesId = Vitals.SpeciesId,
            growthPercent = Vitals.GrowthPercent,
            healthCurrent = Vitals.HealthCurrent,
            healthMaximum = Vitals.HealthMaximum,
            foodCurrent = Vitals.FoodCurrent,
            foodMaximum = Vitals.FoodMaximum,
            waterCurrent = Vitals.WaterCurrent,
            waterMaximum = Vitals.WaterMaximum
        }
    });

    private static object PlayerPayload(IsleyLivePlayer player) => new
    {
        id = player.Id,
        label = player.Label,
        x = player.X,
        y = player.Y,
        z = player.Z,
        yaw = player.Yaw,
        self = player.Self,
        friend = player.Friend
    };
}

internal static class IsleyLiveDataProvider
{
    internal const int MaximumBytes = 256 * 1024;
    internal const int MaximumPlayers = 512;
    internal static readonly TimeSpan FreshnessLimit = TimeSpan.FromSeconds(10);

    internal static IsleyLiveDataSnapshot Parse(string json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumBytes)
        {
            throw new InvalidDataException("The Isley live-data document was empty or too large.");
        }

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The Isley live-data root must be an object.");
        }

        var updatedAt = ReadUpdatedAt(root, now);
        if (updatedAt > now.AddMinutes(5) || now - updatedAt > TimeSpan.FromHours(24))
        {
            throw new InvalidDataException("The Isley live-data timestamp was outside the accepted window.");
        }

        var players = new List<IsleyLivePlayer>();
        if (root.TryGetProperty("players", out var playersElement))
        {
            if (playersElement.ValueKind != JsonValueKind.Array
                || playersElement.GetArrayLength() > MaximumPlayers)
            {
                throw new InvalidDataException("The Isley live-data player list was invalid.");
            }

            var index = 0;
            foreach (var playerElement in playersElement.EnumerateArray())
            {
                players.Add(ReadPlayer(playerElement, index++, forceSelf: false));
            }
        }

        IsleyLivePlayer? self = null;
        if (root.TryGetProperty("self", out var selfElement)
            && selfElement.ValueKind is not JsonValueKind.Null)
        {
            self = ReadPlayer(selfElement, 0, forceSelf: true);
        }
        else
        {
            self = players.FirstOrDefault(player => player.Self);
        }

        players = players
            .Where(player => !player.Self)
            .GroupBy(player => player.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(MaximumPlayers)
            .ToList();

        var vitals = root.TryGetProperty("vitals", out var vitalsElement)
                     && vitalsElement.ValueKind is not JsonValueKind.Null
            ? ReadVitals(vitalsElement)
            : null;
        return new IsleyLiveDataSnapshot(updatedAt, self, players, vitals);
    }

    private static DateTimeOffset ReadUpdatedAt(JsonElement root, DateTimeOffset now)
    {
        if (!root.TryGetProperty("updatedAt", out var value))
        {
            throw new InvalidDataException("The Isley live-data timestamp was missing.");
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var milliseconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidDataException("The Isley live-data timestamp was invalid.");
            }
        }

        if (value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        throw new InvalidDataException("The Isley live-data timestamp was invalid.");
    }

    private static IsleyLivePlayer ReadPlayer(
        JsonElement value,
        int index,
        bool forceSelf)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("An Isley live-data player entry was invalid.");
        }

        var x = ReadBoundedNumber(value, "x", -1_000_000, 1_000_000);
        var y = ReadBoundedNumber(value, "y", -1_000_000, 1_000_000);
        var z = ReadOptionalBoundedNumber(value, "z", -200_000, 200_000);
        var yaw = ReadOptionalBoundedNumber(value, "yaw", -360_000, 360_000);
        var self = forceSelf || ReadBoolean(value, "self");
        var friend = !self && ReadBoolean(value, "friend");
        var id = Clean(
            ReadString(value, "id"),
            self ? "self" : $"player-{index + 1}",
            48);
        var label = Clean(
            ReadString(value, "label") ?? ReadString(value, "name"),
            self ? "You" : "Animal",
            32);
        return new IsleyLivePlayer(id, label, x, y, z, yaw, self, friend);
    }

    private static IsleyLiveVitals ReadVitals(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The Isley live-data vitals entry was invalid.");
        }

        var healthMaximum = ReadBoundedNumber(value, "healthMaximum", 0.001, 1_000_000);
        var foodMaximum = ReadBoundedNumber(value, "foodMaximum", 0.001, 1_000_000);
        var waterMaximum = ReadBoundedNumber(value, "waterMaximum", 0.001, 1_000_000);
        var healthCurrent = ReadBoundedNumber(value, "healthCurrent", 0, healthMaximum);
        var foodCurrent = ReadBoundedNumber(value, "foodCurrent", 0, foodMaximum);
        var waterCurrent = ReadBoundedNumber(value, "waterCurrent", 0, waterMaximum);
        var growth = ReadBoundedNumber(value, "growthPercent", 0, 100);
        var speciesId = Clean(ReadString(value, "speciesId"), string.Empty, 32);
        return new IsleyLiveVitals(
            string.IsNullOrEmpty(speciesId) ? null : speciesId,
            growth,
            healthCurrent,
            healthMaximum,
            foodCurrent,
            foodMaximum,
            waterCurrent,
            waterMaximum);
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private static bool ReadBoolean(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item)
        && item.ValueKind is JsonValueKind.True;

    private static double ReadOptionalBoundedNumber(
        JsonElement value,
        string property,
        double minimum,
        double maximum) =>
        value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.Number
            ? ReadBoundedNumber(item, minimum, maximum, property)
            : 0;

    private static double ReadBoundedNumber(
        JsonElement value,
        string property,
        double minimum,
        double maximum)
    {
        if (!value.TryGetProperty(property, out var item))
        {
            throw new InvalidDataException($"The Isley live-data {property} value was missing.");
        }
        return ReadBoundedNumber(item, minimum, maximum, property);
    }

    private static double ReadBoundedNumber(
        JsonElement value,
        double minimum,
        double maximum,
        string property)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number)
            || number < minimum
            || number > maximum)
        {
            throw new InvalidDataException($"The Isley live-data {property} value was invalid.");
        }
        return number;
    }

    private static string Clean(string? value, string fallback, int maximumLength)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, @"[\u0000-\u001F\u007F]+", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (string.IsNullOrEmpty(cleaned))
        {
            cleaned = fallback;
        }
        return cleaned.Length <= maximumLength ? cleaned : cleaned[..maximumLength];
    }
}
