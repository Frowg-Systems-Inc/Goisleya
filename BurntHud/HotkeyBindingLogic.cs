namespace Isley;

internal sealed record HotkeyActionDefinition(
    string Id,
    string Label,
    string CompactLabel,
    string Description,
    int MessageId,
    uint DefaultModifiers,
    uint DefaultVirtualKey,
    bool Required);

internal sealed record HotkeyBinding(
    string ActionId,
    uint Modifiers,
    uint VirtualKey,
    bool Enabled);

internal sealed record HotkeyBindingValidation(bool Valid, string Error);

internal sealed class HotkeyBindingSettings
{
    public string ActionId { get; set; } = string.Empty;
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }
    public bool Enabled { get; set; } = true;
}

internal static class HotkeyBindingLogic
{
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint AllowedModifiers = ModAlt | ModControl | ModShift;

    internal const string VisibilityId = "visibility";
    internal const string InteractionId = "interaction";
    internal const string RecenterId = "recenter";
    internal const string TimedDangerId = "timed-danger";
    internal const string QuickTimerId = "quick-timer";
    internal const string CommandPaletteId = "command-palette";
    internal const string DeathMarkerId = "death-marker";
    internal const string TrackBearingId = "sound-bearing";
    internal const string VomitRecoveryId = "vomit-recovery";

    internal static readonly HotkeyActionDefinition[] Definitions =
    [
        new(VisibilityId, "Show / hide Isley", "SHOW / HIDE", "Recover or hide the overlay", 0xB001,
            ModControl | ModShift, 0x4F, true),
        new(InteractionId, "Interaction mode", "INTERACT", "Toggle click-through", 0xB002,
            ModControl | ModShift, 0x49, true),
        new(RecenterId, "Recenter player", "RECENTER", "Resume live-player follow", 0xB003,
            ModControl | ModShift, 0x52, false),
        new(TimedDangerId, "Danger marker", "DANGER", "Save a 15-minute sighting", 0xB004,
            ModControl | ModShift, 0x44, false),
        new(QuickTimerId, "Quick timer", "5M TIMER", "Start a five-minute timer", 0xB005,
            ModControl | ModShift, 0x54, false),
        new(CommandPaletteId, "Quick Commands", "FIND", "Open searchable actions", 0xB006,
            ModControl | ModShift, 0x50, false),
        new(DeathMarkerId, "Death marker", "BODY", "Save or replace the latest body", 0xB007,
            ModControl | ModShift, 0x42, false),
        new(TrackBearingId, "Track bearing", "TRACK", "Capture a sound or scent bearing", 0xB008,
            ModControl | ModShift, 0x48, false),
        new(VomitRecoveryId, "Vomit recovery", "SICKNESS", "Start Vomit recovery or open active instructions", 0xB009,
            ModControl | ModShift, 0x53, false)
    ];

    internal static HotkeyActionDefinition? Find(string? actionId) =>
        Definitions.FirstOrDefault(definition =>
            string.Equals(definition.Id, actionId, StringComparison.Ordinal));

    internal static HotkeyActionDefinition? FindByMessageId(int messageId) =>
        Definitions.FirstOrDefault(definition => definition.MessageId == messageId);

    internal static IReadOnlyList<HotkeyBinding> DefaultBindings() =>
        Definitions.Select(DefaultBinding).ToArray();

    internal static HotkeyBinding DefaultBinding(HotkeyActionDefinition definition) =>
        new(definition.Id, definition.DefaultModifiers, definition.DefaultVirtualKey, true);

    internal static IReadOnlyList<HotkeyBinding> Normalize(
        IEnumerable<HotkeyBindingSettings>? savedSettings)
    {
        var saved = (savedSettings ?? [])
            .Where(setting => Find(setting.ActionId) is not null)
            .GroupBy(setting => setting.ActionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<HotkeyBinding>(Definitions.Length);

        foreach (var definition in Definitions)
        {
            var candidate = DefaultBinding(definition);
            if (saved.TryGetValue(definition.Id, out var setting))
            {
                var restored = new HotkeyBinding(
                    definition.Id,
                    setting.Modifiers,
                    setting.VirtualKey,
                    setting.Enabled);
                if (ValidateBasic(restored).Valid
                    && (!definition.Required || restored.Enabled))
                {
                    candidate = restored;
                }
            }

            if (!candidate.Enabled)
            {
                result.Add(candidate);
                continue;
            }

            if (!used.Add(Signature(candidate)))
            {
                candidate = FindAvailableFallback(definition, used);
                used.Add(Signature(candidate));
            }
            result.Add(candidate);
        }

        return result;
    }

    internal static HotkeyBindingValidation ValidateCandidate(
        HotkeyBinding candidate,
        IEnumerable<HotkeyBinding> existing)
    {
        var definition = Find(candidate.ActionId);
        if (definition is null)
        {
            return new HotkeyBindingValidation(false, "UNKNOWN ACTION");
        }
        if (definition.Required && !candidate.Enabled)
        {
            return new HotkeyBindingValidation(false, "RECOVERY SHORTCUT REQUIRED");
        }

        var basic = ValidateBasic(candidate);
        if (!basic.Valid || !candidate.Enabled)
        {
            return basic;
        }

        var duplicate = existing.FirstOrDefault(binding =>
            binding.Enabled
            && !string.Equals(binding.ActionId, candidate.ActionId, StringComparison.Ordinal)
            && string.Equals(Signature(binding), Signature(candidate), StringComparison.Ordinal));
        return duplicate is null
            ? new HotkeyBindingValidation(true, string.Empty)
            : new HotkeyBindingValidation(
                false,
                $"ALREADY USED BY {Find(duplicate.ActionId)?.CompactLabel ?? "ISLEY"}");
    }

    internal static HotkeyBindingValidation ValidateBasic(HotkeyBinding binding)
    {
        if (!binding.Enabled)
        {
            return new HotkeyBindingValidation(true, string.Empty);
        }
        if ((binding.Modifiers & ~AllowedModifiers) != 0
            || (binding.Modifiers & (ModControl | ModAlt)) == 0)
        {
            return new HotkeyBindingValidation(false, "ADD CTRL OR ALT · WINDOWS KEY IS NOT ALLOWED");
        }
        if (!IsAllowedVirtualKey(binding.VirtualKey))
        {
            return new HotkeyBindingValidation(false, "USE A LETTER, NUMBER, OR F1-F12");
        }
        return new HotkeyBindingValidation(true, string.Empty);
    }

    internal static bool IsAllowedVirtualKey(uint virtualKey) =>
        virtualKey is >= 0x30 and <= 0x39
        || virtualKey is >= 0x41 and <= 0x5A
        || virtualKey is >= 0x70 and <= 0x7B;

    internal static string Format(HotkeyBinding binding)
    {
        if (!binding.Enabled)
        {
            return "OFF";
        }

        var parts = new List<string>(4);
        if ((binding.Modifiers & ModControl) != 0) parts.Add("CTRL");
        if ((binding.Modifiers & ModAlt) != 0) parts.Add("ALT");
        if ((binding.Modifiers & ModShift) != 0) parts.Add("SHIFT");
        parts.Add(FormatVirtualKey(binding.VirtualKey));
        return string.Join('+', parts);
    }

    internal static string Signature(HotkeyBinding binding) =>
        $"{binding.Modifiers & AllowedModifiers:X}:{binding.VirtualKey:X}";

    internal static List<HotkeyBindingSettings> ToSettings(IEnumerable<HotkeyBinding> bindings)
    {
        var byId = bindings.ToDictionary(binding => binding.ActionId, StringComparer.Ordinal);
        return Definitions
            .Where(definition => byId.ContainsKey(definition.Id))
            .Select(definition => byId[definition.Id])
            .Select(binding => new HotkeyBindingSettings
            {
                ActionId = binding.ActionId,
                Modifiers = binding.Modifiers,
                VirtualKey = binding.VirtualKey,
                Enabled = binding.Enabled
            }).ToList();
    }

    private static string FormatVirtualKey(uint virtualKey) => virtualKey switch
    {
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x7B => $"F{virtualKey - 0x6F}",
        _ => "?"
    };

    private static HotkeyBinding FindAvailableFallback(
        HotkeyActionDefinition definition,
        ISet<string> used)
    {
        var modifierCandidates = new[]
        {
            definition.DefaultModifiers,
            ModControl | ModAlt,
            ModAlt | ModShift,
            ModControl | ModAlt | ModShift
        };
        foreach (var modifiers in modifierCandidates)
        {
            var candidate = new HotkeyBinding(definition.Id, modifiers, definition.DefaultVirtualKey, true);
            if (!used.Contains(Signature(candidate)))
            {
                return candidate;
            }
        }

        for (uint virtualKey = 0x70; virtualKey <= 0x7B; virtualKey++)
        {
            var candidate = new HotkeyBinding(
                definition.Id,
                ModControl | ModAlt,
                virtualKey,
                true);
            if (!used.Contains(Signature(candidate)))
            {
                return candidate;
            }
        }

        return DefaultBinding(definition);
    }
}
