namespace Isley;

internal enum VoiceBridgeState
{
    Disabled,
    Ready,
    Connecting,
    Connected,
    Error
}

internal readonly record struct VoicePresentation(
    VoiceBridgeState State,
    string Heading,
    string Detail,
    bool Transmitting,
    bool ShowHud);

internal readonly record struct VoiceRangeOption(
    string Label,
    int MaxDistance);

internal readonly record struct VoiceInputDeviceInfo(
    string Id,
    string Label);

internal readonly record struct VoiceOutputDeviceInfo(
    string Id,
    string Label);

internal readonly record struct VoiceParticipantInfo(
    string Id,
    string Name,
    bool Muted,
    int VolumePercent,
    string State,
    bool Talking,
    int? Distance);

internal readonly record struct VoiceMicMeterPresentation(
    string Label,
    int Level,
    int Severity,
    bool Active,
    bool Fresh);

internal readonly record struct VoiceQualityPresentation(
    string Label,
    string Detail,
    int Severity,
    bool Active,
    bool Fresh);

internal static class VoiceIntegrationLogic
{
    internal const int MaximumInputDevices = 16;
    internal const int MaximumOutputDevices = 16;
    internal const int MaximumInputDeviceIdLength = 512;
    internal const int MaximumInputDeviceLabelLength = 80;
    internal const int MaximumParticipants = 31;

    internal static readonly int[] KeyCodes = [0x56, 0x14, 0x12, 0x58, 0x43];

    internal static readonly string[] KeyLabels = ["V", "CAPS", "LEFT ALT", "X", "C"];

    internal static readonly VoiceRangeOption[] RangeOptions =
    [
        new("CLOSE", 55),
        new("NORMAL", 110),
        new("FAR", 180)
    ];

    internal static int NormalizeKeyIndex(int index) =>
        Math.Clamp(index, 0, KeyCodes.Length - 1);

    internal static int KeyCode(int index) => KeyCodes[NormalizeKeyIndex(index)];

    internal static string KeyLabel(int index) => KeyLabels[NormalizeKeyIndex(index)];

    internal static int NormalizeRangeIndex(int index) =>
        Math.Clamp(index, 0, RangeOptions.Length - 1);

    internal static VoiceRangeOption Range(int index) => RangeOptions[NormalizeRangeIndex(index)];

    internal static string SpatialModeLabel(bool proximityEnabled) =>
        proximityEnabled ? "PROXIMITY" : "ROOM RADIO";

    internal static string NormalizeInputDeviceId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length is > 0 and <= MaximumInputDeviceIdLength
               && !normalized.Any(char.IsControl)
            ? normalized
            : string.Empty;
    }

    internal static string NormalizeInputDeviceLabel(string? value, int fallbackIndex)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        normalized = new string(normalized.Where(character => !char.IsControl(character)).ToArray());
        if (string.IsNullOrWhiteSpace(normalized)) normalized = $"Microphone {Math.Max(1, fallbackIndex + 1)}";
        return normalized[..Math.Min(MaximumInputDeviceLabelLength, normalized.Length)];
    }

    internal static string NormalizeOutputDeviceId(string? value) =>
        NormalizeInputDeviceId(value);

    internal static string NormalizeOutputDeviceLabel(string? value, int fallbackIndex)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        normalized = new string(normalized.Where(character => !char.IsControl(character)).ToArray());
        if (string.IsNullOrWhiteSpace(normalized)) normalized = $"Speaker {Math.Max(1, fallbackIndex + 1)}";
        return normalized[..Math.Min(MaximumInputDeviceLabelLength, normalized.Length)];
    }

    internal static string NormalizePeerId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 32 && normalized.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? normalized
            : string.Empty;
    }

    internal static string NormalizeParticipantName(string? value, int fallbackIndex)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        normalized = new string(normalized.Where(character =>
            char.IsLetterOrDigit(character) || character is ' ' or '_' or '.' or '\'' or '-').ToArray());
        if (string.IsNullOrWhiteSpace(normalized)) normalized = $"Player {Math.Max(1, fallbackIndex + 1)}";
        return normalized[..Math.Min(32, normalized.Length)];
    }

    internal static int NormalizeParticipantVolume(int value) => Math.Clamp(value, 0, 100);

    internal static int NextParticipantVolume(int current) => NormalizeParticipantVolume(current) switch
    {
        >= 100 => 75,
        >= 75 => 50,
        >= 50 => 25,
        _ => 100
    };

    internal static string NormalizePeerConnectionState(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized is "NEW" or "CONNECTING" or "CONNECTED" or "DISCONNECTED" or "FAILED" or "CLOSED"
            ? normalized
            : "WAITING";
    }

    internal static int? NormalizeParticipantDistance(double? value)
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || value.Value < 0) return null;
        return (int)Math.Clamp(Math.Round(value.Value / 5d) * 5d, 0, 1_000_000);
    }

    internal static VoiceMicMeterPresentation PresentMicMeter(
        bool enabled,
        bool connected,
        int level,
        bool clipped,
        int ageMilliseconds)
    {
        var normalizedLevel = Math.Clamp(level, 0, 100);
        if (!enabled) return new("METER OFF", 0, 0, false, false);
        if (!connected) return new("CONNECT TO TEST", 0, 0, false, false);
        if (ageMilliseconds is < 0 or > 1_200)
        {
            return new("WAITING FOR SIGNAL", 0, 0, false, false);
        }
        if (clipped || normalizedLevel > 90)
        {
            return new("CLIPPING", normalizedLevel, 2, true, true);
        }
        if (normalizedLevel <= 1) return new("NO SIGNAL", normalizedLevel, 0, true, true);
        if (normalizedLevel <= 20) return new("QUIET", normalizedLevel, 0, true, true);
        if (normalizedLevel <= 72) return new("CLEAR", normalizedLevel, 0, true, true);
        return new("LOUD", normalizedLevel, 1, true, true);
    }

    internal static VoiceQualityPresentation PresentQuality(
        bool enabled,
        bool connected,
        int peerCount,
        int sampleCount,
        double? roundTripMilliseconds,
        double? jitterMilliseconds,
        double? packetLossPercent,
        int ageMilliseconds)
    {
        if (!enabled)
        {
            return new("OFF", "Quality monitor is off", 0, false, false);
        }
        if (!connected)
        {
            return new("WAITING", "Connect built-in voice to monitor peer audio", 0, false, false);
        }

        var normalizedPeerCount = Math.Clamp(peerCount, 0, MaximumParticipants);
        if (normalizedPeerCount == 0)
        {
            return new("SOLO ROOM", "Waiting for another player", 0, true, false);
        }

        var rtt = NormalizeQualityMetric(roundTripMilliseconds, 5_000);
        var jitter = NormalizeQualityMetric(jitterMilliseconds, 1_000);
        var loss = NormalizeQualityMetric(packetLossPercent, 100);
        var fresh = ageMilliseconds is >= 0 and <= 8_000;
        if (!fresh || sampleCount <= 0 || (!rtt.HasValue && !jitter.HasValue && !loss.HasValue))
        {
            return new(
                "CALIBRATING",
                $"Collecting encrypted peer statistics · {normalizedPeerCount} peer{(normalizedPeerCount == 1 ? string.Empty : "s")}",
                0,
                true,
                false);
        }

        var severity = loss is >= 8 || rtt is >= 500 || jitter is >= 80
            ? 2
            : loss is >= 3 || rtt is >= 250 || jitter is >= 40
                ? 1
                : 0;
        var label = severity switch
        {
            2 => "POOR",
            1 => "WEAK",
            _ when loss is >= 1 || rtt is >= 140 || jitter is >= 20 => "GOOD",
            _ => "EXCELLENT"
        };
        var metrics = new List<string>(3);
        if (rtt.HasValue) metrics.Add($"{rtt.Value:0} MS RTT");
        if (jitter.HasValue) metrics.Add($"{jitter.Value:0} MS JITTER");
        if (loss.HasValue) metrics.Add($"{loss.Value:0.0}% LOSS");
        var peers = $"{normalizedPeerCount} PEER{(normalizedPeerCount == 1 ? string.Empty : "S")}";
        return new(
            label,
            $"{peers} · {string.Join(" · ", metrics)}",
            severity,
            true,
            true);
    }

    private static double? NormalizeQualityMetric(double? value, double maximum) =>
        value.HasValue && double.IsFinite(value.Value) && value.Value >= 0
            ? Math.Clamp(value.Value, 0, maximum)
            : null;

    internal static VoiceBridgeState ResolveState(
        bool enabled,
        bool connected,
        bool connecting,
        bool hasError)
    {
        if (!enabled) return VoiceBridgeState.Disabled;
        if (hasError) return VoiceBridgeState.Error;
        if (connected) return VoiceBridgeState.Connected;
        return connecting ? VoiceBridgeState.Connecting : VoiceBridgeState.Ready;
    }

    internal static bool CanTransmit(
        bool enabled,
        bool connected,
        bool allowedForeground,
        bool keyHeld) =>
        enabled && connected && allowedForeground && keyHeld;

    internal static bool HasPttIntent(
        bool enabled,
        bool allowedForeground,
        bool keyHeld) =>
        enabled && allowedForeground && keyHeld;

    internal static bool ResolveObservedKeyState(bool current, bool keyDown, bool keyUp) =>
        keyDown ? true : keyUp ? false : current;

    internal static VoicePresentation Present(
        bool enabled,
        bool connected,
        bool connecting,
        bool hasError,
        bool allowedForeground,
        bool keyHeld,
        bool hudEnabled,
        bool streamerMode,
        int keyIndex,
        int participantCount)
    {
        var state = ResolveState(enabled, connected, connecting, hasError);
        var key = KeyLabel(keyIndex);
        var transmitting = CanTransmit(enabled, connected, allowedForeground, keyHeld);
        var showHud = enabled && hudEnabled && !streamerMode;
        if (transmitting)
        {
            return new VoicePresentation(
                state,
                "PTT LIVE · ISLEY VOICE",
                $"RELEASE {key} TO MUTE · {Math.Max(1, participantCount)} IN ROOM",
                true,
                showHud);
        }

        return state switch
        {
            VoiceBridgeState.Disabled => new(
                state, "VOICE OFF", "ENABLE IN VOICE TOOLS", false, showHud),
            VoiceBridgeState.Error => new(
                state, "VOICE NEEDS ATTENTION", "OPEN VOICE TOOLS", false, showHud),
            VoiceBridgeState.Connecting => new(
                state, "ISLEY VOICE CONNECTING", "MICROPHONE PENDING", false, showHud),
            VoiceBridgeState.Connected => new(
                state,
                "ISLEY VOICE READY",
                $"HOLD {key} · {Math.Max(1, participantCount)} IN ROOM",
                false,
                showHud),
            _ => new(
                state, "ISLEY VOICE READY", "CONNECT A PRIVATE ROOM", false, showHud)
        };
    }
}
