using System.Text.Json;
using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Reject(string? text, string message)
{
    Check(!PortableConfigLogic.TryParse(text, out _, out var error), message);
    Check(error == "INVALID PORTABLE CONFIG", $"rejection keeps generic error copy: {message}");
}

var export = PortableConfigLogic.Export(new
{
    ActiveFocusModeId = "pack",
    HotkeyBindings = new[] { new { ActionId = "visibility" } },
    CommunityServerProfiles = new[] { new { Name = "NA East" }, new { Name = "EU West" } },
    HudDetailModeIndex = 2
});
Check(export.Contains("\"schema\": \"isley-portable-config\""), "export carries schema marker");
Check(export.Contains("\"schemaVersion\": 1"), "export carries schema version");
Check(export.Contains("No Steam tokens, TURN credentials, or relay secrets are included."),
    "export keeps the secret-exclusion note");

Check(PortableConfigLogic.TryParse(export, out var settings, out var error), "export round trips");
Check(error.Length == 0, "accepted export clears the error");
Check(settings.GetProperty("ActiveFocusModeId").GetString() == "pack", "settings payload survives");
Check(settings.GetProperty("HotkeyBindings").GetArrayLength() == 1, "hotkey array survives");

Reject(null, "null rejected");
Reject("   ", "whitespace rejected");
Reject("not json", "malformed JSON rejected");
Reject("[1,2,3]", "non-object root rejected");
Reject("""{"schema":"other","schemaVersion":1,"settings":{}}""", "wrong schema rejected");
Reject("""{"schema":"isley-portable-config","schemaVersion":2,"settings":{}}""",
    "wrong schema version rejected");
Reject("""{"schema":"isley-portable-config","schemaVersion":1}""", "missing settings rejected");
Reject("""{"schema":"isley-portable-config","schemaVersion":1,"settings":[1]}""",
    "non-object settings rejected");
Reject(new string('x', PortableConfigLogic.MaximumCharacters + 1), "oversized config rejected");
Reject("{\"schema\":\"isley-portable-config\",\"schemaVersion\":1,\"settings\":{}}\u0001",
    "control characters rejected");

var summary = PortableConfigLogic.PreviewSummary(settings);
Check(summary.Contains("focus pack"), "summary names the focus mode");
Check(summary.Contains("1 hotkeys"), "summary counts hotkeys");
Check(summary.Contains("2 community profiles"), "summary counts community profiles");
Check(summary.Contains("HUD prefs"), "summary notes HUD prefs");
Check(summary.StartsWith("Includes "), "populated summary uses the Includes prefix");
Check(PortableConfigLogic.PreviewSummary(JsonDocument.Parse("{}").RootElement)
      == "Portable prefs ready to import", "empty settings use the neutral summary");
Check(PortableConfigLogic.PreviewSummary(JsonDocument.Parse("{\"ActiveFocusModeId\":\"  \"}").RootElement)
      == "Portable prefs ready to import", "blank focus mode is not summarized");

Console.WriteLine(
    "Portable config verification passed (schema envelope, secret-exclusion note, bounded and sanitized parsing, rejections, round trip, and preview summary).");
