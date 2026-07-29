using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Isley;
using Isley.Telemetry;

// RelayStreamV2Verifier — proves the BurntHud overlay's opt-in to relay
// viewer stream v2 (delta encoding) against the contract-locked server side.
// Deltas are produced by the shipped Isley.Telemetry builder and serialized
// with the same JSON posture the relay uses (web camelCase, nulls omitted),
// then applied through the client's JSON-driven session, exactly as the
// WebSocket receive loop does.

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

// Same JSON posture the relay uses: web camelCase with nulls omitted.
var relayJson = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

JsonElement Wire(object value)
{
    var json = JsonSerializer.Serialize(value, relayJson);
    using var document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
}

static JsonElement Parse(string json)
{
    using var document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
}

static ViewerTelemetryEntity Entity(
    string id,
    double x,
    double y,
    bool friend = false) =>
    new()
    {
        Id = id,
        Label = $"Label-{id}",
        X = x,
        Y = y,
        Z = 5,
        Yaw = 90,
        DirectionQuality = TelemetryDirectionQuality.ServerAuthoritative,
        Friend = friend,
        Kind = TelemetryEntityKind.Player
    };

static ViewerTelemetryEntity Self(double health) =>
    new()
    {
        Id = "self",
        Label = "You",
        Self = true,
        X = 100,
        Y = 200,
        Z = 5,
        Yaw = 45,
        DirectionQuality = TelemetryDirectionQuality.ServerAuthoritative,
        Kind = TelemetryEntityKind.Player,
        SpeciesId = "triceratops",
        HealthPercent = health,
        GrowthPercent = 82,
        StaminaPercent = 73,
        FoodPercent = 64,
        WaterPercent = 55,
        Conditions = ["vomit-sickness"]
    };

static ViewerTelemetrySnapshot Snapshot(
    string serverId,
    long sequence,
    IReadOnlyList<ViewerTelemetryEntity> players,
    ViewerTelemetryEntity? self,
    string serverName = "Alpha",
    string source = "plugin",
    double? updateRateHz = 5,
    int connectedPlayerNodes = 2) =>
    new()
    {
        ServerId = serverId,
        ServerName = serverName,
        Sequence = sequence,
        SampledAt = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000 + sequence * 100),
        RelayedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_050 + sequence * 100),
        RelayAgeMilliseconds = 50,
        UpdateRateHz = updateRateHz,
        ConnectedPlayerNodes = connectedPlayerNodes,
        VisibleEntityCount = players.Count + (self is null ? 0 : 1),
        Source = source,
        VisibilityPolicy = TelemetryVisibilityPolicy.PrivacyFiltered,
        Capabilities = new TelemetryCapabilities(),
        Self = self,
        Players = players
    };

static bool SameEntity(ViewerTelemetryEntity left, ViewerTelemetryEntity right) =>
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

static void CheckSnapshot(
    ViewerTelemetrySnapshot actual,
    ViewerTelemetrySnapshot expected,
    string label)
{
    Check(actual.ServerId == expected.ServerId, $"{label}: server id drifted.");
    Check(actual.ServerName == expected.ServerName, $"{label}: server name drifted.");
    Check(actual.Sequence == expected.Sequence, $"{label}: sequence drifted.");
    Check(actual.SampledAt == expected.SampledAt, $"{label}: sampledAt drifted.");
    Check(actual.RelayedAt == expected.RelayedAt, $"{label}: relayedAt drifted.");
    Check(actual.RelayAgeMilliseconds == expected.RelayAgeMilliseconds,
        $"{label}: relay age drifted.");
    Check(actual.UpdateRateHz == expected.UpdateRateHz, $"{label}: update rate drifted.");
    Check(actual.ConnectedPlayerNodes == expected.ConnectedPlayerNodes,
        $"{label}: node count drifted.");
    Check(actual.VisibleEntityCount == expected.VisibleEntityCount,
        $"{label}: visible count drifted.");
    Check(actual.Source == expected.Source, $"{label}: source drifted.");
    Check(actual.VisibilityPolicy == expected.VisibilityPolicy,
        $"{label}: visibility policy drifted.");
    Check(actual.Players.Count == expected.Players.Count
          && actual.Players.Zip(expected.Players).All(pair => SameEntity(pair.First, pair.Second)),
        $"{label}: player roster drifted.");
    Check((actual.Self is null) == (expected.Self is null)
          && (actual.Self is null || SameEntity(actual.Self, expected.Self!)),
        $"{label}: self drifted.");
}

IsleyRelayStreamSession NegotiatedSession(ViewerTelemetrySnapshot keyframe)
{
    var session = new IsleyRelayStreamSession();
    Check(session.TryNegotiate(
              Parse("""{"type":"hello","streamVersion":2,"keyframeIntervalFrames":240,"deltaEncoding":true}"""),
              out _) == IsleyRelayFrameVerdict.Ignored,
        "A v2 hello answer was not accepted.");
    Check(session.TryApplySnapshot(
              Wire(new { type = "snapshot", streamVersion = 2, keyframe = true, snapshot = keyframe })
                  .GetProperty("snapshot"),
              out _,
              out _) == IsleyRelayFrameVerdict.Applied,
        "The v2 keyframe was not applied.");
    return session;
}

JsonElement DeltaElement(ViewerTelemetryDelta delta) =>
    Wire(new { type = "delta", streamVersion = TelemetryProtocol.ViewerStreamVersion, delta })
        .GetProperty("delta");

void CheckMalformedLeavesStateUntouched(
    JsonElement malformed,
    ViewerTelemetrySnapshot currentBase,
    ViewerTelemetrySnapshot next,
    string label)
{
    // Every malformed case runs against its own freshly-keyframed session so
    // cases stay independent of each other.
    var session = NegotiatedSession(currentBase);
    Check(session.TryApplyDelta(malformed, out _, out _) == IsleyRelayFrameVerdict.ResyncRequired,
        $"{label}: malformed delta was not refused with a resync.");
    Check(ViewerTelemetryDeltaBuilder.TryCreate(currentBase, next, out var followUp),
        $"{label}: the locked builder refused a same-server transition.");
    Check(session.TryApplyDelta(DeltaElement(followUp), out var recovered, out _)
              == IsleyRelayFrameVerdict.Applied
          && recovered is not null,
        $"{label}: a valid delta after a malformed one did not apply.");
    CheckSnapshot(recovered!, next, $"{label} (post-malformed state)");
}

// ---------------------------------------------------------------- constants
Check(TelemetryProtocol.ViewerStreamVersion == 2,
    "The locked viewer stream version drifted from 2.");
Check(TelemetryProtocol.MaximumEntities == 512,
    "The locked entity bound drifted.");

// ------------------------------------------------- hello negotiation shape
var helloSession = new IsleyRelayStreamSession();
Check(helloSession.NegotiatedStreamVersion == 0 && !helloSession.DeltaEncodingActive,
    "A fresh session must look like a legacy v1 connection.");
Check(helloSession.TryNegotiate(
          Parse("""{"type":"hello","streamVersion":2,"keyframeIntervalFrames":240,"deltaEncoding":true}"""),
          out _) == IsleyRelayFrameVerdict.Ignored
      && helloSession.NegotiatedStreamVersion == 2
      && helloSession.KeyframeIntervalFrames == 240
      && helloSession.DeltaEncodingActive
      && !helloSession.HasBaseState,
    "The v2 hello negotiation shape was not parsed as negotiated.");
Check(new IsleyRelayStreamSession().TryNegotiate(
          Parse("""{"type":"hello","streamVersion":1,"keyframeIntervalFrames":240,"deltaEncoding":false}"""),
          out _) == IsleyRelayFrameVerdict.Ignored,
    "A v1-clamped hello answer was rejected.");
var helloDefaults = new IsleyRelayStreamSession();
Check(helloDefaults.TryNegotiate(Parse("""{"type":"hello"}"""), out _)
          == IsleyRelayFrameVerdict.Ignored
      && helloDefaults.NegotiatedStreamVersion == 1
      && !helloDefaults.DeltaEncodingActive,
    "A hello answer without fields must default to safe v1 behavior.");
var helloV1Deltas = new IsleyRelayStreamSession();
Check(helloV1Deltas.TryNegotiate(
          Parse("""{"type":"hello","streamVersion":1,"deltaEncoding":true}"""), out _)
          == IsleyRelayFrameVerdict.Ignored
      && !helloV1Deltas.DeltaEncodingActive,
    "Delta encoding must require stream version 2.");
var helloClamp = new IsleyRelayStreamSession();
Check(helloClamp.TryNegotiate(
          Parse("""{"type":"hello","streamVersion":2,"keyframeIntervalFrames":999999,"deltaEncoding":true}"""),
          out _) == IsleyRelayFrameVerdict.Ignored
      && helloClamp.KeyframeIntervalFrames == 4096,
    "An unbounded keyframe interval was not clamped.");
var helloFuture = new IsleyRelayStreamSession();
Check(helloFuture.TryNegotiate(
          Parse("""{"type":"hello","streamVersion":3,"keyframeIntervalFrames":240,"deltaEncoding":true}"""),
          out var futureDetail) == IsleyRelayFrameVerdict.UpdateRequired
      && helloFuture.UpdateRequired
      && futureDetail.Contains('3'),
    "A newer stream version must surface the update-required waiting state.");
Check(new IsleyRelayStreamSession().TryNegotiate(
          Parse("""{"type":"hello","streamVersion":0}"""), out _)
          == IsleyRelayFrameVerdict.ResyncRequired,
    "An invalid hello stream version must resync, never parse.");
Check(IsleyRelayStreamLogic.IsUnsupportedStreamVersion(Parse("""{"streamVersion":3}"""), out var unsupported)
      && unsupported == 3,
    "Envelope stream-version screening missed a newer stream.");
Check(!IsleyRelayStreamLogic.IsUnsupportedStreamVersion(Parse("""{"streamVersion":2}"""), out _)
      && !IsleyRelayStreamLogic.IsUnsupportedStreamVersion(Parse("""{}"""), out _),
    "Envelope stream-version screening false-positived on understood frames.");

// ------------------------------------------------------------ delta apply
var s1 = Snapshot("srv-a", 10, [Entity("A", 1, 2), Entity("B", 3, 4, friend: true)], Self(94));
var s2 = Snapshot("srv-b", 10, [Entity("A", 1, 2)], Self(94));
Check(!ViewerTelemetryDeltaBuilder.TryCreate(s1, s2, out _),
    "The locked builder must demand a keyframe when the server changes.");

var base10 = Snapshot("srv-a", 10, [Entity("A", 1, 2), Entity("B", 3, 4)], Self(94));
var next11 = Snapshot(
    "srv-a",
    11,
    [Entity("A", 5, 6), Entity("C", 7, 8)],
    Self(50),
    serverName: "Beta",
    updateRateHz: 6,
    connectedPlayerNodes: 3);
Check(ViewerTelemetryDeltaBuilder.TryCreate(base10, next11, out var delta11),
    "The locked builder refused a same-server transition.");
Check(delta11.BaseSequence == 10
      && delta11.Sequence == 11
      && delta11.Upserted.Count == 2
      && delta11.Removed.SequenceEqual(["B"])
      && delta11.Self is not null
      && delta11.ServerName == "Beta"
      && delta11.Source is null
      && !delta11.SelfRemoved,
    "The locked builder's delta shape drifted.");

var applySession = NegotiatedSession(base10);
Check(applySession.TryApplyDelta(DeltaElement(delta11), out var applied11, out _)
          == IsleyRelayFrameVerdict.Applied
      && applied11 is not null,
    "A valid wire delta was not applied.");
CheckSnapshot(applied11!, next11, "delta apply");
Check(applied11!.Players.Select(player => player.Id).SequenceEqual(["A", "C"]),
    "Delta apply must replace upserts in place, drop removals, and append new ids.");

// Scalar null = unchanged; always-present fields still overwrite.
var next12 = next11 with
{
    Sequence = 12,
    SampledAt = next11.SampledAt.AddMilliseconds(100),
    RelayedAt = next11.RelayedAt.AddMilliseconds(100),
    RelayAgeMilliseconds = 63,
    UpdateRateHz = null
};
Check(ViewerTelemetryDeltaBuilder.TryCreate(next11, next12, out var delta12),
    "The locked builder refused a scalar-only transition.");
Check(applySession.TryApplyDelta(DeltaElement(delta12), out var applied12, out _)
          == IsleyRelayFrameVerdict.Applied
      && applied12 is not null,
    "A scalar-only delta was not applied.");
Check(applied12!.UpdateRateHz == 6,
    "A null delta scalar must leave the applied value unchanged.");
Check(applied12.RelayAgeMilliseconds == 63 && applied12.Sequence == 12,
    "Always-present delta scalars were not overwritten.");
CheckSnapshot(applied12, next12 with { UpdateRateHz = 6 }, "null-scalar apply");

// Self removal and re-add through the wire.
var next13 = next12 with { Sequence = 13, Self = null, VisibleEntityCount = 2 };
Check(ViewerTelemetryDeltaBuilder.TryCreate(next12, next13, out var delta13)
      && delta13.SelfRemoved
      && applySession.TryApplyDelta(DeltaElement(delta13), out var applied13, out _)
          == IsleyRelayFrameVerdict.Applied
      && applied13 is { Self: null },
    "selfRemoved did not remove the applied self.");
var next14 = next13 with { Sequence = 14, Self = Self(71), VisibleEntityCount = 3 };
Check(ViewerTelemetryDeltaBuilder.TryCreate(next13, next14, out var delta14)
      && delta14.Self is not null
      && applySession.TryApplyDelta(DeltaElement(delta14), out var applied14, out _)
          == IsleyRelayFrameVerdict.Applied
      && applied14 is { Self.HealthPercent: 71 },
    "A non-null delta self did not replace the applied self.");

// Chained deltas track the last applied snapshot; gaps are refused.
var gapped = next14 with { Sequence = 16 };
Check(ViewerTelemetryDeltaBuilder.TryCreate(next14, gapped, out var delta16),
    "The locked builder refused the chained transition.");
Check(applySession.TryApplyDelta(DeltaElement(delta16), out _, out _)
          == IsleyRelayFrameVerdict.Applied,
    "A chained delta against the last applied snapshot was refused.");
Check(ViewerTelemetryDeltaBuilder.TryCreate(base10, gapped, out var staleDelta)
      && applySession.TryApplyDelta(DeltaElement(staleDelta), out _, out _)
          == IsleyRelayFrameVerdict.ResyncRequired,
    "A delta that skipped the last applied sequence must force a resync.");

// ---------------------------------------------------------- keyframe reset
var resetSession = NegotiatedSession(base10);
Check(resetSession.TryApplyDelta(DeltaElement(delta11), out _, out _)
          == IsleyRelayFrameVerdict.Applied,
    "Pre-keyframe delta did not apply.");
var keyframeOther = Snapshot("srv-b", 1, [Entity("Z", 9, 9)], null, serverName: "Other");
Check(resetSession.TryApplySnapshot(
          Wire(new { type = "snapshot", streamVersion = 2, keyframe = true, snapshot = keyframeOther })
              .GetProperty("snapshot"),
          out var resetApplied,
          out _) == IsleyRelayFrameVerdict.Applied
      && resetApplied is not null,
    "A keyframe after deltas was not applied.");
CheckSnapshot(resetApplied!, keyframeOther, "keyframe reset");
Check(resetApplied!.Players.Select(player => player.Id).SequenceEqual(["Z"]),
    "A keyframe must fully replace the cached roster — no stale entities.");
Check(ViewerTelemetryDeltaBuilder.TryCreate(base10, next11, out var wrongServerDelta)
      && resetSession.TryApplyDelta(DeltaElement(wrongServerDelta), out _, out _)
          == IsleyRelayFrameVerdict.ResyncRequired,
    "A delta for the pre-keyframe server must resync after a keyframe reset.");

// Renegotiation drops the base so the next frame must be a keyframe.
Check(resetSession.TryNegotiate(
          Parse("""{"type":"hello","streamVersion":2,"keyframeIntervalFrames":240,"deltaEncoding":true}"""),
          out _) == IsleyRelayFrameVerdict.Ignored
      && !resetSession.HasBaseState,
    "Renegotiation must drop the base state so deltas wait for a keyframe.");
Check(ViewerTelemetryDeltaBuilder.TryCreate(keyframeOther, keyframeOther with { Sequence = 2 }, out var orphanDelta)
      && resetSession.TryApplyDelta(DeltaElement(orphanDelta), out _, out _)
          == IsleyRelayFrameVerdict.ResyncRequired,
    "A delta before the post-negotiation keyframe must resync.");

// -------------------------------------------------------------- v1 fallback
var legacySession = new IsleyRelayStreamSession();
var legacySnapshot = Snapshot("srv-a", 10, [Entity("A", 1, 2), Entity("B", 3, 4)], Self(94));
Check(legacySession.TryApplySnapshot(
          Wire(new { type = "snapshot", snapshot = legacySnapshot }).GetProperty("snapshot"),
          out var legacyApplied,
          out _) == IsleyRelayFrameVerdict.Applied
      && legacyApplied is not null,
    "A v1 full snapshot without negotiation was not applied.");
CheckSnapshot(legacyApplied!, legacySnapshot, "v1 fallback");
Check(ViewerTelemetryDeltaBuilder.TryCreate(legacySnapshot, next11, out var legacyDelta)
      && legacySession.TryApplyDelta(DeltaElement(legacyDelta), out _, out _)
          == IsleyRelayFrameVerdict.ResyncRequired,
    "A delta on an unnegotiated v1 connection must resync, never mis-apply.");
var v1Clamped = new IsleyRelayStreamSession();
Check(v1Clamped.TryNegotiate(
          Parse("""{"type":"hello","streamVersion":1,"keyframeIntervalFrames":240,"deltaEncoding":false}"""),
          out _) == IsleyRelayFrameVerdict.Ignored
      && v1Clamped.TryApplyDelta(DeltaElement(legacyDelta), out _, out _)
          == IsleyRelayFrameVerdict.ResyncRequired,
    "A delta on a v1-clamped connection must resync.");

// ---------------------------------------------------- malformed delta safety
var guardBase = Snapshot("srv-a", 10, [Entity("A", 1, 2), Entity("B", 3, 4)], Self(94));
var guardNext = Snapshot("srv-a", 11, [Entity("A", 5, 6)], Self(94));
var guardSession = NegotiatedSession(guardBase);

CheckMalformedLeavesStateUntouched(
    DeltaElement(new ViewerTelemetryDelta
    {
        ServerId = "srv-b",
        Sequence = 11,
        BaseSequence = 10,
        SampledAt = guardNext.SampledAt,
        RelayedAt = guardNext.RelayedAt,
        RelayAgeMilliseconds = 50
    }),
    guardBase,
    guardNext,
    "foreign-server delta");
CheckMalformedLeavesStateUntouched(
    DeltaElement(new ViewerTelemetryDelta
    {
        ServerId = "srv-a",
        Sequence = 11,
        BaseSequence = 999,
        SampledAt = guardNext.SampledAt,
        RelayedAt = guardNext.RelayedAt,
        RelayAgeMilliseconds = 50
    }),
    guardBase,
    guardNext,
    "base-sequence gap");
CheckMalformedLeavesStateUntouched(
    DeltaElement(new ViewerTelemetryDelta
    {
        ServerId = "srv-a",
        Sequence = 11,
        BaseSequence = 10,
        SampledAt = guardNext.SampledAt,
        RelayedAt = guardNext.RelayedAt,
        RelayAgeMilliseconds = 50,
        Upserted = Enumerable.Range(0, TelemetryProtocol.MaximumEntities + 1)
            .Select(index => Entity($"flood-{index}", index, index))
            .ToArray()
    }),
    guardBase,
    guardNext,
    "upsert flood");
CheckMalformedLeavesStateUntouched(
    DeltaElement(new ViewerTelemetryDelta
    {
        ServerId = "srv-a",
        Sequence = 11,
        BaseSequence = 10,
        SampledAt = guardNext.SampledAt,
        RelayedAt = guardNext.RelayedAt,
        RelayAgeMilliseconds = 50,
        Removed = Enumerable.Range(0, TelemetryProtocol.MaximumEntities + 1)
            .Select(index => $"gone-{index}")
            .ToArray()
    }),
    guardBase,
    guardNext,
    "removal flood");
CheckMalformedLeavesStateUntouched(
    DeltaElement(new ViewerTelemetryDelta
    {
        ServerId = "srv-a",
        Sequence = 11,
        BaseSequence = 10,
        SampledAt = guardNext.SampledAt,
        RelayedAt = guardNext.RelayedAt,
        RelayAgeMilliseconds = 50,
        Upserted = [Entity("A", 9, 9), Entity("A", 10, 10)]
    }),
    guardBase,
    guardNext,
    "duplicated upsert id");
CheckMalformedLeavesStateUntouched(
    Parse("""{"serverId":"srv-a","sequence":11,"baseSequence":10,"sampledAt":"2027-01-01T00:00:00+00:00","relayedAt":"2027-01-01T00:00:00+00:00","relayAgeMilliseconds":50,"connectedPlayerNodes":2,"visibleEntityCount":1,"upserted":[{"id":"","x":1,"y":2,"z":3}]}"""),
    guardBase,
    guardNext,
    "empty entity id");
CheckMalformedLeavesStateUntouched(
    Parse("""{"serverId":"srv-a","sequence":11,"baseSequence":10,"sampledAt":"2027-01-01T00:00:00+00:00","relayedAt":"2027-01-01T00:00:00+00:00","relayAgeMilliseconds":50,"connectedPlayerNodes":2,"visibleEntityCount":1,"upserted":[{"id":"cond","x":1,"y":2,"z":3,"conditions":["c0","c1","c2","c3","c4","c5","c6","c7","c8","c9","c10","c11","c12","c13","c14","c15","c16"]}]}"""),
    guardBase,
    guardNext,
    "condition flood");
CheckMalformedLeavesStateUntouched(
    Parse("""[1,2,3]"""),
    guardBase,
    guardNext,
    "non-object delta");
CheckMalformedLeavesStateUntouched(
    Parse("""{}"""),
    guardBase,
    guardNext,
    "missing required delta fields");
Check(guardSession.TryApplyDelta(
          Parse("""{"serverId":"srv-a","sequence":12,"baseSequence":11,"sampledAt":"2027-01-01T00:00:10+00:00","relayedAt":"2027-01-01T00:00:10+00:00","relayAgeMilliseconds":50,"connectedPlayerNodes":2,"visibleEntityCount":1,"streamVersion":3}"""),
          out _,
          out _) == IsleyRelayFrameVerdict.UpdateRequired
      && guardSession.UpdateRequired,
    "A delta carrying a newer stream version must surface update-required.");

// Projected roster bound: a full 512-entity keyframe plus one new id overflows.
var fullBase = Snapshot(
    "srv-a",
    10,
    Enumerable.Range(0, TelemetryProtocol.MaximumEntities)
        .Select(index => Entity($"p{index}", index, index))
        .ToArray(),
    null);
var fullSession = NegotiatedSession(fullBase);
Check(fullSession.TryApplyDelta(
          DeltaElement(new ViewerTelemetryDelta
          {
              ServerId = "srv-a",
              Sequence = 11,
              BaseSequence = 10,
              SampledAt = fullBase.SampledAt.AddMilliseconds(100),
              RelayedAt = fullBase.RelayedAt.AddMilliseconds(100),
              RelayAgeMilliseconds = 50,
              VisibleEntityCount = TelemetryProtocol.MaximumEntities + 1,
              Upserted = [Entity("one-more", 1, 1)]
          }),
          out _,
          out _) == IsleyRelayFrameVerdict.ResyncRequired,
    "A delta that overflows the roster bound must resync.");
// Upsert of an existing id at the bound stays legal (replace, not growth).
Check(fullSession.TryApplyDelta(
          DeltaElement(new ViewerTelemetryDelta
          {
              ServerId = "srv-a",
              Sequence = 11,
              BaseSequence = 10,
              SampledAt = fullBase.SampledAt.AddMilliseconds(100),
              RelayedAt = fullBase.RelayedAt.AddMilliseconds(100),
              RelayAgeMilliseconds = 50,
              Upserted = [Entity("p0", 42, 42)]
          }),
          out var boundApplied,
          out _) == IsleyRelayFrameVerdict.Applied
      && boundApplied is { Players.Count: 512 }
      && SameEntity(boundApplied.Players[0], Entity("p0", 42, 42)),
    "A same-size upsert at the roster bound must apply in place.");

// ---------------------------------------------------- locked contract tokens
var root = Directory.GetCurrentDirectory();
string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, ..parts]));

var clientSource = Source("BurntHud", "IsleyRelayStreamLogic.cs");
Check(clientSource.Contains("TryNegotiate", StringComparison.Ordinal)
      && clientSource.Contains("BaseSequence != _base.Sequence", StringComparison.Ordinal)
      && clientSource.Contains("MaximumEntities", StringComparison.Ordinal)
      && clientSource.Contains("SelfRemoved", StringComparison.Ordinal)
      && clientSource.Contains("UpdateRequired", StringComparison.Ordinal),
    "The client stream session lost its negotiation, gap, bound, or self handling.");
var socketSource = Source("BurntHud", "IsleyRelayClient.cs");
Check(socketSource.Contains("maxStreamVersion", StringComparison.Ordinal)
      && socketSource.Contains("IsleyRelayStreamSession", StringComparison.Ordinal)
      && socketSource.Contains("update-required", StringComparison.Ordinal)
      && socketSource.Contains("DeltaEncodingActive", StringComparison.Ordinal)
      && socketSource.Contains("IsUnsupportedStreamVersion", StringComparison.Ordinal)
      && socketSource.Contains("MaximumFrameBytes", StringComparison.Ordinal)
      && socketSource.Contains("AllowAutoRedirect = false", StringComparison.Ordinal)
      && socketSource.Contains("ReadTrustedVerificationUri", StringComparison.Ordinal),
    "The relay client lost its hello negotiation, frame bounds, or security pins.");
var networkSource = Source("BurntHud", "MainWindow.LiveNetwork.cs");
Check(networkSource.Contains("RelayStreamV2Enabled", StringComparison.Ordinal)
      && networkSource.Contains("v2 deltas", StringComparison.Ordinal)
      && networkSource.Contains("update-required", StringComparison.Ordinal),
    "The overlay lost its v2 opt-in wiring or honest status surface.");
Check(Source("BurntHud", "MainWindow.xaml.cs").Contains(
          "public bool RelayStreamV2Enabled { get; set; } = true;", StringComparison.Ordinal),
    "The persisted v2 opt-in must stay additive and default ON.");
var settingsSource = Source("BurntHud", "MainWindow.Settings.cs");
Check(settingsSource.Contains("EnsureRelayStreamV2Loaded", StringComparison.Ordinal)
      && settingsSource.Contains("SaveRelayStreamV2Preference", StringComparison.Ordinal),
    "The v2 opt-in sidecar wiring is missing.");
Check(Source("Isley.Telemetry", "TelemetryContracts.cs").Contains(
          "ViewerStreamVersion = 2", StringComparison.Ordinal)
      && Source("Isley.Telemetry", "TelemetryDelta.cs").Contains(
          "ViewerTelemetryDeltaBuilder", StringComparison.Ordinal),
    "The locked server stream contract drifted.");

// Quick Commands ledger stays at 124 (no new commands for stream v2).
var windowSource = Source("BurntHud", "MainWindow.xaml.cs");
var catalogMatch = Regex.Match(
    windowSource,
    @"CommandPaletteActions\s*=\s*\[([\s\S]*?)\n\s*\];");
Check(catalogMatch.Success
      && Regex.Matches(catalogMatch.Groups[1].Value, @"new\(""").Count == 124,
    "Quick Commands catalog count drifted from 124.");

Console.WriteLine(
    "Relay stream v2 verification passed: hello negotiation shape and clamps, "
    + "wire-fidelity delta apply (upsert/remove/self/null-scalars), always-present "
    + "scalar overwrite, chained and gapped sequences, keyframe reset, renegotiation "
    + "keyframe wait, v1 fallback and clamped-delta resync, malformed-delta safety "
    + "with untouched state, roster bounds, unknown-version update-required state, "
    + "client/settings wiring, locked server contract, and the 124-command ledger.");
