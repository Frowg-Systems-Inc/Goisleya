using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var live = LiveHealthLogic.Present(
    "Gateway", "live", 120.4, 4.96, true, "good", "connected", false);
Check(live.Strip == "MAP · Gateway · NET · 120ms · 5Hz · VOICE · GOOD", "live strip composition");
Check(live.Tone == "ok", "live plus voice reads ok");
Check(live.Announcement.Contains("Live health."), "announcement narrates the strip");
Check(live.ToolTip.Contains("Open Tools"), "normal tooltip points at Tools");

var noHz = LiveHealthLogic.Present("Gateway", "live", 42.0, null, false, "", "connected", false);
Check(noHz.Strip == "MAP · Gateway · NET · 42ms · VOICE · OFF", "missing rate omits Hz segment");
Check(noHz.Tone == "idle", "silent voice drops the tone to idle");

var syncing = LiveHealthLogic.Present("Gateway", "reconnecting", null, null, false, "", "", false);
Check(syncing.Strip.Contains("NET · SYNC"), "reconnecting relay reads as syncing");

foreach (var state in new[] { "waiting", "connecting", "signing-in" })
{
    Check(LiveHealthLogic.Present("Gateway", state, null, null, false, "", "", false)
              .Strip.Contains("NET · SYNC"), $"{state} relay reads as syncing");
}

var error = LiveHealthLogic.Present("Gateway", "error", null, null, false, "", "", false);
Check(error.Strip.Contains("NET · ERR"), "relay error surfaces in the strip");
Check(error.Tone == "warn", "relay error warns");

var offline = LiveHealthLogic.Present("", "offline", null, null, false, "", "", false);
Check(offline.Strip.StartsWith("MAP · —"), "blank map label shows the placeholder");
Check(offline.Strip.Contains("NET · OFF"), "unknown relay state reads as off");

var stale = LiveHealthLogic.Present("Gateway (STALE)", "live", 9000.0, null, true, "", "", false);
Check(stale.Tone == "warn", "stale map label warns even when live");

var natFail = LiveHealthLogic.Present("Gateway", "live", 10.0, 4.0, true, "good", "FAILED", false);
Check(natFail.Strip.Contains("VOICE · NAT FAIL"), "failed voice network is honest");
Check(natFail.Tone == "warn", "NAT failure warns");

var noQuality = LiveHealthLogic.Present("Gateway", "live", 10.0, 4.0, true, " ", "connected", false);
Check(noQuality.Strip.EndsWith("VOICE · ON"), "missing quality label keeps a plain ON");

var streamer = LiveHealthLogic.Present("Gateway", "live", 10.0, 4.0, true, "great", "connected", true);
Check(streamer.Strip.EndsWith("VOICE · ON"), "Streamer Mode redacts the quality label");
Check(!streamer.Strip.Contains("GREAT"), "Streamer Mode never leaks the quality label");
Check(streamer.ToolTip.Contains("identities hidden"), "streamer tooltip explains redaction");
Check(streamer.Announcement.Contains("Identities hidden"), "streamer announcement stays coarse");

Console.WriteLine(
    "Live health verification passed (strip composition, relay state mapping, voice honesty, warn/ok/idle tones, and Streamer Mode redaction).");
