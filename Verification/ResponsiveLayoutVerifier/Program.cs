using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var normalExpanded = ResponsiveLayoutLogic.Resolve(472, 560, requestedSurvivalDetails: true);
Check(!normalExpanded.IsMicroLayout && normalExpanded.ShowSurvivalDetails,
    "The default overlay should honor expanded survival instructions");
Check(normalExpanded.SurvivalDetailAction == "LESS"
      && normalExpanded.VitalsMinimumWidth == 132
      && normalExpanded.FooterSizeColumnWidth == 70
      && !normalExpanded.StretchToolsDrawer
      && normalExpanded.ShowToolsDrawerSubtitle
      && normalExpanded.ShowMapSectionJumpBar
      && normalExpanded.ToolsDrawerTopInset == 48
      && normalExpanded.ToolsDrawerPadding == 10
      && normalExpanded.ToolsBodyTopInset == 7
      && normalExpanded.ToolsHeaderButtonHeight == 26
      && normalExpanded.ToolsCategoryButtonHeight == 28,
    "The default footer and detail controls should retain their normal geometry");

var normalCollapsed = ResponsiveLayoutLogic.Resolve(472, 560, requestedSurvivalDetails: false);
Check(!normalCollapsed.ShowSurvivalDetails && normalCollapsed.SurvivalDetailAction == "MORE",
    "The normal layout should honor the player's collapsed survival preference");

var minimum = ResponsiveLayoutLogic.Resolve(380, 390, requestedSurvivalDetails: true);
Check(minimum.IsMicroLayout && !minimum.ShowSurvivalDetails,
    "The supported minimum must fold verbose survival details");
Check(minimum.SurvivalDetailAction == "OPEN"
      && minimum.VitalsMinimumWidth == 94
      && minimum.FooterSizeColumnWidth == 54,
    "The minimum layout should expose the scrollable handoff and compact footer geometry");

var freelyResizedMinimum = ResponsiveLayoutLogic.Resolve(320, 280, requestedSurvivalDetails: true);
Check(freelyResizedMinimum.IsMicroLayout && !freelyResizedMinimum.ShowSurvivalDetails,
    "The freely resized 320 by 280 overlay must remain in the safe micro posture");
Check(freelyResizedMinimum.StretchToolsDrawer
      && !freelyResizedMinimum.ShowToolsDrawerSubtitle
      && !freelyResizedMinimum.ShowMapSectionJumpBar
      && freelyResizedMinimum.ToolsDrawerTopInset == 0
      && freelyResizedMinimum.ToolsDrawerPadding == 4
      && freelyResizedMinimum.ToolsBodyTopInset == 3
      && freelyResizedMinimum.ToolsHeaderButtonHeight == 22
      && freelyResizedMinimum.ToolsCategoryButtonHeight == 20,
    "The 320 by 280 Tools drawer must use the full content height and keep its main categories reachable");

var narrow = ResponsiveLayoutLogic.Resolve(420, 560, requestedSurvivalDetails: true);
var shortViewport = ResponsiveLayoutLogic.Resolve(720, 440, requestedSurvivalDetails: true);
Check(narrow.IsMicroLayout && shortViewport.IsMicroLayout,
    "Either constrained axis should activate the micro layout");

var roomy = ResponsiveLayoutLogic.Resolve(421, 441, requestedSurvivalDetails: true);
Check(!roomy.IsMicroLayout && roomy.ShowSurvivalDetails,
    "A viewport beyond both thresholds should restore the requested detail");

var invalid = ResponsiveLayoutLogic.Resolve(double.NaN, double.PositiveInfinity, true);
Check(!invalid.IsMicroLayout && invalid.ShowSurvivalDetails,
    "Invalid measurements should use the stable default geometry");

Check(ResponsiveLayoutLogic.FooterHotkeyStatus(
          minimum, 9, 9, true, false, false, "CTRL+SHIFT+I") == "KEYS 9/9",
    "The micro ready state must remain readable");
Check(ResponsiveLayoutLogic.FooterHotkeyStatus(
          minimum, 8, 9, false, false, false, "CTRL+SHIFT+I") == "KEYS 8/9 !",
    "The micro conflict state must remain visible");
Check(ResponsiveLayoutLogic.FooterHotkeyStatus(
          minimum, 9, 9, true, true, false, "CTRL+SHIFT+I") == "CTRL+SHIFT+I USE",
    "The micro interaction recovery chord must remain visible");
Check(ResponsiveLayoutLogic.FooterHotkeyStatus(
          normalExpanded, 9, 9, true, false, false, "CTRL+SHIFT+I") == "KEYS READY",
    "The normal ready state should remain fully visible beside Core Vitals");

var allHudSurfaces = new HudSurfacePreferences(
    Navigation: true,
    Vitals: true,
    Pack: true,
    Encounters: true,
    Survival: true,
    Voice: true,
    Alerts: true,
    Nearby: true,
    Aim: true,
    QuickKeys: true);
var fullHud = HudSurfaceLogic.Present(allHudSurfaces, streamerMode: false);
Check(fullHud.EnabledCount == 10
      && fullHud.TotalCount == HudSurfaceLogic.SurfaceCount
      && fullHud.Status.Contains("HIDDEN VISUALS KEEP THEIR TOOLS RUNNING", StringComparison.Ordinal)
      && !fullHud.PrivacyHidden,
    "The HUD manager should truthfully summarize all ten independent visual preferences");
var reducedHud = HudSurfaceLogic.Present(
    allHudSurfaces with { Navigation = false, Survival = false, Alerts = false },
    streamerMode: false);
Check(reducedHud.EnabledCount == 7 && reducedHud.Status.StartsWith("7 / 10 ON", StringComparison.Ordinal),
    "The HUD manager should count independently hidden surface groups");
var privateHud = HudSurfaceLogic.Present(allHudSurfaces, streamerMode: true);
Check(privateHud.PrivacyHidden
      && privateHud.EnabledCount == 10
      && privateHud.Status == "PRIVACY HIDES MAP HUD · PREFERENCES PRESERVED"
      && !HudSurfaceLogic.Show(preference: true, streamerMode: true)
      && HudSurfaceLogic.Show(preference: true, streamerMode: false)
      && !HudSurfaceLogic.Show(preference: false, streamerMode: false),
    "Streamer Mode must hide surfaces without destroying their saved preferences");

var mainWindowSource = string.Join("\n", Directory.GetFiles(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText)) + "\n" + File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "Map", "isley-map-controller.js"));
var mainWindowXaml = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "MainWindow.xaml"));
var aimGuideSource = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "AimGuideWindow.xaml.cs"));
var aimGuideXaml = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "AimGuideWindow.xaml"));
var dockWindowXaml = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "IsleyDockWindow.xaml"));
var logoXaml = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "TriceratopsLogo.xaml"));
var appXaml = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "App.xaml"));
var projectSource = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "BurntHud.csproj"));
var brandAssetDirectory = Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "Assets", "Brand");
Check(mainWindowXaml.Contains("MinWidth=\"320\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("MinHeight=\"280\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"ResizeGrip\" Width=\"28\" Height=\"28\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("SizeButton.Visibility = !_isDocked", StringComparison.Ordinal)
      && mainWindowSource.Contains("var sizeColumnWidth = ultraCompact", StringComparison.Ordinal)
      && mainWindowSource.Contains("MinWidth = 320;", StringComparison.Ordinal)
      && mainWindowSource.Contains("MinHeight = 280;", StringComparison.Ordinal),
    "The overlay must not shrink below a usable restore/resize floor");
Check(mainWindowSource.Contains("selected.ShortLabel.ToUpperInvariant()", StringComparison.Ordinal)
      && mainWindowSource.Contains(": \"CHECK\"", StringComparison.Ordinal),
    "The micro sickness header should preserve a readable condition and completed-state action");
Check(mainWindowSource.Contains("new IsleyDockWindow", StringComparison.Ordinal)
      && mainWindowSource.Contains("private void SetDocked(bool docked)", StringComparison.Ordinal)
      && mainWindowSource.Contains("_dockWindow.Show();", StringComparison.Ordinal)
      && mainWindowSource.Contains("Hide();", StringComparison.Ordinal)
      && dockWindowXaml.Contains("Width=\"362\" Height=\"64\"", StringComparison.Ordinal)
      && dockWindowXaml.Contains("MinWidth=\"264\"", StringComparison.Ordinal)
      && dockWindowXaml.Contains("DockVitalsButton", StringComparison.Ordinal)
      && mainWindowSource.Contains("CurrentDockVitalsPresentation()", StringComparison.Ordinal)
      && mainWindowSource.Contains("_vitalsHudVisible && !_streamerMode ? 362 : 264", StringComparison.Ordinal)
      && dockWindowXaml.Contains("Content=\"OPEN\"", StringComparison.Ordinal)
      && dockWindowXaml.Contains("ShowInTaskbar=\"True\"", StringComparison.Ordinal),
    "The overlay should expose a genuine tiny draggable, taskbar-accessible dock with optional truthful vitals in a separate WebView-safe window and a deterministic restore path");
Check(mainWindowSource.Contains("pendingMapAction", StringComparison.Ordinal)
      && mainWindowSource.Contains("document.hasFocus()", StringComparison.Ordinal)
      && mainWindowSource.Contains("event?.type === 'pointerup'", StringComparison.Ordinal)
      && mainWindowSource.Contains("event.isPrimary === true", StringComparison.Ordinal)
      && mainWindowSource.Contains("elapsed <= 5000", StringComparison.Ordinal)
      && mainWindowSource.Contains("mapInteractionRevision += 1", StringComparison.Ordinal)
      && mainWindowSource.Contains("Action clicks deliberately do not capture the pointer", StringComparison.Ordinal)
      && mainWindowSource.Contains("window.addEventListener('blur', onWindowBlur, true)", StringComparison.Ordinal)
      && mainWindowSource.Contains("window.addEventListener('pagehide', onPageHide, true)", StringComparison.Ordinal)
      && mainWindowSource.Contains("event.relatedTarget === null", StringComparison.Ordinal)
      && mainWindowSource.Contains("document.addEventListener('pointerout', onDocumentPointerOut, true)", StringComparison.Ordinal)
      && mainWindowSource.Contains("map.addEventListener('lostpointercapture', onLostPointerCapture", StringComparison.Ordinal)
      && mainWindowSource.Contains("map.releasePointerCapture(capturedPointerId)", StringComparison.Ordinal)
      && mainWindowSource.Contains("cancelPointerGesture(event.pointerId)", StringComparison.Ordinal)
      && mainWindowSource.Contains("cancelMapPointerGesture()", StringComparison.Ordinal)
      && mainWindowSource.Contains("CancelMapPointerGestureAsync()", StringComparison.Ordinal)
      && mainWindowXaml.Contains("MouseLeave=\"Window_MouseLeave\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("MouseLeave=\"LiveMapWebView_MouseLeave\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("LostKeyboardFocus=\"LiveMapWebView_LostKeyboardFocus\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Deactivated=\"Window_Deactivated\"", StringComparison.Ordinal),
    "Map placement must use a short same-pointer interaction token without capture, require a focused in-bounds release, and cancel across browser and native window exits");
Check(mainWindowXaml.Contains("local:TriceratopsLogo", StringComparison.Ordinal)
      && !mainWindowXaml.Contains("Text=\"IY\"", StringComparison.Ordinal)
      && !mainWindowXaml.Contains("Text=\"IS\"", StringComparison.Ordinal),
    "Every letter-tile brand mark should be replaced by the reusable Triceratops head");
Check(logoXaml.Contains("Isley red Triceratops battlecry logo", StringComparison.Ordinal)
      && logoXaml.Contains("isley-triceratops-app-teeth-clean.png", StringComparison.Ordinal)
      && appXaml.Contains("BrandRedBrush", StringComparison.Ordinal)
      && appXaml.Contains("BrandSurfaceBrush", StringComparison.Ordinal)
      && mainWindowXaml.Contains("BrandBorderBrush", StringComparison.Ordinal)
      && dockWindowXaml.Contains("BrandBorderBrush", StringComparison.Ordinal)
      && projectSource.Contains("<ApplicationIcon>Assets\\Brand\\Isley-teeth-clean.ico</ApplicationIcon>", StringComparison.Ordinal)
      && projectSource.Contains("isley-triceratops-app-teeth-clean.png", StringComparison.Ordinal)
      && new FileInfo(Path.Combine(brandAssetDirectory, "isley-triceratops-app-teeth-clean.png")).Length > 100_000
      && new FileInfo(Path.Combine(brandAssetDirectory, "Isley-teeth-clean.ico")).Length > 10_000,
    "The red side-profile battlecry mark must remain shared by the overlay, dock, and Windows executable");
Check(mainWindowXaml.Contains("AimGuideButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VitalsHudButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideGrowthButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideGrowthSyncButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideCameraButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideAreaButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideCenterButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideUncertaintyButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideLabelButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideConfirmMatchButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideInsideMissButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideOutsideHitButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideClearEvidenceButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("AimGuideEvidenceStatusText", StringComparison.Ordinal)
      && mainWindowSource.Contains("new(\"aim-guide\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("new(\"vitals-hud\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("new(\"dock-overlay\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("AimGuideDepthScale = _aimGuideDepthScale", StringComparison.Ordinal)
      && mainWindowSource.Contains("AimGuideHorizontalOffset = _aimGuideHorizontalOffset", StringComparison.Ordinal)
      && mainWindowSource.Contains("AimGuideGrowthSyncEnabled = _aimGuideGrowthSyncEnabled", StringComparison.Ordinal)
      && mainWindowSource.Contains("new(\"aim-growth-sync\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("CurrentAimGrowthContext", StringComparison.Ordinal)
      && mainWindowSource.Contains("AimGuideUncertaintyVisible = _aimGuideUncertaintyVisible", StringComparison.Ordinal)
      && mainWindowSource.Contains("InsideMisses = profile.InsideMisses", StringComparison.Ordinal)
      && mainWindowSource.Contains("OutsideHits = profile.OutsideHits", StringComparison.Ordinal)
       && mainWindowSource.Contains("AimGuideEvidenceButton_Click", StringComparison.Ordinal)
       && aimGuideSource.Contains("WsExTransparent", StringComparison.Ordinal)
       && aimGuideSource.Contains("WsExNoActivate", StringComparison.Ordinal)
       && aimGuideSource.Contains("AlignToForegroundViewport", StringComparison.Ordinal)
       && aimGuideSource.Contains("TryAlignToClientArea", StringComparison.Ordinal)
       && mainWindowXaml.Contains("AimGuideViewportStatusText", StringComparison.Ordinal)
       && mainWindowSource.Contains("foreground == PlayFocusForeground.Game", StringComparison.Ordinal)
       && aimGuideXaml.Contains("OuterUncertaintyArea", StringComparison.Ordinal)
       && aimGuideXaml.Contains("AimDepthTransform", StringComparison.Ordinal)
       && aimGuideXaml.Contains("GuideOffsetTransform", StringComparison.Ordinal),
    "The five-stage live-growth aim reference and compact vitals must be toggleable, and the game-client-aligned guide must never intercept game input");

Check(mainWindowXaml.Contains("HudSurfacesSectionAnchor", StringComparison.Ordinal)
      && mainWindowXaml.Contains("HudNavigationButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("VitalsHudButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("HudPackButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("HudEncounterButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("HudSurvivalButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("HudVoiceButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("HudAlertsButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("HudNearbyButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("HudAimButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("HudQuickKeysButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("Hidden surfaces keep their routes, timers, and alerts running", StringComparison.Ordinal),
    "Visual Comfort must expose one calm ten-switch HUD surface manager");
Check(mainWindowXaml.Contains("x:Name=\"ToolsDrawerSubtitle\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"ToolsCategoryTabs\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"ToolsFindButton\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("presentation.StretchToolsDrawer ? double.NaN : 224", StringComparison.Ordinal)
      && mainWindowSource.Contains("presentation.ToolsDrawerTopInset", StringComparison.Ordinal)
      && mainWindowSource.Contains("presentation.ToolsCategoryButtonHeight", StringComparison.Ordinal)
      && mainWindowSource.Contains("presentation.ShowMapSectionJumpBar", StringComparison.Ordinal),
    "The micro Tools drawer must stretch within the viewport and trade decorative rows for reachable controls");
Check(mainWindowSource.Contains("NavigationHudVisible = _navigationHudVisible", StringComparison.Ordinal)
      && mainWindowSource.Contains("SurvivalHudVisible = _survivalHudVisible", StringComparison.Ordinal)
      && mainWindowSource.Contains("AlertHudVisible = _alertHudVisible", StringComparison.Ordinal)
      && mainWindowSource.Contains("UpdateHudSurfaceControls", StringComparison.Ordinal)
      && mainWindowSource.Contains("HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode)", StringComparison.Ordinal)
      && mainWindowSource.Contains("HudSurfaceLogic.Show(_alertHudVisible, _streamerMode)", StringComparison.Ordinal)
      && mainWindowSource.Contains("new(\"hud-surfaces\"", StringComparison.Ordinal),
    "HUD group switches must persist, gate only presentation, and remain keyboard discoverable");

Console.WriteLine(
    "Responsive overlay verification passed (minimum, narrow, short, roomy, HUD surfaces, invalid, sickness detail, and hotkey footer states)." );
