namespace Isley;

internal readonly record struct LiveHealthPresentation(
    string Strip,
    string ToolTip,
    string Tone,
    string Announcement);

internal static class LiveHealthLogic
{
    public static LiveHealthPresentation Present(
        string mapLabel,
        string relayState,
        double? relayAgeMs,
        double? relayHz,
        bool voiceConnected,
        string voiceQualityLabel,
        string voiceNetworkState,
        bool streamerMode)
    {
        var map = string.IsNullOrWhiteSpace(mapLabel) ? "MAP · —" : $"MAP · {mapLabel.Trim()}";
        var relay = relayState switch
        {
            "live" when relayAgeMs is double age =>
                relayHz is double hz
                    ? $"NET · {age:0}ms · {hz:0.#}Hz"
                    : $"NET · {age:0}ms",
            "waiting" or "connecting" or "signing-in" or "reconnecting" => "NET · SYNC",
            "error" => "NET · ERR",
            _ => "NET · OFF"
        };
        var voice = !voiceConnected
            ? "VOICE · OFF"
            : streamerMode
                ? "VOICE · ON"
                : string.Equals(voiceNetworkState, "FAILED", StringComparison.OrdinalIgnoreCase)
                    ? "VOICE · NAT FAIL"
                    : string.IsNullOrWhiteSpace(voiceQualityLabel)
                        ? "VOICE · ON"
                        : $"VOICE · {voiceQualityLabel.Trim().ToUpperInvariant()}";

        var strip = $"{map} · {relay} · {voice}";
        var tone = relayState is "error"
                   || mapLabel.Contains("STALE", StringComparison.OrdinalIgnoreCase)
                   || voice.Contains("NAT FAIL", StringComparison.Ordinal)
            ? "warn"
            : relayState == "live" && voiceConnected
                ? "ok"
                : "idle";
        var tip = streamerMode
            ? "Live health · identities hidden in Streamer Mode"
            : "Map freshness · Live Network · Voice. Open Tools for details.";
        var announcement = streamerMode
            ? "Live health updated. Identities hidden."
            : $"Live health. {strip.Replace('·', ',')}";
        return new LiveHealthPresentation(strip, tip, tone, announcement);
    }
}
