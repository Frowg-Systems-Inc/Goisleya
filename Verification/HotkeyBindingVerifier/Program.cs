using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Check(HotkeyBindingLogic.Definitions.Length == 9, "nine global actions");
Check(HotkeyBindingLogic.Definitions.Select(item => item.Id).Distinct().Count() == 9, "unique action IDs");
Check(HotkeyBindingLogic.Definitions.Select(item => item.MessageId).Distinct().Count() == 9, "unique message IDs");
Check(HotkeyBindingLogic.Definitions.Count(item => item.Required) == 2, "two recovery shortcuts");

var defaults = HotkeyBindingLogic.DefaultBindings();
Check(defaults.Count == 9 && defaults.All(item => item.Enabled), "enabled defaults");
Check(defaults.Select(HotkeyBindingLogic.Signature).Distinct().Count() == 9, "unique defaults");
Check(HotkeyBindingLogic.Format(defaults.First(item => item.ActionId == HotkeyBindingLogic.VisibilityId))
      == "CTRL+SHIFT+O", "default visibility label");
Check(HotkeyBindingLogic.Format(defaults.First(item => item.ActionId == HotkeyBindingLogic.TrackBearingId))
      == "CTRL+SHIFT+H", "default track-bearing label");
Check(HotkeyBindingLogic.Format(defaults.First(item => item.ActionId == HotkeyBindingLogic.VomitRecoveryId))
      == "CTRL+SHIFT+S", "default Vomit-recovery label");
Check(defaults.First(item => item.ActionId == HotkeyBindingLogic.VomitRecoveryId).VirtualKey != 0x56,
    "Vomit recovery must not share the default voice PTT key");
Check(HotkeyBindingLogic.Format(new("test", HotkeyBindingLogic.ModControl | HotkeyBindingLogic.ModAlt,
          0x70, true)) == "CTRL+ALT+F1", "function-key label");
Check(HotkeyBindingLogic.Format(new("test", 0, 0, false)) == "OFF", "disabled label");

Check(!HotkeyBindingLogic.ValidateBasic(new("test", HotkeyBindingLogic.ModShift, 0x41, true)).Valid,
    "shift-only rejected");
Check(!HotkeyBindingLogic.ValidateBasic(new("test", HotkeyBindingLogic.ModControl, 0x20, true)).Valid,
    "unsupported key rejected");
Check(!HotkeyBindingLogic.ValidateBasic(new("test", 0x0008 | HotkeyBindingLogic.ModControl, 0x41, true)).Valid,
    "Windows modifier rejected");
Check(HotkeyBindingLogic.ValidateBasic(new("test", HotkeyBindingLogic.ModAlt, 0x39, true)).Valid,
    "alt number accepted");

var duplicate = new HotkeyBinding(
    HotkeyBindingLogic.RecenterId,
    defaults[0].Modifiers,
    defaults[0].VirtualKey,
    true);
var duplicateValidation = HotkeyBindingLogic.ValidateCandidate(duplicate, defaults);
Check(!duplicateValidation.Valid && duplicateValidation.Error.Contains("SHOW / HIDE"),
    "duplicate identifies owner");

var requiredDisabled = new HotkeyBinding(HotkeyBindingLogic.VisibilityId, 0, 0, false);
Check(!HotkeyBindingLogic.ValidateCandidate(requiredDisabled, defaults).Valid,
    "required shortcut cannot be disabled");
var optionalDisabled = new HotkeyBinding(HotkeyBindingLogic.VomitRecoveryId, 0, 0, false);
Check(HotkeyBindingLogic.ValidateCandidate(optionalDisabled, defaults).Valid,
    "optional Vomit-recovery shortcut can be disabled");

var normalizedDefaults = HotkeyBindingLogic.Normalize(null);
Check(normalizedDefaults.Select(HotkeyBindingLogic.Format)
          .SequenceEqual(defaults.Select(HotkeyBindingLogic.Format)),
    "missing settings restore defaults");

var restored = HotkeyBindingLogic.Normalize([
    new HotkeyBindingSettings
    {
        ActionId = HotkeyBindingLogic.QuickTimerId,
        Modifiers = 0,
        VirtualKey = 0,
        Enabled = false
    },
    new HotkeyBindingSettings
    {
        ActionId = HotkeyBindingLogic.VisibilityId,
        Modifiers = 0,
        VirtualKey = 0,
        Enabled = false
    }
]);
Check(!restored.First(item => item.ActionId == HotkeyBindingLogic.QuickTimerId).Enabled,
    "optional disabled state restored");
Check(restored.First(item => item.ActionId == HotkeyBindingLogic.VisibilityId).Enabled,
    "required disabled state repaired");

var duplicateSettings = defaults.Select(binding => new HotkeyBindingSettings
{
    ActionId = binding.ActionId,
    Modifiers = defaults[0].Modifiers,
    VirtualKey = defaults[0].VirtualKey,
    Enabled = binding.Enabled
}).ToList();
var normalizedDuplicates = HotkeyBindingLogic.Normalize(duplicateSettings);
Check(normalizedDuplicates.Where(item => item.Enabled)
          .Select(HotkeyBindingLogic.Signature).Distinct().Count()
      == normalizedDuplicates.Count(item => item.Enabled),
    "restored duplicates repaired deterministically");

var roundTripSettings = HotkeyBindingLogic.ToSettings(restored);
var roundTrip = HotkeyBindingLogic.Normalize(roundTripSettings);
Check(roundTrip.Select(HotkeyBindingLogic.Format).SequenceEqual(restored.Select(HotkeyBindingLogic.Format)),
    "settings round trip");

Console.WriteLine(
    "Hotkey binding verification passed (nine actions, emergency Vomit recovery, validation, collisions, recovery bindings, normalization, disable, and round trip)." );
