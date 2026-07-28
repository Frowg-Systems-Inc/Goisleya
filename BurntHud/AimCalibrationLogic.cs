namespace Isley;

internal readonly record struct AimCalibrationProfile(
    string SpeciesId,
    string AttackId,
    int GrowthIndex,
    int CameraIndex,
    int ModeIndex,
    double Size,
    double DepthScale,
    double HorizontalOffset,
    double VerticalOffset,
    int ConfirmedMatches,
    int InsideMisses,
    int OutsideHits,
    long UpdatedAtUnixMs);

internal enum AimCalibrationAdviceState
{
    Untested,
    Retest,
    Hold,
    Narrow,
    Widen,
    Mixed
}

internal readonly record struct AimCalibrationEvidence(
    int Matches,
    int InsideMisses,
    int OutsideHits,
    int EffectiveMatches,
    AimCalibrationAdviceState Advice,
    string Label,
    string Instruction,
    bool HasContradiction);

internal static class AimCalibrationLogic
{
    // 21 current playables x 3 attacks x 5 growth contexts x 3 camera distances.
    internal const int MaxProfiles = 945;
    internal const int DefaultGrowthIndex = 1;
    internal const int DefaultCameraIndex = 1;
    internal const int DefaultModeIndex = 1;
    internal const double DefaultSize = 220;
    internal const double DefaultDepthScale = 1;
    internal const double DefaultHorizontalOffset = 0;
    internal const double DefaultVerticalOffset = 0;
    internal const int MaxConfirmedMatches = 9;
    internal const int MaxEvidenceReports = 9;

    private static readonly string[] AttackIds = ["primary", "secondary", "special"];
    private static readonly string[] AttackLabels = ["PRIMARY", "SECONDARY", "ALT / SPECIAL"];
    // Indices 0 and 1 intentionally retain the legacy Juvenile/Adult meanings so
    // existing locally saved calibrations remain attached to the same context.
    private static readonly string[] GrowthLabels =
        ["JUVENILE", "ADULT", "HATCHLING", "SUBADULT", "ELDER"];
    private static readonly int[] GrowthCycle = [2, 0, 3, 1, 4];
    private static readonly string[] CameraLabels = ["CLOSE CAMERA", "NORMAL CAMERA", "FAR CAMERA"];

    internal static int NormalizeAttackIndex(int value) => Math.Clamp(value, 0, AttackIds.Length - 1);

    internal static int NextAttackIndex(int value) => (NormalizeAttackIndex(value) + 1) % AttackIds.Length;

    internal static string AttackId(int index) => AttackIds[NormalizeAttackIndex(index)];

    internal static string AttackLabel(int index) => AttackLabels[NormalizeAttackIndex(index)];

    internal static int NormalizeGrowthIndex(int value) => Math.Clamp(value, 0, GrowthLabels.Length - 1);

    internal static int NextGrowthIndex(int value)
    {
        var normalized = NormalizeGrowthIndex(value);
        var current = Array.IndexOf(GrowthCycle, normalized);
        return GrowthCycle[(Math.Max(0, current) + 1) % GrowthCycle.Length];
    }

    internal static string GrowthLabel(int index) => GrowthLabels[NormalizeGrowthIndex(index)];

    internal static int GrowthIndexForPercent(int growthPercent) => Math.Clamp(growthPercent, 0, 100) switch
    {
        < 25 => 2,
        < 50 => 0,
        < 75 => 3,
        < 100 => 1,
        _ => 4
    };

    internal static int ResolveGrowthIndex(
        bool liveGrowthAvailable,
        int liveGrowthPercent,
        bool liveGrowthSyncEnabled,
        int manualGrowthIndex) =>
        liveGrowthAvailable && liveGrowthSyncEnabled
            ? GrowthIndexForPercent(liveGrowthPercent)
            : NormalizeGrowthIndex(manualGrowthIndex);

    internal static string GrowthRangeLabel(int index) => NormalizeGrowthIndex(index) switch
    {
        2 => "0-24%",
        0 => "25-49%",
        3 => "50-74%",
        1 => "75-99%",
        _ => "100%"
    };

    internal static int NormalizeCameraIndex(int value) => Math.Clamp(value, 0, CameraLabels.Length - 1);

    internal static int NextCameraIndex(int value) => (NormalizeCameraIndex(value) + 1) % CameraLabels.Length;

    internal static string CameraLabel(int index) => CameraLabels[NormalizeCameraIndex(index)];

    internal static string ConfidenceLabel(int confirmedMatches) =>
        ConfidenceLabel(confirmedMatches, 0, 0);

    internal static string ConfidenceLabel(
        int confirmedMatches,
        int insideMisses,
        int outsideHits)
    {
        var evidence = EvaluateEvidence(confirmedMatches, insideMisses, outsideHits);
        if (evidence.Advice is AimCalibrationAdviceState.Narrow
            or AimCalibrationAdviceState.Widen
            or AimCalibrationAdviceState.Mixed)
        {
            return "CONFLICT FOUND";
        }
        if (evidence.HasContradiction)
        {
            return "RETEST";
        }

        return evidence.EffectiveMatches switch
        {
            0 => "UNTESTED",
            <= 2 => "TENTATIVE",
            <= 4 => "USER TESTED",
            _ => "REPEATEDLY TESTED"
        };
    }

    internal static AimCalibrationEvidence EvaluateEvidence(
        int confirmedMatches,
        int insideMisses,
        int outsideHits)
    {
        var matches = Math.Clamp(confirmedMatches, 0, MaxConfirmedMatches);
        var inside = Math.Clamp(insideMisses, 0, MaxEvidenceReports);
        var outside = Math.Clamp(outsideHits, 0, MaxEvidenceReports);
        var contradictions = inside + outside;
        var effectiveMatches = Math.Max(0, matches - contradictions * 2);
        var hasContradiction = contradictions > 0;

        if (matches + contradictions == 0)
        {
            return new(
                matches,
                inside,
                outside,
                effectiveMatches,
                AimCalibrationAdviceState.Untested,
                "UNTESTED",
                "Repeat one stationary edge test, then report only what you observed.",
                false);
        }
        if (inside > 0 && outside > 0)
        {
            if (inside >= outside + 2)
            {
                return Evidence(
                    matches, inside, outside, effectiveMatches,
                    AimCalibrationAdviceState.Narrow,
                    "NARROW",
                    "Repeated inside misses suggest a smaller guide. Adjust width or depth, then retest.");
            }
            if (outside >= inside + 2)
            {
                return Evidence(
                    matches, inside, outside, effectiveMatches,
                    AimCalibrationAdviceState.Widen,
                    "WIDEN",
                    "Repeated outside hits suggest a larger guide. Adjust width or depth, then retest.");
            }
            return Evidence(
                matches, inside, outside, effectiveMatches,
                AimCalibrationAdviceState.Mixed,
                "MIXED",
                "Both error directions were reported. Hold geometry and repeat under the same camera and target setup.");
        }
        if (inside >= 2)
        {
            return Evidence(
                matches, inside, outside, effectiveMatches,
                AimCalibrationAdviceState.Narrow,
                "NARROW",
                "Repeated inside misses suggest a smaller guide. Adjust width or depth, then retest.");
        }
        if (outside >= 2)
        {
            return Evidence(
                matches, inside, outside, effectiveMatches,
                AimCalibrationAdviceState.Widen,
                "WIDEN",
                "Repeated outside hits suggest a larger guide. Adjust width or depth, then retest.");
        }
        if (hasContradiction || matches < 3)
        {
            return Evidence(
                matches, inside, outside, effectiveMatches,
                AimCalibrationAdviceState.Retest,
                "RETEST",
                hasContradiction
                    ? "One contradiction is not enough to resize. Repeat the same stationary test."
                    : "Collect at least three matching stationary repeats before trusting the edge.");
        }

        return Evidence(
            matches, inside, outside, effectiveMatches,
            AimCalibrationAdviceState.Hold,
            "HOLD",
            "The reported repeats agree. Keep this geometry and recheck after camera, growth, or server changes.");
    }

    private static AimCalibrationEvidence Evidence(
        int matches,
        int inside,
        int outside,
        int effectiveMatches,
        AimCalibrationAdviceState advice,
        string label,
        string instruction) =>
        new(matches, inside, outside, effectiveMatches, advice, label, instruction, inside + outside > 0);

    internal static string ResolveSpeciesId(
        bool liveAvailable,
        string? liveSpeciesId,
        string? selectedSpeciesId,
        Func<string, bool> isKnownSpecies,
        string fallbackSpeciesId = "allosaurus")
    {
        ArgumentNullException.ThrowIfNull(isKnownSpecies);
        var live = liveSpeciesId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (liveAvailable && isKnownSpecies(live)) return live;

        var selected = selectedSpeciesId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (isKnownSpecies(selected)) return selected;

        var fallback = fallbackSpeciesId.Trim().ToLowerInvariant();
        return isKnownSpecies(fallback) ? fallback : string.Empty;
    }

    internal static IReadOnlyList<AimCalibrationProfile> NormalizeProfiles(
        IEnumerable<AimCalibrationProfile>? profiles,
        Func<string, bool> isKnownSpecies)
    {
        ArgumentNullException.ThrowIfNull(isKnownSpecies);
        if (profiles is null) return [];

        return profiles
            .Select(profile => Normalize(profile, isKnownSpecies))
            .Where(profile => profile is not null)
            .Select(profile => profile!.Value)
            .GroupBy(profile => Key(
                profile.SpeciesId,
                profile.AttackId,
                profile.GrowthIndex,
                profile.CameraIndex), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(profile => profile.UpdatedAtUnixMs).First())
            .OrderByDescending(profile => profile.UpdatedAtUnixMs)
            .Take(MaxProfiles)
            .ToArray();
    }

    internal static bool TryFind(
        IEnumerable<AimCalibrationProfile> profiles,
        string speciesId,
        int attackIndex,
        int growthIndex,
        int cameraIndex,
        out AimCalibrationProfile profile)
    {
        var attackId = AttackId(attackIndex);
        var normalizedGrowth = NormalizeGrowthIndex(growthIndex);
        var normalizedCamera = NormalizeCameraIndex(cameraIndex);
        foreach (var candidate in profiles)
        {
            if (string.Equals(candidate.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.AttackId, attackId, StringComparison.OrdinalIgnoreCase)
                && candidate.GrowthIndex == normalizedGrowth
                && candidate.CameraIndex == normalizedCamera)
            {
                profile = candidate;
                return true;
            }
        }

        profile = default;
        return false;
    }

    internal static void Upsert(List<AimCalibrationProfile> profiles, AimCalibrationProfile profile)
    {
        profiles.RemoveAll(candidate =>
            string.Equals(candidate.SpeciesId, profile.SpeciesId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.AttackId, profile.AttackId, StringComparison.OrdinalIgnoreCase)
            && candidate.GrowthIndex == NormalizeGrowthIndex(profile.GrowthIndex)
            && candidate.CameraIndex == NormalizeCameraIndex(profile.CameraIndex));
        profiles.Insert(0, profile with
        {
            AttackId = AttackId(Array.FindIndex(
                AttackIds,
                attack => string.Equals(attack, profile.AttackId, StringComparison.OrdinalIgnoreCase))),
            GrowthIndex = NormalizeGrowthIndex(profile.GrowthIndex),
            CameraIndex = NormalizeCameraIndex(profile.CameraIndex),
            ModeIndex = Math.Clamp(profile.ModeIndex, 0, 2),
            Size = Math.Clamp(profile.Size, 90, 520),
            DepthScale = Math.Clamp(profile.DepthScale, 0.55, 1.40),
            HorizontalOffset = Math.Clamp(profile.HorizontalOffset, -240, 240),
            VerticalOffset = Math.Clamp(profile.VerticalOffset, -240, 240),
            ConfirmedMatches = Math.Clamp(profile.ConfirmedMatches, 0, MaxConfirmedMatches),
            InsideMisses = Math.Clamp(profile.InsideMisses, 0, MaxEvidenceReports),
            OutsideHits = Math.Clamp(profile.OutsideHits, 0, MaxEvidenceReports),
            UpdatedAtUnixMs = Math.Max(0, profile.UpdatedAtUnixMs)
        });
        if (profiles.Count > MaxProfiles)
        {
            profiles.RemoveRange(MaxProfiles, profiles.Count - MaxProfiles);
        }
    }

    internal static bool Remove(
        List<AimCalibrationProfile> profiles,
        string speciesId,
        int attackIndex,
        int growthIndex,
        int cameraIndex)
    {
        var attackId = AttackId(attackIndex);
        return profiles.RemoveAll(candidate =>
            string.Equals(candidate.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.AttackId, attackId, StringComparison.OrdinalIgnoreCase)
            && candidate.GrowthIndex == NormalizeGrowthIndex(growthIndex)
            && candidate.CameraIndex == NormalizeCameraIndex(cameraIndex)) > 0;
    }

    internal static bool Matches(
        AimCalibrationProfile profile,
        int modeIndex,
        double size,
        double depthScale,
        double horizontalOffset,
        double verticalOffset) =>
        profile.ModeIndex == Math.Clamp(modeIndex, 0, 2)
        && Math.Abs(profile.Size - Math.Clamp(size, 90, 520)) < 0.01
        && Math.Abs(profile.DepthScale - Math.Clamp(depthScale, 0.55, 1.40)) < 0.001
        && Math.Abs(profile.HorizontalOffset - Math.Clamp(horizontalOffset, -240, 240)) < 0.01
        && Math.Abs(profile.VerticalOffset - Math.Clamp(verticalOffset, -240, 240)) < 0.01;

    private static AimCalibrationProfile? Normalize(
        AimCalibrationProfile profile,
        Func<string, bool> isKnownSpecies)
    {
        var speciesId = profile.SpeciesId?.Trim().ToLowerInvariant() ?? string.Empty;
        var attackIndex = Array.FindIndex(
            AttackIds,
            attack => string.Equals(attack, profile.AttackId, StringComparison.OrdinalIgnoreCase));
        if (speciesId.Length is < 2 or > 40 || attackIndex < 0 || !isKnownSpecies(speciesId)) return null;

        return new AimCalibrationProfile(
            speciesId,
            AttackId(attackIndex),
            NormalizeGrowthIndex(profile.GrowthIndex),
            NormalizeCameraIndex(profile.CameraIndex),
            Math.Clamp(profile.ModeIndex, 0, 2),
            Math.Clamp(profile.Size, 90, 520),
            Math.Clamp(profile.DepthScale, 0.55, 1.40),
            Math.Clamp(profile.HorizontalOffset, -240, 240),
            Math.Clamp(profile.VerticalOffset, -240, 240),
            Math.Clamp(profile.ConfirmedMatches, 0, MaxConfirmedMatches),
            Math.Clamp(profile.InsideMisses, 0, MaxEvidenceReports),
            Math.Clamp(profile.OutsideHits, 0, MaxEvidenceReports),
            Math.Max(0, profile.UpdatedAtUnixMs));
    }

    private static string Key(string speciesId, string attackId, int growthIndex, int cameraIndex) =>
        $"{speciesId.Trim().ToLowerInvariant()}|{attackId.Trim().ToLowerInvariant()}|" +
        $"{NormalizeGrowthIndex(growthIndex)}|{NormalizeCameraIndex(cameraIndex)}";
}
