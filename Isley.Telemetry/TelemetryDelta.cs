namespace Isley.Telemetry;

/// <summary>
/// Viewer-stream delta frame (stream version 2). Carries only what changed
/// relative to the snapshot the viewer last applied on this connection.
/// Null fields mean "unchanged"; empty lists mean "no entity changes".
/// A delta is always computed against the last snapshot actually sent on the
/// same connection, so it stays valid even when intermediate bridge frames
/// were coalesced; <see cref="BaseSequence"/> is informational for viewers
/// that want to detect gaps.
/// </summary>
public sealed record ViewerTelemetryDelta
{
    public int StreamVersion { get; init; } = TelemetryProtocol.ViewerStreamVersion;
    public required string ServerId { get; init; }
    public long Sequence { get; init; }
    public long BaseSequence { get; init; }
    public DateTimeOffset SampledAt { get; init; }
    public DateTimeOffset RelayedAt { get; init; }
    public double RelayAgeMilliseconds { get; init; }
    public double? UpdateRateHz { get; init; }
    public int ConnectedPlayerNodes { get; init; }
    public int VisibleEntityCount { get; init; }
    public string? ServerName { get; init; }
    public string? Source { get; init; }
    public TelemetryVisibilityPolicy? VisibilityPolicy { get; init; }
    public TelemetryCapabilities? Capabilities { get; init; }
    public ViewerTelemetryEntity? Self { get; init; }
    public bool SelfRemoved { get; init; }
    public IReadOnlyList<ViewerTelemetryEntity> Upserted { get; init; } = [];
    public IReadOnlyList<string> Removed { get; init; } = [];
}

public static class ViewerTelemetryDeltaBuilder
{
    /// <summary>
    /// Builds the delta from <paramref name="previous"/> to
    /// <paramref name="current"/>. Returns false when no delta can represent
    /// the transition (different server) and a full keyframe must be sent.
    /// </summary>
    public static bool TryCreate(
        ViewerTelemetrySnapshot previous,
        ViewerTelemetrySnapshot current,
        out ViewerTelemetryDelta delta)
    {
        delta = null!;
        if (!string.Equals(previous.ServerId, current.ServerId, StringComparison.Ordinal))
        {
            return false;
        }

        var previousById = new Dictionary<string, ViewerTelemetryEntity>(StringComparer.Ordinal);
        foreach (var entity in previous.Players)
        {
            previousById[entity.Id] = entity;
        }
        var currentIds = new HashSet<string>(StringComparer.Ordinal);
        var upserted = new List<ViewerTelemetryEntity>();
        foreach (var entity in current.Players)
        {
            currentIds.Add(entity.Id);
            if (!previousById.TryGetValue(entity.Id, out var before) || !SameEntity(before, entity))
            {
                upserted.Add(entity);
            }
        }
        var removed = new List<string>();
        foreach (var entity in previous.Players)
        {
            if (!currentIds.Contains(entity.Id))
            {
                removed.Add(entity.Id);
            }
        }

        var selfRemoved = previous.Self is not null && current.Self is null;
        var selfChanged = current.Self is not null
                          && (previous.Self is null || !SameEntity(previous.Self, current.Self));

        delta = new ViewerTelemetryDelta
        {
            ServerId = current.ServerId,
            Sequence = current.Sequence,
            BaseSequence = previous.Sequence,
            SampledAt = current.SampledAt,
            RelayedAt = current.RelayedAt,
            RelayAgeMilliseconds = current.RelayAgeMilliseconds,
            UpdateRateHz = current.UpdateRateHz,
            ConnectedPlayerNodes = current.ConnectedPlayerNodes,
            VisibleEntityCount = current.VisibleEntityCount,
            ServerName = string.Equals(
                previous.ServerName, current.ServerName, StringComparison.Ordinal)
                ? null
                : current.ServerName,
            Source = string.Equals(previous.Source, current.Source, StringComparison.Ordinal)
                ? null
                : current.Source,
            VisibilityPolicy = previous.VisibilityPolicy == current.VisibilityPolicy
                ? null
                : current.VisibilityPolicy,
            Capabilities = previous.Capabilities == current.Capabilities
                ? null
                : current.Capabilities,
            Self = selfChanged ? current.Self : null,
            SelfRemoved = selfRemoved,
            Upserted = upserted,
            Removed = removed
        };
        return true;
    }

    private static bool SameEntity(ViewerTelemetryEntity left, ViewerTelemetryEntity right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal)
        && string.Equals(left.Label, right.Label, StringComparison.Ordinal)
        && left.Self == right.Self
        && left.Friend == right.Friend
        && left.Kind == right.Kind
        && string.Equals(left.SpeciesId, right.SpeciesId, StringComparison.Ordinal)
        && left.X == right.X
        && left.Y == right.Y
        && left.Z == right.Z
        && left.Yaw == right.Yaw
        && left.DirectionQuality == right.DirectionQuality
        && left.HealthPercent == right.HealthPercent
        && left.GrowthPercent == right.GrowthPercent
        && left.StaminaPercent == right.StaminaPercent
        && left.FoodPercent == right.FoodPercent
        && left.WaterPercent == right.WaterPercent
        && left.Conditions.SequenceEqual(right.Conditions, StringComparer.Ordinal);
}
