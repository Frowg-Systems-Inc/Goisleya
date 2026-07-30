using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Check(VoicePeerQualityLogic.MaximumTrackedPeers == VoiceIntegrationLogic.MaximumParticipants,
    "quality tracking is bounded by the participant cap");
Check(VoicePeerQualityLogic.MaximumTrackedPeers == 31, "the participant cap stays at 31");

Check(VoicePeerQualityLogic.Severity(new VoicePeerQualitySnapshot(null, null, null)) == 0,
    "an empty sample is healthy");
Check(VoicePeerQualityLogic.Severity(new VoicePeerQualitySnapshot(null, null, 7.9)) == 1,
    "moderate loss warns");
Check(VoicePeerQualityLogic.Severity(new VoicePeerQualitySnapshot(null, null, 8)) == 2,
    "eight percent loss is critical");
Check(VoicePeerQualityLogic.Severity(new VoicePeerQualitySnapshot(500, null, null)) == 2,
    "half-second round trips are critical");
Check(VoicePeerQualityLogic.Severity(new VoicePeerQualitySnapshot(null, 80, null)) == 2,
    "eighty milliseconds of jitter is critical");
Check(VoicePeerQualityLogic.Severity(new VoicePeerQualitySnapshot(250, null, null)) == 1,
    "quarter-second round trips warn");
Check(VoicePeerQualityLogic.Severity(new VoicePeerQualitySnapshot(null, 40, null)) == 1,
    "forty milliseconds of jitter warns");
Check(VoicePeerQualityLogic.Severity(new VoicePeerQualitySnapshot(null, null, 3)) == 1,
    "three percent loss warns");
Check(VoicePeerQualityLogic.Severity(new VoicePeerQualitySnapshot(120, 12, 0.4)) == 0,
    "healthy metrics stay healthy");
Check(VoicePeerQualityLogic.Severity(new VoicePeerQualitySnapshot(600, 90, 12)) == 2,
    "the worst metric wins");

Check(VoicePeerQualityLogic.FormatSuffix(new VoicePeerQualitySnapshot(120, 12, 0.4), false) == string.Empty,
    "an inactive monitor renders no suffix");
Check(VoicePeerQualityLogic.FormatSuffix(null, true) == " · —",
    "a missing sample renders an honest placeholder");
Check(VoicePeerQualityLogic.FormatSuffix(new VoicePeerQualitySnapshot(null, null, null), true) == " · —",
    "an empty sample renders an honest placeholder");
Check(VoicePeerQualityLogic.FormatSuffix(new VoicePeerQualitySnapshot(120.4, null, null), true) == " · 120 MS",
    "round trip only");
Check(VoicePeerQualityLogic.FormatSuffix(new VoicePeerQualitySnapshot(null, 12.4, null), true) == " · J 12 MS",
    "jitter only");
Check(VoicePeerQualityLogic.FormatSuffix(new VoicePeerQualitySnapshot(null, null, 2.5), true) == " · 2.5% LOSS",
    "loss only");
Check(VoicePeerQualityLogic.FormatSuffix(new VoicePeerQualitySnapshot(120, 12, 2.5), true)
        == " · 120 MS · J 12 MS · 2.5% LOSS",
    "all three metrics compose in a stable order");

Check(VoicePeerQualityLogic.Describe(new VoicePeerQualitySnapshot(120, 12, 0.4), false)
        == "Per-peer quality monitor is off",
    "an inactive monitor says so");
Check(VoicePeerQualityLogic.Describe(null, true)
        == "No WebRTC sample yet — appears after peers talk while the monitor is on; audio is unaffected",
    "a missing sample explains when quality appears and never blames audio");
Check(VoicePeerQualityLogic.Describe(new VoicePeerQualitySnapshot(120.4, 12.4, 2.5), true)
        == "WebRTC stats measured on this encrypted peer connection · round trip 120 ms · jitter 12 ms · interval packet loss 2.5%",
    "a live sample attributes real WebRTC stats");

Console.WriteLine(
    "Voice peer quality verification passed (severity thresholds, placeholder honesty, suffix composition, and measured-stats copy).");
