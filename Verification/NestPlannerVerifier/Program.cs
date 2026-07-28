using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var empty = NestPlannerLogic.Normalize(new NestPlannerSnapshot(
    false, 99, true, true, true, true, 9, 99, 99, 99, 99, 99));
Check(!empty.Active && empty.PhaseIndex == 0, "inactive normalization");
Check(!empty.PartnerReady && empty.EggsLaid == 0 && empty.EggsHatched == 0 && empty.YoungRaised == 0,
    "inactive state clearing");
Check(empty.EggTarget == NestPlannerLogic.MaxEggs, "egg target bound");

var active = NestPlannerLogic.Normalize(new NestPlannerSnapshot(
    true, 5, true, true, true, true, 1, 4, 8, 7, 6, 1));
Check(active.EggsLaid == 4 && active.EggsHatched == 4 && active.YoungRaised == 4,
    "dependent clutch bounds");
Check(NestPlannerLogic.Phases.Length == 9, "phase catalog");
Check(NestPlannerLogic.Phase(active).Id == "incubate", "phase lookup");
Check(NestPlannerLogic.ReadinessCount(active) == 4, "readiness count");
Check(NestPlannerLogic.TimerLabel(active) == "Nest incubation", "incubation timer label");
Check(NestPlannerLogic.TimerDurationMinutes(active) == 10, "timer duration");
Check(NestPlannerLogic.AccessLabel(active.AccessIndex) == "PUBLIC", "access label");
Check(NestPlannerLogic.CompactSummary(active) == "NEST INCUBATE 4/4 HATCHED 4 RAISED",
    "compact summary");

var missing = active with { PartnerReady = false, SiteReady = false, DebrisReady = false, ReservesReady = false };
Check(NestPlannerLogic.NextAction(missing).Contains("partner", StringComparison.OrdinalIgnoreCase),
    "readiness priority");
var gestating = active with { PhaseIndex = 3 };
Check(NestPlannerLogic.TimerLabel(gestating) == "Egg gestation", "gestation timer label");
var autoHatchPending = active with { PhaseIndex = 5, EggsLaid = 4, EggsHatched = 1, YoungRaised = 0 };
var autoHatch = NestPlannerLogic.EvaluateAutoHatch(autoHatchPending, true);
Check(autoHatch is
      {
          State: NestAutoHatchState.Pending,
          Heading: "AUTO-HATCH CHECK · 3 UNSYNCED",
          RequiresAttention: true
      },
    "pending auto-hatch guidance");
Check(autoHatch.Detail.Contains("after appearing", StringComparison.OrdinalIgnoreCase)
      && autoHatch.Detail.Contains("confirm in game", StringComparison.OrdinalIgnoreCase),
    "truthful auto-hatch detail");
Check(NestPlannerLogic.NextAction(autoHatchPending, true) == autoHatch.Detail,
    "auto-hatch Next Move bridge");
Check(NestPlannerLogic.NextAction(autoHatchPending, false)
      == NestPlannerLogic.Phase(autoHatchPending).Action,
    "toggleable auto-hatch guidance");
Check(NestPlannerLogic.EvaluateAutoHatch(autoHatchPending, false) is
      {
          State: NestAutoHatchState.Off,
          RequiresAttention: false
      },
    "disabled auto-hatch state");
Check(NestPlannerLogic.EvaluateAutoHatch(
        autoHatchPending with { EggsHatched = 4 },
        true) is
      {
          State: NestAutoHatchState.Synchronized,
          Heading: "HATCH LOG SYNCED · 4/4",
          RequiresAttention: false
      },
    "synchronized auto-hatch state");
Check(NestPlannerLogic.EvaluateAutoHatch(
        autoHatchPending with { PhaseIndex = 4 },
        true) is
      {
          State: NestAutoHatchState.Armed,
          RequiresAttention: false
      },
    "pre-incubation auto-hatch state");
Check(NestPlannerLogic.Phases.All(phase => !string.IsNullOrWhiteSpace(phase.Label)
                                           && !string.IsNullOrWhiteSpace(phase.Action)),
    "complete phase guidance");

Console.WriteLine("Nest planner verification passed (phases, readiness, clutch bounds, access, timers, summaries, and toggleable public-branch auto-hatch guidance).");
