using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var standard = HudDockLogic.Resolve(false, true, 46, 436);
Check(standard.NavigationSide == "left", "standard navigation rail");
Check(standard.IntelSide == "right", "standard tactical rail");
Check(standard.SurvivalSide == "left", "standard survival rail");
Check(standard.VoiceSide == "right", "standard voice rail");
Check(standard.Label == "RIGHT", "standard label");
Check(Math.Abs(standard.IntelBottomInset - 61) < 0.001, "standard voice clearance");
Check(standard.Description.Contains("Navigation left", StringComparison.Ordinal), "standard description");

var mirrored = HudDockLogic.Resolve(true, true, 46, 436);
Check(mirrored.NavigationSide == "right", "mirrored navigation rail");
Check(mirrored.IntelSide == "left", "mirrored tactical rail");
Check(mirrored.SurvivalSide == "right", "mirrored survival rail");
Check(mirrored.VoiceSide == "left", "mirrored voice rail");
Check(mirrored.Label == "LEFT", "mirrored label");
Check(mirrored.Description.Contains("Pack and contact left", StringComparison.Ordinal), "mirrored description");

var hiddenVoice = HudDockLogic.Resolve(false, false, 46, 436);
Check(Math.Abs(hiddenVoice.IntelBottomInset - HudDockLogic.EdgeInset) < 0.001,
    "hidden voice should release tactical clearance");

var invalidVoiceHeight = HudDockLogic.Resolve(false, true, double.NaN, 436);
Check(Math.Abs(invalidVoiceHeight.IntelBottomInset - 61) < 0.001,
    "invalid voice height should use the default");

var clamped = HudDockLogic.Resolve(false, true, 500, 300);
Check(Math.Abs(clamped.IntelBottomInset - 102) < 0.001,
    "voice clearance should remain bounded on a short viewport");

var invalidViewport = HudDockLogic.Resolve(false, true, 46, double.PositiveInfinity);
Check(Math.Abs(invalidViewport.IntelBottomInset - 61) < 0.001,
    "invalid viewport should use the stable default");

Console.WriteLine(
    "HUD dock verification passed (mirroring, voice clearance, viewport clamps, and deterministic rail labels).");
