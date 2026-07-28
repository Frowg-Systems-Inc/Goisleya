namespace Isley;

internal readonly record struct NestPhaseEntry(
    string Id,
    string Label,
    string Action,
    string TimerLabel);

internal readonly record struct NestPlannerSnapshot(
    bool Active,
    int PhaseIndex,
    bool PartnerReady,
    bool SiteReady,
    bool DebrisReady,
    bool ReservesReady,
    int AccessIndex,
    int EggTarget,
    int EggsLaid,
    int EggsHatched,
    int YoungRaised,
    int TimerDurationIndex);

internal enum NestAutoHatchState
{
    Off,
    Armed,
    Pending,
    Synchronized
}

internal readonly record struct NestAutoHatchGuidance(
    NestAutoHatchState State,
    string Heading,
    string Detail,
    bool RequiresAttention);

internal static class NestPlannerLogic
{
    internal const string Snapshot = "2026-07-22";
    internal const int MaxEggs = 8;

    internal static readonly NestPhaseEntry[] Phases =
    [
        new("prepare", "PREPARE", "Reach breeding age, choose cover, and top off food and water.", string.Empty),
        new("court", "COURT", "Verify a compatible same-species pair or an eligible solo-nesting mutation in game.", string.Empty),
        new("build", "BUILD", "Place the nest on land, gather twigs, and stock enough debris for warmth.", string.Empty),
        new("gestate", "GESTATE", "Choose the clutch in the Nest menu and protect food and nutrient reserves.", "Egg gestation"),
        new("lay", "LAY EGGS", "Sit on the built nest and follow the in-game prompt to lay the clutch.", string.Empty),
        new("incubate", "INCUBATE", "Keep a parent on the nest and maintain debris so incubation keeps moving.", "Nest incubation"),
        new("hatch", "HATCH", "Choose Public or Private access, handle hatch requests, and guard eggs that may auto-hatch after appearing.", string.Empty),
        new("raise", "RAISE YOUNG", "Feed and protect the hatchlings; mark a young raised only at the server's required growth.", string.Empty),
        new("complete", "CYCLE LOGGED", "The current nest cycle is logged. Guard survivors or reset for another clutch.", string.Empty)
    ];

    internal static readonly int[] TimerMinutes = [5, 10, 15, 20, 30];

    internal static NestPlannerSnapshot Normalize(NestPlannerSnapshot snapshot)
    {
        var active = snapshot.Active;
        var phase = active ? Math.Clamp(snapshot.PhaseIndex, 0, Phases.Length - 1) : 0;
        var target = Math.Clamp(snapshot.EggTarget, 1, MaxEggs);
        var laid = active ? Math.Clamp(snapshot.EggsLaid, 0, target) : 0;
        var hatched = active ? Math.Clamp(snapshot.EggsHatched, 0, laid) : 0;
        var raised = active ? Math.Clamp(snapshot.YoungRaised, 0, hatched) : 0;
        return snapshot with
        {
            Active = active,
            PhaseIndex = phase,
            PartnerReady = active && snapshot.PartnerReady,
            SiteReady = active && snapshot.SiteReady,
            DebrisReady = active && snapshot.DebrisReady,
            ReservesReady = active && snapshot.ReservesReady,
            AccessIndex = Math.Clamp(snapshot.AccessIndex, 0, 1),
            EggTarget = target,
            EggsLaid = laid,
            EggsHatched = hatched,
            YoungRaised = raised,
            TimerDurationIndex = Math.Clamp(snapshot.TimerDurationIndex, 0, TimerMinutes.Length - 1)
        };
    }

    internal static NestPhaseEntry Phase(NestPlannerSnapshot snapshot) =>
        Phases[Normalize(snapshot).PhaseIndex];

    internal static int ReadinessCount(NestPlannerSnapshot snapshot)
    {
        var normalized = Normalize(snapshot);
        return new[]
        {
            normalized.PartnerReady,
            normalized.SiteReady,
            normalized.DebrisReady,
            normalized.ReservesReady
        }.Count(value => value);
    }

    internal static string NextAction(
        NestPlannerSnapshot snapshot,
        bool autoHatchGuidanceEnabled = false)
    {
        var normalized = Normalize(snapshot);
        if (!normalized.Active) return "Start a nest plan when this life is ready to breed.";
        if (!normalized.PartnerReady) return "Confirm a compatible partner or verified solo-nesting mutation.";
        if (!normalized.SiteReady) return "Choose and secure a defensible nest site.";
        if (!normalized.ReservesReady) return "Top off food, water, and nutrients before gestation.";
        if (!normalized.DebrisReady) return "Gather and deposit debris to improve nest warmth.";
        var autoHatch = EvaluateAutoHatch(normalized, autoHatchGuidanceEnabled);
        if (autoHatch.RequiresAttention)
        {
            return autoHatch.Detail;
        }
        return Phase(normalized).Action;
    }

    internal static NestAutoHatchGuidance EvaluateAutoHatch(
        NestPlannerSnapshot snapshot,
        bool enabled)
    {
        var normalized = Normalize(snapshot);
        if (!enabled)
        {
            return new(
                NestAutoHatchState.Off,
                "AUTO-HATCH GUIDANCE OFF",
                "Isley will keep the manual clutch ledger but will not surface public-branch auto-hatch guidance.",
                false);
        }

        var pending = Math.Max(0, normalized.EggsLaid - normalized.EggsHatched);
        if (normalized.EggsLaid > 0 && pending == 0)
        {
            return new(
                NestAutoHatchState.Synchronized,
                $"HATCH LOG SYNCED · {normalized.EggsHatched}/{normalized.EggsLaid}",
                "Every laid egg is logged as hatched. Continue to verify the live clutch in game.",
                false);
        }

        if (normalized.Active && normalized.PhaseIndex >= 5 && pending > 0)
        {
            var eggLabel = pending == 1 ? "egg" : "eggs";
            return new(
                NestAutoHatchState.Pending,
                $"AUTO-HATCH CHECK · {pending} UNSYNCED",
                $"Keep the nest warm and guarded. {pending} laid {eggLabel} can auto-hatch after appearing if not manually hatched; confirm in game and sync the HATCHED count.",
                true);
        }

        return new(
            NestAutoHatchState.Armed,
            "AUTO-HATCH GUIDANCE ARMED",
            "When incubation reaches laid eggs, Isley will remind you that public-branch eggs can auto-hatch after appearing. No duration is guessed.",
            false);
    }

    internal static string AccessLabel(int accessIndex) =>
        Math.Clamp(accessIndex, 0, 1) == 1 ? "PUBLIC" : "PRIVATE";

    internal static int TimerDurationMinutes(NestPlannerSnapshot snapshot) =>
        TimerMinutes[Normalize(snapshot).TimerDurationIndex];

    internal static string TimerLabel(NestPlannerSnapshot snapshot) => Phase(snapshot).TimerLabel;

    internal static string CompactSummary(NestPlannerSnapshot snapshot)
    {
        var normalized = Normalize(snapshot);
        if (!normalized.Active) return string.Empty;
        var phase = Phase(normalized).Label;
        return $"NEST {phase} {normalized.EggsHatched}/{normalized.EggsLaid} HATCHED " +
               $"{normalized.YoungRaised} RAISED";
    }
}
