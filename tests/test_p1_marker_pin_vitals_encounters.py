"""P1 regression contracts for marker interpolation, pin sharing, vitals, and encounters."""

from pathlib import Path
import re
import subprocess


ROOT = Path("/app")
BURNTHUD = ROOT / "BurntHud"
MAIN = (BURNTHUD / "MainWindow.xaml.cs").read_text(encoding="utf-8")
COMMANDS = (BURNTHUD / "MainWindow.Commands.cs").read_text(encoding="utf-8")
MAP_TOOLS = (BURNTHUD / "MainWindow.MapTools.cs").read_text(encoding="utf-8")
ENCOUNTERS = (BURNTHUD / "MainWindow.FriendsEncounters.cs").read_text(encoding="utf-8")
VITALS = (BURNTHUD / "VitalsTrendLogic.cs").read_text(encoding="utf-8")
MAP_HTML = (BURNTHUD / "Map/index.html").read_text(encoding="utf-8")
CONTROLLER = (BURNTHUD / "Map/isley-map-controller.js").read_text(encoding="utf-8")


def method_body(source: str, signature: str) -> str:
    """Extract a C# or JavaScript method body using balanced braces."""
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 0
    for index in range(brace, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:index]
    raise AssertionError(f"Unclosed method: {signature}")


def run_node_harness(tmp_path: Path, body: str) -> subprocess.CompletedProcess[str]:
    """Run an on-disk Node harness and return its completed process."""
    harness = tmp_path / "pin-share-harness.cjs"
    harness.write_text(body, encoding="utf-8")
    return subprocess.run(
        ["node", str(harness)],
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=False,
    )


def pin_harness_prelude() -> str:
    """Build a harness that evaluates the shipped share-code functions unchanged."""
    return r"""
const assert = require('node:assert/strict');
const fs = require('node:fs');
const source = fs.readFileSync('/app/BurntHud/Map/isley-map-controller.js', 'utf8');
const start = source.indexOf("const pinShareCodePrefix = 'ISLEYPINS1.';");
const end = source.indexOf('const partitionPinsByExpiry', start);
assert.ok(start >= 0 && end > start);
const implementation = source.slice(start, end);
const pinTypes = {
  safe: { label: 'Safe' }, nest: { label: 'Nest' }, food: { label: 'Food' },
  danger: { label: 'Danger' }, water: { label: 'Water' },
  rally: { label: 'Rally' }, death: { label: 'Death' }
};
let savedPins = [];
let persistCalls = 0;
let drawCalls = 0;
const persistSavedPins = () => { persistCalls += 1; return true; };
const drawSavedPins = () => { drawCalls += 1; };
let api;
eval(`${implementation}\napi = { exportPinShareCode, importPinShareCode };`);
const encode = payload => 'ISLEYPINS1.'
  + btoa(unescape(encodeURIComponent(JSON.stringify(payload))));
const decode = code => JSON.parse(decodeURIComponent(escape(
  atob(code.slice('ISLEYPINS1.'.length)))));
"""


def test_catalog_dispatch_and_methods_are_complete():
    command_start = MAIN.index("CommandPaletteActions =")
    command_end = MAIN.index("];", command_start)
    ids = re.findall(r'new\("([^"]+)"', MAIN[command_start:command_end])
    assert len(ids) == 121
    assert len(ids) == len(set(ids))

    contracts = {
        "map-pins-share": ("await CopyPinShareCodeAsync();", MAP_TOOLS, "CopyPinShareCodeAsync"),
        "map-pins-import": (
            "await ImportPinShareCodeFromClipboardAsync();",
            MAP_TOOLS,
            "ImportPinShareCodeFromClipboardAsync",
        ),
        "encounter-history": (
            "await CopyEncounterHistoryAsync();",
            ENCOUNTERS,
            "CopyEncounterHistoryAsync",
        ),
    }
    dispatch = method_body(
        COMMANDS,
        "private async Task ExecuteCommandPaletteActionAsync(string actionId)",
    )
    for command_id, (call, source, method_name) in contracts.items():
        assert ids.count(command_id) == 1
        assert dispatch.count(f'case "{command_id}":') == 1
        assert call in dispatch
        assert f"Task {method_name}(" in source


def test_marker_interpolation_reuses_nodes_and_reduced_motion_parses():
    scripts = re.findall(r"<script>([\s\S]*?)</script>", MAP_HTML)
    assert len(scripts) == 1
    parsed = subprocess.run(
        ["node", "--check", "-"],
        input=scripts[0],
        text=True,
        capture_output=True,
        check=False,
    )
    assert parsed.returncode == 0, parsed.stderr

    render = method_body(scripts[0], "const render = () =>")
    assert "playerLayer.replaceChildren" not in render
    assert "heatmapLayer.replaceChildren" not in render
    for contract in (
        "const playerNodes = new Map();",
        "playerNodes.get(key) || createPlayerNode(key, player)",
        "node.group.style.transform = `translate(",
        "node.heat.style.transform = `translate(",
        "for (const [key, node] of playerNodes)",
        "node.group.remove();",
        "node.heat.remove();",
        "playerNodes.delete(key);",
        "transition: transform 900ms linear",
    ):
        assert contract in MAP_HTML
    reduced_motion = MAP_HTML[MAP_HTML.index("@media (prefers-reduced-motion: reduce)"):]
    assert "#providerPlayers g[data-isley-player]" in reduced_motion
    assert "#layer-heatmap circle[data-isley-live-heat]" in reduced_motion
    assert "transition: none;" in reduced_motion


def test_pin_share_code_extracted_runtime_roundtrip_and_limits(tmp_path):
    result = run_node_harness(
        tmp_path,
        pin_harness_prelude()
        + r"""
savedPins = [{ type: 'food', x: 512.3, y: 400, label: 'Lake', id: 'one' }];
const exported = api.exportPinShareCode();
assert.deepEqual(decode(exported), [{ t: 'food', x: 512.3, y: 400, l: 'Lake' }]);
savedPins = [];
assert.equal(api.importPinShareCode(encode([
  { t: 'food', x: 512.3, y: 400, l: 'Lake' }
])), 1);
assert.deepEqual(
  { t: savedPins[0].type, x: savedPins[0].x, y: savedPins[0].y, l: savedPins[0].label },
  { t: 'food', x: 512.3, y: 400, l: 'Lake' });
assert.equal(persistCalls, 1);
assert.equal(drawCalls, 1);
assert.equal(api.importPinShareCode(exported), 0);
assert.equal(persistCalls, 1);
assert.equal(drawCalls, 1);
for (const malformed of [
  '', 'WRONG.' + exported, 'ISLEYPINS1.not-base64',
  encode({ t: 'food', x: 1, y: 2 }),
  'ISLEYPINS1.' + 'A'.repeat(8192)
]) assert.equal(api.importPinShareCode(malformed), -1);
savedPins = [];
assert.equal(api.importPinShareCode(encode([
  { t: 'water', x: -50, y: 1200, l: 'Clamped' },
  { t: 'invalid', x: 10, y: 20, l: 'Rejected' },
  { t: 'food', x: 'NaN', y: 20, l: 'Rejected' }
])), 1);
assert.equal(savedPins[0].x, 0);
assert.equal(savedPins[0].y, 1000);
savedPins = Array.from({ length: 20 }, (_, i) => ({
  id: String(i), type: 'safe', x: i * 2, y: i * 2, label: `P${i}`
}));
assert.equal(api.importPinShareCode(encode([
  { t: 'food', x: 900, y: 900, l: 'Newest' }
])), 1);
assert.equal(savedPins.length, 20);
assert.equal(savedPins[0].id, '1');
assert.equal(savedPins.at(-1).label, 'Newest');
""",
    )
    assert result.returncode == 0, result.stderr


def test_pin_type_whitelist_rejects_inherited_object_keys(tmp_path):
    result = run_node_harness(
        tmp_path,
        pin_harness_prelude()
        + r"""
const added = api.importPinShareCode(encode([
  { t: '__proto__', x: 10, y: 20, l: 'Prototype key' }
]));
assert.equal(added, 0, 'inherited property bypassed the pin type whitelist');
assert.equal(savedPins.length, 0);
""",
    )
    assert result.returncode == 0, result.stderr


def test_vitals_warning_uses_adaptive_boundary_without_old_gate():
    analyze = method_body(VITALS, "internal static VitalsTrendAnalysis Analyze(")
    assert "CurrentPercent > 35" not in analyze
    assert "warningMetric.BoundaryLabel" in analyze
    assert '"LOW"' in VITALS and '"CRITICAL"' in VITALS and '"EMPTY"' in VITALS


def test_encounter_history_is_bounded_recorded_twice_and_guarded():
    assert ENCOUNTERS.count("RecordEncounterHistory(") == 3
    assert "_encounterHistory.Count > 10" in ENCOUNTERS
    assert "_encounterHistory.RemoveAt(0);" in ENCOUNTERS
    copy = method_body(ENCOUNTERS, "private async Task CopyEncounterHistoryAsync()")
    assert "if (_encounterHistory.Count == 0)" in copy
    assert "Clipboard.SetText(text);" in copy
    assert "ExternalException or InvalidOperationException" in copy
    assert "ToLocalTime():HH:mm" in copy


def test_windows_smoke_checklist_covers_both_batches():
    smoke = (ROOT / "docs/WINDOWS_SMOKE_TEST.md").read_text(encoding="utf-8")
    assert re.findall(r"(?m)^\d+\. \*\*", smoke) == [
        f"{index}. **" for index in range(1, 11)
    ]
    for topic in (
        "Private server connect",
        "Tile retry",
        "Marker interpolation",
        "Pin share codes",
        "Vitals low-boundary warnings",
        "Encounter history",
    ):
        assert topic in smoke
