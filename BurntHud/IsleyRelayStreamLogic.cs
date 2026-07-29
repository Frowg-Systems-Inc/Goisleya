using System.Text.Json;
using Isley.Telemetry;

namespace Isley;

/// <summary>
/// Verdict of applying one relay viewer-stream frame to the client's stream
/// state. The receive loop turns every non-Applied verdict into an honest UI
/// state or a resyncing reconnect; partial state is never surfaced.
/// </summary>
internal enum IsleyRelayFrameVerdict
{
    // A materialized full snapshot is ready for consumers.
    Applied,
    // Control frame (hello negotiation) handled; no snapshot produced.
    Ignored,
    // The frame could not be applied safely; reconnect to force a keyframe resync.
    ResyncRequired,
    // The relay speaks a stream version newer than this build understands;
    // show a waiting/update-required state and never parse further frames.
    UpdateRequired
}

/// <summary>
/// Client-side state machine for the relay viewer stream (versions 1 and 2).
/// Version 1 connections receive full snapshots only and pass them through
/// exactly as before. Version 2 connections negotiate via a hello exchange,
/// keep a bounded entity cache keyed by id, and apply delta frames against the
/// last snapshot actually applied on this connection. Consumers always see the
/// same materialized <see cref="ViewerTelemetrySnapshot"/> shape regardless of
/// the negotiated version. All mutation is validation-first: a malformed delta
/// leaves the cached state untouched so a reconnect can resync from a keyframe.
/// </summary>
internal sealed class IsleyRelayStreamSession
{
    private static readonly JsonSerializerOptions WireOptions =
        new(JsonSerializerDefaults.Web);

    private readonly List<ViewerTelemetryEntity> _entities = [];
    private readonly Dictionary<string, int> _indexById = new(StringComparer.Ordinal);
    private ViewerTelemetrySnapshot? _base;

    /// <summary>0 until a hello answer arrives; 1 or 2 after negotiation.</summary>
    internal int NegotiatedStreamVersion { get; private set; }
    internal int KeyframeIntervalFrames { get; private set; }
    internal bool DeltaEncodingActive { get; private set; }
    internal bool UpdateRequired { get; private set; }
    internal bool HasBaseState => _base is not null;

    internal void MarkUpdateRequired() => UpdateRequired = true;

    /// <summary>
    /// Handles the relay's hello answer ({type:"hello", streamVersion,
    /// keyframeIntervalFrames, deltaEncoding}). Missing fields default to the
    /// version-1 behavior so older or partial answers stay safe. Any prior base
    /// state is dropped because the relay restarts delta delivery with a
    /// keyframe after (re)negotiation.
    /// </summary>
    internal IsleyRelayFrameVerdict TryNegotiate(JsonElement hello, out string detail)
    {
        detail = string.Empty;
        var streamVersion = hello.TryGetProperty("streamVersion", out var versionElement)
                            && versionElement.ValueKind == JsonValueKind.Number
                            && versionElement.TryGetInt32(out var parsedVersion)
            ? parsedVersion
            : 1;
        if (streamVersion > TelemetryProtocol.ViewerStreamVersion)
        {
            UpdateRequired = true;
            detail =
                $"Relay viewer stream v{streamVersion} is newer than this Isley build (v{TelemetryProtocol.ViewerStreamVersion}) · update Isley to watch this server";
            return IsleyRelayFrameVerdict.UpdateRequired;
        }
        if (streamVersion < 1)
        {
            detail = "the relay answered hello with an invalid stream version";
            return IsleyRelayFrameVerdict.ResyncRequired;
        }

        NegotiatedStreamVersion = streamVersion;
        KeyframeIntervalFrames = hello.TryGetProperty("keyframeIntervalFrames", out var intervalElement)
                                 && intervalElement.ValueKind == JsonValueKind.Number
                                 && intervalElement.TryGetInt32(out var parsedInterval)
            ? Math.Clamp(parsedInterval, 1, 4096)
            : 0;
        DeltaEncodingActive = streamVersion >= 2
                              && hello.TryGetProperty("deltaEncoding", out var deltaElement)
                              && deltaElement.ValueKind == JsonValueKind.True;
        ResetBase();
        return IsleyRelayFrameVerdict.Ignored;
    }

    /// <summary>
    /// Applies a full snapshot frame (version-1 frames and version-2 keyframes
    /// are both complete state replacements). The snapshot is validated and
    /// then becomes the base for any following deltas.
    /// </summary>
    internal IsleyRelayFrameVerdict TryApplySnapshot(
        JsonElement snapshotElement,
        out ViewerTelemetrySnapshot? snapshot,
        out string detail)
    {
        snapshot = null;
        detail = string.Empty;
        ViewerTelemetrySnapshot? parsed;
        try
        {
            parsed = snapshotElement.Deserialize<ViewerTelemetrySnapshot>(WireOptions);
        }
        catch (JsonException)
        {
            detail = "snapshot frame was not well-formed";
            return IsleyRelayFrameVerdict.ResyncRequired;
        }
        if (parsed is null || parsed.ProtocolVersion != TelemetryProtocol.Version)
        {
            detail = "the relay telemetry version is not supported";
            return IsleyRelayFrameVerdict.ResyncRequired;
        }

        ReplaceBase(parsed);
        snapshot = parsed;
        return IsleyRelayFrameVerdict.Applied;
    }

    /// <summary>
    /// Applies one version-2 delta frame against the last applied snapshot:
    /// upserts replace entities by id, removed ids are deleted, self is
    /// replaced when non-null and removed when selfRemoved, and scalar fields
    /// are overwritten only when present (null means unchanged; sequence,
    /// sampledAt, relayedAt, relayAgeMilliseconds, and the counts are always
    /// present). Everything is validated before any mutation so a malformed
    /// delta can never leave partial state behind.
    /// </summary>
    internal IsleyRelayFrameVerdict TryApplyDelta(
        JsonElement deltaElement,
        out ViewerTelemetrySnapshot? snapshot,
        out string detail)
    {
        snapshot = null;
        detail = string.Empty;
        if (NegotiatedStreamVersion < 2 || _base is null)
        {
            detail = "a delta frame arrived before v2 negotiation delivered a keyframe";
            return IsleyRelayFrameVerdict.ResyncRequired;
        }

        ViewerTelemetryDelta? delta;
        try
        {
            delta = deltaElement.Deserialize<ViewerTelemetryDelta>(WireOptions);
        }
        catch (JsonException)
        {
            detail = "delta frame was not well-formed";
            return IsleyRelayFrameVerdict.ResyncRequired;
        }
        if (delta is null)
        {
            detail = "delta frame was empty";
            return IsleyRelayFrameVerdict.ResyncRequired;
        }
        if (delta.StreamVersion > TelemetryProtocol.ViewerStreamVersion)
        {
            UpdateRequired = true;
            detail =
                $"Relay viewer stream v{delta.StreamVersion} is newer than this Isley build (v{TelemetryProtocol.ViewerStreamVersion}) · update Isley to watch this server";
            return IsleyRelayFrameVerdict.UpdateRequired;
        }
        if (!string.Equals(delta.ServerId, _base.ServerId, StringComparison.Ordinal))
        {
            detail = "a delta frame targeted a different server than the applied keyframe";
            return IsleyRelayFrameVerdict.ResyncRequired;
        }
        if (delta.BaseSequence != _base.Sequence)
        {
            // Deltas are computed against the last snapshot actually sent on
            // this connection; a gap means an applied frame was skipped and
            // only a keyframe can restore a trustworthy base.
            detail = "a delta frame skipped the last applied snapshot sequence";
            return IsleyRelayFrameVerdict.ResyncRequired;
        }
        if (delta.Upserted.Count > TelemetryProtocol.MaximumEntities
            || delta.Removed.Count > TelemetryProtocol.MaximumEntities)
        {
            detail = "a delta frame exceeded the entity bounds";
            return IsleyRelayFrameVerdict.ResyncRequired;
        }

        var upsertIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in delta.Upserted)
        {
            if (!IsValidEntity(entity) || !upsertIds.Add(entity.Id))
            {
                detail = "a delta frame carried an invalid or duplicated entity";
                return IsleyRelayFrameVerdict.ResyncRequired;
            }
        }
        foreach (var id in delta.Removed)
        {
            if (!IsValidId(id))
            {
                detail = "a delta frame carried an invalid removal id";
                return IsleyRelayFrameVerdict.ResyncRequired;
            }
        }

        // Project the resulting roster size before mutating anything. The
        // contract applies upserts first, then removals, so an id carried by
        // both lists ends up removed.
        var projected = _entities.Count;
        foreach (var entity in delta.Upserted)
        {
            if (!_indexById.ContainsKey(entity.Id))
            {
                projected++;
            }
        }
        var countedRemovals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in delta.Removed)
        {
            if (!countedRemovals.Add(id))
            {
                continue;
            }
            if (_indexById.ContainsKey(id) || upsertIds.Contains(id))
            {
                projected--;
            }
        }
        if (projected > TelemetryProtocol.MaximumEntities)
        {
            detail = "a delta frame would grow the roster past the entity bound";
            return IsleyRelayFrameVerdict.ResyncRequired;
        }

        foreach (var entity in delta.Upserted)
        {
            if (_indexById.TryGetValue(entity.Id, out var index))
            {
                _entities[index] = entity;
            }
            else
            {
                _indexById[entity.Id] = _entities.Count;
                _entities.Add(entity);
            }
        }
        var removedAny = false;
        foreach (var id in delta.Removed)
        {
            if (_indexById.TryGetValue(id, out var index))
            {
                _entities.RemoveAt(index);
                removedAny = true;
            }
        }
        if (removedAny)
        {
            RebuildIndex();
        }

        var self = delta.Self ?? (delta.SelfRemoved ? null : _base.Self);
        var materialized = _base with
        {
            Sequence = delta.Sequence,
            SampledAt = delta.SampledAt,
            RelayedAt = delta.RelayedAt,
            RelayAgeMilliseconds = delta.RelayAgeMilliseconds,
            UpdateRateHz = delta.UpdateRateHz ?? _base.UpdateRateHz,
            ConnectedPlayerNodes = delta.ConnectedPlayerNodes,
            VisibleEntityCount = delta.VisibleEntityCount,
            ServerName = delta.ServerName ?? _base.ServerName,
            Source = delta.Source ?? _base.Source,
            VisibilityPolicy = delta.VisibilityPolicy ?? _base.VisibilityPolicy,
            Capabilities = delta.Capabilities ?? _base.Capabilities,
            Self = self,
            Players = _entities.ToArray()
        };
        _base = materialized;
        snapshot = materialized;
        return IsleyRelayFrameVerdict.Applied;
    }

    private void ResetBase()
    {
        _base = null;
        _entities.Clear();
        _indexById.Clear();
    }

    private void ReplaceBase(ViewerTelemetrySnapshot snapshot)
    {
        _base = snapshot;
        _entities.Clear();
        _indexById.Clear();
        foreach (var entity in snapshot.Players)
        {
            // Full snapshots pass through to consumers untouched; the cache
            // keeps the first copy of any duplicated id purely so later delta
            // bookkeeping cannot throw on a malformed-but-tolerated keyframe.
            if (_indexById.ContainsKey(entity.Id))
            {
                continue;
            }
            _indexById[entity.Id] = _entities.Count;
            _entities.Add(entity);
        }
    }

    private void RebuildIndex()
    {
        _indexById.Clear();
        for (var index = 0; index < _entities.Count; index++)
        {
            _indexById[_entities[index].Id] = index;
        }
    }

    private static bool IsValidEntity(ViewerTelemetryEntity entity) =>
        IsValidId(entity.Id)
        && entity.Conditions.Count <= TelemetryProtocol.MaximumConditionsPerEntity;

    private static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 96;
}

internal static class IsleyRelayStreamLogic
{
    /// <summary>
    /// True when a frame envelope carries a streamVersion newer than this
    /// build understands. Such frames must never be parsed; the client shows
    /// a waiting/update-required state instead.
    /// </summary>
    internal static bool IsUnsupportedStreamVersion(JsonElement envelope, out int streamVersion)
    {
        streamVersion = 0;
        return envelope.TryGetProperty("streamVersion", out var element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetInt32(out streamVersion)
               && streamVersion > TelemetryProtocol.ViewerStreamVersion;
    }
}
