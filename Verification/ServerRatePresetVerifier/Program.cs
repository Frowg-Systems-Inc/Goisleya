using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Check(ServerRatePresetLogic.MaximumCustomPresets == 4, "custom presets stay bounded at four");
Check(ServerRatePresetLogic.MaximumLabelLength == 24, "preset labels stay short");
Check(ServerRatePresetLogic.CustomIdPrefix == "custom-", "custom ids keep their prefix");
Check(ServerRatePresetLogic.BuiltInPresets.Length == 2
      && ServerRatePresetLogic.BuiltInPresets[0] == new ServerRatePreset("official-1x", "OFFICIAL 1X", 0)
      && ServerRatePresetLogic.BuiltInPresets[1] == new ServerRatePreset("boosted-2x", "BOOSTED 2X", 2),
    "the two built-in presets stay stable");

Check(ServerRatePresetLogic.NormalizeMultiplierIndex(-1) == 0, "multiplier index clamps low");
Check(ServerRatePresetLogic.NormalizeMultiplierIndex(99)
        == GrowthPlannerLogic.ServerMultipliers.Length - 1,
    "multiplier index clamps to the known multiplier roster");

Check(ServerRatePresetLogic.SanitizeId(null) == string.Empty, "null ids sanitize to empty");
Check(ServerRatePresetLogic.SanitizeId("  My Preset!  ") == "mypreset",
    "ids keep only safe characters and lowercase");
Check(ServerRatePresetLogic.SanitizeId("--edge_case--") == "edge_case",
    "ids trim boundary dashes and underscores");
Check(ServerRatePresetLogic.SanitizeId(new string('a', 40)).Length == 32, "ids cap at 32 characters");

Check(ServerRatePresetLogic.SanitizeLabel(null, "FALLBACK") == "FALLBACK", "null labels fall back");
Check(ServerRatePresetLogic.SanitizeLabel("   ", "FALLBACK") == "FALLBACK", "blank labels fall back");
Check(ServerRatePresetLogic.SanitizeLabel("my  rate|2x", "FALLBACK") == "MY RATE/2X",
    "labels collapse whitespace, map pipes to slashes, and uppercase");
Check(ServerRatePresetLogic.SanitizeLabel("badname", "FALLBACK") == "BADNAME",
    "labels strip control characters");
Check(ServerRatePresetLogic.SanitizeLabel(new string('x', 30), "FALLBACK").Length
        <= ServerRatePresetLogic.MaximumLabelLength,
    "labels cap at 24 characters");

Check(ServerRatePresetLogic.CustomLabel(0) == "CUSTOM 1X", "custom label for the base rate");
Check(ServerRatePresetLogic.CustomLabel(1) == "CUSTOM 1.5X", "custom label keeps fractional rates");
Check(ServerRatePresetLogic.CustomLabel(4) == "CUSTOM 5X", "custom label for the top rate");

var normalized = ServerRatePresetLogic.NormalizeCustomPresets(new[]
{
    new ServerRatePreset("custom-1", "  First Rate ", 1),
    new ServerRatePreset("official-9", "NOT CUSTOM", 3),
    new ServerRatePreset("custom-1", "DUPLICATE", 4),
    new ServerRatePreset("", "EMPTY", 0),
    new ServerRatePreset("custom-2", "Second", 2),
    new ServerRatePreset("custom-3", "Third", 3),
    new ServerRatePreset("custom-4", "Fourth", 4),
    new ServerRatePreset("custom-5", "Fifth", 0)
});
Check(normalized.Count == 4, "custom presets cap at four");
Check(normalized[0].Id == "custom-1"
      && normalized[0].Label == "FIRST RATE"
      && normalized[0].MultiplierIndex == 1,
    "custom presets are sanitized and deduped");
Check(normalized.All(preset => preset.Id.StartsWith("custom-", StringComparison.Ordinal)),
    "non-custom ids are refused");
Check(ServerRatePresetLogic.NormalizeCustomPresets(null).Count == 0, "null normalizes to empty");

var all = ServerRatePresetLogic.All(normalized);
Check(all.Count == 6
      && all[0].Id == "official-1x"
      && all[1].Id == "boosted-2x"
      && all[2].Id == "custom-1",
    "built-ins always lead the combined roster");
Check(ServerRatePresetLogic.All(null).Count == 2, "the combined roster works without customs");

var builtIns = ServerRatePresetLogic.All(null);
Check(ServerRatePresetLogic.Find(builtIns, "official-1x") is { Label: "OFFICIAL 1X" },
    "built-ins resolve by id");
Check(ServerRatePresetLogic.Find(builtIns, " OFFICIAL-1X! ") is { Id: "official-1x" },
    "lookup ids are sanitized before matching");
Check(ServerRatePresetLogic.Find(builtIns, "custom-x") is null, "unknown ids do not resolve");
Check(ServerRatePresetLogic.Find(builtIns, null) is null, "null ids do not resolve");

Check(ServerRatePresetLogic.Next(builtIns, "official-1x", 0).Id == "boosted-2x",
    "next advances past the current preset");
Check(ServerRatePresetLogic.Next(builtIns, "boosted-2x", 2).Id == "official-1x",
    "next wraps around the roster");
Check(ServerRatePresetLogic.Next(builtIns, null, 2).Id == "boosted-2x",
    "without a selection next matches the active multiplier first");
Check(ServerRatePresetLogic.Next(builtIns, null, 1).Id == "official-1x",
    "without a selection or multiplier match next picks the first preset");
Check(ServerRatePresetLogic.Next(Array.Empty<ServerRatePreset>(), null, 0).Id == "official-1x",
    "an empty roster falls back to the first built-in");

Check(ServerRatePresetLogic.TryCreateCustom(0, Array.Empty<ServerRatePreset>(), out _)
        == ServerRatePresetSaveResult.AlreadyTracked,
    "a built-in rate cannot be duplicated");
Check(ServerRatePresetLogic.TryCreateCustom(1,
            new[] { new ServerRatePreset("custom-1", "CUSTOM 1.5X", 1) }, out _)
        == ServerRatePresetSaveResult.AlreadyTracked,
    "an already-tracked custom rate cannot be duplicated");
var limited = Enumerable.Range(0, 4)
    .Select(index => new ServerRatePreset($"custom-dup-{index}", "DUP", 1))
    .ToArray();
Check(ServerRatePresetLogic.TryCreateCustom(3, limited, out _)
        == ServerRatePresetSaveResult.LimitReached,
    "the four-custom cap blocks new rates");
Check(ServerRatePresetLogic.TryCreateCustom(3, Array.Empty<ServerRatePreset>(), out var created)
        == ServerRatePresetSaveResult.Created
      && created == new ServerRatePreset("custom-3", "CUSTOM 3X", 3),
    "a fresh rate creates a stable custom preset");

Console.WriteLine(
    "Server rate preset verification passed (built-in stability, id and label sanitization, roster composition, cycling, and bounded custom creation).");
