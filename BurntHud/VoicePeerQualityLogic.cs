namespace Isley;

internal readonly record struct VoicePeerQualitySnapshot(
    double? RoundTripMilliseconds,
    double? JitterMilliseconds,
    double? PacketLossPercent);

// Per-peer voice connection quality. Every surfaced number is a real WebRTC
// statistic measured on the encrypted peer connection (RTCPeerConnection.getStats
// inside the built-in voice page); nothing is estimated or fabricated. When no
// sample exists the UI shows an honest placeholder and never blocks audio.
internal static class VoicePeerQualityLogic
{
    internal const int MaximumTrackedPeers = VoiceIntegrationLogic.MaximumParticipants;

    internal static int Severity(VoicePeerQualitySnapshot snapshot)
    {
        var loss = snapshot.PacketLossPercent;
        var roundTrip = snapshot.RoundTripMilliseconds;
        var jitter = snapshot.JitterMilliseconds;
        return loss is >= 8 || roundTrip is >= 500 || jitter is >= 80
            ? 2
            : loss is >= 3 || roundTrip is >= 250 || jitter is >= 40
                ? 1
                : 0;
    }

    internal static string FormatSuffix(VoicePeerQualitySnapshot? snapshot, bool monitorActive)
    {
        if (!monitorActive)
        {
            return string.Empty;
        }

        if (snapshot is not { } quality
            || (!quality.RoundTripMilliseconds.HasValue
                && !quality.JitterMilliseconds.HasValue
                && !quality.PacketLossPercent.HasValue))
        {
            return " · —";
        }

        var metrics = new List<string>(3);
        if (quality.RoundTripMilliseconds.HasValue)
        {
            metrics.Add($"{quality.RoundTripMilliseconds.Value:0} MS");
        }

        if (quality.JitterMilliseconds.HasValue)
        {
            metrics.Add($"J {quality.JitterMilliseconds.Value:0} MS");
        }

        if (quality.PacketLossPercent.HasValue)
        {
            metrics.Add($"{quality.PacketLossPercent.Value:0.0}% LOSS");
        }

        return $" · {string.Join(" · ", metrics)}";
    }

    internal static string Describe(VoicePeerQualitySnapshot? snapshot, bool monitorActive)
    {
        if (!monitorActive)
        {
            return "Per-peer quality monitor is off";
        }

        if (snapshot is not { } quality
            || (!quality.RoundTripMilliseconds.HasValue
                && !quality.JitterMilliseconds.HasValue
                && !quality.PacketLossPercent.HasValue))
        {
            return "No WebRTC sample yet — appears after peers talk while the monitor is on; audio is unaffected";
        }

        var metrics = new List<string>(3);
        if (quality.RoundTripMilliseconds.HasValue)
        {
            metrics.Add($"round trip {quality.RoundTripMilliseconds.Value:0} ms");
        }

        if (quality.JitterMilliseconds.HasValue)
        {
            metrics.Add($"jitter {quality.JitterMilliseconds.Value:0} ms");
        }

        if (quality.PacketLossPercent.HasValue)
        {
            metrics.Add($"interval packet loss {quality.PacketLossPercent.Value:0.0}%");
        }

        return $"WebRTC stats measured on this encrypted peer connection · {string.Join(" · ", metrics)}";
    }
}
