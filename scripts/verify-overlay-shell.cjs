const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.join(__dirname, "..");
const mainXaml = fs.readFileSync(path.join(root, "BurntHud", "MainWindow.xaml"), "utf8");
const mainCode = fs.readdirSync(path.join(root, "BurntHud"))
  .filter(name => name.startsWith("MainWindow") && name.endsWith(".cs"))
  .sort()
  .map(name => fs.readFileSync(path.join(root, "BurntHud", name), "utf8"))
  .join("\n");
const dockXaml = fs.readFileSync(path.join(root, "BurntHud", "IsleyDockWindow.xaml"), "utf8");
const dockCode = fs.readFileSync(path.join(root, "BurntHud", "IsleyDockWindow.xaml.cs"), "utf8");
const nativeCode = fs.readFileSync(path.join(root, "BurntHud", "NativeMethods.cs"), "utf8");
const lockHitTestCode = fs.readFileSync(
  path.join(root, "BurntHud", "SelectiveLockHitTest.cs"),
  "utf8");

for (const scrollOwner of [
  "ToolsScrollViewer",
  "CommandPaletteScrollViewer",
  "OnboardingScrollViewer",
  "UniversalSessionScrollViewer",
]) {
  assert.match(mainXaml, new RegExp(`x:Name="${scrollOwner}"`));
}

assert.match(mainXaml, /PreviewMouseWheel="Window_PreviewMouseWheel"/);
assert.match(mainCode, /private void Window_PreviewMouseWheel/);
assert.match(mainCode, /target = ToolsScrollViewer;/);
assert.match(mainCode, /target = CommandPaletteScrollViewer;/);
assert.match(mainCode, /target = OnboardingScrollViewer;/);
assert.match(mainCode, /target = UniversalSessionScrollViewer;/);

for (const panel of [
  "MapToolsPanel",
  "PinsToolsPanel",
  "LayerToolsPanel",
  "OverlayToolsPanel",
  "VoiceToolsPanel",
  "GuideToolsPanel",
  "HubToolsPanel",
]) {
  const scrollStart = mainXaml.indexOf('x:Name="ToolsScrollViewer"');
  const scrollEnd = mainXaml.indexOf("</ScrollViewer>", scrollStart);
  const panelIndex = mainXaml.indexOf(`x:Name="${panel}"`);
  assert.ok(panelIndex > scrollStart && panelIndex < scrollEnd, `${panel} must use the Tools scroll owner`);
}

assert.match(mainXaml, /x:Name="LockButton"/);
assert.match(mainCode, /public bool OverlayLocked \{ get; set; \}/);
assert.match(mainCode, /_overlayLocked = settings\.OverlayLocked;/);
assert.match(mainCode, /OverlayLocked = _overlayLocked,/);
assert.match(mainCode, /if \(!_overlayLocked && e\.LeftButton == MouseButtonState\.Pressed\)/);
assert.match(mainCode, /if \(_isDocked \|\| _overlayLocked\)/);
assert.match(mainXaml, /PreviewMouseDown="Window_PreviewMouseDown"/);
assert.match(mainXaml, /PreviewMouseUp="Window_PreviewMouseUp"/);
assert.match(mainCode, /message == NativeMethods\.WmNcHitTest && _overlayLocked/);
assert.match(mainCode, /SelectiveLockHitTest\.ContainsPackedScreenPoint\(LockButton, lParam\)/);
assert.match(mainCode, /new nint\(NativeMethods\.HtTransparent\)/);
assert.match(mainCode, /LiveMapWebView\.IsHitTestVisible = !_overlayLocked/);
assert.match(mainCode, /LOCKED · UNLOCK ONLY/);
assert.match(mainXaml, /Content="PLAY"/);
assert.match(mainXaml, /Content="MARKERS"/);
assert.match(mainXaml, /Content="MAP VIEW"/);
assert.match(mainXaml, /Content="SETTINGS"/);
assert.match(mainXaml, /Content="FIELD GUIDE"/);
assert.match(mainXaml, /x:Name="ToolsSectionHeadingText"/);
assert.match(mainXaml, /x:Name="ToolsSectionHelpText"/);
assert.match(mainCode, /"MARKERS & DESTINATIONS"/);
assert.match(mainCode, /"Choose a simple preset or turn individual map details on and off\."/);
assert.match(dockXaml, /x:Name="DockLockButton"/);
assert.match(dockCode, /if \(!_isLocked && e\.LeftButton == MouseButtonState\.Pressed\)/);
assert.match(dockCode, /internal void UpdateLockState\(bool locked\)/);
assert.match(dockCode, /message != NativeMethods\.WmNcHitTest \|\| !_isLocked/);
assert.match(dockCode, /SelectiveLockHitTest\.ContainsPackedScreenPoint\(DockLockButton, lParam\)/);
assert.match(nativeCode, /WmNcHitTest = 0x0084/);
assert.match(nativeCode, /HtClient = 1/);
assert.match(nativeCode, /HtTransparent = -1/);
assert.match(lockHitTestCode, /ContainsPackedScreenPoint/);
assert.match(lockHitTestCode, /PointToScreen/);

console.log("Overlay shell verification passed: scrolling and unlock-only click-through locking are present.");
