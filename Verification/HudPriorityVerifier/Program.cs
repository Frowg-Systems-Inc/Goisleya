using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var idle = HudPriorityLogic.Resolve(new HudPriorityContext(
    true, 472, 560, false, false, false, false));
Check(idle.IsCompactViewport && !idle.IsSafetyFocusActive,
    "Compact idle layout should remain at normal detail");
Check(!idle.HideAmbientHud && !idle.HideWaitingNavigation
      && !idle.CompactPackHud && !idle.SuppressIdleVoice,
    "Idle layout should not suppress enabled surfaces");

var safetyOffline = HudPriorityLogic.Resolve(new HudPriorityContext(
    true, 472, 560, true, false, false, false));
Check(safetyOffline.IsSafetyFocusActive, "Compact survival state should activate safety focus");
Check(safetyOffline.HideAmbientHud, "Safety focus should fold ambient HUD cards");
Check(safetyOffline.HideWaitingNavigation, "Offline navigation should yield during safety focus");
Check(safetyOffline.CompactPackHud, "Pack detail should compact during safety focus");
Check(safetyOffline.SuppressIdleVoice, "Idle healthy voice HUD should yield during safety focus");

var safetyLive = HudPriorityLogic.Resolve(new HudPriorityContext(
    true, 472, 560, true, true, false, false));
Check(!safetyLive.HideWaitingNavigation,
    "Authorized live navigation must remain visible during safety focus");

var speaking = HudPriorityLogic.Resolve(new HudPriorityContext(
    true, 472, 560, true, true, true, false));
Check(!speaking.SuppressIdleVoice, "Active PTT must remain visible during safety focus");

var voiceFault = HudPriorityLogic.Resolve(new HudPriorityContext(
    true, 472, 560, true, false, false, true));
Check(!voiceFault.SuppressIdleVoice, "Voice setup and observer faults must remain visible");

var expanded = HudPriorityLogic.Resolve(new HudPriorityContext(
    true, 720, 800, true, false, false, false));
Check(!expanded.IsCompactViewport && !expanded.IsSafetyFocusActive,
    "Expanded layout should retain normal HUD detail");

var disabled = HudPriorityLogic.Resolve(new HudPriorityContext(
    false, 472, 560, true, false, false, false));
Check(!disabled.IsSafetyFocusActive && !disabled.HideAmbientHud
      && !disabled.HideWaitingNavigation && !disabled.CompactPackHud
      && !disabled.SuppressIdleVoice,
    "Disabled Smart HUD should never suppress another enabled surface");

var invalidDimensions = HudPriorityLogic.Resolve(new HudPriorityContext(
    true, double.NaN, double.PositiveInfinity, true, false, false, false));
Check(invalidDimensions.IsCompactViewport && invalidDimensions.IsSafetyFocusActive,
    "Invalid dimensions should fall back to the safe compact profile");

Console.WriteLine(
    "Smart HUD priority verification passed (compact safety focus, live navigation, voice activity/faults, expanded layouts, and opt-out).");
