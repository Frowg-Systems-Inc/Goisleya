using System.Text.RegularExpressions;

namespace Isley.Telemetry;

public static class TelemetryProtocol
{
    public const int Version = 1;

    /// <summary>
    /// Relay-to-viewer stream format version. Version 1 is full snapshots only.
    /// Version 2 adds keyframe-tagged snapshots and delta frames. Viewers must
    /// reject stream versions greater than the newest one they understand and
    /// fall back to a waiting/update-required state instead of mis-parsing.
    /// </summary>
    public const int ViewerStreamVersion = 2;
    public const int MaximumEntities = 512;
    public const int MaximumConditionsPerEntity = 16;
    public const int MaximumFrameBytes = 512 * 1024;
    public static readonly TimeSpan MaximumFrameAge = TimeSpan.FromSeconds(15);
}

public enum TelemetryEntityKind
{
    Player,
    AiAnimal
}

public enum TelemetryShareScope
{
    Self,
    Friends,
    Server
}

public enum TelemetryDirectionQuality
{
    Missing,
    MotionInferred,
    ServerAuthoritative
}

public enum TelemetryVisibilityPolicy
{
    PrivacyFiltered,
    ServerWide
}

public sealed record TelemetryCapabilities
{
    public bool Position { get; init; } = true;
    public bool AuthoritativeDirection { get; init; }
    public bool Health { get; init; }
    public bool Growth { get; init; }
    public bool Stamina { get; init; }
    public bool Food { get; init; }
    public bool Water { get; init; }
    public bool Conditions { get; init; }
    public bool AiAnimals { get; init; }
}

public sealed record TelemetryEntity
{
    public required string EntityId { get; init; }
    public string? SteamId { get; init; }
    public string DisplayName { get; init; } = "Animal";
    public TelemetryEntityKind Kind { get; init; } = TelemetryEntityKind.Player;
    public string? SpeciesId { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double? Yaw { get; init; }
    public TelemetryDirectionQuality DirectionQuality { get; init; }
    public double? HealthPercent { get; init; }
    public double? GrowthPercent { get; init; }
    public double? StaminaPercent { get; init; }
    public double? FoodPercent { get; init; }
    public double? WaterPercent { get; init; }
    public IReadOnlyList<string> Conditions { get; init; } = [];
    public TelemetryShareScope ShareScope { get; init; } = TelemetryShareScope.Self;
    public IReadOnlyList<string> AllowedViewerSteamIds { get; init; } = [];
}

public sealed record TelemetryFrame
{
    public int ProtocolVersion { get; init; } = TelemetryProtocol.Version;
    public required string ServerId { get; init; }
    public string ServerName { get; init; } = "The Isle server";
    public required string BridgeSessionId { get; init; }
    public long Sequence { get; init; }
    public DateTimeOffset SampledAt { get; init; }
    public string Source { get; init; } = "unknown";
    public TelemetryVisibilityPolicy VisibilityPolicy { get; init; }
    public TelemetryCapabilities Capabilities { get; init; } = new();
    public IReadOnlyList<TelemetryEntity> Entities { get; init; } = [];
}

public sealed record ViewerTelemetryEntity
{
    public required string Id { get; init; }
    public string Label { get; init; } = "Animal";
    public bool Self { get; init; }
    public bool Friend { get; init; }
    public TelemetryEntityKind Kind { get; init; }
    public string? SpeciesId { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double? Yaw { get; init; }
    public TelemetryDirectionQuality DirectionQuality { get; init; }
    public double? HealthPercent { get; init; }
    public double? GrowthPercent { get; init; }
    public double? StaminaPercent { get; init; }
    public double? FoodPercent { get; init; }
    public double? WaterPercent { get; init; }
    public IReadOnlyList<string> Conditions { get; init; } = [];
}

public sealed record ViewerTelemetrySnapshot
{
    public int ProtocolVersion { get; init; } = TelemetryProtocol.Version;
    public required string ServerId { get; init; }
    public string ServerName { get; init; } = "The Isle server";
    public long Sequence { get; init; }
    public DateTimeOffset SampledAt { get; init; }
    public DateTimeOffset RelayedAt { get; init; }
    public double RelayAgeMilliseconds { get; init; }
    public double? UpdateRateHz { get; init; }
    public int ConnectedPlayerNodes { get; init; }
    public int VisibleEntityCount { get; init; }
    public string Source { get; init; } = "unknown";
    public TelemetryVisibilityPolicy VisibilityPolicy { get; init; }
    public TelemetryCapabilities Capabilities { get; init; } = new();
    public ViewerTelemetryEntity? Self { get; init; }
    public IReadOnlyList<ViewerTelemetryEntity> Players { get; init; } = [];
}

public sealed record PluginTelemetryFrame
{
    public DateTimeOffset SampledAt { get; init; }
    public string Source { get; init; } = "plugin";
    public TelemetryCapabilities Capabilities { get; init; } = new()
    {
        Position = true,
        AuthoritativeDirection = true,
        Health = true,
        Growth = true,
        Stamina = true,
        Food = true,
        Water = true,
        Conditions = true,
        AiAnimals = true
    };
    public IReadOnlyList<TelemetryEntity> Entities { get; init; } = [];
}

public static partial class TelemetryValidation
{
    public static IReadOnlyList<string> Validate(TelemetryFrame frame, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (frame.ProtocolVersion != TelemetryProtocol.Version)
        {
            errors.Add("Unsupported protocol version.");
        }
        if (!IsSlug(frame.ServerId, 64))
        {
            errors.Add("ServerId must be a lowercase slug.");
        }
        if (!IsOpaqueId(frame.BridgeSessionId, 32, 96))
        {
            errors.Add("BridgeSessionId is invalid.");
        }
        if (frame.Sequence < 1)
        {
            errors.Add("Sequence must be positive.");
        }
        if (frame.SampledAt == default
            || frame.SampledAt > now.AddMinutes(2)
            || now - frame.SampledAt > TelemetryProtocol.MaximumFrameAge)
        {
            errors.Add("SampledAt is outside the accepted window.");
        }
        if (frame.Entities.Count > TelemetryProtocol.MaximumEntities)
        {
            errors.Add("Too many telemetry entities.");
        }

        var entityIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in frame.Entities)
        {
            if (!IsOpaqueId(entity.EntityId, 1, 96) || !entityIds.Add(entity.EntityId))
            {
                errors.Add("Entity IDs must be unique, printable identifiers.");
                break;
            }
            if (entity.SteamId is not null && !SteamIdRegex().IsMatch(entity.SteamId))
            {
                errors.Add($"Entity {entity.EntityId} has an invalid SteamID64.");
            }
            if (!IsFiniteBounded(entity.X, -2_000_000, 2_000_000)
                || !IsFiniteBounded(entity.Y, -2_000_000, 2_000_000)
                || !IsFiniteBounded(entity.Z, -500_000, 500_000))
            {
                errors.Add($"Entity {entity.EntityId} has invalid coordinates.");
            }
            if (entity.Yaw is double yaw && !IsFiniteBounded(yaw, -360_000, 360_000))
            {
                errors.Add($"Entity {entity.EntityId} has an invalid direction.");
            }
            if (!ValidPercent(entity.HealthPercent)
                || !ValidPercent(entity.GrowthPercent)
                || !ValidPercent(entity.StaminaPercent)
                || !ValidPercent(entity.FoodPercent)
                || !ValidPercent(entity.WaterPercent))
            {
                errors.Add($"Entity {entity.EntityId} has an invalid percentage.");
            }
            if (entity.Conditions.Count > TelemetryProtocol.MaximumConditionsPerEntity
                || entity.Conditions.Any(value => !IsShortText(value, 48)))
            {
                errors.Add($"Entity {entity.EntityId} has invalid conditions.");
            }
            if (entity.AllowedViewerSteamIds.Count > 128
                || entity.AllowedViewerSteamIds.Any(value => !SteamIdRegex().IsMatch(value)))
            {
                errors.Add($"Entity {entity.EntityId} has invalid viewer grants.");
            }
        }
        return errors;
    }

    public static bool IsSteamId(string? value) =>
        value is not null && SteamIdRegex().IsMatch(value);

    public static string CleanLabel(string? value, string fallback, int maximumLength)
    {
        var cleaned = ControlCharacterRegex().Replace(value ?? string.Empty, " ");
        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim();
        if (string.IsNullOrEmpty(cleaned))
        {
            cleaned = fallback;
        }
        return cleaned.Length <= maximumLength ? cleaned : cleaned[..maximumLength];
    }

    private static bool ValidPercent(double? value) =>
        value is null || IsFiniteBounded(value.Value, 0, 100);

    private static bool IsFiniteBounded(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;

    private static bool IsSlug(string? value, int maximumLength) =>
        value is not null && value.Length <= maximumLength && SlugRegex().IsMatch(value);

    private static bool IsOpaqueId(string? value, int minimumLength, int maximumLength) =>
        value is not null
        && value.Length >= minimumLength
        && value.Length <= maximumLength
        && OpaqueIdRegex().IsMatch(value);

    private static bool IsShortText(string? value, int maximumLength) =>
        value is not null
        && value.Length is > 0
        && value.Length <= maximumLength
        && !ControlCharacterRegex().IsMatch(value);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex OpaqueIdRegex();

    [GeneratedRegex("^7656119[0-9]{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex SteamIdRegex();

    [GeneratedRegex("[\\u0000-\\u001F\\u007F]", RegexOptions.CultureInvariant)]
    private static partial Regex ControlCharacterRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
