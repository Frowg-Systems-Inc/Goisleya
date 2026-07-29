const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

// Git may materialize text as CRLF on Windows runners. Keep source-contract
// comparisons deterministic without changing any shipped application files.
const nativeReadFileSync = fs.readFileSync.bind(fs);
fs.readFileSync = (...args) => {
  const value = nativeReadFileSync(...args);
  return typeof value === 'string' ? value.replace(/\r\n?/g, '\n') : value;
};

// MainWindow is split into feature partial classes (MainWindow.*.cs); the
// source contract surface is the concatenation of every partial plus the
// packaged map controller script.
const mainWindowDirectory = path.join(__dirname, '..', 'BurntHud');
const controllerScriptPath = path.join(mainWindowDirectory, 'Map', 'isley-map-controller.js');
const controllerScriptSource = fs.readFileSync(controllerScriptPath, 'utf8');
const source = fs.readdirSync(mainWindowDirectory)
  .filter(name => name.startsWith('MainWindow') && name.endsWith('.cs'))
  .sort()
  .map(name => fs.readFileSync(path.join(mainWindowDirectory, name), 'utf8'))
  .join('\n')
  + '\n' + controllerScriptSource;
const xamlSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'MainWindow.xaml'), 'utf8');
const nativeMethodsSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'NativeMethods.cs'), 'utf8');
const projectSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'BurntHud.csproj'), 'utf8')
  + fs.readFileSync(path.join(__dirname, '..', 'Directory.Build.props'), 'utf8');
const manifestSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'app.manifest'), 'utf8');
const appXamlSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'App.xaml'), 'utf8');
const logoXamlSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'TriceratopsLogo.xaml'), 'utf8');
const manualSightingLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'ManualSightingLogic.cs'),
  'utf8');
const quickKeysLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'QuickKeysLogic.cs'),
  'utf8');
const dockXamlSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'IsleyDockWindow.xaml'), 'utf8');
const dockCodeSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'IsleyDockWindow.xaml.cs'), 'utf8');
const dockVitalsLogicSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'DockVitalsLogic.cs'), 'utf8');
const overlayLinksSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'OverlayLinks.cs'), 'utf8');
const voiceIntegrationLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'VoiceIntegrationLogic.cs'),
  'utf8');
const voiceInviteLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'VoiceInviteLogic.cs'),
  'utf8');
const voiceRouteOfferLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'VoiceRouteOfferLogic.cs'),
  'utf8');
const voiceServerReadinessSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'VoiceServerReadinessClient.cs'),
  'utf8');
const voiceClientSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'Voice', 'voice.js'),
  'utf8');
const voiceCryptoSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'Voice', 'voice-crypto.js'),
  'utf8');
const voiceServerSource = fs.readFileSync(
  path.join(__dirname, '..', 'Isley.VoiceServer', 'Program.cs'),
  'utf8');
const voiceIntegrationVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'VoiceIntegrationVerifier', 'Program.cs'),
  'utf8');
const voiceAudioOutputVerifierSource = fs.readFileSync(
  path.join(__dirname, 'verify-voice-audio-output.cjs'),
  'utf8');
const steamFriendLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'SteamFriendLogic.cs'),
  'utf8');
const steamFriendVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'SteamFriendVerifier', 'Program.cs'),
  'utf8');
const hudDockLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'HudDockLogic.cs'),
  'utf8');
const hudDockVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'HudDockVerifier', 'Program.cs'),
  'utf8');
const hudPriorityLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'HudPriorityLogic.cs'),
  'utf8');
const hudPriorityVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'HudPriorityVerifier', 'Program.cs'),
  'utf8');
const responsiveLayoutLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'ResponsiveLayoutLogic.cs'),
  'utf8');
const responsiveLayoutVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'ResponsiveLayoutVerifier', 'Program.cs'),
  'utf8');
const liteModeLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'LiteModeLogic.cs'),
  'utf8');
const liteModeVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'LiteModeVerifier', 'Program.cs'),
  'utf8');
const onboardingTutorialLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'OnboardingTutorialLogic.cs'),
  'utf8');
const onboardingTutorialVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'OnboardingTutorialVerifier', 'Program.cs'),
  'utf8');
const terrainGapPolicyLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'TerrainGapPolicyLogic.cs'),
  'utf8');
const aimCalibrationLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'AimCalibrationLogic.cs'),
  'utf8');
const aimViewportLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'AimViewportLogic.cs'),
  'utf8');
const aimGuideWindowSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'AimGuideWindow.xaml.cs'),
  'utf8');
const aimGuideWindowXamlSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'AimGuideWindow.xaml'),
  'utf8');
const aimCalibrationVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'AimCalibrationVerifier', 'Program.cs'),
  'utf8');
const gatewayResourceLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'GatewayResourceLogic.cs'),
  'utf8');
const gatewayResourceVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'GatewayResourceVerifier', 'Program.cs'),
  'utf8');
const serverSessionLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'ServerSessionLogic.cs'),
  'utf8');
const serverSessionVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'ServerSessionVerifier', 'Program.cs'),
  'utf8');
const universalCoordinateLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'UniversalCoordinateLogic.cs'),
  'utf8');
const slopeSafetyLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'SlopeSafetyLogic.cs'),
  'utf8');
const universalCoordinateVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'UniversalCoordinateVerifier', 'Program.cs'),
  'utf8');
const communityServerWatchLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'CommunityServerWatchLogic.cs'),
  'utf8');
const communityServerWatchVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'CommunityServerWatchVerifier', 'Program.cs'),
  'utf8');
const fieldGuideLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'FieldGuideLogic.cs'),
  'utf8');
const combatGuideLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'CombatGuideLogic.cs'),
  'utf8');
const fieldGuideVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'FieldGuideVerifier', 'Program.cs'),
  'utf8');
const lifeRunLogicSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'LifeRunLogic.cs'), 'utf8');
const lifeRunHistoryLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'LifeRunHistoryLogic.cs'),
  'utf8');
const lifeRunVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'LifeRunVerifier', 'Program.cs'),
  'utf8');
const elderLineageLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'ElderLineageLogic.cs'),
  'utf8');
const elderLineageVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'ElderLineageVerifier', 'Program.cs'),
  'utf8');
const growthPlannerLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'GrowthPlannerLogic.cs'),
  'utf8');
const growthPlannerVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'GrowthPlannerVerifier', 'Program.cs'),
  'utf8');
const liveGrowthBridgeLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'LiveGrowthBridgeLogic.cs'),
  'utf8');
const liveGrowthBridgeVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'LiveGrowthBridgeVerifier', 'Program.cs'),
  'utf8');
const liveSpeciesBridgeLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'LiveSpeciesBridgeLogic.cs'),
  'utf8');
const liveSpeciesBridgeVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'LiveSpeciesBridgeVerifier', 'Program.cs'),
  'utf8');
const lifeTransitionLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'LifeTransitionLogic.cs'),
  'utf8');
const lifeTransitionVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'LifeTransitionVerifier', 'Program.cs'),
  'utf8');
const growthGateWatchLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'GrowthGateWatchLogic.cs'),
  'utf8');
const growthGateWatchVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'GrowthGateWatchVerifier', 'Program.cs'),
  'utf8');
const approachBriefLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'ApproachBriefLogic.cs'),
  'utf8');
const approachBriefVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'ApproachBriefVerifier', 'Program.cs'),
  'utf8');
const nestPlannerLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'NestPlannerLogic.cs'),
  'utf8');
const nestPlannerVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'NestPlannerVerifier', 'Program.cs'),
  'utf8');
const mutationPlannerLogicSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'MutationPlannerLogic.cs'), 'utf8');
const mutationBuildLogicSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'MutationBuildLogic.cs'), 'utf8');
const mutationPlannerVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'MutationPlannerVerifier', 'Program.cs'),
  'utf8');
const nextMoveLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'NextMoveLogic.cs'),
  'utf8');
const fightCheckLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'FightCheckLogic.cs'),
  'utf8');
const nextMoveVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'NextMoveVerifier', 'Program.cs'),
  'utf8');
const mutationUnlockLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'MutationUnlockLogic.cs'),
  'utf8');
const mutationUnlockVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'MutationUnlockVerifier', 'Program.cs'),
  'utf8');
const dietCoachLogicSource = fs.readFileSync(path.join(__dirname, '..', 'BurntHud', 'DietCoachLogic.cs'), 'utf8');
const dietCoachVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'DietCoachVerifier', 'Program.cs'),
  'utf8');
const survivalAssistantLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'SurvivalAssistantLogic.cs'),
  'utf8');
const survivalAssistantVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'SurvivalAssistantVerifier', 'Program.cs'),
  'utf8');
const recoveryMonitorLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'RecoveryMonitorLogic.cs'),
  'utf8');
const recoveryMonitorVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'RecoveryMonitorVerifier', 'Program.cs'),
  'utf8');
const coreVitalsLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'CoreVitalsLogic.cs'),
  'utf8');
const woundCheckLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'WoundCheckLogic.cs'),
  'utf8');
const coreVitalsVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'CoreVitalsVerifier', 'Program.cs'),
  'utf8');
const playerSnapshotLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'PlayerSnapshotLogic.cs'),
  'utf8');
const playerSnapshotVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'PlayerSnapshotVerifier', 'Program.cs'),
  'utf8');
const vitalsTrendLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'VitalsTrendLogic.cs'),
  'utf8');
const vitalsTrendVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'VitalsTrendVerifier', 'Program.cs'),
  'utf8');
const tripReadinessLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'TripReadinessLogic.cs'),
  'utf8');
const tripReadinessVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'TripReadinessVerifier', 'Program.cs'),
  'utf8');
const fieldConditionsLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'FieldConditionsLogic.cs'),
  'utf8');
const fieldConditionsVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'FieldConditionsVerifier', 'Program.cs'),
  'utf8');
const safeLogoutLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'SafeLogoutLogic.cs'),
  'utf8');
const safeLogoutVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'SafeLogoutVerifier', 'Program.cs'),
  'utf8');
const serverRestartWatchLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'ServerRestartWatchLogic.cs'),
  'utf8');
const serverRestartWatchVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'ServerRestartWatchVerifier', 'Program.cs'),
  'utf8');
const hotkeyBindingLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'HotkeyBindingLogic.cs'),
  'utf8');
const hotkeyBindingVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'HotkeyBindingVerifier', 'Program.cs'),
  'utf8');
const soundFinderLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'SoundFinderLogic.cs'),
  'utf8');
const soundFinderVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'SoundFinderVerifier', 'Program.cs'),
  'utf8');
const focusModeLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'FocusModeLogic.cs'),
  'utf8');
const focusModeVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'FocusModeVerifier', 'Program.cs'),
  'utf8');
const serverStatusSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'IsleServerStatusClient.cs'),
  'utf8');
const patchWatchSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'OfficialPatchWatch.cs'),
  'utf8');
const contentBaselineSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'IsleContentBaseline.cs'),
  'utf8');
const patchWatchVerifierSource = fs.readFileSync(
  path.join(__dirname, '..', 'Verification', 'PatchWatchVerifier', 'Program.cs'),
  'utf8');
const body = controllerScriptSource;
if (!body.includes('window.__isley')) {
  throw new Error('Packaged map controller script was not found or is incomplete.');
}

new Function(body);

const requiredContracts = [
  ['controller version', 'version: 78'],
  ['pointer-exit cancellation', 'onDocumentPointerOut'],
  ['pointer-capture loss cancellation', 'onLostPointerCapture'],
  ['page-hide cancellation', "window.addEventListener('pagehide', onPageHide, true)"],
  ['non-capturing map placement', 'Action clicks deliberately do not capture the pointer'],
  ['bounded map interaction token', 'elapsed <= 5000'],
  ['map replacement interaction invalidation', 'mapInteractionRevision += 1'],
  ['native pointer cancellation bridge', 'cancelMapPointerGesture()'],
  ['controller reuse gate', 'existing?.version === 78'],
  ['paused-follow freshness refresh', 'refreshSelfMarkerFreshness'],
  ['acquire freshness API', 'refreshSelfFreshness()'],
  ['turn-aware half-second poll fallback', 'fastPollIntervalMs = Number(pagePollControl?.targetDelayMs) || 500'],
  ['sub-second request gap', 'Math.max(250, fastPollIntervalMs - 50)'],
  ['single-flight marker request', 'if (!markerFetchPromise)'],
  ['rate-limit backoff', 'markerResponseStatus === 429'],
  ['future-page fallback scheduler', 'scheduleFastPoll(250)'],
  ['response-frame render', "requestAnimationFrame(() => tick('marker-response'))"],
  ['turn-only pose freshness', '${pose.rawX ?? pose.x}:${pose.rawY ?? pose.y}:${pose.rotation}'],
  ['authorized yaw source', 'Number.isFinite(Number(self.yaw))'],
  ['route style normalization', 'normalizeTerrainRouteStyle'],
  ['route style weighted costs', "road: 0.85, trail: 1.4, learned: 1.05, connector: 1.5, endpoint: 2"],
  ['route style hard-constraint reroute', 'terrain-course-style-changed'],
  ['terrain gap policy normalization', 'normalizeTerrainGapPolicy'],
  ['terrain gap policy limits', 'maximumConnectorDistance'],
  ['terrain gap policy hard-constraint reroute', 'terrain-course-gap-policy-changed'],
  ['route edge evidence metrics', 'unknownSegmentCount'],
  ['bounded typed course segments', 'segments: selectedEdges.length <= 5000'],
  ['typed course renderer', 'drawTypedTerrainCourse'],
  ['solid road evidence color', "color: '#2dd4bf'"],
  ['dashed trail evidence color', "color: '#60a5fa'"],
  ['mixed learned-passage evidence color', "color: '#c084fc'"],
  ['amber unknown connector evidence', "color: '#f59e0b'"],
  ['toggleable course evidence API', 'setTerrainRouteEvidenceVisible(visible)'],
  ['blocked-passage area builder', 'buildBlockedPassageArea'],
  ['blocked-passage reporting API', 'reportBlockedTerrainPassage()'],
  ['measured-slope area builder', 'buildMeasuredSlopeArea'],
  ['measured-slope route-avoidance API', 'saveMeasuredSlopeAvoidance('],
  ['typed road/trail/learned graph', "['road', 'trail', 'learned'].includes(pathType)"],
  ['persistent pin store', "the-isle-mapper-saved-pins-v4"],
  ['configurable range-ring radii', 'rangeRingRadii = [25, 50]'],
  ['range-ring mode whitelist', "['10:25', '25:50', '50:100'].includes(requestedSignature)"],
  ['range-ring redraw signature', 'isleMapperRangeSignature'],
  ['restrained range-ring reveal', '{ duration: 220, easing: \'ease-out\' }'],
  ['transform-accurate placement', 'getScreenCTM'],
  ['typed pin placement', 'addSavedPin'],
  ['water marker type', "water: { label: 'Water'"],
  ['rally marker type', "rally: { label: 'Rally'"],
  ['death marker type', "death: { label: 'Death'"],
  ['authorized death-position selection', 'selectDeathMarkerPoint'],
  ['death marker replacement API', 'dropDeathPin()'],
  ['single latest death marker', "savedPins = savedPins.filter(pin => pin.type !== 'death')"],
  ['pin routing', 'routeToNewestPin'],
  ['nearest-friend calculation', 'updateNearestFriend'],
  ['streamer pin hiding', "root.style.display = streamerMode ? 'none' : ''"],
  ['named-place routing', 'routeToNamedPlace'],
  ['explicit shared-route paste API', 'startSharedRouteText(query)'],
  ['place-label coordinate normalization', 'textScreenMatrix'],
  ['world-coordinate routing', 'routeToWorldCoordinates'],
  ['live friend routing', 'routeToNearestFriend'],
  ['destination waypoint labels', 'waypointLabel'],
  ['friend roster bridge', 'friendRoster: friendRoster.map'],
  ['specific friend routing', 'routeToFriend'],
  ['saved destination roster', 'pinRoster: buildPinRoster()'],
  ['specific pin routing', 'routeToPin'],
  ['nearest recovery-pin routing', 'routeToNearestPinType'],
  ['death recovery course allow-list', "['safe', 'water', 'food', 'death']"],
  ['specific pin removal', 'removePin'],
  ['shareable pin coordinates', 'mapToWorldPoint'],
  ['persisted last live position', 'the-isle-mapper-last-live-position-v1'],
  ['session start capture', 'sessionStartPosition ??='],
  ['session start route', 'routeToSessionStart'],
  ['last live position route', 'routeToLastPosition'],
  ['last-position privacy control', 'rememberLastPositionEnabled'],
  ['two-point map ruler', 'armMeasurement'],
  ['persistent ruler line', 'data-isle-mapper-measurement'],
  ['exact ruler distance', 'measurementDistance: end ? Math.hypot(dx, dy) : null'],
  ['calibrated ruler endpoints', 'measurementStartWorldX'],
  ['streamer-safe ruler redaction', "root.style.display = streamerMode || !start ? 'none' : ''"],
  ['multi-stop route planning', 'armRoutePlan'],
  ['ordered route activation', 'startRoutePlan'],
  ['numbered route overlay', 'data-isle-mapper-route-plan'],
  ['manual route advancement', 'advanceRouteStopInternal'],
  ['automatic route advancement', 'route-auto-advanced'],
  ['remaining-route bridge', 'routeRemainingDistance'],
  ['calibrated route sharing', 'routeStops: routeStops.map'],
  ['road/trail network bridge', 'loadTerrainRoadNetwork(payload)'],
  ['current road/trail map rendering', 'terrainRoadDisplayRoot.dataset.isleyCurrentTerrainNetwork'],
  ['current drinking-water map rendering', 'terrainWaterVisual.dataset.isleyCurrentWaterMask'],
  ['bounded public terrain danger transform', 'loadTerrainCommunityHazards'],
  ['public terrain danger overlay', 'data-isley-terrain-community-hazards'],
  ['public terrain danger obstacle bridge', "kind: 'community-hazard'"],
  ['toggleable public terrain danger API', 'setTerrainCommunityHazardsEnabled(enabled)'],
  ['bounded public resource routing', 'routeMapPoint(payload)'],
  ['resource route event', "notify('resource-site-routed')"],
  ['obstacle-aware course engine', 'calculateTerrainRoadCourse'],
  ['direct-route obstacle risk engine', 'calculateDirectRouteObstacleRisk'],
  ['Trip Check route-risk bridge', '...buildTripRouteRiskState()'],
  ['terrain-course risk suppression', "routePlanSource === 'terrain'"],
  ['bounded escape-route engine', 'calculateEscapeRoute'],
  ['live escape-route command', 'startEscapeRoute()'],
  ['server-authorized escape route API', 'startEscapeRoute() {'],
  ['Danger-zone hard obstacles', "pin.type === 'danger' && Number(pin.alertRadius) > 0"],
  ['persistent no-go area store', "isley-no-go-areas-v1"],
  ['bounded polygon tracing', 'noGoAreaMaximumVertices = 12'],
  ['self-crossing boundary refusal', 'routePolygonSelfIntersects'],
  ['polygon course obstacle bridge', "kind: 'polygon'"],
  ['padded polygon route blocking', 'routeSegmentIntersectsPolygon'],
  ['streamer-safe no-go state', "noGoLastStatus: 'hidden'"],
  ['native trace bridge', 'beginNoGoTrace(label)'],
  ['road/trail route source', "routePlanSource = 'terrain'"],
  ['road-course auto replan threshold', 'offCourseDistance > 25'],
  ['road-course replan cooldown', 'now - terrainCourseReplanAt >= 10000'],
  ['road-course direct comparison', 'terrainCourseDirectDistance'],
  ['streamer-safe terrain state', "terrainCourseStatus: 'hidden'"],
  ['session breadcrumb sampling', 'recordBreadcrumbSample'],
  ['private session trail render root', 'data-isle-mapper-breadcrumb-trail'],
  ['session trail point simplification', 'simplifyBreadcrumbTrailPoints'],
  ['session trail configuration', "'breadcrumbTrailVisible' in options"],
  ['guarded session trail clear API', 'clearBreadcrumbTrail()'],
  ['streamer-hidden session trail', 'breadcrumbTrailVisible && !streamerMode'],
  ['bounded learned-passage store', "isley-learned-passages-v1"],
  ['explicit learned-passage save', 'saveCurrentSessionPassage()'],
  ['toggleable learned routing', 'setLearnedPassageRoutingEnabled(enabled)'],
  ['toggleable learned display', 'setLearnedPassageVisible(visible)'],
  ['guarded learned-passage clear API', 'clearLearnedPassages()'],
  ['source-aware learned freshness', 'learnedPassageIsCurrent'],
  ['learned geometry renderer', 'data-isley-learned-passages'],
  ['exact official layer state application', 'applyOfficialLayerState'],
  ['privacy-safe layer state whitelist', "'food', 'heatmap', 'selfTrail', 'friendTrails'"],
  ['breadcrumb route simplification', 'buildBreadcrumbRouteStops'],
  ['breadcrumb return activation', 'startBreadcrumbReturn'],
  ['breadcrumb route source', "routePlanSource = 'breadcrumb'"],
  ['breadcrumb privacy bridge', 'breadcrumbReturnAvailable: false'],
  ['tactical grid overlay', 'data-isle-mapper-grid'],
  ['map-point grid references', 'mapPointToGridReference'],
  ['grid reference routing', 'const gridPoint = resolveGridReference(query);'],
  ['streamer-safe grid visibility', 'mapGridVisible && !streamerMode'],
  ['grid reference bridge', 'selfGridReference'],
  ['shared route parsing', 'parseSharedRouteTokens'],
  ['shared route activation', "routePlanSource = 'shared'"],
  ['tactical cursor inspector', 'isleMapperCursor'],
  ['map quick actions', 'isleMapperQuickActions'],
  ['tactical UI isolation exemption', "sibling.dataset?.isleMapperUi !== 'true'"],
  ['right-click map interaction', "map.addEventListener('contextmenu'"],
  ['quick map routing', "notify('quick-route-set')"],
  ['quick typed pin saving', "notify('quick-pin-saved')"],
  ['quick location clipboard bridge', 'isley-copy-location'],
  ['streamer tactical close', 'closeMapQuickActions();'],
  ['streamer tactical hide', 'hideCursorInspector();'],
  ['streamer tactical accessibility redaction', "tacticalUiRoot.setAttribute('aria-hidden', 'true')"],
  ['off-screen waypoint geometry', 'calculateOffscreenWaypointCue'],
  ['off-screen waypoint beacon', 'waypointEdgeCue.dataset.isleMapperWaypointCue'],
  ['animated waypoint beacon direction', 'isle-mapper-waypoint-cue-arrow'],
  ['waypoint beacon snapshot', 'waypointEdgeCueVisible'],
  ['official landmark catalog', 'getOfficialLandmarkCatalog'],
  ['landmark catalog cache', 'officialLandmarkCatalogUpdatedAt < 3000'],
  ['offline-safe landmark catalog refresh', 'getOfficialLandmarkCatalog();'],
  ['adaptive landmark label selection', 'selectLandmarkLabels'],
  ['overlap-safe landmark hiding', 'the-isle-mapper-landmark-hidden'],
  ['label-backplate-safe container selection', 'resolveLandmarkLabelContainer'],
  ['landmark label density configuration', "'landmarkLabelDensity' in options"],
  ['visible landmark bridge', 'visibleLandmarkCount'],
  ['nearest official place calculation', 'selectNearestLandmark'],
  ['nearest official place routing', 'routeToNearestPlace'],
  ['nearest official place bridge', 'nearestPlaceName'],
  ['session activity metrics', 'calculateSessionStats'],
  ['adaptive navigation ETA', 'calculateNavigationEta'],
  ['recent movement pace samples', 'movementSpeedSamples'],
  ['whole-route ETA bridge', 'navigationEtaDistance'],
  ['session activity reset', 'resetActivityStats'],
  ['fresh-pose speed sampling', 'lastMotionSample = { x: pose.x, y: pose.y, at: now }'],
  ['stationary speed timestamp gate', 'if (now - lastMotionSample.at >= 5000)'],
  ['waypoint approach intelligence', 'calculateWaypointApproach'],
  ['waypoint approach sampling', 'waypointApproachSamples.push'],
  ['waypoint closing-rate bridge', 'waypointClosingRate'],
  ['waypoint leg-progress bridge', 'waypointProgressPercent'],
  ['public terrain danger proximity roster', 'buildCommunityTerrainDangerRoster'],
  ['public terrain danger proximity gate', "terrainCommunityHazardStatus === 'ready'"],
  ['nearest danger-pin selection', 'selectNearestDangerPin'],
  ['danger proximity bridge', 'nearestDangerPinId'],
  ['saved destination alert radii', 'pinAlertRadii'],
  ['alert-zone rendering', 'isleMapperAlertZone'],
  ['alert-zone radius cycling', 'cycleSavedPinAlertRadius'],
  ['alert-zone nearest-boundary selection', 'selectNearestAlertZone'],
  ['alert-zone bridge state', 'nearestAlertZonePinId'],
  ['alert-zone backup persistence', 'alertRadius: pinAlertRadii.includes'],
  ['dynamic MU scale selection', 'chooseMapScaleBar'],
  ['map scale bridge', 'scaleBarUnits'],
  ['smart destination search', 'searchDestinations'],
  ['typo-tolerant label scoring', 'scoreMapLabel'],
  ['deduplicated place ranking', 'rankNamedPlaces'],
  ['saved destination ranking', 'rankSavedDestinations'],
  ['saved destination search routing', 'findSavedDestination(query)'],
  ['portable pin-library backup', 'buildPinLibraryBackup'],
  ['atomic pin-library import plan', 'buildPinLibraryImportPlan'],
  ['saved destination rename', 'renameSavedPin'],
  ['saved destination favorites', 'toggleSavedPinFavorite'],
  ['timed destination options', 'pinExpiryMinutes'],
  ['timed destination cycling', 'cycleSavedPinExpiry'],
  ['automatic expired-pin purge', 'purgeExpiredPins(now)'],
  ['expired active-route cleanup', 'expiredIds.has(activePinId)'],
  ['temporary marker map styling', "stroke-dasharray', '3 2'"],
  ['timed destination search context', 'expiresInMs'],
  ['timed self-position pin API', 'dropTimedPinAtSelf'],
  ['timed self-position pin notification', "notify('timed-pin-dropped')"],
  ['guarded pin-library import API', 'previewPinLibraryImport'],
  ['session route history', 'recordRecentRoute'],
  ['recent route bridge', 'recentRoutes: recentRouteRoster'],
  ['previous destination routing', 'routeBack()'],
  ['specific recent destination routing', 'routeToRecentDestination(id)'],
  ['streamer route-history purge', 'recentRoutes = [];'],
  ['private exploration storage', 'the-isle-mapper-exploration-v1'],
  ['exploration-sector normalization', 'normalizeExplorationSectors'],
  ['exploration-sector mapping', 'explorationSectorIndex'],
  ['authorized exploration sampling', 'recordExplorationSample(pose)'],
  ['exploration map rendering', 'data-isle-mapper-exploration'],
  ['exploration configuration', "'explorationEnabled' in options"],
  ['exploration bridge state', 'explorationVisitedCount'],
  ['guarded exploration clear API', 'clearExploration()'],
  ['streamer-paused exploration', 'if (!explorationEnabled || streamerMode) return false']
];
for (const [label, contract] of requiredContracts) {
  if (!body.includes(contract)) {
    throw new Error(`Embedded map controller is missing ${label}: ${contract}`);
  }
}

const requiredDocumentStartContracts = [
  ['local bundled-map host', 'LocalMapHost = "isley.local"'],
  ['local bundled-map mapping', 'SetVirtualHostNameToFolderMapping('],
  ['quarter-second local provider check', '_ = RefreshIndependentLiveDataAsync();'],
  ['ten-second provider freshness boundary', 'IsleyLiveDataProvider.FreshnessLimit'],
  ['responsive heading-up render', 'transition: transform 70ms linear !important'],
  ['asset-location freshness bump', 'window.__isley?.refreshSelfFreshness?.()'],
  ['provider snapshot freshness bump', 'setSnapshot({payload}); window.__isley?.refreshSelfFreshness?.()']
];
for (const [label, contract] of requiredDocumentStartContracts) {
  if (!source.includes(contract)) {
    throw new Error(`Document-start poll controller is missing ${label}: ${contract}`);
  }
}

const liteModeSurface = `${source}\n${xamlSource}\n${liteModeLogicSource}\n${liteModeVerifierSource}`;
const requiredLiteModeContracts = [
  ['persistent Lite Mode setting', 'public bool LiteModeEnabled'],
  ['saved Lite Mode preference', 'LiteModeEnabled = _liteModeEnabled'],
  ['restored Lite Mode preference', '_liteModeEnabled = settings.LiteModeEnabled'],
  ['plain-language toggle', 'x:Name="LiteModeButton"'],
  ['plain-language performance status', 'x:Name="LiteModeStatusText"'],
  ['keyboard-discoverable command', 'new("lite-mode", "Toggle Lite Mode"'],
  ['native dispatcher profile', 'ApplyLiteModeProfile()'],
  ['one-second survival refresh', 'SurvivalRefreshMilliseconds: 1000'],
  ['responsive Play Focus refresh', 'PlayFocusMilliseconds: 750'],
  ['full-speed voice status', 'VoiceStatusMilliseconds: 1000'],
  ['shadow removal', 'Shell.Effect = profile.UseShellShadow ? ShellShadowEffect : null'],
  ['continuous-animation removal', 'shouldPulse = shouldPulse && !_liteModeEnabled'],
  ['embedded map bridge', 'liteMode = _liteModeEnabled'],
  ['one-second authorized marker target', 'nextLiteMode ? 1000 : 500'],
  ['one-second controller refresh', 'liteMode ? 1000 : 250'],
  ['duplicate timer-work suppression', "liteMode && reason === 'timer' && tickAt - lastControllerWorkAt < 850"],
  ['preserved server backoff', 'const pollWasBackedOff = Number(pagePollControl.delayMs) >= 5000'],
  ['reduced embedded effects', 'html[data-isley-lite="true"]'],
  ['core-feature preservation verifier', 'Lite Mode verification passed']
];
for (const [label, contract] of requiredLiteModeContracts) {
  if (!liteModeSurface.includes(contract)) {
    throw new Error(`Lite Mode is missing ${label}: ${contract}`);
  }
}

const onboardingTutorialSurface = `${source}\n${xamlSource}\n${onboardingTutorialLogicSource}\n${onboardingTutorialVerifierSource}`;
const requiredOnboardingTutorialContracts = [
  ['five-step quick start', 'steps.Count == 5'],
  ['independent Live Map tutorial version', 'OnboardingTutorialLogic.CurrentVersion == 6'],
  ['first-run version gate', 'OnboardingTutorialLogic.ShouldShow(_onboardingTutorialVersionCompleted)'],
  ['persistent completion', 'OnboardingTutorialVersionCompleted = _onboardingTutorialVersionCompleted'],
  ['modal tutorial layer', 'x:Name="OnboardingTutorialLayer"'],
  ['visible progress', 'x:Name="OnboardingProgressBar"'],
  ['skip control', 'x:Name="OnboardingSkipButton"'],
  ['back control', 'x:Name="OnboardingBackButton"'],
  ['next control', 'x:Name="OnboardingNextButton"'],
  ['contained tab order', 'KeyboardNavigation.TabNavigation="Cycle"'],
  ['keyboard escape', 'CloseOnboardingTutorial(completed: false)'],
  ['keyboard arrows', 'key == Key.Right'],
  ['interaction recovery', 'SetClickThrough(false)'],
  ['App replay', 'x:Name="OnboardingReplayButton"'],
  ['Quick Commands replay', 'new("tutorial", "Replay Isley quick start"'],
  ['interactive server choice', 'x:Name="OnboardingServerChoicePanel"'],
  ['Live Map choice', 'x:Name="OnboardingServerLiveMapButton"'],
  ['visible independence disclosure', 'AutomationProperties.Name="Onboarding independent map disclosure"'],
  ['explicit independence guidance', "Isley's map stays independent"],
  ['Official choice', 'x:Name="OnboardingServerOfficialButton"'],
  ['Any Server choice', 'x:Name="OnboardingServerAnyButton"'],
  ['server choice handoff', 'ServerSessionProfileButton_Click'],
  ['private and unlisted guidance', 'private, passworded, or unlisted'],
  ['truthful position boundary', 'never invents a position'],
  ['truthful route boundary', 'Verify cliffs'],
  ['deterministic verifier', 'Onboarding tutorial verification passed']
];
for (const [label, contract] of requiredOnboardingTutorialContracts) {
  if (!onboardingTutorialSurface.includes(contract)) {
    throw new Error(`Onboarding tutorial is missing ${label}: ${contract}`);
  }
}

const supportSurface = `${source}\n${xamlSource}\n${overlayLinksSource}`;
const requiredSupportContracts = [
  ['exact creator profile', 'internal const string KoFi = "https://ko-fi.com/theoneboundinink"'],
  ['visible support action', 'Content="Support Isley on Ko-fi"'],
  ['accessible support action', 'AutomationProperties.Name="Support Isley on Ko-fi"'],
  ['browser disclosure', 'Optional support · Opens in your browser'],
  ['fixed-link click handler', 'OpenExternalUri(OverlayLinks.KoFi)']
];
for (const [label, contract] of requiredSupportContracts) {
  if (!supportSurface.includes(contract)) {
    throw new Error(`Ko-fi support surface is missing ${label}: ${contract}`);
  }
}
console.log(`Ko-fi support contracts: PASS (${requiredSupportContracts.length} checks)`);

const requiredUiContracts = [
  ['turn-right guidance', 'TURN RIGHT'],
  ['turn-left guidance', 'TURN LEFT'],
  ['arrival guidance', 'ARRIVED'],
  ['arrival alert distances', '_arrivalAlertDistances = [0, 5, 10, 20]'],
  ['recovery controls', 'UpdateRecoveryControls()'],
  ['persisted recovery preference', 'RememberLastPosition'],
  ['compact ruler readout', 'MeasurementPanel'],
  ['ruler interaction control', 'MeasureButton'],
  ['ruler clipboard control', 'CopyMeasurementButton'],
  ['route planner status', 'RoutePlanStatusText'],
  ['route planner primary action', 'RoutePlanButton'],
  ['route undo control', 'UndoRouteStopButton'],
  ['route skip control', 'SkipRouteStopButton'],
  ['route sharing control', 'CopyRoutePlanButton'],
  ['one-click shared-route paste control', 'PasteRoutePlanButton'],
  ['one-shot shared-route paste handler', 'PasteSharedRouteFromClipboardAsync'],
  ['bounded shared-route clipboard input', 'MaximumSharedRouteClipboardCharacters = 1600'],
  ['explicit Unicode clipboard read', 'Clipboard.ContainsText(TextDataFormat.UnicodeText)'],
  ['safe shared-route script serialization', 'JsonSerializer.Serialize(clipboardText)'],
  ['clipboard text release after handoff', 'clipboardText = string.Empty'],
  ['searchable shared-route paste command', 'Paste shared route'],
  ['road/trail course status', 'TerrainCourseStatusText'],
  ['road/trail course action', 'TerrainCourseButton'],
  ['road/trail source attribution', 'TerrainCourseSourceButton'],
  ['validated terrain source load', 'TerrainRoadNetworkClient.FetchAsync'],
  ['terrain mapper injection', 'loadTerrainRoadNetwork({payload})'],
  ['terrain course command action', 'Plot road/trail course'],
  ['route evidence display', 'TerrainRouteConfidencePanel'],
  ['route evidence preference', 'TerrainRouteConfidenceVisible'],
  ['blocked-passage action', 'TerrainBlockedPassageButton'],
  ['blocked-passage command', 'Report blocked passage'],
  ['Terrain Probe action', 'TerrainProbeSaveAvoidanceButton'],
  ['Terrain Probe command', 'Save measured slope avoidance'],
  ['compact route progress action', 'Next stop'],
  ['route completion guidance', 'ROUTE COMPLETE'],
  ['breadcrumb return control', 'BreadcrumbReturnButton'],
  ['breadcrumb return action', 'BreadcrumbReturnButton_Click'],
  ['breadcrumb completion guidance', 'BACKTRACK COMPLETE'],
  ['session trail status', 'BreadcrumbTrailStatusText'],
  ['session trail visibility control', 'BreadcrumbTrailToggleButton'],
  ['guarded session trail clear control', 'ClearBreadcrumbTrailButton'],
  ['persisted session trail preference', 'BreadcrumbTrailVisible'],
  ['native session trail configuration source', 'breadcrumbTrailVisible = _breadcrumbTrailVisible'],
  ['session trail control-state model', 'UpdateBreadcrumbTrailControls'],
  ['session trail command action', 'Open session trail'],
  ['learned-passage status', 'LearnedPassageStatusText'],
  ['learned-passage save control', 'SaveLearnedPassageButton'],
  ['learned-passage routing control', 'LearnedPassageRoutingButton'],
  ['learned-passage display control', 'LearnedPassageVisibilityButton'],
  ['guarded learned-passage clear control', 'ClearLearnedPassagesButton'],
  ['persisted learned-routing preference', 'LearnedPassageRoutingEnabled'],
  ['persisted learned-display preference', 'LearnedPassageVisible'],
  ['focus mode workspace', 'FocusModeStatusText'],
  ['balanced focus mode', 'BalancedFocusModeButton'],
  ['travel focus mode', 'TravelFocusModeButton'],
  ['survival focus mode', 'SurvivalFocusModeButton'],
  ['pack focus mode', 'PackFocusModeButton'],
  ['combat focus mode', 'CombatFocusModeButton'],
  ['nest focus mode', 'NestFocusModeButton'],
  ['focus mode restore action', 'RestoreFocusModeButton'],
  ['persistent focus mode restore snapshot', 'FocusModeRestoreSnapshot'],
  ['persistent active focus mode', 'ActiveFocusModeId'],
  ['focus mode detection', 'DetectActiveFocusMode'],
  ['focus mode display reconciliation', 'FocusDisplaySettingsMatch'],
  ['focus mode reconnect resync', 'ReapplyActiveFocusModeLayersAsync'],
  ['focus mode reconnect hook', 'await ReapplyActiveFocusModeLayersAsync();'],
  ['focus mode selection persistence', '_activeFocusModeId = modeId'],
  ['focus mode command action', 'Open focus modes'],
  ['direct combat focus command', 'Apply Combat focus'],
  ['direct nest focus command', 'Apply Nest focus'],
  ['map grid control', 'MapGridButton'],
  ['map grid interaction', 'MapGridButton_Click'],
  ['persisted map grid preference', 'MapGridVisible'],
  ['four range-ring modes', '_rangeRingModes = [(0, 0), (10, 25), (25, 50), (50, 100)]'],
  ['range-ring control model', 'UpdateRangeRingControl()'],
  ['persisted range-ring mode', 'RangeRingModeIndex'],
  ['legacy range-ring migration', 'settings.RangeRingsVisible ? 2 : 0'],
  ['range-ring command action', 'Cycle range rings'],
  ['landmark label density control', 'LandmarkLabelDensityButton'],
  ['landmark label density status', 'LandmarkLabelDensityStatusText'],
  ['persisted landmark label density', 'LandmarkLabelDensityIndex'],
  ['landmark density focus-mode reconciliation', 'definition.LandmarkLabelDensityIndex'],
  ['landmark density command action', 'Cycle place label detail'],
  ['compact live grid readout', 'GRID {_currentGridReference}'],
  ['shared-route dispatch guard', '!isSharedRoute'],
  ['destination Asset Location routing', 'TryParseDestinationWorldPoint'],
  ['paste coords route action', 'PasteDestinationCoordinatesButton'],
  ['clipboard coordinate route command', 'route-clipboard-coords'],
  ['validated location clipboard handling', 'clipboardText.StartsWith("Isley location | "'],
  ['compact nearest-place readout', 'NearestPlacePanel'],
  ['nearest-place visibility control', 'NearestPlaceButton'],
  ['nearest-place route control', 'RouteNearestPlaceButton'],
  ['persisted nearest-place preference', 'NearestPlaceVisible'],
  ['compact session activity readout', 'SessionStatsText'],
  ['adaptive ETA source guidance', 'BuildNavigationEtaText'],
  ['whole-route ETA presentation', 'ROUTE ETA'],
  ['moving-away route warning', 'MOVING AWAY'],
  ['route approach telemetry', 'WaypointApproachText'],
  ['animated leg progress', 'WaypointProgressTransform'],
  ['copyable session summary', 'CopySessionStatsButton'],
  ['resettable session activity', 'ResetSessionStatsButton'],
  ['danger proximity warning', 'DangerAlertBorder'],
  ['public terrain danger warning heading', 'TERRAIN DANGER NEARBY'],
  ['public terrain danger alert identity', 'community-terrain-hazard-'],
  ['configurable danger radius', 'DangerAlertButton'],
  ['persisted danger radius', 'DangerAlertIndex'],
  ['selected destination alert-zone control', 'PinAlertRadiusButton'],
  ['alert-zone entry guidance', 'ALERT ZONE ENTERED'],
  ['alert-zone command action', 'Open alert zones'],
  ['five-second selected-marker removal window', 'Select remove again within 5 seconds'],
  ['five-second marker-library clear window', 'Select again within 5 seconds to remove every saved marker'],
  ['compact compass ribbon', 'CompassRibbon'],
  ['relative active-course marker', 'CompassCourseMarker'],
  ['authenticated MU scale bar', 'MapScalePanel'],
  ['animated course movement', 'TranslateTransform.XProperty'],
  ['live place suggestions', 'PlaceSuggestionsPanel'],
  ['destination keyboard routing', 'DestinationInputBox_KeyDown'],
  ['debounced destination search', 'Task.Delay(170)'],
  ['selected destination editor', 'SelectedPinEditorPanel'],
  ['selected destination favorite control', 'FavoriteSelectedPinButton'],
  ['selected destination expiry control', 'PinExpiryButton'],
  ['selected expiry countdown', 'EXPIRES IN'],
  ['global recenter shortcut', 'HotkeyBindingLogic.RecenterId'],
  ['global timed-danger shortcut', 'HotkeyBindingLogic.TimedDangerId'],
  ['gameplay shortcut feedback', 'HotkeyToastBorder'],
  ['shortcut feedback animation', 'ShowHotkeyToastAsync'],
  ['shortcut registration cleanup', 'UnregisterAllHotkeys()'],
  ['responsive shortcut health', 'ResponsiveLayoutLogic.FooterHotkeyStatus'],
  ['mixed saved-pin search presentation', 'MY PIN'],
  ['portable destination backup control', 'CopyPinLibraryButton'],
  ['guarded destination restore control', 'ImportPinLibraryButton'],
  ['pin import confirmation window', 'Task.Delay(5000)'],
  ['previous destination control', 'PreviousDestinationButton'],
  ['previous destination action', 'PreviousDestinationButton_Click'],
  ['recent destinations workspace', 'RecentDestinationsPanel'],
  ['recent destination action', 'RecentDestinationButton_Click'],
  ['compact survival timer HUD', 'SurvivalTimerHudPanel'],
  ['survival timer workspace', 'SurvivalTimerListPanel'],
  ['four timer capacity guard', '_survivalTimers.Count >= 4'],
  ['timer preset action', 'TimerPresetButton_Click'],
  ['timer input one-click replacement', 'TimerInputBox_PreviewMouseLeftButtonDown'],
  ['custom timer action', 'StartCustomSurvivalTimer'],
  ['timer pause resume restart action', 'SurvivalTimerButton_Click'],
  ['timer removal action', 'RemoveSurvivalTimerButton_Click'],
  ['timer clear confirmation', '_clearTimersConfirmationPending'],
  ['timer completion feedback', 'AnnounceTimerCompletionAsync'],
  ['timer progress presentation', '156 * progress'],
  ['timer completion sound preference', 'TimerSoundEnabled = _timerSoundEnabled'],
  ['persistent timer restoration', 'RestoreSurvivalTimers(settings.SurvivalTimers)'],
  ['persistent timer snapshot', 'SurvivalTimers = _survivalTimers.Take(4)'],
  ['global quick timer shortcut', 'HotkeyBindingLogic.QuickTimerId'],
  ['quick timer registration cleanup', 'UnregisterAllHotkeys()'],
  ['quick timer shortcut action', 'StartQuickTimerFromHotkeyAsync'],
  ['global command palette shortcut', 'HotkeyBindingLogic.CommandPaletteId'],
  ['command palette registration cleanup', 'UnregisterAllHotkeys()'],
  ['command palette shortcut action', 'ToggleCommandPalette()'],
  ['command palette surface', 'CommandPaletteBorder'],
  ['command palette action catalog', 'CommandPaletteActions'],
  ['command palette search ranking', 'ScoreCommandPaletteAction'],
  ['command palette keyboard navigation', 'CommandPaletteInputBox_PreviewKeyDown'],
  ['command palette action execution', 'ExecuteCommandPaletteActionAsync'],
  ['command palette favorite action', 'CommandPaletteFavoriteButton_Click'],
  ['command palette recent action', 'RecordRecentCommandAction'],
  ['command palette clear recent action', 'CommandPaletteClearRecentButton_Click'],
  ['command palette favorite persistence', 'CommandFavoriteActionIds = _commandFavoriteActionIds.ToList()'],
  ['command palette recent persistence', 'CommandRecentActionIds = _commandRecentActionIds.ToList()'],
  ['command palette accessible star control', 'AutomationProperties.SetName'],
  ['command palette click-through recovery', 'SetClickThrough(false)'],
  ['sticky map section navigation', 'MapSectionJumpBar'],
  ['timer section direct jump', 'SessionActivitySectionAnchor'],
  ['route section direct jump', 'RouteSectionAnchor'],
  ['recovery section direct jump', 'RecoverySectionAnchor'],
  ['player section direct jump', 'PlayersSectionAnchor'],
  ['scroll-aware section state', 'ToolsScrollViewer_ScrollChanged'],
  ['drawer workspace scroll reset', 'ToolsScrollViewer.ScrollToTop()'],
  ['global death-marker shortcut', 'HotkeyBindingLogic.DeathMarkerId'],
  ['death-marker registration cleanup', 'UnregisterAllHotkeys()'],
  ['death-marker shortcut action', 'DropDeathMarkerAsync'],
  ['focused-window death shortcut fallback', 'Window_PreviewKeyDown'],
  ['dynamic focused shortcut fallback', 'TryHandleFocusedHotkey'],
  ['death shortcut duplicate guard', '_lastDeathMarkerRequestTick'],
  ['persistent death shortcut feedback', 'SAVING BODY MARKER...'],
  ['death-marker recovery control', 'DeathMarkerButton'],
  ['death-marker mouse input hardening', 'DeathMarkerButton_PreviewMouseLeftButtonDown'],
  ['live-loss recovery prompt', 'RecoveryPromptBorder'],
  ['recovery prompt confirmation gate', 'ShouldOfferRecoveryPrompt'],
  ['confirmed-loss debounce', 'ConfirmRecoveryPromptAsync'],
  ['confirmed-loss delay', 'Task.Delay(1400)'],
  ['reacquisition cancellation revision', '_recoveryPromptRevision++'],
  ['explicit death confirmation copy', 'save only if this was a death'],
  ['recovery prompt save action', 'RecoveryPromptSaveButton_Click'],
  ['recovery prompt dismissal action', 'RecoveryPromptDismissButton_Click'],
  ['observable death-marker result', 'Task<bool> DropDeathMarkerAsync'],
  ['death-marker attempt telemetry', '_deathMarkerAttemptCount++'],
  ['exploration workspace', 'ExplorationSectionAnchor'],
  ['exploration coverage progress', 'ExplorationProgressTransform'],
  ['exploration tracking control', 'ExplorationToggleButton'],
  ['guarded exploration clear control', 'ClearExplorationButton'],
  ['persisted exploration preference', 'ExplorationEnabled'],
  ['native exploration configuration source', 'explorationEnabled = _explorationEnabled'],
  ['exploration control-state model', 'UpdateExplorationControls'],
  ['exploration command action', 'Open exploration map'],
  ['survival assistant workspace', 'SurvivalAssistantSectionAnchor'],
  ['progressive survival issue picker', 'SurvivalAssistantPickerPanel'],
  ['active survival priority card', 'SurvivalAssistantActivePanel'],
  ['conditional survival alert HUD', 'SurvivalIncidentHudBorder'],
  ['survival recovery action', 'SurvivalRecoveryButton_Click'],
  ['persistent survival condition', 'SurvivalIncidentId = _survivalIncidentId'],
  ['survival assistant direct jump', '"survival-assistant" => SurvivalAssistantSectionAnchor'],
  ['primary preference storage', 'PrimarySettingsPath'],
  ['portable preference fallback', 'PortableSettingsPath'],
  ['portable-mode marker', 'Isley.portable'],
  ['portable-mode private data root', 'PortableDataDirectory'],
  ['portable-mode WebView isolation', 'Path.Combine(PortableDataDirectory, "WebView2")'],
  ['newest preference candidate selection', '.OrderByDescending(File.GetLastWriteTimeUtc)'],
  ['valid preference fallback loop', 'foreach (var settingsPath in settingsPaths)'],
  ['atomic preference replacement', 'File.Move(temporaryPath, candidate, overwrite: true)'],
  ['preference write read-back', 'File.ReadAllText(candidate), serializedSettings'],
  ['non-fatal preference save guard', 'SaveSettingsCore();'],
  ['visible preference save timestamp', 'Last saved {savedAt:HH:mm:ss}'],
  ['preference storage health', 'UpdateSettingsStorageStatus()'],
  ['visible preference failure state', 'PREFERENCES NOT SAVED'],
  ['native tactical hover dismissal', 'NativeChrome_MouseEnter'],
  ['embedded tactical dismissal API', 'dismissTacticalUi()'],
  ['expanded marker selector', 'WaterPinTypeButton']
];
for (const [label, contract] of requiredUiContracts) {
  if (!source.includes(contract)) {
    throw new Error(`Desktop overlay is missing ${label}: ${contract}`);
  }
}

const requiredXamlUiContracts = [
  ['typed course legend', 'TerrainRouteLegendPanel'],
  ['accessible evidence control', 'Toggle typed route evidence'],
  ['combined saved and public danger alert copy', 'saved Danger markers and enabled public terrain-danger points'],
  ['solid road legend', 'ROAD SOLID'],
  ['dashed trail legend', 'TRAIL DASH'],
  ['unknown connector legend', 'UNKNOWN DOT']
];
for (const [label, contract] of requiredXamlUiContracts) {
  if (!xamlSource.includes(contract)) {
    throw new Error(`Desktop XAML is missing ${label}: ${contract}`);
  }
}

const identitySurface = `${source}\n${xamlSource}\n${projectSource}\n${manifestSource}\n${appXamlSource}\n${logoXamlSource}\n${dockXamlSource}`;
const requiredIdentityContracts = [
  ['Isley assembly identity', '<AssemblyName>Isley</AssemblyName>'],
  ['Isley product identity', '<Product>Isley</Product>'],
  ['Isley manifest identity', 'name="Isley.app"'],
  ['Isley window title', 'Title="Isley - Live Minimap"'],
  ['Isley header identity', 'Text="ISLEY"'],
  ['red Triceratops battlecry accessible identity', 'Isley red Triceratops battlecry logo'],
  ['shared red Triceratops raster mark', 'isley-triceratops-app-teeth-clean.png'],
  ['Windows executable icon', '<ApplicationIcon>Assets\\Brand\\Isley-teeth-clean.ico</ApplicationIcon>'],
  ['dark logo presentation surface', 'BrandSurfaceBrush'],
  ['red logo presentation border', 'BrandBorderBrush'],
  ['Isley namespace', 'namespace Isley;'],
  ['Isley bridge', 'window.__isley = api'],
  ['legacy bridge alias', 'window.__theIsleMapper = api'],
  ['bundled map virtual host', 'SetVirtualHostNameToFolderMapping('],
  ['new Isley profile root', 'Path.Combine(localAppData, "Isley", "WebView2")'],
  ['legacy settings migration source', 'LegacyMapperSettingsPath'],
  ['new Isley settings root', 'Path.Combine(localAppData, "Isley", "settings.json")'],
  ['new Isley portable marker', 'Isley.portable'],
  ['legacy portable marker compatibility', 'TheIsleMapper.portable']
];
for (const [label, contract] of requiredIdentityContracts) {
  if (!identitySurface.includes(contract)) {
    throw new Error(`Isley identity migration is missing ${label}: ${contract}`);
  }
}

const minimizedVitalsSurface = `${source}\n${dockXamlSource}\n${dockCodeSource}\n${dockVitalsLogicSource}`;
const requiredMinimizedVitalsContracts = [
  ['minimized vital action', 'DockVitalsButton'],
  ['minimized vital values', 'DockVitalValuesText'],
  ['truthful live source label', 'LIVE {PlayerSnapshotLogic.FormatAge'],
  ['manual stale fallback label', 'MANUAL / LIVE STALE'],
  ['stale vital refusal label', 'STALE / REPORT'],
  ['streamer and user toggle boundary', '!requestedVisible || streamerMode'],
  ['stale visual drain', 'presentation.Fresh ? 1 : 0.66'],
  ['critical transition pulse', 'RepeatBehavior(2)'],
  ['direct Core Vitals access', 'OpenMapToolsAtSection("core-vitals")'],
  ['live dock refresh', '_dockWindow?.UpdateVitals'],
  ['compact hidden width with persistent lock control', 'presentation.Visible ? 362 : 264']
];
for (const [label, contract] of requiredMinimizedVitalsContracts) {
  if (!minimizedVitalsSurface.includes(contract)) {
    throw new Error(`Minimized Core Vitals is missing ${label}: ${contract}`);
  }
}

const aimCalibrationSurface = `${source}\n${xamlSource}\n${nativeMethodsSource}\n${aimCalibrationLogicSource}\n${aimViewportLogicSource}\n${aimGuideWindowSource}\n${aimGuideWindowXamlSource}\n${aimCalibrationVerifierSource}`;
const requiredAimCalibrationContracts = [
  ['game client rectangle source', 'GetClientRect'],
  ['client-to-screen alignment source', 'ClientToScreen'],
  ['bounded viewport resolver', 'TryResolveClientArea'],
  ['game-only client-area preference', 'foreground == PlayFocusForeground.Game'],
  ['foreground viewport alignment', 'AlignToForegroundViewport'],
  ['exact game-client status', 'VIEWPORT · GAME CLIENT ALIGNED'],
  ['disclosed monitor fallback', 'VIEWPORT · MONITOR FALLBACK · VERIFY ALIGNMENT'],
  ['accessible viewport status', 'AimGuideViewportStatusText'],
  ['species and attack profile key', 'SpeciesId'],
  ['growth-aware profile key', 'GrowthIndex'],
  ['camera-aware profile key', 'CameraIndex'],
  ['independent depth geometry', 'DepthScale'],
  ['horizontal guide placement', 'HorizontalOffset'],
  ['user-reported repeat evidence', 'ConfirmedMatches'],
  ['inside-miss contradiction evidence', 'InsideMisses'],
  ['outside-hit contradiction evidence', 'OutsideHits'],
  ['bounded evidence advisor', 'EvaluateEvidence'],
  ['non-automatic narrow advice', 'Repeated inside misses suggest a smaller guide'],
  ['non-automatic widen advice', 'Repeated outside hits suggest a larger guide'],
  ['mixed-result restraint', 'Both error directions were reported'],
  ['honest confidence language', 'ConfidenceLabel'],
  ['growth context control', 'AimGuideGrowthButton'],
  ['live growth sync control', 'AimGuideGrowthSyncButton'],
  ['five-stage growth mapping', 'GrowthIndexForPercent'],
  ['legacy-safe growth order', 'GrowthCycle = [2, 0, 3, 1, 4]'],
  ['exact live growth evidence', 'LIVE GROWTH {growthContext.Percent}%'],
  ['persistent growth sync', 'AimGuideGrowthSyncEnabled = _aimGuideGrowthSyncEnabled'],
  ['searchable growth sync action', 'new("aim-growth-sync"'],
  ['camera context control', 'AimGuideCameraButton'],
  ['width calibration controls', 'WIDTH -'],
  ['depth calibration controls', 'DEPTH -'],
  ['horizontal calibration controls', 'AimGuideHorizontalOffsetButton_Click'],
  ['area visibility toggle', 'AimGuideAreaButton'],
  ['center visibility toggle', 'AimGuideCenterButton'],
  ['uncertainty visibility toggle', 'AimGuideUncertaintyButton'],
  ['profile label visibility toggle', 'AimGuideLabelButton'],
  ['user-observed match action', 'AimGuideConfirmMatchButton_Click'],
  ['user-observed contradiction actions', 'AimGuideEvidenceButton_Click'],
  ['geometry-preserving evidence clear', 'AimGuideClearEvidenceButton_Click'],
  ['inside-miss UI', 'AimGuideInsideMissButton'],
  ['outside-hit UI', 'AimGuideOutsideHitButton'],
  ['evidence advice UI', 'AimGuideEvidenceStatusText'],
  ['separate uncertainty geometry', 'OuterUncertaintyArea'],
  ['non-invasive window transparency', 'WsExTransparent'],
  ['non-activating overlay window', 'WsExNoActivate'],
  ['explicit telemetry boundary', 'not live game hitbox data'],
  ['full-context deterministic verifier', 'exact game-client viewport, species, attack, five-stage live growth, camera, geometry, contradiction-aware evidence, reset, and bounds']
];
for (const [label, contract] of requiredAimCalibrationContracts) {
  if (!aimCalibrationSurface.includes(contract)) {
    throw new Error(`Aim calibration is missing ${label}: ${contract}`);
  }
}
for (const forbidden of ['ReadProcessMemory', 'WriteProcessMemory', 'CreateRemoteThread']) {
  if (aimCalibrationSurface.includes(forbidden)) {
    throw new Error(`Aim calibration must remain external and cannot use ${forbidden}`);
  }
}

const voiceSurface = `${source}\n${xamlSource}\n${nativeMethodsSource}\n${overlayLinksSource}\n${voiceIntegrationLogicSource}\n${voiceInviteLogicSource}\n${voiceRouteOfferLogicSource}\n${voiceServerReadinessSource}\n${voiceClientSource}\n${voiceCryptoSource}\n${voiceServerSource}\n${voiceIntegrationVerifierSource}\n${voiceAudioOutputVerifierSource}`;
const requiredVoiceContracts = [
  ['voice workspace', 'VoiceToolsPanel'],
  ['compact voice HUD', 'VoiceHudBorder'],
  ['voice client readiness row', 'VoiceClientStateText'],
  ['PTT observer readiness row', 'VoicePttObserverStateText'],
  ['voice focus readiness row', 'VoiceFocusStateText'],
  ['voice command action', 'Open Isley Voice'],
  ['persistent voice enabled setting', 'VoiceEnabled = _voiceEnabled'],
  ['persistent voice auto-open setting', 'VoiceAutoOpen = _voiceAutoOpen'],
  ['persistent voice HUD setting', 'VoiceHudVisible = _voiceHudVisible'],
  ['persistent PTT key setting', 'VoicePttKeyIndex = _voicePttKeyIndex'],
  ['persistent proximity preference', 'VoiceProximityEnabled = _voiceProximityEnabled'],
  ['persistent range preference', 'VoiceRangeIndex = _voiceRangeIndex'],
  ['low-level keyboard observation', 'SetWindowsHookEx'],
  ['keyboard hook cleanup', 'UnhookWindowsHookEx'],
  ['non-blocking keyboard chain', 'CallNextHookEx'],
  ['keyboard observer failure state', 'PTT OBSERVER UNAVAILABLE'],
  ['foreground transmit boundary', 'PlayFocusForeground.Game or PlayFocusForeground.Mapper'],
  ['built-in PTT intent', 'HasPttIntent'],
  ['rapid PTT edge reconciliation', 'ResolveObservedKeyState'],
  ['built-in voice client', 'VoiceWebView'],
  ['built-in voice connect action', 'Connect built-in voice'],
  ['pre-microphone readiness action', 'VoiceServerCheckButton'],
  ['pre-microphone readiness status', 'VoiceServerCheckStatusText'],
  ['automatic readiness gate', 'CheckVoiceServerReadinessAsync'],
  ['fail-closed microphone copy', 'VOICE SERVER NOT READY · MICROPHONE KEPT OFF'],
  ['redirect-safe readiness client', 'AllowAutoRedirect = false'],
  ['bounded readiness payload', 'MaximumPayloadBytes = 32 * 1024'],
  ['versioned server readiness endpoint', 'app.MapGet("/ready"'],
  ['fail-closed server configuration', 'ValidateOnStart'],
  ['global voice capacity', 'MaxTotalPeers'],
  ['privacy-declaring readiness', 'RoomIdsExposed'],
  ['room-key signaling encryption', 'deriveSignalKey'],
  ['AES-GCM signaling envelope', "name: 'AES-GCM'"],
  ['random signaling IV', 'getRandomValues(new Uint8Array(12))'],
  ['encrypted signaling send', 'sendEncryptedSignal'],
  ['encrypted signaling receive', 'handleEncryptedSignal'],
  ['serialized signaling message chain', 'messageChain = messageChain'],
  ['signaling roster peer count', 'signalingPeers'],
  ['safe ICE candidate absorption', 'addIceCandidateSafely'],
  ['signaling failure disconnect', "disconnect('SIGNALING MESSAGE FAILED')"],
  ['peer-only display-name exchange', "type: 'profile', name: displayName"],
  ['server plaintext-signal refusal', 'root.TryGetProperty("data", out _)'],
  ['server display-name privacy declaration', 'DisplayNamesReceived'],
  ['private room key', 'VoiceRoomKeyInputBox'],
  ['versioned private-room invite', 'ISLEY-VOICE/1'],
  ['copy complete voice invite', 'COPY INVITE'],
  ['paste and validate voice invite', 'VoiceJoinInviteButton'],
  ['explicit microphone-off join state', 'MICROPHONE STILL OFF'],
  ['bounded invite clipboard input', 'MaximumInviteCharacters = 512'],
  ['legacy room-key join compatibility', 'LegacyKeyOnly'],
  ['voice invite Quick Command', 'Paste Isley Voice invite'],
  ['peer route share action', 'VoiceShareRouteButton'],
  ['explicit route offer consent', 'VoiceRouteOfferAcceptButton'],
  ['bounded route offer protocol', 'MaximumRouteCharacters = 1600'],
  ['expiring route offer', 'OfferLifetime = TimeSpan.FromMinutes(2)'],
  ['route offer Quick Command', 'Share route to voice room'],
  ['peer route transport', "command.type === 'send-route-offer'"],
  ['receiver route staging', "post('voice-route-offer'"],
  ['no automatic navigation copy', 'cannot auto-start navigation'],
  ['session microphone selector', 'VoiceInputDeviceComboBox'],
  ['session speaker selector', 'VoiceOutputDeviceComboBox'],
  ['speaker output enumeration', "device.kind === 'audiooutput'"],
  ['live peer output routing', 'await audio.setSinkId(normalized)'],
  ['speaker switch rollback', 'await applyOutputDevice(audio, previousDeviceId)'],
  ['speaker output privacy verifier', 'audio hardware identity leaked to the signaling server'],
  ['local-only microphone meter', 'VoiceMicLevelBar'],
  ['microphone meter preference', 'VoiceMicMeterEnabled = _voiceMicMeterEnabled'],
  ['microphone analyser', 'createAnalyser()'],
  ['toggleable voice quality row', 'VoiceQualityStateText'],
  ['voice quality preference', 'VoiceQualityMonitorEnabled = _voiceQualityMonitorEnabled'],
  ['aggregate WebRTC quality sampling', "post('voice-quality'"],
  ['round-trip quality evidence', 'currentRoundTripTime'],
  ['jitter quality evidence', 'report.jitter'],
  ['packet-loss quality evidence', 'report.packetsLost'],
  ['bounded quality cadence', 'qualityTimer = setInterval'],
  ['coarse quality presentation', 'PresentQuality'],
  ['voice quality Quick Command', 'Open Voice Quality'],
  ['microphone time-domain sampling', 'getByteTimeDomainData'],
  ['microphone meter cleanup', 'stopMicMeter'],
  ['microphone meter privacy copy', 'no playback, recording, room, or server data'],
  ['time-bounded microphone permission', 'ClearVoiceMicrophonePermissionArmAsync'],
  ['local participant controls', 'VoiceParticipantListPanel'],
  ['optional TURN relay', 'VoiceTurnRelayButton'],
  ['honest active PTT copy', 'PTT LIVE · ISLEY VOICE'],
  ['streamer voice redaction', 'hudEnabled && !streamerMode'],
  ['deterministic voice verifier', 'Built-in voice integration passed']
];
for (const [label, contract] of requiredVoiceContracts) {
  if (!voiceSurface.includes(contract)) {
    throw new Error(`Voice integration is missing ${label}: ${contract}`);
  }
}
const voiceCryptoVerification = spawnSync(
  process.execPath,
  [path.join(__dirname, 'verify-voice-crypto.cjs')],
  { encoding: 'utf8' });
if (voiceCryptoVerification.status !== 0) {
  throw new Error(
    `Voice crypto verification failed: ${voiceCryptoVerification.stderr || voiceCryptoVerification.stdout}`);
}
process.stdout.write(voiceCryptoVerification.stdout);
const voiceAudioOutputVerification = spawnSync(
  process.execPath,
  [path.join(__dirname, 'verify-voice-audio-output.cjs')],
  { encoding: 'utf8' });
if (voiceAudioOutputVerification.status !== 0) {
  throw new Error(
    `Voice audio-output verification failed: ${voiceAudioOutputVerification.stderr || voiceAudioOutputVerification.stdout}`);
}
process.stdout.write(voiceAudioOutputVerification.stdout);

const steamFriendSurface = `${source}\n${xamlSource}\n${steamFriendLogicSource}\n${steamFriendVerifierSource}`;
const requiredSteamFriendContracts = [
  ['Steam friend workspace anchor', 'SteamFriendsSectionAnchor'],
  ['Steam profile input', 'SteamFriendProfileInputBox'],
  ['authorized map-name input', 'SteamFriendMapNameInputBox'],
  ['authorized live-friend picker', 'SteamFriendLiveFriendPicker'],
  ['picker exact-name handoff', 'SteamFriendMapNameInputBox.Text = liveName'],
  ['explicit add and track action', 'ADD + TRACK'],
  ['primary action arms auto-follow', 'SaveSteamFriendWatchAsync(openSteamAdd: true, armAutoFollow: true)'],
  ['new watch becomes auto-follow target', '_autoFollowSteamFriendWatchId = entry.Id'],
  ['watch-only action', 'WATCH ONLY'],
  ['persisted watchlist', 'SteamFriendWatchlist = _steamFriendWatchlist.Select'],
  ['persisted selected watch', 'SelectedSteamFriendWatchId = _selectedSteamFriendWatchId'],
  ['persisted auto-follow watch', 'AutoFollowSteamFriendWatchId = _autoFollowSteamFriendWatchId'],
  ['auto-follow control', 'AutoTrackSteamFriendButton'],
  ['auto-follow state machine', 'EvaluateAutoFollow'],
  ['unrelated-route protection', 'SteamFriendAutoFollowState.RouteBusy'],
  ['guarded auto-follow command', '_steamAutoFollowCommandPending'],
  ['auto-follow route handoff', 'TryAutoFollowSteamFriendAsync'],
  ['bounded watchlist', 'MaximumEntries = 12'],
  ['strict Steam Community host', 'string.Equals(uri.Host, "steamcommunity.com"'],
  ['SteamID64 validation', '@"^7656119\\d{10}$"'],
  ['query and fragment rejection', '!string.IsNullOrEmpty(uri.Query)'],
  ['trusted Steam add URI', 'steam://friends/add/'],
  ['trusted Steam profile URI', 'steam://openurl/'],
  ['browser profile fallback', 'target.CanonicalProfileUrl'],
  ['exact authorized live-name match', 'FindLiveMatch'],
  ['authorized friend roster bridge', '_friendRoster.FirstOrDefault'],
  ['existing friend-route bridge', 'routeToFriend'],
  ['Live Map tracking boundary', 'Switch to Live Map mode to track authorized live friends'],
  ['no Steam login retention copy', 'Isley stores no Steam login'],
  ['streamer watchlist redaction', 'Steam friend watch hidden in streamer mode'],
  ['guarded local watch removal', '_removeSteamFriendWatchConfirmationPending'],
  ['Steam friend Quick Command', 'Add or track Steam friend'],
  ['deterministic Steam friend verifier', 'conservative auto-follow arbitration']
];
for (const [label, contract] of requiredSteamFriendContracts) {
  if (!steamFriendSurface.includes(contract)) {
    throw new Error(`Steam friend watch is missing ${label}: ${contract}`);
  }
}

const hudDockSurface = `${source}\n${xamlSource}\n${hudDockLogicSource}\n${hudDockVerifierSource}`;
const requiredHudDockContracts = [
  ['tactical rail anchor', 'x:Name="TacticalIntelStack"'],
  ['drawer control', 'x:Name="HudDockButton"'],
  ['plain-language status', 'x:Name="HudDockStatusText"'],
  ['persisted dock choice', 'HudDockMirrored = _hudDockMirrored'],
  ['restored dock choice', '_hudDockMirrored = settings.HudDockMirrored'],
  ['deterministic plan', 'HudDockLogic.Resolve'],
  ['visible voice clearance', 'VoiceHudBorder.Visibility == Visibility.Visible'],
  ['computed tactical inset', 'plan.IntelBottomInset'],
  ['navigation docking', 'NavigationReadoutPanel.HorizontalAlignment'],
  ['tactical docking', 'TacticalIntelStack.HorizontalAlignment'],
  ['survival docking', 'SurvivalHudStack.HorizontalAlignment'],
  ['voice docking', 'VoiceHudBorder.HorizontalAlignment'],
  ['staggered transition', 'BeginTime = TimeSpan.FromMilliseconds(index * 28)'],
  ['resize recalculation', 'UpdateHudDockLayout();'],
  ['searchable mirror command', 'Mirror HUD dock'],
  ['command action', 'case "hud-dock"'],
  ['deterministic verifier', 'HUD dock verification passed']
];
for (const [label, contract] of requiredHudDockContracts) {
  if (!hudDockSurface.includes(contract)) {
    throw new Error(`HUD dock is missing ${label}: ${contract}`);
  }
}

const hudPrioritySurface = `${source}\n${xamlSource}\n${hudPriorityLogicSource}\n${hudPriorityVerifierSource}`;
const requiredHudPriorityContracts = [
  ['persistent Smart HUD preference', 'SmartHudEnabled = _smartHudEnabled'],
  ['restored Smart HUD preference', '_smartHudEnabled = settings.SmartHudEnabled'],
  ['default-on Smart HUD', 'public bool SmartHudEnabled { get; set; } = true'],
  ['drawer toggle', 'x:Name="SmartHudButton"'],
  ['plain-language state', 'x:Name="SmartHudStatusText"'],
  ['searchable Smart HUD command', 'Toggle Smart HUD'],
  ['command execution', 'case "smart-hud"'],
  ['compact-layout gate', 'CompactWidth = 520'],
  ['survival-state gate', 'SurvivalActive'],
  ['ambient card folding', 'HideAmbientHud'],
  ['live navigation preservation', '!context.MarkerAvailable'],
  ['pack detail compaction', 'CompactPackHud'],
  ['active voice preservation', '!context.VoiceActive && !context.VoiceProblem'],
  ['environmental warning preservation', 'guidance.Warning'],
  ['runtime refresh on incident change', 'RefreshSmartHudPresentation(force: true)'],
  ['resize-aware refresh', 'RefreshSmartHudPresentation();'],
  ['deterministic verifier', 'Smart HUD priority verification passed']
];
for (const [label, contract] of requiredHudPriorityContracts) {
  if (!hudPrioritySurface.includes(contract)) {
    throw new Error(`Smart HUD is missing ${label}: ${contract}`);
  }
}

const responsiveLayoutSurface = `${source}\n${xamlSource}\n${responsiveLayoutLogicSource}\n${responsiveLayoutVerifierSource}`;
const requiredResponsiveLayoutContracts = [
  ['minimum width threshold', 'MicroMaximumWidth = 420'],
  ['minimum height threshold', 'MicroMaximumHeight = 440'],
  ['minimum survival fold', 'requestedSurvivalDetails && !isMicroLayout'],
  ['full instructions handoff', 'SurvivalDetailAction'],
  ['scrollable Survival Assistant handoff', 'OpenMapToolsAtSection("survival-assistant")'],
  ['compact vitals geometry', 'VitalsMinimumWidth'],
  ['compact size-column geometry', 'FooterSizeColumnWidth'],
  ['named footer size column', 'x:Name="FooterSizeColumn"'],
  ['resize refresh', 'UpdateResponsiveOverlayLayout();'],
  ['full ready shortcut state', '"KEYS READY"'],
  ['micro ready shortcut state', 'KEYS {enabledCount}/{enabledCount}'],
  ['deterministic verifier', 'Responsive overlay verification passed']
];
for (const [label, contract] of requiredResponsiveLayoutContracts) {
  if (!responsiveLayoutSurface.includes(contract)) {
    throw new Error(`Responsive overlay is missing ${label}: ${contract}`);
  }
}

const gatewayResourceSurface = `${source}\n${xamlSource}\n${gatewayResourceLogicSource}\n${gatewayResourceVerifierSource}`;
const requiredGatewayResourceContracts = [
  ['fixed HTTPS index', 'https://myislemap.com/'],
  ['same-origin asset allowlist', 'IsTrustedAsset'],
  ['versioned map-data discovery', '/map-data.js'],
  ['bounded index download', 'MaxIndexBytes = 256_000'],
  ['bounded asset download', 'MaxAssetBytes = 768_000'],
  ['bounded site count', 'MaxPointCount = 5_000'],
  ['non-executed data envelope validation', 'window.MAP_OVERLAYS = MAP_OVERLAYS'],
  ['source-map height normalization', 'SourceMapHeight = 1003'],
  ['three allowed resource buckets', '"animals" or "herbs" or "earth"'],
  ['strict update-date parsing', 'DateOnly.TryParseExact'],
  ['local nearest-site selection', 'ResourceFinderLogic.Select'],
  ['salt alias', '["salt"] = "saltrock"'],
  ['prey bucket alias', '["prey"] = "animals"'],
  ['plant bucket alias', '["plant"] = "herbs"'],
  ['diet suggestion bridge', 'SuggestedDietQuery'],
  ['compact finder section', 'x:Name="ResourceFinderSectionAnchor"'],
  ['local resource search', 'x:Name="ResourceFinderSearchInputBox"'],
  ['fast need presets', 'ResourceFinderPresetButton_Click'],
  ['alternate-site navigation', 'ResourceFinderNextButton_Click'],
  ['normal waypoint handoff', 'routeMapPoint({payload})'],
  ['route replacement refusal', 'CLEAR THE CURRENT ROUTE FIRST'],
  ['static-not-live disclosure', 'static site, not a live spawn'],
  ['streamer redaction', 'RESOURCE FINDER HIDDEN'],
  ['startup source refresh', 'LoadGatewayResourceNetworkAsync'],
  ['shutdown cancellation', '_gatewayResourceCancellation?.Cancel()'],
  ['Diet Coach shortcut', 'x:Name="DietFindResourceButton"'],
  ['searchable command', 'Open Resource Finder'],
  ['deterministic verifier', 'strict source parsing, aliases, nearest/alternate sites']
];
for (const [label, contract] of requiredGatewayResourceContracts) {
  if (!gatewayResourceSurface.includes(contract)) {
    throw new Error(`Gateway Resource Finder is missing ${label}: ${contract}`);
  }
}

const serverSessionSurface = `${source}\n${xamlSource}\n${nativeMethodsSource}\n${serverSessionLogicSource}\n${serverSessionVerifierSource}\n${universalCoordinateLogicSource}\n${slopeSafetyLogicSource}\n${universalCoordinateVerifierSource}\n${communityServerWatchLogicSource}\n${communityServerWatchVerifierSource}\n${serverStatusSource}`;
const requiredServerSessionContracts = [
  ['three explicit profiles', 'ServerSessionLogic.Profiles.Length == 3'],
  ['Live Map full-live profile', 'LiveMapServicesAvailable'],
  ['official universal profile', 'Official server'],
  ['Any Server universal profile', 'Any Isle server'],
  ['universal tools in every profile', 'profile.UniversalToolsAvailable'],
  ['optional server names', '!profile.RequiresServerName'],
  ['optional server addresses', '!profile.RequiresServerAddress'],
  ['private and unlisted coverage', 'PRIVATE / UNLISTED SUPPORTED'],
  ['safe invalid-profile fallback', 'invalid fallback'],
  ['bounded custom name', 'custom name bound'],
  ['persisted profile id', 'ServerSessionProfileId = _serverSessionProfileId'],
  ['persisted community name', 'ServerSessionName = _serverSessionName'],
  ['session selector', 'ServerSessionModeButton'],
  ['explicit three-mode selector', 'ServerSessionProfileSelector'],
  ['Live Map selector', 'ServerModeLiveMapButton'],
  ['Live Map visible profile name', 'LiveMapDisplayName = "Live Map"'],
  ['independent profile disclosure', 'NO SERVER-OPERATOR DEPENDENCY'],
  ['explicit independence disclosure', 'never uses the game server you play on'],
  ['Official selector', 'ServerModeOfficialButton'],
  ['Any Server selector', 'ServerModeAnyServerButton'],
  ['compatibility status', 'ServerCompatibilityStatusText'],
  ['capability detail', 'ServerCapabilityDetailText'],
  ['community name input', 'ServerSessionNameInputBox'],
  ['compact vertical server actions', 'ServerSessionActionsPanel'],
  ['focused universal surface', 'UniversalSessionSurface'],
  ['universal guide action', 'UniversalSessionGuideButton_Click'],
  ['universal Life Run action', 'UniversalSessionLifeRunButton_Click'],
  ['universal timer action', 'UniversalSessionTimersButton_Click'],
  ['universal voice-key action', 'UniversalSessionVoiceButton_Click'],
  ['universal Trip Check action', 'UniversalSessionTripButton_Click'],
  ['universal Fight Check action', 'UniversalSessionFightButton_Click'],
  ['universal Growth Clock action', 'UniversalSessionGrowthButton_Click'],
  ['universal Nest Planner action', 'UniversalSessionNestButton_Click'],
  ['universal app action', 'UniversalSessionAppButton_Click'],
  ['explicit no-server-position copy', 'NO SERVER-FED POSITIONS'],
  ['Live Map disposal', 'window.__isley?.dispose?.()'],
  ['background map suspension', 'Navigate("about:blank")'],
  ['internal blank-page allowlist', 'string.Equals(value, "about:blank"'],
  ['map initialization boundary', '!LiveMapServicesActive || _initializingMap'],
  ['map command boundary', 'if (!LiveMapServicesActive)'],
  ['Live Map polling boundary', 'PUBLIC WATCH IS OPTIONAL IN ANY SERVER MODE'],
  ['built-in voice remains available', 'Open Isley Voice'],
  ['built-in voice ready heading', 'ISLEY VOICE READY'],
  ['universal coordinate workspace', 'UniversalCoordinatePanel'],
  ['coordinate-only clipboard parser', 'UniversalCoordinateLogic.TryParseClipboard'],
  ['new-change clipboard baseline', 'GetClipboardSequenceNumber'],
  ['foreground-only clipboard read', 'PlayFocusForeground.Game or PlayFocusForeground.Mapper'],
  ['opt-in coordinate preference', 'UniversalCoordinateCaptureEnabled = _universalCoordinateCaptureEnabled'],
  ['session-only coordinate clearing', 'ClearUniversalCoordinateSession'],
  ['streamer coordinate redaction', '!LiveMapServicesActive && !_streamerMode'],
  ['streamer universal-surface removal', 'if (live || _streamerMode)'],
  ['coordinate-shaped rejection boundary', 'non-coordinate clipboard accepted'],
  ['localized coordinate parsing', 'localized coordinate separators should parse'],
  ['two-capture hill geometry', 'DescribeHill'],
  ['short-baseline hill refusal', 'short baseline should not fabricate hill evidence'],
  ['compact hill evidence rail', 'UniversalCoordinateHillText'],
  ['hill passability truth boundary', 'surface traction, and passability must be verified in game'],
  ['auto location on game start setting', 'AutoLocateOnGameStart = _autoLocateOnGameStart'],
  ['auto location game-start resume', 'HandleGameStartedLocationResumeAsync'],
  ['auto location control', 'x:Name="AutoLocateOnGameStartButton"'],
  ['all-session Terrain Probe gate', 'if (!_universalCoordinateCaptureEnabled || _streamerMode)'],
  ['Live Map Terrain Probe workspace', 'TerrainProbePanel'],
  ['explicit slope-avoidance action', 'TerrainProbeSaveAvoidanceButton'],
  ['derived local corridor', 'buildMeasuredSlopeArea'],
  ['raw-coordinate session boundary', 'exact coordinates remain session-only'],
  ['searchable coordinate action', 'Toggle Player Sync'],
  ['searchable avoidance action', 'Save measured slope avoidance'],
  ['deterministic coordinate verifier', 'Universal coordinate capture verification passed'],
  ['community public-watch surface', 'CommunityServerWatchPanel'],
  ['strict host-port input', 'CommunityServerAddressInputBox'],
  ['opt-in public polling', '_communityServerWatchEnabled'],
  ['persisted public address', 'CommunityServerAddress = _communityServerAddress'],
  ['persisted watch preference', 'CommunityServerWatchEnabled = _communityServerWatchEnabled'],
  ['persisted slot alert', 'CommunityServerSlotAlertEnabled = _communityServerSlotAlertEnabled'],
  ['saved Community profile settings', 'CommunityServerProfiles = _communityServerProfiles.Select'],
  ['persisted selected saved server', 'SelectedCommunityServerProfileId = _selectedCommunityServerProfileId'],
  ['legacy singleton profile migration', 'RestoreCommunityServerProfiles'],
  ['six-server safety cap', 'MaximumProfiles = 6'],
  ['compact saved-server rail', 'CommunityServerDeckPanel'],
  ['saved-server position label', 'CommunityServerDeckStatusText'],
  ['previous saved server action', 'CommunityServerPreviousButton_Click'],
  ['next saved server action', 'CommunityServerNextButton_Click'],
  ['wraparound saved-server navigation', 'MoveProfileIndex'],
  ['clean saved-server creation', 'CreateProfile(_communityServerProfiles)'],
  ['guarded saved-server removal', '_communityServerRemoveConfirmationPending'],
  ['three-second removal guard', 'await Task.Delay(3000)'],
  ['final saved-server protection', 'KEEP ONE SAVED SERVER'],
  ['per-server watch synchronization', 'profile.WatchEnabled = _communityServerWatchEnabled'],
  ['per-server slot-alert synchronization', 'profile.SlotAlertEnabled = _communityServerSlotAlertEnabled'],
  ['per-server growth-rate handoff', 'includeGrowthRate: true'],
  ['profile-switch request cancellation', 'CancelServerStatusRefreshAsync'],
  ['restrained saved-server crossfade', 'TimeSpan.FromMilliseconds(140)'],
  ['address edit suspends watch', '_communityServerWatchEnabled = false'],
  ['fixed public provider endpoint', 'api.gamemonitoring.net/servers?limit=5&game=376210&connect='],
  ['strict public address match', 'string.Equals(item.Connect, normalizedAddress'],
  ['strict Isle game match', 'item.Game == 376210'],
  ['community one-minute timer reuse', 'ShouldPollServerStatus'],
  ['slot transition evaluator', 'EvaluateSlotTransition'],
  ['one-shot slot announcement', 'AnnounceCommunitySlotOpenAsync'],
  ['exact search-name copy', 'SERVER SEARCH NAME COPIED'],
  ['current unofficial join guide', 'UnofficialServerGuide'],
  ['community public tactical status', 'SERVER PUBLIC'],
  ['Any Server setup command', 'Open Any Server setup'],
  ['deterministic watch verifier', 'Any Server public-status verification passed'],
  ['Live Map service restoration', 'LIVE MAP SERVICES ON'],
  ['map-only tab boundary', 'PinsToolsTabButton.IsEnabled = live'],
  ['universal tactical summary', 'BuildUniversalTacticalBriefSummary'],
  ['universal tactical copy', 'LIVE MAP DATA OMITTED'],
  ['profile growth handoff', 'ServerSessionGrowthButton_Click'],
  ['official 1x suggestion', 'official growth suggestion'],
  ['community manual-rate boundary', 'community manual growth boundary'],
  ['session command action', 'Choose server mode'],
  ['live-only command guard', 'RequiresLiveMapServices'],
  ['restrained surface reveal', 'TimeSpan.FromMilliseconds(180)'],
  ['deterministic session verifier', 'Server session verification passed']
];
for (const [label, contract] of requiredServerSessionContracts) {
  if (!serverSessionSurface.includes(contract)) {
    throw new Error(`Server session support is missing ${label}: ${contract}`);
  }
}

const fieldGuideSurface = `${source}\n${xamlSource}\n${overlayLinksSource}\n${fieldGuideLogicSource}\n${combatGuideLogicSource}\n${contentBaselineSource}\n${fieldGuideVerifierSource}`;
const requiredFieldGuideContracts = [
  ['field guide workspace', 'GuideToolsPanel'],
  ['two-row primary tools', '<UniformGrid Columns="3">'],
  ['readable secondary tool rows', 'Click="GuideToolsTabButton_Click" Content="FIELD GUIDE"'],
  ['guide search', 'GuideSearchInputBox'],
  ['guide diet filters', 'GuideCarnivoreFilterButton'],
  ['guide favorites', 'GuideFavoriteSpeciesIds'],
  ['persistent guide selection', 'GuideSelectedSpeciesId'],
  ['selected profile presentation', 'GuideProfileCard'],
  ['essential control reference', 'GuideControlsPanel'],
  ['diet coach bridge', 'FieldGuideLogic.DietSpeciesIndex'],
  ['snapshot variability disclosure', 'server and Hordetest rosters can differ'],
  ['current species reference', 'https://www.theisle.info/dinosaurs'],
  ['current controls reference', 'https://www.theisle.info/guide/controls'],
  ['field guide command action', 'Open Field Guide'],
  ['21 public combat briefs', 'CombatGuideLogic.Briefs.Length == 21'],
  ['combat snapshot date', 'SnapshotDate = "2026-07-09"'],
  ['shared combat public branch', 'PublicBranch = IsleContentBaseline.PublicBranch'],
  ['reviewed public branch', 'PublicBranch = "0.21.734"'],
  ['exact roster alignment', 'combat roster identity mismatch'],
  ['Hordetest roster exclusion', 'Hordetest or upcoming animals must not enter the public roster'],
  ['combat-aware species search', 'CombatGuideLogic.SearchText(entry.Id)'],
  ['selected combat dossier', 'GuideCombatBriefAnchor'],
  ['combat damage style', 'GuideCombatDamageText'],
  ['combat signature action', 'GuideCombatSignatureText'],
  ['combat positioning', 'GuideCombatPositionText'],
  ['combat abort condition', 'GuideCombatAbortText'],
  ['current Kentrosaurus controls', 'kentro power swing rmb lmb ctrl defensive stance reflect spikes'],
  ['current Pteranodon spearfishing bind', 'current spearfishing bind'],
  ['current Tyrannosaurus attack controls', 'Hold-LMB Crush'],
  ['combat mutation query bridge', 'CombatGuideLogic.MutationSearchQuery'],
  ['combat mutation action', 'GuideCombatMutationsButton_Click'],
  ['damage triage action', 'GuideCombatTriageButton_Click'],
  ['current combat source action', 'OpenCombatGuideButton_Click'],
  ['exact combat brief scroll', 'GuideCombatBriefAnchor.TranslatePoint'],
  ['combat command action', 'Open species combat brief'],
  ['no false damage precision', 'not a damage calculator'],
  ['deterministic field guide verifier', 'Field guide verification passed']
];
for (const [label, contract] of requiredFieldGuideContracts) {
  if (!fieldGuideSurface.includes(contract)) {
    throw new Error(`Field Guide is missing ${label}: ${contract}`);
  }
}

const serverStatusSurface = `${serverStatusSource}\n${source}\n${xamlSource}`;
const requiredServerStatusContracts = [
  ['no fixed server address', 'BuildStatusEndpoint(normalizedAddress)'],
  ['anonymous public JSON endpoint', '"https://api.gamemonitoring.net/servers?limit=5&game=376210&connect="'],
  ['strict address validation', 'string.Equals(item.Connect, normalizedAddress'],
  ['strict game validation', 'item.Game == 376210'],
  ['invalid population refusal', 'server.NumPlayers > server.MaxPlayers'],
  ['provider timestamp parsing', 'FromUnixTimeSeconds(server.LastUpdate)'],
  ['bounded request timeout', 'Timeout = TimeSpan.FromSeconds(8)'],
  ['status refresh cancellation', '_serverStatusCancellation?.Cancel()'],
  ['one-minute background refresh', 'Interval = TimeSpan.FromSeconds(60)'],
  ['last-good snapshot fallback', 'Showing the last good public snapshot'],
  ['aged snapshot threshold', 'sourceAge is { TotalMinutes: > 30 }'],
  ['compact header status', 'ServerStatusText'],
  ['detailed server status card', 'ServerStatusCard'],
  ['population occupancy bar', 'ServerPopulationFill'],
  ['session population trend', 'ServerPopulationTrendText'],
  ['trend sample deduplication', 'TimeSpan.FromSeconds(45)'],
  ['bounded two-hour trend history', 'TimeSpan.FromHours(2)'],
  ['no-extra-request trend explanation', 'no additional requests'],
  ['manual server refresh', 'RefreshServerStatusButton_Click'],
  ['one-click server address copy', 'CopyServerAddressButton_Click'],
  ['server address command action', 'Copy optional public server address'],
  ['public source action', 'OpenServerStatusSourceButton_Click'],
  ['server status command action', 'Check optional public server'],
  ['external-only safety guidance', 'no game data is read']
];
for (const [label, contract] of requiredServerStatusContracts) {
  if (!serverStatusSurface.includes(contract)) {
    throw new Error(`Server intelligence is missing ${label}: ${contract}`);
  }
}

const patchWatchSurface = `${patchWatchSource}\n${contentBaselineSource}\n${combatGuideLogicSource}\n${source}\n${xamlSource}\n${patchWatchVerifierSource}`;
const requiredPatchWatchContracts = [
  ['fixed official Steam endpoint', 'https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=376210&count=20&maxlength=1200&format=json'],
  ['official announcement hub', 'https://steamcommunity.com/app/376210/announcements/'],
  ['bounded response', 'MaxPayloadBytes = 512 * 1024'],
  ['redirect refusal', 'AllowAutoRedirect = false'],
  ['strict app id', 'envelope.AppNews?.AppId != 376210'],
  ['strict item app id', 'item.AppId != 376210'],
  ['official announcement feed', 'steam_community_announcements'],
  ['patch-note tag gate', 'item.Tags.Contains("patchnotes"'],
  ['strict announcement id', 'AnnouncementIdPattern.IsMatch'],
  ['future-time refusal', 'publishedAt > retrievedAt.AddDays(1)'],
  ['trusted notes construction', 'https://steamcommunity.com/ogg/376210/announcements/detail/'],
  ['shared guide baseline', 'internal const string PublicBranch = IsleContentBaseline.PublicBranch'],
  ['compact App rail', 'x:Name="PatchWatchSectionAnchor"'],
  ['plain current state', 'GUIDES MATCH PUBLIC'],
  ['plain review state', 'REVIEW PATCH'],
  ['server-build divergence', 'SERVER BUILD'],
  ['contextual version impact', 'BuildImpact'],
  ['collapsed aligned impact', '!currentImpact.Visible'],
  ['version impact rail', 'x:Name="PatchWatchImpactPanel"'],
  ['version impact scope', 'TERRAIN / ROUTES'],
  ['private review checklist', 'combat, species abilities, and aim calibration'],
  ['trusted URL exclusion test', 'untrusted patch URL entered the copied checklist'],
  ['copy review action', 'PatchWatchImpactCopyButton_Click'],
  ['clipboard review handoff', 'Clipboard.SetText(impact.CopyText)'],
  ['server version extraction', 'TryExtractVersion'],
  ['server status handoff', 'LiveMapServicesActive ? _lastServerStatus?.Version : null'],
  ['thirty-minute cadence', 'Interval = TimeSpan.FromMinutes(30)'],
  ['cancellation on shutdown', '_officialPatchCancellation?.Cancel()'],
  ['last-good disclosure', 'last good checked'],
  ['one-per-version warning', '_officialPatchWarningAnnouncedVersion'],
  ['anonymous tactical-log handoff', 'Official patch needs review'],
  ['explicit refresh action', 'PatchWatchRefreshButton_Click'],
  ['official notes action', 'PatchWatchNotesButton_Click'],
  ['searchable command', 'Open Official Patch Watch'],
  ['exact full-rail jump', 'ToolsScrollViewer.ScrollToVerticalOffset(Math.Max(0, offset - 4))'],
  ['deterministic verifier', 'Official Patch Watch: PASS']
];
for (const [label, contract] of requiredPatchWatchContracts) {
  if (!patchWatchSurface.includes(contract)) {
    throw new Error(`Official Patch Watch is missing ${label}: ${contract}`);
  }
}

const visualComfortSurface = `${source}\n${xamlSource}\n${appXamlSource}`;
const requiredVisualComfortContracts = [
  ['three deliberate light modes', '_mapLightModeLabels = ["Day", "Dim", "Night"]'],
  ['bounded light strengths', '_mapLightModeOpacities = [0, 0.18, 0.34]'],
  ['non-interactive map dimmer', 'x:Name="MapLightOverlay"'],
  ['dimmer input passthrough', 'IsHitTestVisible="False"'],
  ['visual comfort section', 'Text="VISUAL COMFORT"'],
  ['map light control', 'MapLightModeButton'],
  ['map light explanation', 'MapLightModeStatusText'],
  ['restrained map-light transition', 'Duration = TimeSpan.FromMilliseconds(180)'],
  ['persistent map-light setting', 'public int MapLightModeIndex { get; set; }'],
  ['validated map-light restoration', 'settings.MapLightModeIndex, 0, _mapLightModeOpacities.Length - 1'],
  ['saved map-light preference', 'MapLightModeIndex = _mapLightModeIndex'],
  ['default reset integration', '_mapLightModeIndex = 0'],
  ['searchable map-light action', 'Cycle map lighting'],
  ['desktop guidance contrast promise', 'HUD and alerts stay clear'],
  ['privacy mask priority', 'Panel.ZIndex="55"'],
  ['active-label hover contrast', '<Setter Property="Foreground" Value="{StaticResource PrimaryTextBrush}" />'],
  ['active-toggle contrast surface', '<SolidColorBrush x:Key="ActiveToggleBrush" Color="#FF075985" />'],
  ['active-toggle readable text', 'button.Foreground = (Brush)FindResource("PrimaryTextBrush");'],
  ['pressure-readable recovery type', 'FontSize="8.25"'],
  ['bounded recovery line rhythm', 'LineHeight="10.5"'],
  ['footer overflow restraint', 'TextTrimming="CharacterEllipsis"']
];
for (const [label, contract] of requiredVisualComfortContracts) {
  if (!visualComfortSurface.includes(contract)) {
    throw new Error(`Visual comfort is missing ${label}: ${contract}`);
  }
}
const hudDetailSurface = `${source}\n${xamlSource}`;
const requiredHudDetailContracts = [
  ['three HUD detail modes', '_hudDetailModeLabels = ["Full", "Essential", "Clean"]'],
  ['persistent HUD detail setting', 'public int HudDetailModeIndex { get; set; }'],
  ['validated HUD detail restoration', 'settings.HudDetailModeIndex, 0, _hudDetailModeLabels.Length - 1'],
  ['saved HUD detail preference', 'HudDetailModeIndex = _hudDetailModeIndex'],
  ['default Full reset', '_hudDetailModeIndex = 0'],
  ['visual comfort control', 'x:Name="HudDetailButton"'],
  ['plain-language mode status', 'x:Name="HudDetailStatusText"'],
  ['single-click mode cycling', 'HudDetailButton_Click'],
  ['central HUD detail presentation', 'UpdateHudDetailModeControls'],
  ['searchable HUD detail action', 'Cycle HUD detail'],
  ['command execution', 'case "hud-detail"'],
  ['Clean navigation-card suppression', '_hudDetailModeIndex >= 2'],
  ['ambient nearby-place suppression', '_hudDetailModeIndex == 0 && _nearestPlaceVisible && available'],
  ['encounter warning override', '_hudDetailModeIndex == 0 ? _encounterHudVisible || alerting : alerting'],
  ['Essential active pack-route context', 'alerting || _packRouteActive || _packOutlierRouteActive || friendRouteActive'],
  ['Clean pack warning override', '_ => alerting'],
  ['warning transition remains armed while hidden', '_packSpreadAlertInitialized = true'],
  ['active guidance safety promise', 'dedicated routes, timers, and safety warnings'],
  ['startup visibility refresh', 'UpdateNavigationReadout(_markerAvailable)']
];
for (const [label, contract] of requiredHudDetailContracts) {
  if (!hudDetailSurface.includes(contract)) {
    throw new Error(`HUD detail is missing ${label}: ${contract}`);
  }
}
const tacticalLogSurface = `${source}\n${xamlSource}`;
const requiredTacticalLogContracts = [
  ['typed event model', 'private sealed record TacticalEventEntry('],
  ['session-only event collection', 'private readonly List<TacticalEventEntry> _tacticalEvents = []'],
  ['bounded twenty-four-event history', '_tacticalEvents.RemoveRange(24, _tacticalEvents.Count - 24)'],
  ['three-second duplicate suppression', 'now - newest.OccurredAt < TimeSpan.FromSeconds(3)'],
  ['latest-eight rendering cap', '_tacticalEvents.Take(8).ToList()'],
  ['cardless tactical log section', 'Text="TACTICAL LOG"'],
  ['plain-language log status', 'x:Name="TacticalLogStatusText"'],
  ['copy action', 'x:Name="CopyTacticalLogButton"'],
  ['guarded clear action', 'x:Name="ClearTacticalLogButton"'],
  ['timeline list surface', 'x:Name="TacticalLogListPanel"'],
  ['central event recorder', 'AddTacticalEvent('],
  ['central timeline renderer', 'UpdateTacticalLog('],
  ['chronological clipboard export', '_tacticalEvents.AsEnumerable().Reverse()'],
  ['three-second clear confirmation', 'Select Clear Log again within 3 seconds'],
  ['streamer timeline hiding', 'Timeline hidden in streamer mode'],
  ['streamer copy protection', 'CopyTacticalLogButton.IsEnabled = false'],
  ['streamer transition removes rendered rows', 'UpdateSessionStats();\n        UpdateTacticalBrief();\n        UpdateTacticalLog();\n        UpdateBreadcrumbTrailControls();'],
  ['restrained newest-item reveal', 'Duration = TimeSpan.FromMilliseconds(180)'],
  ['subtle newest-item motion', 'From = 4'],
  ['authenticated-feed event', '"Live map connected"'],
  ['game-process event', '"The Isle started"'],
  ['stale-feed event', '"Location feed stale"'],
  ['feed-recovery event', '"Location feed recovered"'],
  ['marker-loss event', '"Player marker lost"'],
  ['marker-recovery event', '"Player marker recovered"'],
  ['danger-boundary event', '"Alert zone entered"'],
  ['pack-spread event', '"Pack spread warning"'],
  ['pack-recovery event', '"Pack regrouped"'],
  ['encounter event', '"Player nearby"'],
  ['encounter-clear event', '"Encounter clear"'],
  ['route-start event', '"Route started"'],
  ['arrival event', '"Destination reached"'],
  ['timer-start event', '"Timer started"'],
  ['timer-completion event', '"Timer complete"'],
  ['death-marker event', '"Death marker saved"'],
  ['searchable tactical-log action', 'Open tactical log'],
  ['tactical-log jump target', '"tactical-log" => TacticalLogSectionAnchor']
];
for (const [label, contract] of requiredTacticalLogContracts) {
  if (!tacticalLogSurface.includes(contract)) {
    throw new Error(`Tactical log is missing ${label}: ${contract}`);
  }
}
const tacticalBriefSurface = `${source}\n${xamlSource}`;
const requiredTacticalBriefContracts = [
  ['cardless brief section', 'Text="TACTICAL BRIEF"'],
  ['live summary surface', 'x:Name="TacticalBriefStatusText"'],
  ['explicit copy action', 'x:Name="CopyTacticalBriefButton"'],
  ['central brief formatter', 'BuildTacticalBriefText()'],
  ['compact summary formatter', 'BuildTacticalBriefSummary()'],
  ['identity-free tooltip', 'identity-free live callout'],
  ['streamer redaction', 'return "Hidden in streamer mode"'],
  ['streamer export refusal', 'if (_streamerMode)\n        {\n            return string.Empty;'],
  ['feed health segment', '!_tacticalMapReadyLogged ? "FEED CONNECTING" : _staleAlertActive ? "FEED STALE" : "FEED ACTIVE"'],
  ['self waiting honesty', 'parts.Add("YOU WAITING")'],
  ['route distance capture', '_currentWaypointDistance = waypointDistance'],
  ['route segment', '"ROUTE ACTIVE"'],
  ['safety segment', '"SAFETY INSIDE SAVED ALERT ZONE"'],
  ['anonymous pack segment', '$"PACK {_packFriendCount}"'],
  ['anonymous contact segment', '$"CONTACT {_encounterPlayerCount}"'],
  ['conditional contact timing', '$"contact {contactEta.ToLowerInvariant()} if unchanged"'],
  ['server population segment', '$"SERVER {status.Players}/{status.Capacity}"'],
  ['manual life-run segment', 'parts.Add(BuildLifeRunSummary(compact: true))'],
  ['bounded clipboard payload', 'brief.Length > 900'],
  ['copy feedback', '"TACTICAL BRIEF COPIED"'],
  ['searchable brief action', '"Copy tactical brief"'],
  ['palette copy execution', 'await CopyTacticalBriefAsync()'],
  ['live bridge refresh', 'UpdateSessionStats();\n                UpdateTacticalBrief();\n                UpdateBreadcrumbTrailControls();']
];
for (const [label, contract] of requiredTacticalBriefContracts) {
  if (!tacticalBriefSurface.includes(contract)) {
    throw new Error(`Tactical brief is missing ${label}: ${contract}`);
  }
}
const tacticalBriefStart = source.indexOf('private string BuildTacticalBriefText()');
const tacticalBriefEnd = source.indexOf('private void UpdateTacticalBrief()', tacticalBriefStart);
if (tacticalBriefStart < 0 || tacticalBriefEnd <= tacticalBriefStart) {
  throw new Error('Tactical brief formatter boundaries could not be inspected');
}
const tacticalBriefFormatterSource = source.slice(tacticalBriefStart, tacticalBriefEnd);
const forbiddenTacticalBriefIdentityFields = [
  '_nearestFriendName',
  '_packFarthestFriendName',
  '_waypointLabel',
  '_nearestDangerLabel',
  '_nearestAlertZoneLabel',
  '_currentSelfX:',
  '_currentSelfY:'
];
for (const field of forbiddenTacticalBriefIdentityFields) {
  if (tacticalBriefFormatterSource.includes(field)) {
    throw new Error(`Tactical brief can leak identity or exact-position field: ${field}`);
  }
}
const commandCatalogMatch = source.match(/CommandPaletteActions\s*=\s*\[([\s\S]*?)\n\s*\];/);
const commandCatalogCount = commandCatalogMatch
  ? (commandCatalogMatch[1].match(/new\("/g) || []).length
  : -1;
if (commandCatalogCount !== 117) {
  throw new Error(`Quick Commands catalog count drifted: ${commandCatalogCount}`);
}
const quickKeysSurface = `${source}\n${xamlSource}\n${quickKeysLogicSource}\n${overlayLinksSource}`;
for (const [label, contract] of [
  ['three reference modes', 'internal const int ModeCount = 3'],
  ['responsive entry trimming', 'safeWidth < 330 ? 3 : safeWidth < 430 ? 4 : 5'],
  ['default-rebindable HUD copy', 'DEFAULT · REBINDABLE'],
  ['click-through map rail', 'x:Name="QuickKeysHudBorder"'],
  ['independent HUD switch', 'x:Name="HudQuickKeysButton"'],
  ['mode control', 'x:Name="QuickKeysModeButton"'],
  ['default-off persistence', 'QuickKeysHudVisible = _quickKeysHudVisible'],
  ['Streamer Mode gating', 'HudSurfaceLogic.Show(_quickKeysHudVisible, _streamerMode)'],
  ['collision-aware lower rails', 'quickKeysInset'],
  ['current controls handoff', 'https://www.theisle.info/guide/controls'],
  ['searchable toggle', 'new("quick-keys"']
]) {
  if (!quickKeysSurface.includes(contract)) {
    throw new Error(`Quick Keys contract missing: ${label}`);
  }
}
const manualSightingSurface =
  `${source}\n${xamlSource}\n${manualSightingLogicSource}\n${fightCheckLogicSource}\n${nextMoveLogicSource}`;
for (const [label, contract] of [
  ['session-only relative sighting anchor', 'x:Name="ManualSightingSectionAnchor"'],
  ['collapsible sighting controls', 'x:Name="ManualSightingPanel"'],
  ['universal-session sighting action', 'x:Name="UniversalSessionSightingButton"'],
  ['explicit report action', 'ManualSightingReportButton_Click'],
  ['explicit clear action', 'ManualSightingClearButton_Click'],
  ['fixed freshness boundary', 'FreshnessSeconds = 45'],
  ['automatic expiry presentation', 'SIGHTING EXPIRED'],
  ['no-detection uncertainty copy', 'No identity, exact distance, count, motion, or species is inferred'],
  ['Fight Check integration', 'ManualSightingActive'],
  ['Next Move handoff', '"sighting-check"'],
  ['server-change clearing', 'ClearManualSighting(logEvent: false, updateUi: false, resetDraft: true, collapse: true)'],
  ['new-life clearing', 'ClearManualSighting(logEvent: false, updateUi: true, resetDraft: true, collapse: true)'],
  ['streamer clearing', 'ClearManualSighting(logEvent: false, updateUi: false, resetDraft: true, collapse: true)']
]) {
  if (!manualSightingSurface.includes(contract)) {
    throw new Error(`Manual Sighting contract missing: ${label}`);
  }
}
const terrainGapPolicySurface = `${source}\n${xamlSource}\n${terrainGapPolicyLogicSource}`;
for (const [label, contract] of [
  ['strict connector limit', 'StrictId'],
  ['balanced connector limit', 'MaximumConnectorDistance'],
  ['gap control', 'TerrainGapPolicyButton'],
  ['gap command', 'new("route-gaps"'],
  ['active-course gap reroute', 'terrain-course-gap-policy-changed']
]) {
  if (!terrainGapPolicySurface.includes(contract)) {
    throw new Error(`Terrain gap-policy contract missing: ${label}`);
  }
}
const sharedRoutePasteCallCount = (source.match(/PasteSharedRouteFromClipboardAsync\(\)/g) || []).length;
if (sharedRoutePasteCallCount !== 3) {
  throw new Error(
    `Shared-route clipboard reads must remain explicit user actions: ${sharedRoutePasteCallCount}`);
}
const focusModeSurface = `${source}\n${xamlSource}\n${focusModeLogicSource}\n${focusModeVerifierSource}`;
for (const [label, contract] of [
  ['six deterministic definitions', 'var expectedIds = new HashSet<string>'],
  ['combat awareness profile', '"combat", "Combat"'],
  ['nest perimeter profile', '"nest", "Nest"'],
  ['focus marker snapshot', 'public int? MarkerStyleIndex'],
  ['focus HUD-detail snapshot', 'public int? HudDetailModeIndex'],
  ['focus encounter HUD snapshot', 'public bool? EncounterHudVisible'],
  ['focus encounter alert snapshot', 'public int? EncounterAlertIndex'],
  ['focus encounter memory snapshot', 'public int? EncounterMemoryIndex'],
  ['compatibility-safe restore', 'snapshot.EncounterHudVisible ?? _encounterHudVisible'],
  ['single focus application path', 'private async Task ApplyFocusModeAsync'],
  ['direct combat focus dispatch', 'await ApplyFocusModeAsync("combat")'],
  ['direct nest focus dispatch', 'await ApplyFocusModeAsync("nest")']
]) {
  if (!focusModeSurface.includes(contract)) {
    throw new Error(`Focus Modes are missing ${label}: ${contract}`);
  }
}
const nextMoveSurface = `${source}\n${xamlSource}\n${nextMoveLogicSource}\n${nextMoveVerifierSource}`;
const nextUpgradesSurface = `${source}\n${xamlSource}`;
const requiredNextUpgradeContracts = [
  ['live health strip', 'LiveHealthText'],
  ['live health presentation', 'UpdateLiveHealthStrip'],
  ['focus mode next-move suggest', 'NextMoveFocusSuggestButton'],
  ['whats-new release notes', 'WhatsNewButton'],
  ['portable prefs export', 'ExportPortableConfigButton'],
  ['voice NAT coach', 'VoiceNatCoachText'],
  ['pack rally on route accept', "dropPinAtSelf('rally')"],
  ['recovery route to body', 'RecoveryPromptRouteButton'],
  ['community join-link binding', 'IsleyJoinLink']
];
for (const [label, contract] of requiredNextUpgradeContracts) {
  if (!nextUpgradesSurface.includes(contract)) {
    throw new Error(`Next upgrades surface is missing ${label}: ${contract}`);
  }
}
const requiredNextMoveContracts = [
  ['single compact decision rail', 'x:Name="NextMoveSectionAnchor"'],
  ['one dominant action', 'x:Name="NextMoveActionButton"'],
  ['context explanation', 'x:Name="NextMoveDetailText"'],
  ['explicit fixed priority', 'internal static NextMoveRecommendation Evaluate'],
  ['critical survival precedence', 'raw.SurvivalUrgency >= 3'],
  ['critical Core Vitals precedence', 'raw.CoreVitalsUrgency >= 3'],
  ['closing contact escalation', 'string.Equals(encounterMotion, "closing"'],
  ['Live Map contact boundary', 'raw.LiveMapServicesActive && (closeContact || closingContact)'],
  ['direct Escape Route handoff', '"escape-route",\n                "PLAN ESCAPE"'],
  ['self-position contact fallback', '"OPEN CONTACTS",\n                    950'],
  ['pack boundary escalation', 'raw.PackSpreadAlertActive'],
  ['moving-away route escalation', 'string.Equals(waypointTrend, "away"'],
  ['due-soon timer escalation', 'raw.SoonestTimerSeconds is >= 0 and <= 60'],
  ['lifecycle review escalation', 'raw.LifeRunActive && raw.LifeTransitionPending'],
  ['lifecycle review handoff', '"REVIEW LIFE"'],
  ['growth-gate escalation', 'raw.LifeRunActive && raw.GrowthGatePending'],
  ['growth-gate action handoff', 'raw.GrowthGateActionId'],
  ['urgent approach escalation', 'raw.ApproachBriefActive && raw.ApproachBriefUrgency >= 2'],
  ['normal approach handoff', 'if (raw.ApproachBriefActive)'],
  ['approach route action', 'raw.ApproachBriefActionId'],
  ['expanded priority verifier', '28-level priority ladder'],
  ['final-minute restart priority', 'raw.RestartWatchActive && raw.RestartWatchRemainingSeconds <= 60'],
  ['five-minute restart priority', 'raw.RestartWatchActive && raw.RestartWatchRemainingSeconds <= 300'],
  ['restart Safe Logout handoff', 'RestartWatchActionId'],
  ['Core Vitals warning handoff', '"core-vitals"'],
  ['resource-trend warning handoff', 'raw.ResourceTrendWarning'],
  ['resource-trend priority', '860,\n                NextMoveTone.Warning'],
  ['live-species mismatch handoff', 'raw.LifeRunActive && raw.SpeciesMismatch'],
  ['species synchronization action', '"SYNC SPECIES"'],
  ['paused growth escalation', 'raw.LifeRunActive && raw.GrowthPaused'],
  ['Prime verification gate', '"VERIFY PRIME"'],
  ['Elder verification gate', '"VERIFY ELDER"'],
  ['nest phase handoff', '"nest-planner"'],
  ['route handoff', '"routes"'],
  ['Life Run handoff', '"life-run"'],
  ['Streamer Mode redaction', '"NEXT MOVE HIDDEN"'],
  ['invalid distance refusal', 'double.IsFinite(value.Value)'],
  ['anonymous compact summary', 'internal static string CompactSummary'],
  ['quarter-second refresh', 'UpdateNextMove();'],
  ['exact action dispatch', 'await ExecuteCommandPaletteActionAsync(actionId)'],
  ['drawer-to-map Escape Route handoff', 'SetToolsOpen(false);'],
  ['searchable Next Move command', 'Open Next Move'],
  ['direct Next Move jump', '"next-move" => NextMoveSectionAnchor'],
  ['manual-data boundary', 'no prediction, game memory, or automatic action'],
  ['deterministic verifier', 'Next Move: PASS']
];
for (const [label, contract] of requiredNextMoveContracts) {
  if (!nextMoveSurface.includes(contract)) {
    throw new Error(`Next Move is missing ${label}: ${contract}`);
  }
}
const lifeRunSurface = `${source}\n${xamlSource}\n${lifeRunLogicSource}\n${lifeRunHistoryLogicSource}\n${lifeRunVerifierSource}`;
const requiredLifeRunContracts = [
  ['conditional map card', 'x:Name="LifeRunHudBorder"'],
  ['drawer workspace', 'x:Name="LifeRunSectionAnchor"'],
  ['explicit start action', 'LifeRunStartButton_Click'],
  ['five growth stages', '_lifeRunStageLabels = ["Hatchling", "Juvenile", "Subadult", "Adult", "Elder"]'],
  ['stage cycle action', 'LifeRunStageButton_Click'],
  ['manual milestone actions', 'LifeRunMilestoneButton_Click'],
  ['bounded zone counters', 'Math.Clamp(_lifeRunMigrationVisits + delta, 0, 99)'],
  ['migration decrement guard', 'LifeRunMigrationMinusButton.IsEnabled = _lifeRunMigrationVisits > 0'],
  ['patrol decrement guard', 'LifeRunPatrolMinusButton.IsEnabled = _lifeRunPatrolVisits > 0'],
  ['identity-free copy action', 'CopyLifeRunButton_Click'],
  ['HUD visibility control', 'LifeRunHudButton_Click'],
  ['guarded new-life reset', '_newLifeRunConfirmationPending'],
  ['three-second reset window', 'await Task.Delay(3000)'],
  ['persistent tracker restoration', 'RestoreLifeRun(settings.LifeRun)'],
  ['persistent tracker snapshot', 'LifeRun = new LifeRunSettings'],
  ['streamer redaction', 'LifeRunStatusText.Text = "Hidden in streamer mode"'],
  ['clean-view declutter', '_hudDetailModeIndex < 2'],
  ['anonymous brief integration', 'RUN {_lifeRunStageShortLabels[_lifeRunStageIndex]}'],
  ['manual accuracy disclaimer', 'not automatic Prime certification'],
  ['searchable life-run action', 'Open life run tracker'],
  ['direct life-run jump', '"life-run" => LifeRunSectionAnchor'],
  ['Prime readiness strip', 'x:Name="LifeRunPrimeScoreText"'],
  ['Prime progress indicator', 'x:Name="LifeRunPrimeProgressTransform"'],
  ['Mass Migration condition', 'x:Name="LifeRunMassMigrationButton"'],
  ['tri-state fertility condition', 'x:Name="LifeRunFertilityButton"'],
  ['tri-state spasm condition', 'x:Name="LifeRunSpasmButton"'],
  ['species-class condition', 'x:Name="LifeRunSpeciesClassButton"'],
  ['manual tri-state action', 'LifeRunPrimeStateButton_Click'],
  ['current Prime guide action', 'OpenPrimeGuideButton_Click'],
  ['ten-condition count', 'PrimeConditionCount(LifeRunSnapshot run)'],
  ['small-species threshold', 'run.SpeciesClass == 1 ? 4 : 5'],
  ['migration threshold', 'run.MigrationVisits >= 2 ? 1 : 0'],
  ['patrol threshold', 'run.PatrolVisits >= 4 ? 1 : 0'],
  ['in-game verification guidance', 'VERIFY 4TH SLOT AT 75%'],
  ['persistent Mass Migration condition', 'MassMigrationVisited = _lifeRunMassMigrationVisited'],
  ['persistent passive states', 'FertilityStatus = _lifeRunFertilityStatus'],
  ['searchable Prime action', 'Open Prime planner'],
  ['direct Prime jump', '"prime-planner" => LifeRunSectionAnchor'],
  ['juvenile Sanctuary priority', 'run.StageIndex <= 1 && !run.SanctuaryVisited'],
  ['subadult safety ordering', 'if (!run.PerfectDiet) return "PERFECT DIET"'],
  ['completion state', 'return "ALL TRACKED"'],
  ['deterministic logic verifier', 'Life Run logic: PASS'],
  ['survival journal workspace', 'x:Name="LifeRunHistorySectionAnchor"'],
  ['manual death archive action', 'Tag="death"'],
  ['manual survived archive action', 'Tag="survived"'],
  ['neutral ended archive action', 'Tag="ended"'],
  ['manual archive handler', 'LifeRunArchiveOutcomeButton_Click'],
  ['bounded journal capacity', 'internal const int MaximumEntries = 25'],
  ['newest-first journal normalization', '.OrderByDescending(entry => entry.EndedAtUnixMs)'],
  ['unique journal identifiers', 'History ID uniqueness failed'],
  ['bounded history progress', 'TrackedMilestones = Math.Clamp(source.TrackedMilestones, 0, 6)'],
  ['private aggregate summary', 'LifeRunHistoryLogic.Summarize'],
  ['three recent journal rows', 'internal const int VisibleEntries = 3'],
  ['restrained row reveal', 'BeginTime = TimeSpan.FromMilliseconds(index * 25)'],
  ['identity-free journal export', 'Private manual history; no game memory, automatic death detection, player identity, or coordinates.'],
  ['journal copy action', 'CopyLifeRunHistoryButton_Click'],
  ['guarded journal clearing', '_clearLifeRunHistoryConfirmationPending'],
  ['persistent journal restoration', 'RestoreLifeRunHistory(settings.LifeRunHistory)'],
  ['persistent journal snapshot', 'LifeRunHistory = _lifeRunHistory.Select'],
  ['streamer journal redaction', 'LifeRunHistoryContentPanel.Visibility = _streamerMode'],
  ['tactical archive event', 'Life archived · {LifeRunHistoryLogic.OutcomeLabel(entry.Outcome)}'],
  ['searchable journal action', 'Open survival journal'],
  ['direct journal jump', '"life-journal" => LifeRunHistorySectionAnchor']
];
for (const [label, contract] of requiredLifeRunContracts) {
  if (!lifeRunSurface.includes(contract)) {
    throw new Error(`Life Run is missing ${label}: ${contract}`);
  }
}
const elderLineageSurface = `${source}\n${xamlSource}\n${elderLineageLogicSource}\n${elderLineageVerifierSource}\n${lifeRunHistoryLogicSource}`;
const requiredElderLineageContracts = [
  ['lineage workspace anchor', 'x:Name="ElderLineageSectionAnchor"'],
  ['single lifecycle rail', 'x:Name="ElderLineageProgressTransform"'],
  ['one next action', 'x:Name="ElderLineageNextText"'],
  ['inherited mutation readout', 'x:Name="ElderLineageMutationText"'],
  ['lineage backward correction', 'x:Name="ElderLineageMinusButton"'],
  ['lineage forward correction', 'x:Name="ElderLineagePlusButton"'],
  ['manual Prime verification', 'x:Name="ElderPrimeConfirmedButton"'],
  ['manual Elder verification', 'x:Name="ElderConfirmedButton"'],
  ['guarded Entomb recording', 'x:Name="RecordEntombButton"'],
  ['three-second Entomb confirmation', '_recordEntombConfirmationPending'],
  ['100-percent Elder gate', 'growth >= 100 && snapshot.ElderConfirmed'],
  ['Prime preparation state', 'ElderLineageState.PrimePreparation'],
  ['Prime fourth-slot verification state', 'ElderLineageState.PrimeVerification'],
  ['Prime check is never inferred', 'snapshot.PrimeConfirmed ? ElderLineageState.PrimeWindow'],
  ['Prime window state', 'ElderLineageState.PrimeWindow'],
  ['Frail path honesty', 'ElderLineageState.FrailPath'],
  ['Elder verification state', 'ElderLineageState.ElderVerification'],
  ['Entomb ready state', 'ElderLineageState.EntombReady'],
  ['bounded lineage ledger', 'internal const int MaximumEntombCount = 15'],
  ['reported mutation boost cap', 'internal const int ReportedMutationBoostCap = 2'],
  ['mutation carry-forward', 'CarryForwardMutationStatus'],
  ['active mutations become carried', '1 or 2 => 2'],
  ['same-species carryover', '_dietSpeciesIndex = retainedSpeciesIndex'],
  ['per-life Prime reset', '_lifeRunMassMigrationVisited = false'],
  ['per-life unlock reset', '_mutationUnlockProgress.Clear()'],
  ['per-life nest reset', 'ApplyNestPlannerSnapshot(NestPlannerLogic.Normalize(new NestPlannerSnapshot('],
  ['Entomb journal outcome', 'EntombedOutcome = "entombed"'],
  ['persistent Entomb count', 'ElderEntombCount = _elderEntombCount'],
  ['persistent Prime check', 'ElderPrimeConfirmed = _elderPrimeConfirmed'],
  ['persistent Elder check', 'ElderConfirmed = _elderConfirmed'],
  ['restored lineage count', 'saved?.ElderEntombCount'],
  ['compact existing-card HUD line', 'x:Name="LifeRunHudLineageText"'],
  ['verified Prime HUD state', 'PRIME VERIFIED · {primeCount}/10'],
  ['late-life next action handoff', 'lineageGuidesNext ? elder.NextAction : primeNext'],
  ['anonymous Tactical Brief integration', 'LINEAGE {_elderEntombCount + 1} ENT {_elderEntombCount}'],
  ['Tactical Brief Prime verification', 'PRIME-CHECK {(_elderPrimeConfirmed ? "Y" : "N")}'],
  ['searchable Elder action', 'Open Elder lineage'],
  ['direct Elder jump', '"elder-lineage" => _lifeRunActive ? ElderLineageSectionAnchor : LifeRunSectionAnchor'],
  ['current Elder guide action', 'CURRENT ELDER GUIDE'],
  ['update-sensitive disclosure', 'inherited-mutation boost cap is community-tested and may change'],
  ['deterministic Elder verifier', 'Elder lineage verification passed']
];
for (const [label, contract] of requiredElderLineageContracts) {
  if (!elderLineageSurface.includes(contract)) {
    throw new Error(`Elder lineage is missing ${label}: ${contract}`);
  }
}
const growthPlannerSurface = `${source}\n${xamlSource}\n${growthPlannerLogicSource}\n${growthPlannerVerifierSource}`;
const requiredGrowthPlannerContracts = [
  ['compact growth workspace', 'x:Name="GrowthClockSectionAnchor"'],
  ['manual exact growth control', 'x:Name="GrowthPercentButton"'],
  ['five-percent step controls', 'Tag="-5"'],
  ['progress indicator', 'x:Name="GrowthClockProgressTransform"'],
  ['server multiplier control', 'x:Name="GrowthServerMultiplierButton"'],
  ['survival-floor pause', 'x:Name="GrowthPauseButton"'],
  ['twenty-one species timing catalog', 'GrowthPlannerLogic.SpeciesTimings.Length == 21'],
  ['current timing snapshot', 'SnapshotDate = "2026-05-28"'],
  ['server-neutral Live Map multiplier', 'DefaultLiveMapMultiplierIndex = 0'],
  ['five multiplier presets', 'ServerMultipliers = [1, 1.5, 2, 3, 5]'],
  ['lifecycle milestone gates', 'new(87, "PRIME PEAK")'],
  ['growth-stage synchronization', '_lifeRunStageIndex = GrowthPlannerLogic.StageIndex(_lifeRunGrowthPercent)'],
  ['Prime deadline advice', 'Prime deadline: complete'],
  ['fourth-slot verification advice', 'verify the fourth mutation slot in game'],
  ['survival-floor honesty', 'Growth paused: restore food and water'],
  ['ballpark formula disclosure', 'base time × remaining percent ÷ server multiplier ÷ logged nutrient count'],
  ['persistent growth percent', 'public int? GrowthPercent { get; set; }'],
  ['legacy stage migration', 'GrowthPlannerLogic.StageAnchor(_lifeRunStageIndex)'],
  ['saved server multiplier', 'GrowthServerMultiplierIndex = _growthServerMultiplierIndex'],
  ['saved pause state', 'GrowthPaused = _growthPaused'],
  ['compact HUD percentage', 'LifeRunHudStageText.Text = $"{compactStage} {_lifeRunGrowthPercent}%'],
  ['anonymous tactical summary', 'GrowthPlannerLogic.CompactSummary(growth)'],
  ['searchable Growth Clock action', 'Open Growth Clock'],
  ['direct Growth Clock jump', '"growth-clock" => _lifeRunActive ? GrowthClockSectionAnchor : LifeRunSectionAnchor'],
  ['current growth guide action', 'OpenGrowthGuideButton_Click'],
  ['update-sensitive UI disclosure', 'base times and lifecycle gates can change by patch or server'],
  ['deterministic growth verifier', 'Growth planner verification passed']
];
for (const [label, contract] of requiredGrowthPlannerContracts) {
  if (!growthPlannerSurface.includes(contract)) {
    throw new Error(`Growth Clock is missing ${label}: ${contract}`);
  }
}
const liveGrowthBridgeSurface = `${source}\n${xamlSource}\n${liveGrowthBridgeLogicSource}\n${liveGrowthBridgeVerifierSource}`;
const requiredLiveGrowthBridgeContracts = [
  ['quiet live-source rail', 'x:Name="GrowthLiveBridgePanel"'],
  ['fresh value presentation', 'x:Name="GrowthLiveValuesText"'],
  ['explicit adoption action', 'x:Name="GrowthLiveAdoptButton"'],
  ['live-aware existing start control', 'x:Name="LifeRunLiveStartText"'],
  ['clear live-start disclosure', 'START USES LIVE DINO'],
  ['start-from-live behavior', 'Life run started from live growth'],
  ['saved-run synchronization', 'Life run synchronized'],
  ['fresh provider boundary', 'LiveMapServicesActive && playerSnapshot.LiveFresh'],
  ['offline manual fallback', 'Live Growth is waiting; manual controls remain authoritative.'],
  ['live planning authority', 'bridge.EffectiveGrowthPercent'],
  ['read-only Prime boundary', 'live Prime remains read-only'],
  ['no automatic Prime confirmation', '_elderPrimeConfirmed'],
  ['stale fail-closed verifier', 'offline and stale data must fail closed to manual values'],
  ['deterministic bridge verifier', 'Live growth bridge verification passed']
];
for (const [label, contract] of requiredLiveGrowthBridgeContracts) {
  if (!liveGrowthBridgeSurface.includes(contract)) {
    throw new Error(`Live Growth bridge is missing ${label}: ${contract}`);
  }
}
const liveSpeciesBridgeSurface = `${source}\n${xamlSource}\n${dietCoachLogicSource}\n${liveSpeciesBridgeLogicSource}\n${liveSpeciesBridgeVerifierSource}\n${nextMoveLogicSource}\n${nextMoveVerifierSource}`;
const requiredLiveSpeciesBridgeContracts = [
  ['bounded host identifier', 'ReadBoundedIdentifier(root, "speciesId", 32)'],
  ['exact application allowlist', 'DietCoachLogic.Species'],
  ['fresh provider boundary', 'LiveMapServicesActive && playerSnapshot.LiveFresh'],
  ['effective live guidance', 'CurrentEffectiveSpeciesIndex()'],
  ['growth timing integration', 'CurrentEffectiveSpeciesIndex(),\n            bridge.EffectiveGrowthPercent'],
  ['diet recommendation integration', 'DietCoachLogic.FoodForNutrient(effectiveSpeciesIndex'],
  ['Fight Check integration', 'CombatGuideLogic.Find(CurrentEffectiveSpeciesId())'],
  ['existing Life Run start integration', 'ApplyLiveSpeciesToSavedRun(speciesBridge)'],
  ['single in-context sync action', 'x:Name="DietLiveSpeciesButton"'],
  ['explicit saved-run handler', 'DietLiveSpeciesButton_Click'],
  ['saved guide alignment', '_guideSelectedSpeciesId = bridge.LiveSpeciesId'],
  ['Next Move mismatch escalation', 'SYNC LIVE SPECIES'],
  ['last-dino reference-only label', 'LAST DINO · NOT LIVE'],
  ['dedicated species identity line', 'speciesIdentityLine}GROWTH'],
  ['streamer-safe species action', 'speciesBridge.CanAdopt && _lifeRunActive && !_streamerMode'],
  ['deterministic species verifier', 'Live species bridge verification passed']
];
for (const [label, contract] of requiredLiveSpeciesBridgeContracts) {
  if (!liveSpeciesBridgeSurface.includes(contract)) {
    throw new Error(`Live Species bridge is missing ${label}: ${contract}`);
  }
}
const lifeTransitionSurface = `${source}\n${xamlSource}\n${lifeTransitionLogicSource}\n${lifeTransitionVerifierSource}\n${nextMoveLogicSource}\n${nextMoveVerifierSource}`;
const requiredLifeTransitionContracts = [
  ['consecutive fresh sample model', 'internal readonly record struct LiveDinoSample'],
  ['minimum duplicate gap', 'MinimumSampleGapSeconds = 5'],
  ['maximum disconnected gap', 'MaximumSampleGapSeconds = 180'],
  ['growth jitter restraint', 'GrowthResetThreshold = 3'],
  ['recognized-species allowlist', 'LiveSpeciesBridgeLogic.SpeciesIndex(sample.SpeciesId)'],
  ['session-only baseline', 'private LiveDinoSample? _lastLiveDinoSample'],
  ['session-only pending signal', 'private LifeTransitionAnalysis? _lifeTransitionPending'],
  ['live source observation', 'ObserveLiveDinoTransition(evaluation, receivedAt)'],
  ['conditional Life Run surface', 'x:Name="LifeTransitionPanel"'],
  ['explicit death outcome', 'Content="DIED + NEW"'],
  ['explicit safe outcome', 'Content="SAFE + NEW"'],
  ['explicit neutral outcome', 'Content="ENDED + NEW"'],
  ['keep-run escape', 'Content="KEEP RUN"'],
  ['parked-dinosaur disclosure', 'a species switch may be a parked dinosaur, not a death'],
  ['restrained reveal motion', 'TimeSpan.FromMilliseconds(160)'],
  ['fresh source required for handoff', 'FRESH LIVE DINOSAUR REQUIRED · SIGNAL KEPT'],
  ['manual archive before new run', 'PrependLifeRunHistory(archived, now)'],
  ['new run adopts live species', 'ApplyLiveSpeciesToSavedRun(speciesBridge)'],
  ['new run adopts live growth', '_lifeRunGrowthPercent = growthBridge.LiveGrowthPercent'],
  ['no automatic death inference', 'never infer death'],
  ['Next Move lifecycle routing', 'LifeTransitionPending = true'],
  ['session reset boundary', 'ClearLifeTransitionSession()'],
  ['deterministic transition verifier', 'Life transition verification passed']
];
for (const [label, contract] of requiredLifeTransitionContracts) {
  if (!lifeTransitionSurface.includes(contract)) {
    throw new Error(`Life Transition is missing ${label}: ${contract}`);
  }
}
const growthGateWatchSurface = `${source}\n${xamlSource}\n${growthGateWatchLogicSource}\n${growthGateWatchVerifierSource}\n${nextMoveLogicSource}\n${nextMoveVerifierSource}`;
const requiredGrowthGateWatchContracts = [
  ['fixed current lifecycle gates', 'internal static readonly int[] Gates = [50, 75, 87, 100]'],
  ['consecutive live sample model', 'internal readonly record struct LiveGrowthGateSample'],
  ['duplicate-fast refusal', 'MinimumSampleGapSeconds = 5'],
  ['disconnected-gap refusal', 'MaximumSampleGapSeconds = 180'],
  ['same-species continuity', '!string.Equals(prior.SpeciesId, latest.SpeciesId'],
  ['upward crossing only', 'latest.GrowthPercent <= prior.GrowthPercent'],
  ['highest crossed gate', '.DefaultIfEmpty(0)\n            .Max()'],
  ['recognized species boundary', 'LiveSpeciesBridgeLogic.SpeciesIndex(sample.SpeciesId)'],
  ['session-only sample state', 'private LiveGrowthGateSample? _lastGrowthGateSample'],
  ['session-only pending state', 'private GrowthGateWatchAnalysis? _growthGatePending'],
  ['live snapshot observer', 'ObserveLiveGrowthGate(evaluation, receivedAt)'],
  ['lifecycle transition suppression', '_lifeTransitionPending?.Detected == true'],
  ['conditional existing-clock rail', 'x:Name="GrowthGateWatchPanel"'],
  ['one exact gate label', 'x:Name="GrowthGateWatchLabelText"'],
  ['explicit contextual action', 'x:Name="GrowthGateWatchActionButton"'],
  ['acknowledge without write', 'GrowthGateWatchAcknowledgeButton_Click'],
  ['restricted action targets', 'actionId is not "mutation-planner" and not "prime-planner" and not "elder-lineage"'],
  ['restrained 160ms reveal', 'TimeSpan.FromMilliseconds(160)'],
  ['one-shot tactical event', 'analysis.Heading'],
  ['Next Move growth-gate routing', 'GrowthGatePending = true'],
  ['reset with lifecycle session', 'ClearGrowthGateWatchSession()'],
  ['manual authority copy', 'Verify Elder and Entomb eligibility in game before recording anything.'],
  ['deterministic gate verifier', 'Growth Gate Watch verification passed']
];
for (const [label, contract] of requiredGrowthGateWatchContracts) {
  if (!growthGateWatchSurface.includes(contract)) {
    throw new Error(`Growth Gate Watch is missing ${label}: ${contract}`);
  }
}
const approachBriefSurface = `${source}\n${xamlSource}\n${gatewayResourceLogicSource}\n${approachBriefLogicSource}\n${approachBriefVerifierSource}\n${nextMoveLogicSource}\n${nextMoveVerifierSource}`;
const requiredApproachBriefContracts = [
  ['bounded destination vocabulary', 'internal static readonly string[] DestinationKinds'],
  ['strict kind normalization', 'DestinationKinds.Contains(normalized, StringComparer.Ordinal)'],
  ['streamer and inactive-route refusal', 'raw.StreamerMode || !raw.WaypointActive'],
  ['invalid distance refusal', '!double.IsFinite(raw.Distance.Value)'],
  ['destination-specific radii', '"danger" or "death" => 60'],
  ['final-stage radius', '"water" or "nest" or "estimate" => 12'],
  ['terrestrial water check', '"WALK · LISTEN · KEEP AN EXIT"'],
  ['aerial landing check', '"WATER LANDING CHECK"'],
  ['aquatic waterline check', 'species is "deinosuchus" or "beipiaosaurus"'],
  ['carnivore carcass check', '"SCENT · CHECK CARCASS · DO NOT TUNNEL"'],
  ['safe-pin honesty', 'a saved pin is not a live safety guarantee'],
  ['death and danger warning', '"THREAT MAY REMAIN · USE COVER"'],
  ['estimate honesty', 'Isley did not detect or identify the source'],
  ['escape endpoint honesty', 'Do not treat the endpoint as safe'],
  ['map bridge provenance', 'waypointKind: waypoint?.kind ||'],
  ['saved-pin provenance', 'setStaticWaypoint(pin, label, pin.id, true, pin.type)'],
  ['road-course provenance retention', "kind: index === course.stops.length - 1 ? destination.kind : ''"],
  ['resource provenance', 'ResourceFinderLogic.ApproachKind(selection.Site)'],
  ['existing waypoint-card integration', 'x:Name="WaypointApproachBriefText"'],
  ['restrained reveal', 'TimeSpan.FromMilliseconds(160)'],
  ['one-notice route boundary', 'private string _approachBriefNoticeKey'],
  ['anonymous tactical event', '"APPROACH",\n            view.Heading'],
  ['Next Move urgent handoff', 'raw.ApproachBriefActive && raw.ApproachBriefUrgency >= 2'],
  ['Next Move normal handoff', 'if (raw.ApproachBriefActive)'],
  ['route-only action', '"routes",\n            "OPEN ROUTE"'],
  ['deterministic verifier', 'Approach Brief verification passed']
];
for (const [label, contract] of requiredApproachBriefContracts) {
  if (!approachBriefSurface.includes(contract)) {
    throw new Error(`Approach Brief is missing ${label}: ${contract}`);
  }
}
const nestPlannerSurface = `${source}\n${xamlSource}\n${overlayLinksSource}\n${nestPlannerLogicSource}\n${nestPlannerVerifierSource}`;
const requiredNestPlannerContracts = [
  ['compact workspace anchor', 'x:Name="NestPlannerSectionAnchor"'],
  ['nine-phase workflow', 'NestPlannerLogic.Phases.Length == 9'],
  ['current phase guidance', 'NestPlannerPhaseActionText'],
  ['four readiness checks', 'NestReservesButton'],
  ['dependent clutch bounds', 'var raised = active ? Math.Clamp(snapshot.YoungRaised, 0, hatched) : 0'],
  ['bounded egg target', 'internal const int MaxEggs = 8'],
  ['private or public access', 'NestPlannerLogic.AccessLabel'],
  ['gestation timer bridge', '"Egg gestation"'],
  ['incubation timer bridge', '"Nest incubation"'],
  ['existing survival timer reuse', 'StartSurvivalTimer(label, minutes)'],
  ['toggleable auto-hatch guidance', 'NestAutoHatchGuidanceButton_Click'],
  ['public-branch auto-hatch evaluator', 'EvaluateAutoHatch'],
  ['unsynchronized clutch attention', 'AUTO-HATCH CHECK · 3 UNSYNCED'],
  ['no fabricated auto-hatch duration', 'No duration is guessed'],
  ['auto-hatch Life Run HUD bridge', 'AUTO-HATCH CHECK" : string.Empty'],
  ['auto-hatch Next Move bridge', 'NestPlannerLogic.NextAction(nest, _nestAutoHatchGuidanceEnabled)'],
  ['persistent auto-hatch preference', 'AutoHatchGuidanceEnabled = _nestAutoHatchGuidanceEnabled'],
  ['persistent planner restoration', 'saved?.NestPlanner'],
  ['persistent planner snapshot', 'NestPlanner = new NestPlannerSettings'],
  ['new-life reset', 'ApplyNestPlannerSnapshot(NestPlannerLogic.Normalize(new NestPlannerSnapshot('],
  ['conditional HUD line', 'x:Name="LifeRunHudNestText"'],
  ['tactical brief integration', 'NestPlannerLogic.CompactSummary(nest)'],
  ['raised-young Life Run bridge', '_lifeRunRaisedYoung = true'],
  ['current nesting guide action', 'OpenNestingGuideButton_Click'],
  ['update-sensitive disclosure', 'exact clutch limits and timing vary by species, mutation, patch, and server'],
  ['searchable planner action', 'Open Nest Planner'],
  ['direct planner jump', '"nest-planner" => _lifeRunActive ? NestPlannerSectionAnchor : LifeRunSectionAnchor'],
  ['deterministic planner verifier', 'Nest planner verification passed']
];
for (const [label, contract] of requiredNestPlannerContracts) {
  if (!nestPlannerSurface.includes(contract)) {
    throw new Error(`Nest Planner is missing ${label}: ${contract}`);
  }
}
const mutationPlannerSurface = `${source}\n${xamlSource}\n${mutationPlannerLogicSource}\n${mutationBuildLogicSource}\n${mutationPlannerVerifierSource}`;
const requiredMutationPlannerContracts = [
  ['mutation workspace anchor', 'x:Name="MutationPlannerSectionAnchor"'],
  ['search input', 'x:Name="MutationSearchInputBox"'],
  ['progressive result inspector', 'x:Name="MutationSearchResultBorder"'],
  ['dynamic loadout list', 'x:Name="MutationLoadoutListPanel"'],
  ['forty-one current public entries', 'MutationPlannerLogic.Catalog.Length == 41'],
  ['catalog snapshot date', 'CatalogDate = "2026-07-03"'],
  ['official temporary removal', 'FindById("traumatic-thrombosis") is null'],
  ['experimental exclusion disclaimer', 'experimental and removed entries excluded'],
  ['ranked catalog search', 'internal static IReadOnlyList<MutationCatalogEntry> Search'],
  ['short-query guard', 'normalized.Length < 2'],
  ['six-result bound', 'MutationPlannerLogic.Search(MutationSearchInputBox.Text, 6)'],
  ['next-free slot allocator', 'internal static int NextFreeSlot'],
  ['restriction-aware slot allocator', 'NextFreeSlotForMutation'],
  ['Slot 2 exclusivity', 'return [2];'],
  ['Slots 2 and 4 allocation', 'return [2, 4];'],
  ['sixteen-slot cap', 'internal const int MaxLoadoutSize = 16'],
  ['duplicate mutation rejection', 'MUTATION ALREADY SAVED'],
  ['loadout validation', 'NormalizeLoadout'],
  ['three manual states', '1 => "ACTIVE"'],
  ['carried mutation state', '2 => "CARRIED"'],
  ['status cycling', 'Status = (current.Status + 1) % 3'],
  ['guarded row removal', '_mutationRemoveConfirmationSlot == slot'],
  ['three-second removal window', 'await Task.Delay(3000)'],
  ['persistent loadout restoration', 'saved?.MutationLoadout is { Count: > 0 }'],
  ['persistent loadout snapshot', 'MutationLoadout = _mutationLoadout.Select'],
  ['new-life loadout reset', '_mutationLoadout.Clear()'],
  ['conditional mutation HUD line', 'x:Name="LifeRunHudMutationText"'],
  ['anonymous brief count', 'MutationPlannerLogic.EquippedCount(_mutationLoadout)'],
  ['identity-free loadout copy', 'CopyMutationLoadoutButton_Click'],
  ['current guide action', 'OpenMutationGuideButton_Click'],
  ['searchable mutation action', 'Open mutation loadout'],
  ['direct mutation jump', '"mutation-planner" => _lifeRunActive ? MutationPlannerSectionAnchor : LifeRunSectionAnchor'],
  ['compact Build Lab anchor', 'x:Name="MutationBuildSectionAnchor"'],
  ['eight build focuses', 'MutationBuildLogic.Focuses.Length == 8'],
  ['four-part coverage', 'SUSTAIN {analysis.SustainPercent} · FIGHT {analysis.FightPercent}'],
  ['synergy catalog', 'internal static readonly MutationSynergyDefinition[] Synergies'],
  ['diet-class compatibility', 'IsDietCompatible'],
  ['restriction-safe recommendation', 'RecommendationSlot'],
  ['recommendation search handoff', 'MutationBuildRecommendationButton_Click'],
  ['persistent build focus restore', 'saved?.MutationBuildFocusIndex ?? 0'],
  ['persistent build focus snapshot', 'MutationBuildFocusIndex = _mutationBuildFocusIndex'],
  ['streamer Build Lab redaction', 'FOCUS · HIDDEN'],
  ['unknown-species recommendation disclosure', 'SPECIES UNSET · {analysis.RecommendationMeta}'],
  ['same-lineage focus retention', '_mutationBuildFocusIndex = retainedBuildFocusIndex'],
  ['new-life focus reset', '_mutationBuildFocusIndex = 0'],
  ['anonymous compact build summary', 'MutationBuildLogic.CompactSummary'],
  ['Tactical Brief build integration', 'CurrentMutationBuildAnalysis())'],
  ['searchable Build Lab action', 'Open mutation Build Lab'],
  ['direct Build Lab jump', '"mutation-build-lab" => _lifeRunActive ? MutationBuildSectionAnchor : LifeRunSectionAnchor'],
  ['manual recommendation disclosure', 'confirm species, sex, availability, and effect in game'],
  ['deterministic mutation verifier', 'Mutation planner: PASS']
];
for (const [label, contract] of requiredMutationPlannerContracts) {
  if (!mutationPlannerSurface.includes(contract)) {
    throw new Error(`Mutation planner is missing ${label}: ${contract}`);
  }
}
const mutationUnlockSurface = `${source}\n${xamlSource}\n${mutationUnlockLogicSource}\n${mutationUnlockVerifierSource}`;
const requiredMutationUnlockContracts = [
  ['compact unlock tracker', 'x:Name="MutationUnlockSectionAnchor"'],
  ['selected challenge label', 'x:Name="MutationUnlockNameText"'],
  ['single progress rail', 'x:Name="MutationUnlockProgressTransform"'],
  ['reversible manual correction', 'x:Name="MutationUnlockMinusButton"'],
  ['dominant next action', 'x:Name="MutationUnlockActionButton"'],
  ['guarded reset', '_mutationUnlockResetConfirmationId'],
  ['three-second reset window', 'await Task.Delay(3000)'],
  ['seven current challenges', 'challenges.Length == 7'],
  ['current challenge snapshot', 'SnapshotDate = "2026-05-28"'],
  ['two timed streaks', 'challenges.Count(item => item.Mode == MutationUnlockMode.Timer) == 2'],
  ['four counter challenges', 'challenges.Count(item => item.Mode == MutationUnlockMode.Counter) == 4'],
  ['one binary challenge', 'challenges.Count(item => item.Mode == MutationUnlockMode.Toggle) == 1'],
  ['night-kill goal', 'Kill five players at night.'],
  ['nutrient timer goal', 'Maintain nutrients for 60 minutes.'],
  ['hunger timer goal', 'Keep current hunger above 80% for 30 minutes.'],
  ['stamina drain goal', 'Drain 4,500 stamina by sprinting or fast-swimming.'],
  ['fractured-bone goal', 'Eat bones while you have a broken bone.'],
  ['jump goal', 'Jump 50 times.'],
  ['saltwater goal', 'Lose 1,250 thirst by drinking saltwater.'],
  ['existing timer HUD bridge', 'StartSurvivalTimer(challenge.TimerLabel, challenge.TimerMinutes)'],
  ['condition-break reset guidance', 'reset immediately if hunger drops below it'],
  ['timer completion synchronization', 'SyncCompletedMutationUnlockTimer'],
  ['persistent progress restoration', 'saved?.MutationUnlockProgress is { Count: > 0 }'],
  ['persistent progress snapshot', 'MutationUnlockProgress = _mutationUnlockProgress.Select'],
  ['new-life progress reset', '_mutationUnlockProgress.Clear()'],
  ['new-life timer cleanup', '_survivalTimers.RemoveAll(timer => MutationUnlockLogic.Challenges.Any'],
  ['shared mutation HUD line', 'UNLOCKS · {unlockCompleted}'],
  ['anonymous summary integration', 'MutationUnlockLogic.CompactSummary'],
  ['tactical log integration', '"Unlock progress recorded"'],
  ['streamer guard', 'if (!_lifeRunActive || _streamerMode) return;'],
  ['manual-data disclosure', 'Manual challenge ledger'],
  ['searchable unlock action', 'Open mutation unlock tracker'],
  ['direct unlock jump', '"mutation-unlocks" => _lifeRunActive ? MutationUnlockSectionAnchor : LifeRunSectionAnchor'],
  ['deterministic unlock verifier', 'Mutation unlock verification passed']
];
for (const [label, contract] of requiredMutationUnlockContracts) {
  if (!mutationUnlockSurface.includes(contract)) {
    throw new Error(`Mutation unlock tracker is missing ${label}: ${contract}`);
  }
}
const dietCoachSurface = `${source}\n${xamlSource}\n${dietCoachLogicSource}\n${dietCoachVerifierSource}`;
const requiredDietCoachContracts = [
  ['diet workspace anchor', 'x:Name="DietCoachSectionAnchor"'],
  ['three nutrient controls', 'x:Name="DietSlotThreeButton"'],
  ['species selector', 'DietSpeciesButton_Click'],
  ['target selector', 'DietTargetButton_Click'],
  ['five goal presets', 'DietCoachLogic.Targets.Length == 5'],
  ['twenty-one current playables', 'DietCoachLogic.Species.Length == 21'],
  ['ten nutrient combinations', 'expectedCombos'],
  ['order-independent combination key', '.OrderBy(value => value)'],
  ['migration-driven herbivore guidance', 'Migration zones set the current plant diet'],
  ['manual accuracy disclaimer', 'server rules and game updates may differ'],
  ['current species snapshot', 'SpeciesSnapshot = "2026-07-03"'],
  ['current combination snapshot', 'CombinationSnapshot = "2026-03-02"'],
  ['persistent diet restoration', 'saved?.DietSpeciesIndex ?? 0'],
  ['persistent diet snapshot', 'DietSlot3 = _dietSlot3'],
  ['new-life diet reset', '_dietSlot3 = DietCoachLogic.Empty'],
  ['conditional diet HUD line', 'x:Name="LifeRunHudDietText"'],
  ['anonymous tactical diet state', 'NUTR {dietState} {dietNeed}'],
  ['desired-state food layer bridge', 'setOfficialLayer(key, desiredState)'],
  ['food layer never toggles off', "setOfficialLayer('food', true)"],
  ['current diet guide action', 'OpenDietGuideButton_Click'],
  ['searchable diet action', 'Open diet and growth coach'],
  ['direct diet jump', '"diet-coach" => _lifeRunActive ? DietCoachSectionAnchor : LifeRunSectionAnchor'],
  ['deterministic diet verifier', 'Diet coach: PASS']
];
for (const [label, contract] of requiredDietCoachContracts) {
  if (!dietCoachSurface.includes(contract)) {
    throw new Error(`Diet coach is missing ${label}: ${contract}`);
  }
}
const survivalAssistantSurface = `${source}\n${xamlSource}\n${survivalAssistantLogicSource}\n${survivalAssistantVerifierSource}`;
const requiredSurvivalAssistantContracts = [
  ['eleven-condition catalog', 'SurvivalAssistantLogic.Incidents.Length == 11'],
  ['mechanics snapshot', 'MechanicsSnapshot = "2026-07-23"'],
  ['one dominant priority', 'string Priority'],
  ['three immediate actions', 'item.Steps.Length == 3'],
  ['conservative recovery countdown', 'RemainingSeconds'],
  ['separate vomit recovery', 'new("vomit", "Vomit sickness"'],
  ['separate food-poisoning recovery', 'new("food-poisoning", "Rotten-food poisoning"'],
  ['manual health-state cycle', 'NextHealthState'],
  ['always-visible status beacon', 'x:Name="StatusBeaconButton"'],
  ['status beacon opens exact help', 'StatusBeaconButton_Click'],
  ['one-click sickness footer', 'x:Name="SurvivalQuickButton"'],
  ['explicit game-warning report label', 'Content="VOMIT WARNING? START 5M"'],
  ['dedicated drawer sickness action', 'x:Name="SurvivalVomitStartButton"'],
  ['one-click sickness execution', 'SurvivalQuickButton_Click'],
  ['state-aware vomit quick action', 'SurvivalAssistantLogic.QuickAction('],
  ['active vomit report execution', 'await ReportAdditionalVomitAsync();'],
  ['game-state-aware recovery presentation', 'SurvivalAssistantLogic.Presentation('],
  ['time-bounded stop-eating warning', 'StopEatingWarningActive'],
  ['expired warning withdrawal', 'CHECK IN-GAME WARNING'],
  ['stale warning restore refusal', 'ShouldRestoreIncident('],
  ['current game warning cleared action', 'IN-GAME WARNING CLEARED'],
  ['complete compact recovery steps', 'incidentPresentation.HudSteps'],
  ['visible recovery progress', 'x:Name="SurvivalIncidentHudProgressTransform"'],
  ['expandable recovery HUD control', 'x:Name="SurvivalIncidentHudDetailButton"'],
  ['compact recovery HUD presentation', 'SurvivalAssistantLogic.HudPresentation('],
  ['compact recovery HUD persistence', 'SurvivalIncidentHudCollapsed = _survivalIncidentHudCollapsed'],
  ['new incident restores full guidance', '_survivalIncidentHudCollapsed = false'],
  ['stacked vomit duration', 'SurvivalVomitAgainButton_Click'],
  ['map-level additional-vomit action', 'x:Name="SurvivalIncidentHudVomitAgainButton"'],
  ['expired additional-vomit restart', 'SurvivalAssistantLogic.ReportAdditionalVomit('],
  ['stacked duration persistence', 'SurvivalIncidentAdditionalSeconds = _survivalIncidentAdditionalSeconds'],
  ['non-stale guidance reopens after restore', 'savedHudCollapsed && !expiredEstimate'],
  ['safe detection limitation disclosure', 'authorized map feed has no sickness field'],
  ['final-minute warning phase', 'UpdateSurvivalFinalMinutePulse'],
  ['completion requires game check', 'CHECK IN GAME'],
  ['bounded saved-pin remedy', 'var pinType = normalized == "dehydrated" ? "water" : "safe"'],
  ['manual incident selection', 'SurvivalIncidentButton_Click'],
  ['resolved action', 'SurvivalAssistantResolvedButton_Click'],
  ['recovery estimate restart', 'SurvivalRestartTimerButton_Click'],
  ['saved recovery-pin routing', 'routeToNearestPinType'],
  ['food-layer fallback', 'FOOD LAYER ON - NO SAVED FOOD PIN'],
  ['persistent restore', 'RestoreSurvivalIncident('],
  ['persistent snapshot', 'SurvivalIncidentStartedAtUnixMs = _survivalIncidentStartedAt'],
  ['anonymous tactical status', 'SurvivalAssistantLogic.CompactSummary'],
  ['streamer HUD redaction', 'SurvivalIncidentHudBorder.Visibility = Visibility.Collapsed'],
  ['current guide action', 'OpenSurvivalGuideButton_Click'],
  ['searchable survival action', 'Open survival assistant'],
  ['direct vomit recovery action', 'Start vomit sickness recovery'],
  ['direct vomit recovery execution', 'case "vomit-help"'],
  ['deterministic survival verifier', 'Survival assistant: PASS']
];
for (const [label, contract] of requiredSurvivalAssistantContracts) {
  if (!survivalAssistantSurface.includes(contract)) {
    throw new Error(`Survival assistant is missing ${label}: ${contract}`);
  }
}

const recoveryMonitorSurface = `${source}\n${xamlSource}\n${recoveryMonitorLogicSource}\n${recoveryMonitorVerifierSource}`;
const requiredRecoveryMonitorContracts = [
  ['guidance snapshot', 'GuidanceSnapshot = "2026-07-22"'],
  ['bounded movement threshold', 'MovementThresholdMuPerMinute = 0.25'],
  ['supported injury scope', '"bleeding",\n            "fracture",\n            "wounded"'],
  ['compact map status', 'x:Name="SurvivalRecoveryMonitorHudText"'],
  ['drawer status', 'x:Name="SurvivalRecoveryMonitorText"'],
  ['drawer explanation', 'x:Name="SurvivalRecoveryMonitorDetailText"'],
  ['fresh authorized marker gate', '_currentMarkerFreshnessAgeMs <= 6000'],
  ['streamer redaction', 'snapshot.StreamerMode || !Supports(incidentId)'],
  ['manual universal fallback', 'MANUAL REST CHECK'],
  ['missing marker honesty', 'MOVEMENT CHECK WAITING'],
  ['moving priority override', 'STOP MOVING · REST NOW'],
  ['settling window', 'HOLD STILL · {restSeconds}/{SettlingSeconds}S'],
  ['elapsed rest streak', 'RESTING · {FormatElapsed(restSeconds)}'],
  ['no heal-timer claim', 'this is not a heal timer'],
  ['qualified resumed-movement warning', 'RECOVERY · MOVEMENT RESUMED'],
  ['Next Move priority handoff', '_recoveryMonitorPriorityOverride'],
  ['identity-free tactical brief handoff', 'RecoveryMonitorBriefLabel()'],
  ['deterministic recovery verifier', 'Rest & Recovery Monitor: PASS']
];
for (const [label, contract] of requiredRecoveryMonitorContracts) {
  if (!recoveryMonitorSurface.includes(contract)) {
    throw new Error(`Rest & Recovery Monitor is missing ${label}: ${contract}`);
  }
}

const coreVitalsSurface = `${source}\n${xamlSource}\n${coreVitalsLogicSource}\n${woundCheckLogicSource}\n${coreVitalsVerifierSource}\n${nextMoveLogicSource}\n${nextMoveVerifierSource}`;
const requiredCoreVitalsContracts = [
  ['focused drawer section', 'x:Name="CoreVitalsSectionAnchor"'],
  ['manual health control', 'x:Name="ReportedHealthButton"'],
  ['visual wound fallback toggle', 'x:Name="WoundCheckToggleButton"'],
  ['four explicit wound observations', 'WoundCheckLogic.Options.Length == 4'],
  ['broad non-exact ranges', 'RangeLabel.StartsWith'],
  ['conservative wound-to-health mapping', 'ReportedHealthState.Critical'],
  ['fresh live HP precedence', 'exact live HP still wins'],
  ['wound estimate reset', '_woundObservationId = string.Empty'],
  ['wound estimate freshness expiry', 'WoundCheckLogic.IsCurrent'],
  ['current visual-health source', 'OverlayLinks.CombatGuide'],
  ['searchable wound command', 'new("wound-check"'],
  ['manual food control', 'x:Name="ReportedFoodButton"'],
  ['manual water control', 'x:Name="ReportedWaterButton"'],
  ['manual stamina control', 'x:Name="ReportedStaminaButton"'],
  ['five-minute expiry', 'FreshnessSeconds = 300'],
  ['independent report timestamps', '_reportedFoodReportedAt'],
  ['always-present compact strip', 'x:Name="StatusBeaconButton"'],
  ['one-click all-clear snapshot', 'CoreVitalsAllStableButton_Click'],
  ['explicit report clearing', 'CoreVitalsClearButton_Click'],
  ['dominant recovery action', 'x:Name="CoreVitalsRouteButton"'],
  ['saved marker routing', 'routeToNearestPinType'],
  ['food layer fallback', 'FOOD LAYER ON - NO SAVED FOOD PIN'],
  ['manual fallback disclosure', 'stamina and named conditions stay manual'],
  ['restrained state transition', 'TimeSpan.FromMilliseconds(160)'],
  ['server-session reset', 'ClearCoreVitals(logEvent: false, updateUi: false)'],
  ['community-profile reset', 'ClearCoreVitals(logEvent: false, updateUi: true)'],
  ['new-life reset', 'ClearCoreVitals(logEvent: false, updateUi: false);'],
  ['streamer redaction', 'StatusBeaconButton.Content = "VITALS HIDDEN"'],
  ['Next Move priority handoff', 'CoreVitalsUrgency'],
  ['anonymous tactical brief', 'coreVitals.BriefLabel'],
  ['universal-session action', 'x:Name="UniversalSessionVitalsButton"'],
  ['searchable command', 'Open Core Vitals'],
  ['direct section jump', '"core-vitals" => CoreVitalsSectionAnchor'],
  ['deterministic verifier', 'Core vitals: PASS']
];
for (const [label, contract] of requiredCoreVitalsContracts) {
  if (!coreVitalsSurface.includes(contract)) {
    throw new Error(`Core Vitals is missing ${label}: ${contract}`);
  }
}

const playerSnapshotSurface = `${body}\n${source}\n${xamlSource}\n${playerSnapshotLogicSource}\n${playerSnapshotVerifierSource}`;
const requiredPlayerSnapshotContracts = [
  ['local provider vitals source', 'window.__isleyLocalMap?.getVitals?.()'],
  ['local provider health value', 'healthCurrent: Number(vitals.healthCurrent)'],
  ['local provider hunger value', 'foodCurrent: Number(vitals.foodCurrent)'],
  ['local provider thirst value', 'waterCurrent: Number(vitals.waterCurrent)'],
  ['local provider growth value', 'growthPercent: Number(vitals.growthPercent)'],
  ['provider file refresh', 'RefreshIndependentLiveDataAsync'],
  ['provider freshness refusal', 'IsleyLiveDataProvider.FreshnessLimit'],
  ['bounded anonymous bridge message', "type: 'isley-player-snapshot'"],
  ['two-second full health cadence', 'fullPlayerSnapshotIntervalMs = 2000'],
  ['five-second Lite health cadence', 'litePlayerSnapshotIntervalMs = 5000'],
  ['single-flight health request', 'playerSnapshotInFlight'],
  ['bounded health error retry', 'playerSnapshotMaximumErrorRetryMs'],
  ['health cadence follows Lite Mode', 'nextPlayerSnapshotIntervalMs'],
  ['runtime health cadence diagnostics', 'playerSnapshotIntervalMs: liteMode'],
  ['manual refresh API', 'refreshPlayerSnapshot()'],
  ['streamer abort', 'playerSnapshotAbortController?.abort()'],
  ['host-side bounded number parsing', 'ReadBoundedNumber(root, "healthCurrent", 0, 1_000_000)'],
  ['host-side bounded species parsing', 'ReadBoundedIdentifier(root, "speciesId", 32)'],
  ['fifteen-second freshness gate', 'FreshnessSeconds = 15'],
  ['offline values excluded from live guidance', 'liveFresh ? HealthState(healthPercent) : ReportedHealthState.Unknown'],
  ['exact live footer percentages', 'HP {snapshot.HealthPercent} · F {snapshot.FoodPercent} · W {snapshot.WaterPercent}'],
  ['source state rail', 'x:Name="PlayerSnapshotStateText"'],
  ['manual refresh control', 'x:Name="PlayerSnapshotRefreshButton"'],
  ['last-dino honesty', 'LAST DINO · NOT LIVE'],
  ['stale fail-closed copy', 'Live values expired safely; manual fallback is active.'],
  ['streamer redaction', 'PlayerSnapshotPanel.Visibility = Visibility.Collapsed'],
  ['trip and fight refresh', 'UpdateFightCheck(force: true)'],
  ['deterministic verifier', 'Player snapshot: PASS']
];
for (const [label, contract] of requiredPlayerSnapshotContracts) {
  if (!playerSnapshotSurface.includes(contract)) {
    throw new Error(`Player snapshot is missing ${label}: ${contract}`);
  }
}

const vitalsTrendSurface = `${source}\n${xamlSource}\n${dockVitalsLogicSource}\n${vitalsTrendLogicSource}\n${vitalsTrendVerifierSource}\n${nextMoveLogicSource}\n${nextMoveVerifierSource}\n${tripReadinessLogicSource}\n${tripReadinessVerifierSource}`;
const requiredVitalsTrendContracts = [
  ['three-sample evidence gate', 'MinimumSamples = 3'],
  ['minimum observation span', 'MinimumSpanSeconds = 50'],
  ['bounded in-memory history', 'MaximumSampleCount = 12'],
  ['fifteen-minute source window', 'MaximumWindowMinutes = 15'],
  ['snapshot freshness inheritance', 'FreshnessSeconds = PlayerSnapshotLogic.FreshnessSeconds'],
  ['independent refill reset', 'value(samples[index]) - value(samples[index - 1]) >= RefillResetPercent'],
  ['three-percent refill threshold', 'RefillResetPercent = 3'],
  ['health damage reset', 'HealthDamageResetPercent = 1'],
  ['bounded healing horizon', 'MaximumHealthEtaMinutes = 120'],
  ['health recovery detail', 'HealthRecoveryDetail'],
  ['damage-reset disclosure', 'Damage resets the estimate.'],
  ['median rate estimator', 'var rate = Median(slopes)'],
  ['monotonicity gate', 'fallingShare >= 0.67'],
  ['minimum directional change', 'MinimumDirectionalChangePercent = 1'],
  ['bounded ETA horizon', 'estimate is > 0 and <= 60'],
  ['fifteen-minute early warning', 'EarlyWarningMinutes = 15'],
  ['short-window honesty', 'cannot predict future activity'],
  ['stale fail-closed state', 'TREND PAUSED · SNAPSHOT STALE'],
  ['offline fail-closed state', 'TREND PAUSED · LAST DINO'],
  ['session-only sample collection', 'private readonly List<VitalsTrendSample> _vitalsTrendSamples = []'],
  ['live-only sampling', 'sourceState == PlayerSnapshotSourceState.Live'],
  ['rapid-refresh replacement', 'TotalSeconds < 10'],
  ['new-life growth reset', 'snapshot.GrowthPercent + 2 < _vitalsTrendSamples[^1].GrowthPercent'],
  ['streamer/session clear path', 'ClearVitalsTrendSamples()'],
  ['single cardless trend line', 'x:Name="PlayerSnapshotTrendText"'],
  ['restrained trend transition', 'TimeSpan.FromMilliseconds(160)'],
  ['always-visible resource direction glyph', 'VitalsTrendLogic.FooterGlyph(vitalsTrend.Food)'],
  ['always-visible HP direction glyph', 'VitalsTrendLogic.FooterGlyph(vitalsTrend.Health)'],
  ['minimized HP direction glyph', 'VitalsTrendLogic.FooterGlyph(trend)'],
  ['recovery monitor HP bridge', 'recoveryVitalsTrend.Health.Rising'],
  ['one-shot warning key', '_vitalsTrendWarningKey'],
  ['in-overlay early warning', 'CHECK VITALS'],
  ['Next Move early warning', 'ResourceTrendWarning = false'],
  ['Trip Check early warning', 'A fresh resource trend is approaching the low threshold.'],
  ['deterministic verifier', 'Vitals trend: PASS']
];
for (const [label, contract] of requiredVitalsTrendContracts) {
  if (!vitalsTrendSurface.includes(contract)) {
    throw new Error(`Vitals trend is missing ${label}: ${contract}`);
  }
}

const fieldConditionsSurface = `${source}\n${xamlSource}\n${fieldConditionsLogicSource}\n${fieldConditionsVerifierSource}\n${nextMoveLogicSource}\n${nextMoveVerifierSource}`;
const requiredFieldConditionsContracts = [
  ['focused drawer section', 'x:Name="FieldConditionsSectionAnchor"'],
  ['direct weather report', 'FieldWeatherButton_Click'],
  ['direct light report', 'FieldLightButton_Click'],
  ['ten-minute expiry', 'FreshnessSeconds = 600'],
  ['independent report timestamps', '_fieldWeatherReportedAt'],
  ['conditional compact HUD', 'x:Name="FieldConditionsHudBorder"'],
  ['restrained HUD reveal', 'TimeSpan.FromMilliseconds(160)'],
  ['manual feed disclosure', 'does not expose weather or world time'],
  ['session-switch reset', 'ClearFieldConditions(logEvent: false, updateUi: false)'],
  ['streamer redaction', 'FieldConditionsHudBorder.Visibility = Visibility.Collapsed'],
  ['map-light handoff', 'FieldConditionsMatchMapButton_Click'],
  ['mutation-window integration', 'ACTIVE WINDOW -'],
  ['species night context', 'species is "dilophosaurus" or "troodon"'],
  ['Next Move warning handoff', 'FieldConditionsWarning'],
  ['anonymous tactical brief', 'fieldConditions.BriefLabel'],
  ['universal-session action', 'x:Name="UniversalSessionFieldConditionsButton"'],
  ['searchable command', 'Open Field Conditions'],
  ['direct section jump', '"field-conditions" => FieldConditionsSectionAnchor'],
  ['deterministic verifier', 'Field conditions: PASS']
];
for (const [label, contract] of requiredFieldConditionsContracts) {
  if (!fieldConditionsSurface.includes(contract)) {
    throw new Error(`Field conditions is missing ${label}: ${contract}`);
  }
}

const safeLogoutSurface = `${source}\n${xamlSource}\n${safeLogoutLogicSource}\n${safeLogoutVerifierSource}`;
const requiredSafeLogoutContracts = [
  ['focused drawer workspace', 'x:Name="SafeLogoutSectionAnchor"'],
  ['conditional map HUD', 'x:Name="SafeLogoutHudBorder"'],
  ['universal-session status', 'x:Name="SafeLogoutUniversalPanel"'],
  ['universal one-click action', 'x:Name="SafeLogoutUniversalButton"'],
  ['searchable command', 'Start Safe Logout Guard'],
  ['command execution', 'case "safe-logout"'],
  ['session-only state', '_safeLogoutGuardState = SafeLogoutGuardState.Ready'],
  ['authorized monitor boundary', 'LiveMapServicesActive'],
  ['fresh-feed boundary', '!_staleAlertActive'],
  ['movement threshold', 'MovementThresholdMuPerMinute = 0.25'],
  ['movement grace', 'MovementGraceSeconds = 2'],
  ['marker-loss fail closed', 'SafeLogoutGuardState.MonitorLost'],
  ['manual-session disclosure', 'No live movement monitor on this session'],
  ['truthful completion', 'VERIFY IN GAME'],
  ['no game-control claim', 'Isley never presses the logout control'],
  ['tactical event integration', 'Safe Logout Guard interrupted'],
  ['tactical brief integration', 'LOGOUT MONITORED'],
  ['deterministic verifier', 'Safe logout verification passed']
];
for (const [label, contract] of requiredSafeLogoutContracts) {
  if (!safeLogoutSurface.includes(contract)) {
    throw new Error(`Safe Logout Guard is missing ${label}: ${contract}`);
  }
}

const serverRestartWatchSurface = `${source}\n${xamlSource}\n${serverRestartWatchLogicSource}\n${serverRestartWatchVerifierSource}\n${nextMoveLogicSource}`;
const requiredServerRestartWatchContracts = [
  ['focused report workspace', 'x:Name="RestartWatchSectionAnchor"'],
  ['conditional live-map rail', 'x:Name="ServerRestartWatchHudBorder"'],
  ['universal-session rail', 'x:Name="ServerRestartUniversalPanel"'],
  ['30-minute report', 'Tag="1800"'],
  ['15-minute report', 'Tag="900"'],
  ['10-minute report', 'Tag="600"'],
  ['5-minute report', 'Tag="300"'],
  ['session-only state', '_serverRestartWatchActive'],
  ['player-reported boundary', 'Player-reported in-game warning'],
  ['five-minute escalation', 'ServerRestartWatchPhase.FinalFive'],
  ['two-minute escalation', 'ServerRestartWatchPhase.FinalTwo'],
  ['final-minute pulse', 'ServerRestartWatchPhase.FinalMinute'],
  ['truthful elapsed state', 'RESTART WINDOW ELAPSED'],
  ['Safe Logout handoff', 'RestartWatchSafeLogoutButton_Click'],
  ['Next Move handoff', 'RestartWatchRemainingSeconds'],
  ['tactical brief integration', 'RESTART {view.Countdown} · REPORTED'],
  ['streamer reset', 'CancelServerRestartWatch(logEvent: false, updateUi: false)'],
  ['server-switch reset', '_serverStatusCancellation?.Cancel();\n        CancelServerRestartWatch'],
  ['searchable command', 'Open Server Restart Watch'],
  ['direct section jump', '"restart-watch" => RestartWatchSectionAnchor'],
  ['deterministic verifier', 'Server restart watch: PASS']
];
for (const [label, contract] of requiredServerRestartWatchContracts) {
  if (!serverRestartWatchSurface.includes(contract)) {
    throw new Error(`Server Restart Watch is missing ${label}: ${contract}`);
  }
}

const hotkeyBindingSurface = `${source}\n${xamlSource}\n${nativeMethodsSource}\n${hotkeyBindingLogicSource}\n${hotkeyBindingVerifierSource}`;
const requiredHotkeyBindingContracts = [
  ['nine-action catalog', 'HotkeyBindingLogic.Definitions.Length == 9'],
  ['Vomit-recovery action', 'VomitRecoveryId = "vomit-recovery"'],
  ['conflict-free sickness default', 'ModControl | ModShift, 0x53, false'],
  ['Vomit-recovery dispatch', 'case HotkeyBindingLogic.VomitRecoveryId:'],
  ['shared sickness trigger', 'TriggerVomitRecoveryAsync(openPanelWhenStarted: false)'],
  ['dynamic sickness tooltip', 'CurrentHotkeyBinding(HotkeyBindingLogic.VomitRecoveryId)'],
  ['rebind-aware sickness cache', '_streamerMode}:{vomitHotkeyLabel}'],
  ['disabled sickness-copy guard', 'string.IsNullOrEmpty(vomitHotkeyLabel)'],
  ['focused App workspace', 'x:Name="HotkeysSectionAnchor"'],
  ['registration health', 'x:Name="HotkeyStudioStatusText"'],
  ['dynamic binding list', 'x:Name="HotkeyBindingListPanel"'],
  ['capture guidance', 'x:Name="HotkeyCaptureHintText"'],
  ['default restore action', 'HotkeyRestoreDefaultsButton_Click'],
  ['persistent binding snapshot', 'HotkeyBindings = HotkeyBindingLogic.ToSettings'],
  ['persistent binding restore', 'RestoreHotkeyBindings(settings.HotkeyBindings)'],
  ['live key capture', 'ApplyCapturedHotkey(new HotkeyBinding'],
  ['capture cancellation', 'CAPTURE CANCELED'],
  ['optional disable gesture', 'key is Key.Back or Key.Delete'],
  ['required recovery binding', 'RECOVERY SHORTCUT REQUIRED'],
  ['Ctrl-or-Alt boundary', '(binding.Modifiers & (ModControl | ModAlt)) == 0'],
  ['Windows-key rejection', 'WINDOWS KEY IS NOT ALLOWED'],
  ['bounded key catalog', 'USE A LETTER, NUMBER, OR F1-F12'],
  ['duplicate ownership feedback', 'ALREADY USED BY'],
  ['deterministic settings repair', 'FindAvailableFallback'],
  ['live registration swap', 'RegisterHotkey(definition, candidate)'],
  ['failed-registration rollback', 'RegisterHotkey(definition, previous)'],
  ['repeat suppression', 'NativeMethods.ModNoRepeat'],
  ['unified cleanup', 'UnregisterAllHotkeys()'],
  ['click-through recovery gate', 'IsHotkeyRegistered(HotkeyBindingLogic.InteractionId)'],
  ['focused-window fallback', 'TryHandleFocusedHotkey'],
  ['dynamic recovery copy', 'x:Name="HotkeyRecoveryText"'],
  ['searchable command', 'Open Hotkey Studio'],
  ['command handoff', 'OpenHotkeyStudio();'],
  ['reset integration', 'RestoreDefaultHotkeys(logEvent: false)'],
  ['deterministic verifier', 'Hotkey binding verification passed']
];
for (const [label, contract] of requiredHotkeyBindingContracts) {
  if (!hotkeyBindingSurface.includes(contract)) {
    throw new Error(`Hotkey Studio is missing ${label}: ${contract}`);
  }
}
const soundFinderSurface = `${body}\n${source}\n${xamlSource}\n${hotkeyBindingLogicSource}\n${soundFinderLogicSource}\n${soundFinderVerifierSource}`;
const requiredSoundFinderContracts = [
  ['single navigation card', 'Text="TRACK FINDER"'],
  ['mode switch', 'x:Name="TrackFinderModeButton"'],
  ['scent target selector', 'x:Name="TrackFinderTargetButton"'],
  ['sound and scent modes', 'internal enum TrackFinderMode'],
  ['four scent target types', 'internal enum ScentTargetKind'],
  ['bounded scent target cycle', 'ScentTargetKind.Trail => ScentTargetKind.Carcass'],
  ['two-step capture action', 'SoundFinderCaptureButton_Click'],
  ['route action', 'SoundFinderRouteButton_Click'],
  ['clear action', 'SoundFinderClearButton_Click'],
  ['minimum movement baseline', 'MinimumBaseline = 5'],
  ['bounded reading lifetime', 'MaximumReadingAge = TimeSpan.FromSeconds(120)'],
  ['minimum crossing angle', 'MinimumIntersectionAngle = 12'],
  ['forward-ray refusal', 'distanceA <= 0.5 || distanceB <= 0.5'],
  ['bounded estimate range', 'MaximumEstimateDistance = 1200'],
  ['geometry-aware uncertainty', 'UncertaintyRadius'],
  ['sound verification language', 'verify by sound'],
  ['scent verification language', 'verify with scent in game'],
  ['fresh authorized-position gate', '_currentMarkerFreshnessAgeMs <= 8000'],
  ['normalized live-map coordinate bridge', 'selfMapX'],
  ['session-only native readings', '_soundBearingFirst'],
  ['map ray endpoint clipping', 'soundBearingRayEnd'],
  ['quiet SVG overlay', 'data-isle-mapper-sound-finder'],
  ['official-place isolation', "text.closest('[data-isle-mapper-sound-finder=\"true\"]')"],
  ['controller state bridge', 'setSoundFinder(state)'],
  ['controller mode normalization', "value?.mode || ''"],
  ['controller scent target whitelist', "['water', 'food', 'trail', 'carcass'].includes(requestedTarget)"],
  ['scent Q-bearing labels', "soundFinderState.mode === 'scent' ? 'Q' : 'B'"],
  ['controller clear API', 'clearSoundFinder()'],
  ['estimate route API', 'routeSoundFinderEstimate()'],
  ['streamer native clear', 'ClearSoundFinderAsync(showToast: false, logEvent: false)'],
  ['streamer controller clear', "mode: 'sound', target: 'water', first: null, second: null, estimate: null"],
  ['searchable sound command', 'Open Sound Finder'],
  ['searchable scent command', 'Open Scent Finder'],
  ['mode-aware command handoff', 'SetTrackFinderModeAsync(TrackFinderMode.Scent'],
  ['global capture hotkey', 'TrackBearingId = "sound-bearing"'],
  ['deterministic verifier', 'Track Finder verification passed']
];
for (const [label, contract] of requiredSoundFinderContracts) {
  if (!soundFinderSurface.includes(contract)) {
    throw new Error(`Track Finder is missing ${label}: ${contract}`);
  }
}
const markerAccessibilitySurface = `${body}\n${source}\n${xamlSource}`;
const requiredMarkerAccessibilityContracts = [
  ['three marker styles', '_markerStyleModes = ["standard", "contrast", "shapes"]'],
  ['plain marker-style labels', '_markerStyleLabels = ["Standard", "High contrast", "Shape coded"]'],
  ['marker-style resolver', 'resolveMarkerStyle'],
  ['persistent marker style', 'MarkerStyleIndex'],
  ['marker-style control', 'MarkerStyleButton'],
  ['marker-style action', 'MarkerStyleButton_Click'],
  ['searchable accessibility action', 'Cycle marker accessibility'],
  ['controller marker-style whitelist', "['standard', 'contrast', 'shapes'].includes(requestedMarkerStyle)"],
  ['controller marker-style bridge', 'markerStyle,'],
  ['zoom-safe styled markers', 'scale(${inverseScale})'],
  ['shape-coded friend marker', "shape: 'circle-plus'"],
  ['shape-coded other marker', "shape: 'diamond-alert'"],
  ['high-contrast friend fill', "shape: 'circle', fill: '#f8fafc'"],
  ['high-contrast other fill', "shape: 'diamond', fill: '#fde047'"],
  ['non-black explicit native fill', "marker.style.fill = style.fill"],
  ['hidden native marker under custom style', "marker.style.opacity = style.shape === 'native' ? '1' : '0'"],
  ['identity-minimizing accessible titles', 'Authorized non-friend marker'],
  ['friend-only custom marker filtering', '!friendOnly || player.isFriend'],
  ['streamer-safe custom markers', "markerStyle === 'standard' || streamerMode"],
  ['restrained marker-style reveal', "{ duration: 180, easing: 'ease-out' }"],
  ['shape legend copy', 'green plus-circle is a friend'],
  ['saved marker style', 'MarkerStyleIndex = _markerStyleIndex'],
  ['reset marker style', '_markerStyleIndex = 0']
];
for (const [label, contract] of requiredMarkerAccessibilityContracts) {
  if (!markerAccessibilitySurface.includes(contract)) {
    throw new Error(`Marker accessibility is missing ${label}: ${contract}`);
  }
}
const smartFollowSurface = `${body}\n${source}\n${xamlSource}`;
const requiredSmartFollowContracts = [
  ['directional follow-target geometry', 'calculateFollowTarget'],
  ['speed-aware scale selection', 'chooseSmartFollowScale'],
  ['heading-up viewport-safe target', 'followViewportWidth = map.clientWidth'],
  ['look-ahead controller state', 'lookAheadEnabled'],
  ['smart-zoom controller state', 'smartZoomEnabled'],
  ['manual zoom suspension', "['wheel-zoom', 'button-zoom', 'preset-zoom'].includes(reason)"],
  ['recenter resumes smart zoom', 'smartZoomSuspended = false'],
  ['smart-follow bridge state', 'smartZoomSuspended,'],
  ['persistent look-ahead preference', 'LookAheadEnabled'],
  ['persistent smart-zoom preference', 'SmartZoomEnabled'],
  ['look-ahead control', 'FollowFramingButton'],
  ['smart-zoom control', 'SmartZoomButton'],
  ['smart-follow status copy', 'SmartFollowStatusText'],
  ['look-ahead control action', 'FollowFramingButton_Click'],
  ['smart-zoom control action', 'SmartZoomButton_Click'],
  ['searchable look-ahead action', 'Toggle look-ahead framing'],
  ['searchable smart-zoom action', 'Toggle Smart Zoom'],
  ['manual priority guidance', 'Manual zoom has priority'],
  ['look-ahead tracking state', 'TRACKING AHEAD']
];
for (const [label, contract] of requiredSmartFollowContracts) {
  if (!smartFollowSurface.includes(contract)) {
    throw new Error(`Smart Follow is missing ${label}: ${contract}`);
  }
}
const playFocusSurface = `${source}\n${xamlSource}\n${nativeMethodsSource}\n${liteModeLogicSource}`;
const requiredPlayFocusContracts = [
  ['opt-in Play Focus control', 'PlayFocusButton'],
  ['plain-language Play Focus state', 'PlayFocusStatusText'],
  ['persistent Play Focus preference', 'public bool PlayFocusEnabled { get; set; }'],
  ['restored Play Focus preference', '_playFocusEnabled = settings.PlayFocusEnabled'],
  ['saved Play Focus preference', 'PlayFocusEnabled = _playFocusEnabled'],
  ['responsive foreground polling', 'PlayFocusMilliseconds: 250'],
  ['Lite Mode foreground polling', 'PlayFocusMilliseconds: 750'],
  ['foreground-window detection', 'GetForegroundWindow'],
  ['foreground-process detection', 'GetWindowThreadProcessId'],
  ['game foreground presentation', 'case PlayFocusForeground.Game:'],
  ['mapper foreground presentation', 'case PlayFocusForeground.Mapper:'],
  ['unrelated-app suppression', '_playFocusSuppressed = true'],
  ['manual visibility ownership', '_visibilityRequested'],
  ['no-game configuration fallback', 'if (!_gameWasRunning)'],
  ['interaction recovery path', 'EnterPlayFocusInteraction()'],
  ['unified interaction toggle', 'ToggleInteractionMode()'],
  ['global foreground recovery', 'SetForegroundWindow(_windowHandle)'],
  ['searchable Play Focus action', 'Toggle Play Focus'],
  ['correct interaction recovery legend', 'Ctrl+Shift+I interact'],
  ['correct visibility recovery legend', 'Ctrl+Shift+O show/hide']
];
for (const [label, contract] of requiredPlayFocusContracts) {
  if (!playFocusSurface.includes(contract)) {
    throw new Error(`Play Focus is missing ${label}: ${contract}`);
  }
}
const overlayZOrderLogicSource = fs.readFileSync(
  path.join(__dirname, '..', 'BurntHud', 'OverlayZOrderLogic.cs'),
  'utf8');
const overlayZOrderSurface = `${source}\n${nativeMethodsSource}\n${overlayZOrderLogicSource}\n${dockCodeSource}`;
const requiredOverlayZOrderContracts = [
  ['overlay z-order reassert', 'EnsureOverlayPriority'],
  ['native topmost reassert helper', 'TryReassertTopMost'],
  ['HWND_TOPMOST constant', 'HwndTopMost'],
  ['no-activate topmost flags', 'SwpNoActivate'],
  ['overlay z-order gate logic', 'OverlayZOrderLogic.ShouldHoldAboveGame'],
  ['dock topmost reassert', 'EnsureTopMost'],
  ['game-start topmost toggle', 'EnsureOverlayPriority(forceToggle: true)']
];
for (const [label, contract] of requiredOverlayZOrderContracts) {
  if (!overlayZOrderSurface.includes(contract)) {
    throw new Error(`Overlay z-order is missing ${label}: ${contract}`);
  }
}
const packCohesionSurface = `${body}\n${source}\n${xamlSource}`;
const requiredPackCohesionContracts = [
  ['pack geometry calculation', 'calculatePackCohesion'],
  ['pack-center availability bridge', 'packCenterAvailable: Boolean(packCenterPoint)'],
  ['moving pack-center route API', 'routeToPackCenter()'],
  ['pack route state', 'packRouteActive'],
  ['true pack outlier geometry', 'farthestPoint = { x: point.x, y: point.y, name: point.name }'],
  ['moving pack-outlier route API', 'routeToPackOutlier()'],
  ['pack-outlier moving route state', 'packOutlierRouteActive'],
  ['pack-outlier route control', 'RoutePackOutlierButton'],
  ['pack-outlier HUD readout', 'PackOutlierText'],
  ['pack spread thresholds', '_packSpreadAlertDistances = [0, 25, 50, 100]'],
  ['persistent pack alert threshold', 'PackSpreadAlertIndex'],
  ['pack cohesion heading', 'PackCohesionHeadingText'],
  ['pack cohesion metrics', 'PackCohesionText'],
  ['pack dynamics calculation', 'calculatePackSpreadMotion'],
  ['pack course calculation', 'calculatePackCourse'],
  ['accepted-response pack samples', 'lastSample.responseToken !== responseToken'],
  ['pack roster reset', 'nextRosterKey !== packSpreadMotionRosterKey'],
  ['pack motion bridge', 'packSpreadMotionSampleCount'],
  ['pack dynamics HUD readout', 'PackMotionText'],
  ['pack course HUD readout', 'PackCourseText'],
  ['pack course direction bridge', 'packCourseCardinal'],
  ['pack course speed bridge', 'packCourseSpeed'],
  ['pack course brief context', 'pack moving {_packCourseCardinal}'],
  ['pack boundary timing', 'CalculatePackBoundarySeconds'],
  ['conditional boundary wording', 'boundary {boundaryTime.ToLowerInvariant()} if unchanged'],
  ['no pack position prediction', 'straight-line timing only; no position prediction'],
  ['streamer pack dynamics clearing', 'resetPackSpreadMotion(true)'],
  ['native streamer state clearing', '_packSpreadMotionSampleCount = 0'],
  ['tactical brief pack dynamics', 'holding formation'],
  ['pack-center route control', 'RoutePackCenterButton'],
  ['pack spread alert control', 'PackSpreadAlertButton'],
  ['one-shot spread warning sound', 'SystemSounds.Exclamation.Play()'],
  ['spread warning crossing state', '_packSpreadAlertInitialized'],
  ['streamer-safe pack HUD control', 'FriendRadarButton.IsEnabled = !_streamerMode'],
  ['searchable pack route action', 'Route to pack center'],
  ['searchable pack outlier action', 'Route to pack outlier'],
  ['searchable pack alert action', 'Cycle pack spread alert']
];
for (const [label, contract] of requiredPackCohesionContracts) {
  if (!packCohesionSurface.includes(contract)) {
    throw new Error(`Pack cohesion is missing ${label}: ${contract}`);
  }
}
const encounterAwarenessSurface = `${body}\n${source}\n${xamlSource}`;
const requiredEncounterAwarenessContracts = [
  ['encounter geometry calculation', 'calculateEncounterAwareness'],
  ['authorized non-friend selection', '!player.isSelf && !player.isFriend'],
  ['encounter bridge update', 'updateEncounterAwareness(players)'],
  ['nearest encounter distance bridge', 'nearestEncounterDistance'],
  ['encounter motion calculation', 'calculateEncounterMotion'],
  ['session-only motion tracks', 'const encounterMotionTracks = new Map()'],
  ['streamer motion clearing', 'encounterMotionTracks.clear()'],
  ['accepted-response motion samples', 'const responseToken = Number(markerResponseCount)'],
  ['nearest contact motion bridge', 'nearestEncounterRelativeSpeed'],
  ['conditional contact estimate', 'nearestEncounterInterceptSeconds'],
  ['one-glance direction badge', 'EncounterDirectionBadge'],
  ['motion provenance copy', 'no position is predicted between responses'],
  ['three encounter radius buckets', 'encounterWithin10'],
  ['encounter alert thresholds', '_encounterAlertDistances = [0, 10, 25, 50]'],
  ['persistent encounter threshold', 'EncounterAlertIndex'],
  ['persistent encounter HUD visibility', 'EncounterHudVisible'],
  ['conditional encounter HUD', 'EncounterAwarenessPanel'],
  ['encounter HUD control', 'EncounterHudButton'],
  ['contextual escape-route control', 'EncounterEscapeButton'],
  ['native escape-route action', 'StartEscapeRouteAsync'],
  ['bounded escape-route geometry', 'calculateEscapeRoute'],
  ['live-contact escape-route API', 'startEscapeRoute()'],
  ['escape route avoids local obstacles', 'terrainCourseObstacles()'],
  ['escape route refuses stale contact memory', 'NO_LIVE_CONTACT'],
  ['escape route does not predict movement', 'no position is predicted'],
  ['searchable escape-route action', 'Plan escape route'],
  ['encounter alert control', 'EncounterAlertButton'],
  ['one-shot encounter sound', 'SystemSounds.Asterisk.Play()'],
  ['encounter crossing state', '_encounterAlertInitialized'],
  ['streamer-safe encounter state', 'Encounter awareness hidden in streamer mode'],
  ['identity-minimizing encounter copy', 'Only provider-authorized non-friend markers'],
  ['searchable encounter HUD action', 'Toggle encounter HUD'],
  ['searchable encounter alert action', 'Cycle encounter alert'],
  ['last-seen memory geometry', 'summarizeEncounterMemory'],
  ['session-only encounter memory', 'const encounterMemoryTracks = new Map()'],
  ['bounded contact samples', '.slice(-300)'],
  ['fading last-seen overlay', 'data-isle-mapper-encounter-memory'],
  ['identity-minimizing last-seen marker', 'Last authorized sighting'],
  ['friend-only display privacy', '!streamerMode && !friendOnly'],
  ['streamer memory clearing', 'clearEncounterMemoryInternal(false)'],
  ['last-seen duration choices', '_encounterMemoryDurations = [0, 120, 300, 600]'],
  ['persistent last-seen duration', 'EncounterMemoryIndex'],
  ['last-seen memory control', 'EncounterMemoryButton'],
  ['session clear control', 'ClearEncounterMemoryButton'],
  ['session clear API', 'clearEncounterMemory()'],
  ['no stale contact alerts', 'no stale alert is issued'],
  ['searchable last-seen action', 'Cycle last-seen memory'],
  ['searchable recent-contact clear', 'Clear recent contacts']
];
for (const [label, contract] of requiredEncounterAwarenessContracts) {
  if (!encounterAwarenessSurface.includes(contract)) {
    throw new Error(`Encounter awareness is missing ${label}: ${contract}`);
  }
}
const escapeRouteStart = body.indexOf('const startEscapeRoute = () => {');
const escapeRouteEnd = body.indexOf('const terrainCourseFailureMessage', escapeRouteStart);
if (escapeRouteStart < 0 || escapeRouteEnd <= escapeRouteStart) {
  throw new Error('Unable to inspect Escape Route live-data boundary');
}
const escapeRouteSource = body.slice(escapeRouteStart, escapeRouteEnd);
for (const forbiddenSource of [
  'nearestRememberedEncounter', 'rememberedEncounterCount',
  'encounterMemoryTracks', 'lastLivePosition'
]) {
  if (escapeRouteSource.includes(forbiddenSource)) {
    throw new Error(`Escape Route can consume forbidden stale source: ${forbiddenSource}`);
  }
}
for (const liveGate of [
  'encounterPlayerCount < 1', 'nearestEncounterBearing',
  '!selfPose', 'terrainCourseObstacles()', 'setStaticWaypoint'
]) {
  if (!escapeRouteSource.includes(liveGate)) {
    throw new Error(`Escape Route is missing live safety gate: ${liveGate}`);
  }
}
const webViewIndex = xamlSource.indexOf('x:Name="LiveMapWebView"');
const mapLightIndex = xamlSource.indexOf('x:Name="MapLightOverlay"');
const streamerMaskIndex = xamlSource.indexOf('x:Name="StreamerMask"');
if (!(webViewIndex >= 0 && webViewIndex < mapLightIndex && mapLightIndex < streamerMaskIndex)) {
  throw new Error('Map lighting must stay above the WebView and below streamer privacy masking');
}
if (source.includes('_explorationEnabled = explorationEnabled;')) {
  throw new Error('Embedded startup state can overwrite the persisted native exploration preference');
}
if (source.includes('_breadcrumbTrailVisible = breadcrumbTrailVisible;')) {
  throw new Error('Embedded startup state can overwrite the persisted native session trail preference');
}
if (source.includes('_lookAheadEnabled = lookAheadEnabled;')) {
  throw new Error('Embedded startup state can overwrite the persisted native look-ahead preference');
}
if (source.includes('_smartZoomEnabled = smartZoomEnabled;')) {
  throw new Error('Embedded startup state can overwrite the persisted native Smart Zoom preference');
}
const focusModeApplyStart = source.indexOf('private void ApplyFocusModeDefinition');
const focusModeApplyEnd = source.indexOf('private void ApplyFocusModeSnapshot', focusModeApplyStart);
if (focusModeApplyStart < 0 || focusModeApplyEnd < 0) {
  throw new Error('Unable to inspect Focus Mode application safety');
}
const focusModeApplyBody = source.slice(focusModeApplyStart, focusModeApplyEnd);
for (const protectedPreference of [
  '_explorationEnabled', '_streamerMode', '_rememberLastPosition', '_clickThrough',
  '_mapLightModeIndex', '_packSpreadAlertIndex', '_lookAheadEnabled', '_smartZoomEnabled'
]) {
  if (focusModeApplyBody.includes(protectedPreference)) {
    throw new Error(`Focus Modes must not change protected preference ${protectedPreference}`);
  }
}

function compileArrowBetween(name, nextName) {
  const prefix = `const ${name} = `;
  const start = body.indexOf(prefix);
  const end = body.indexOf(`const ${nextName} = `, start + prefix.length);
  if (start < 0 || end < 0) {
    throw new Error(`Unable to extract ${name} for behavioral verification`);
  }
  const expression = body.slice(start + prefix.length, end).trim().replace(/;$/, '');
  return new Function('isCalibration', `return (${expression});`)(calibration => Boolean(
    calibration?.a && calibration?.b
    && Number.isFinite(Number(calibration.a.worldX))
    && Number.isFinite(Number(calibration.a.worldY))
    && Number.isFinite(Number(calibration.a.u))
    && Number.isFinite(Number(calibration.a.v))
    && Number.isFinite(Number(calibration.b.worldX))
    && Number.isFinite(Number(calibration.b.worldY))
    && Number.isFinite(Number(calibration.b.u))
    && Number.isFinite(Number(calibration.b.v))));
}

function compileArrowBetweenWithBindings(name, nextName, bindings) {
  const prefix = `const ${name} = `;
  const start = body.indexOf(prefix);
  const end = body.indexOf(`const ${nextName} = `, start + prefix.length);
  if (start < 0 || end < 0) {
    throw new Error(`Unable to extract ${name} for behavioral verification`);
  }
  const expression = body.slice(start + prefix.length, end).trim().replace(/;$/, '');
  const names = Object.keys(bindings);
  return new Function(...names, `return (${expression});`)(...names.map(name => bindings[name]));
}

const worldToMap = compileArrowBetween('worldToMapPoint', 'mapToWorldPoint');
const calculateFollowTarget = compileArrowBetween(
  'calculateFollowTarget',
  'chooseSmartFollowScale');
const chooseSmartFollowScale = compileArrowBetween(
  'chooseSmartFollowScale',
  'normalizeLearnedPassageLibrary');
const normalizeLearnedPassageLibrary = compileArrowBetweenWithBindings(
  'normalizeLearnedPassageLibrary',
  'learnedPassageIsCurrent',
  {
    learnedPassageMaximumCount: 12,
    learnedPassageMaximumPoints: 120,
    learnedPassageRetentionMs: 180 * 24 * 60 * 60 * 1000
  });
const learnedPassageIsCurrent = compileArrowBetweenWithBindings(
  'learnedPassageIsCurrent',
  'buildLearnedPassageFromTrail',
  { learnedPassageActiveAgeMs: 90 * 24 * 60 * 60 * 1000 });
const buildLearnedPassageFromTrail = compileArrowBetweenWithBindings(
  'buildLearnedPassageFromTrail',
  'normalizeExplorationSectors',
  { learnedPassageMaximumPoints: 120 });
const mapToWorld = compileArrowBetween('mapToWorldPoint', 'readModelSelfPose');
const buildBreadcrumbRouteStops = compileArrowBetween(
  'buildBreadcrumbRouteStops',
  'persistLearnedPassages');
const simplifyBreadcrumbTrailPoints = compileArrowBetween(
  'simplifyBreadcrumbTrailPoints',
  'buildBreadcrumbRouteStops');
const selectDeathMarkerPoint = compileArrowBetween(
  'selectDeathMarkerPoint',
  'recordBreadcrumbSample');
const mapPointToGridReference = compileArrowBetween(
  'mapPointToGridReference',
  'resolveGridReference');
const resolveGridReference = compileArrowBetween(
  'resolveGridReference',
  'parseSharedRouteTokens');
const parseSharedRouteTokens = compileArrowBetween(
  'parseSharedRouteTokens',
  'ensureExplorationRoot');
const positionFloatingPanel = compileArrowBetween(
  'positionFloatingPanel',
  'calculateOffscreenWaypointCue');
const evaluateMapActionRelease = compileArrowBetween(
  'evaluateMapActionRelease',
  'endPointer');
const calculateOffscreenWaypointCue = compileArrowBetween(
  'calculateOffscreenWaypointCue',
  'buildTacticalPoint');
const selectNearestLandmark = compileArrowBetween(
  'selectNearestLandmark',
  'readSvgTextPose');
const recordRecentRoute = compileArrowBetween(
  'recordRecentRoute',
  'buildRecentRouteRoster');
const calculateSessionStats = compileArrowBetween(
  'calculateSessionStats',
  'chooseMapScaleBar');
const calculateNavigationEta = compileArrowBetween(
  'calculateNavigationEta',
  'calculateWaypointApproach');
const calculateWaypointApproach = compileArrowBetween(
  'calculateWaypointApproach',
  'buildNavigationEtaState');
const calculatePackSpreadMotion = compileArrowBetween(
  'calculatePackSpreadMotion',
  'calculatePackCourse');
const calculatePackCourse = compileArrowBetween(
  'calculatePackCourse',
  'calculatePackCohesion');
const calculatePackCohesion = compileArrowBetween(
  'calculatePackCohesion',
  'updateNearestFriend');
const summarizeEncounterMemory = compileArrowBetween(
  'summarizeEncounterMemory',
  'calculateEncounterMotion');
const calculateEncounterMotion = compileArrowBetween(
  'calculateEncounterMotion',
  'calculateEncounterAwareness');
const calculateEncounterAwareness = compileArrowBetween(
  'calculateEncounterAwareness',
  'resetPackSpreadMotion');
const resolveMarkerStyle = compileArrowBetween(
  'resolveMarkerStyle',
  'ensureSelfNavigationMarker');
const chooseMapScaleBar = compileArrowBetween(
  'chooseMapScaleBar',
  'buildSessionStatsState');
const buildCommunityTerrainDangerRoster = compileArrowBetween(
  'buildCommunityTerrainDangerRoster',
  'selectNearestDangerPin');
const selectNearestDangerPin = compileArrowBetween(
  'selectNearestDangerPin',
  'buildDangerState');
const selectNearestAlertZone = compileArrowBetween(
  'selectNearestAlertZone',
  'buildAlertZoneState');
const buildAlertZoneState = compileArrowBetweenWithBindings(
  'buildAlertZoneState',
  'notify',
  { streamerMode: false, selectNearestAlertZone });
const routeDistanceBetween = compileArrowBetween(
  'routeDistanceBetween',
  'distancePointToSegment');
const distancePointToSegment = compileArrowBetween(
  'distancePointToSegment',
  'segmentIntersectsCircle');
const segmentIntersectsCircle = compileArrowBetweenWithBindings(
  'segmentIntersectsCircle',
  'routeOrientation',
  { distancePointToSegment });
const routeOrientation = compileArrowBetween('routeOrientation', 'routePointOnSegment');
const routePointOnSegment = compileArrowBetween('routePointOnSegment', 'routeSegmentsIntersect');
const routeSegmentsIntersect = compileArrowBetweenWithBindings(
  'routeSegmentsIntersect',
  'routePolygonArea',
  { routeOrientation, routePointOnSegment });
const routePolygonArea = compileArrowBetween('routePolygonArea', 'routePolygonSelfIntersects');
const routePolygonSelfIntersects = compileArrowBetweenWithBindings(
  'routePolygonSelfIntersects',
  'routePointInPolygon',
  { routeSegmentsIntersect });
const routePointInPolygon = compileArrowBetweenWithBindings(
  'routePointInPolygon',
  'routeSegmentIntersectsPolygon',
  { distancePointToSegment });
const routeSegmentIntersectsPolygon = compileArrowBetweenWithBindings(
  'routeSegmentIntersectsPolygon',
  'calculateEscapeRoute',
  { routePointInPolygon, routeSegmentsIntersect, distancePointToSegment });
const calculateEscapeRoute = compileArrowBetweenWithBindings(
  'calculateEscapeRoute',
  'simplifyTerrainCoursePoints',
  {
    routeDistanceBetween,
    segmentIntersectsCircle,
    routePolygonArea,
    routePolygonSelfIntersects,
    routePointInPolygon,
    routeSegmentIntersectsPolygon,
    noGoAreaMaximumVertices: 12
  });
const simplifyTerrainCoursePoints = compileArrowBetweenWithBindings(
  'simplifyTerrainCoursePoints',
  'terrainWaterPixelForPoint',
  { distancePointToSegment });
const normalizeTerrainRouteStyle = compileArrowBetween(
  'normalizeTerrainRouteStyle',
  'setTerrainRouteStyle');
const normalizeTerrainGapPolicy = compileArrowBetween(
  'normalizeTerrainGapPolicy',
  'terrainGapLimit');
const terrainGapLimit = compileArrowBetweenWithBindings(
  'terrainGapLimit',
  'setTerrainGapPolicy',
  { normalizeTerrainGapPolicy });
const buildBlockedPassageArea = compileArrowBetween(
  'buildBlockedPassageArea',
  'buildMeasuredSlopeArea');
const buildMeasuredSlopeArea = compileArrowBetweenWithBindings(
  'buildMeasuredSlopeArea',
  'calculateTerrainRoadCourse',
  { sanitizePinLabel: value => String(value || '').trim().slice(0, 40) });
const calculateTerrainRoadCourse = compileArrowBetweenWithBindings(
  'calculateTerrainRoadCourse',
  'buildRoutePlanState',
  {
    routeDistanceBetween,
    distancePointToSegment,
    segmentIntersectsCircle,
    routePolygonArea,
    routePolygonSelfIntersects,
    routePointInPolygon,
    routeSegmentIntersectsPolygon,
    noGoAreaMaximumVertices: 12,
    simplifyTerrainCoursePoints,
    normalizeTerrainRouteStyle,
    normalizeTerrainGapPolicy,
    terrainGapLimit,
    terrainWaterSafetyEnabled: false,
    terrainWaterMaskStatus: 'unavailable',
    isTerrainWaterPoint: () => false,
    segmentCrossesTerrainWater: () => false
  });
const calculateDirectRouteObstacleRisk = compileArrowBetweenWithBindings(
  'calculateDirectRouteObstacleRisk',
  'buildTripRouteRiskState',
  {
    routeDistanceBetween,
    segmentIntersectsCircle,
    routePolygonArea,
    routePolygonSelfIntersects,
    routePointInPolygon,
    routeSegmentIntersectsPolygon,
    noGoAreaMaximumVertices: 12
  });
const normalizeMapLabel = compileArrowBetween(
  'normalizeMapLabel',
  'mapLabelEditDistance');
const selectLandmarkLabels = compileArrowBetweenWithBindings(
  'selectLandmarkLabels',
  'selectNearestLandmark',
  { normalizeMapLabel });
const mapLabelEditDistance = compileArrowBetween(
  'mapLabelEditDistance',
  'scoreMapLabel');
const scoreMapLabel = compileArrowBetweenWithBindings(
  'scoreMapLabel',
  'selectLandmarkLabels',
  { normalizeMapLabel, mapLabelEditDistance });
const rankNamedPlaces = compileArrowBetweenWithBindings(
  'rankNamedPlaces',
  'rankSavedDestinations',
  { normalizeMapLabel, scoreMapLabel, mapPointToGridReference });
const rankSavedDestinations = compileArrowBetweenWithBindings(
  'rankSavedDestinations',
  'searchDestinations',
  {
    scoreMapLabel,
    mapPointToGridReference,
    pinTypes: {
      safe: { label: 'Safe' },
      nest: { label: 'Nest' },
      food: { label: 'Food' },
      danger: { label: 'Danger' },
      water: { label: 'Water' },
      rally: { label: 'Rally' },
      death: { label: 'Death' }
    }
  });
const sanitizePinLabel = compileArrowBetween(
  'sanitizePinLabel',
  'buildPinLibraryBackup');
const partitionPinsByExpiry = compileArrowBetween(
  'partitionPinsByExpiry',
  'purgeExpiredPins');
const normalizeExplorationSectors = compileArrowBetween(
  'normalizeExplorationSectors',
  'explorationSectorIndex');
const explorationSectorIndex = compileArrowBetween(
  'explorationSectorIndex',
  'buildExplorationState');
const pinTypesFixture = {
  safe: { label: 'Safe' },
  nest: { label: 'Nest' },
  food: { label: 'Food' },
  danger: { label: 'Danger' },
  water: { label: 'Water' },
  rally: { label: 'Rally' },
  death: { label: 'Death' }
};
const buildPinLibraryBackup = compileArrowBetweenWithBindings(
  'buildPinLibraryBackup',
  'parsePinLibraryBackup',
  {
    mapToWorldPoint: mapToWorld,
    pinTypes: pinTypesFixture,
    pinExpiryMinutes: [0, 5, 15, 30, 60],
    pinAlertRadii: [0, 10, 25, 50, 100],
    noGoAreaMaximumCount: 8,
    noGoAreaMaximumVertices: 12,
    sanitizePinLabel
  });
const parsePinLibraryBackup = compileArrowBetweenWithBindings(
  'parsePinLibraryBackup',
  'buildPinLibraryImportPlan',
  {
    worldToMapPoint: worldToMap,
    pinTypes: pinTypesFixture,
    pinExpiryMinutes: [0, 5, 15, 30, 60],
    pinAlertRadii: [0, 10, 25, 50, 100],
    noGoAreaMaximumCount: 8,
    noGoAreaMaximumVertices: 12,
    routePolygonArea,
    routePolygonSelfIntersects,
    sanitizePinLabel
  });
const buildPinLibraryImportPlan = compileArrowBetweenWithBindings(
  'buildPinLibraryImportPlan',
  'addSavedPin',
  {
    parsePinLibraryBackup,
    sanitizePinLabel,
    routeDistanceBetween,
    noGoAreaMaximumCount: 8
  });
const calibrationFixture = {
  a: { worldX: -450000, worldY: 310000, u: 0.08, v: 0.12 },
  b: { worldX: 520000, worldY: -610000, u: 0.94, v: 0.91 }
};
const centeredFollowTarget = calculateFollowTarget(400, 200, 90, false, false, 60);
if (centeredFollowTarget.x !== 200 || centeredFollowTarget.y !== 100
    || centeredFollowTarget.offsetPx !== 0) {
  throw new Error(`Centered follow framing failed: ${JSON.stringify(centeredFollowTarget)}`);
}
const northAheadTarget = calculateFollowTarget(400, 200, 0, false, true, 0);
const eastAheadTarget = calculateFollowTarget(400, 200, 90, false, true, 0);
const headingUpFastTarget = calculateFollowTarget(400, 200, 247, true, true, 60);
if (Math.abs(northAheadTarget.x - 200) > 0.000001
    || Math.abs(northAheadTarget.y - 124) > 0.000001
    || Math.abs(eastAheadTarget.x - 176) > 0.000001
    || Math.abs(eastAheadTarget.y - 100) > 0.000001
    || Math.abs(headingUpFastTarget.x - 200) > 0.000001
    || Math.abs(headingUpFastTarget.y - 140) > 0.000001
    || calculateFollowTarget(0, 200, 0, false, true, 0) !== null) {
  throw new Error(`Directional look-ahead framing failed: ${JSON.stringify({
    northAheadTarget, eastAheadTarget, headingUpFastTarget
  })}`);
}
const smartScaleFixture = [
  [0, 12, 6],
  [5, 6, 12],
  [15, 12, 12],
  [20, 12, 6],
  [30, 2.5, 2.5],
  [50, 12, 2.5],
  [20, 2.5, 6]
];
for (const [speed, currentScale, expectedScale] of smartScaleFixture) {
  const actualScale = chooseSmartFollowScale(speed, currentScale);
  if (actualScale !== expectedScale) {
    throw new Error(`Smart Zoom scale failed for ${speed}/${currentScale}: ${actualScale}`);
  }
}
const spreadingPackMotion = calculatePackSpreadMotion([
  { at: 1000, spread: 20 },
  { at: 3000, spread: 22 },
  { at: 5000, spread: 24 },
  { at: 7000, spread: 26 }
], 7000);
if (spreadingPackMotion.state !== 'spreading'
    || Math.abs(spreadingPackMotion.rate - 60) > 0.000001
    || spreadingPackMotion.sampleCount !== 4) {
  throw new Error(`Spreading pack motion failed: ${JSON.stringify(spreadingPackMotion)}`);
}
const regroupingPackMotion = calculatePackSpreadMotion([
  { at: 1000, spread: 30 },
  { at: 3000, spread: 28 },
  { at: 5000, spread: 26 },
  { at: 7000, spread: 24 }
], 7000);
if (regroupingPackMotion.state !== 'regrouping'
    || Math.abs(regroupingPackMotion.rate + 60) > 0.000001) {
  throw new Error(`Regrouping pack motion failed: ${JSON.stringify(regroupingPackMotion)}`);
}
const steadyPackMotion = calculatePackSpreadMotion([
  { at: 1000, spread: 20 },
  { at: 3000, spread: 20.02 },
  { at: 5000, spread: 20.04 }
], 5000);
if (steadyPackMotion.state !== 'steady' || steadyPackMotion.rate !== 0) {
  throw new Error(`Steady pack motion failed: ${JSON.stringify(steadyPackMotion)}`);
}
const waitingPackMotion = calculatePackSpreadMotion([
  { at: 1000, spread: 20 },
  { at: 3000, spread: 22 }
], 3000);
if (waitingPackMotion.state !== '' || waitingPackMotion.rate !== null
    || waitingPackMotion.sampleCount !== 2) {
  throw new Error(`Waiting pack motion failed: ${JSON.stringify(waitingPackMotion)}`);
}
const stalePackMotion = calculatePackSpreadMotion([
  { at: 1000, spread: 20 },
  { at: 3000, spread: 22 },
  { at: 5000, spread: 24 }
], 60000);
if (stalePackMotion.state !== '' || stalePackMotion.sampleCount !== 0) {
  throw new Error(`Stale pack motion failed: ${JSON.stringify(stalePackMotion)}`);
}
const movingPackCourse = calculatePackCourse([
  { at: 1000, centerX: 0, centerY: 0 },
  { at: 3000, centerX: 1, centerY: -1 },
  { at: 5000, centerX: 2, centerY: -2 },
  { at: 7000, centerX: 3, centerY: -3 }
], 7000);
if (movingPackCourse.state !== 'moving'
    || Math.abs(movingPackCourse.speed - Math.hypot(30, -30)) > 0.000001
    || Math.abs(movingPackCourse.bearing - 45) > 0.000001
    || movingPackCourse.cardinal !== 'NE'
    || movingPackCourse.sampleCount !== 4) {
  throw new Error(`Moving pack course failed: ${JSON.stringify(movingPackCourse)}`);
}
const stationaryPackCourse = calculatePackCourse([
  { at: 1000, centerX: 10, centerY: 20 },
  { at: 3000, centerX: 10.01, centerY: 20 },
  { at: 5000, centerX: 10.02, centerY: 20 }
], 5000);
if (stationaryPackCourse.state !== 'stationary'
    || stationaryPackCourse.speed !== 0
    || stationaryPackCourse.bearing !== null
    || stationaryPackCourse.cardinal !== '') {
  throw new Error(`Stationary pack course failed: ${JSON.stringify(stationaryPackCourse)}`);
}
const waitingPackCourse = calculatePackCourse([
  { at: 1000, centerX: 0, centerY: 0 },
  { at: 3000, centerX: 1, centerY: 0 }
], 3000);
if (waitingPackCourse.state !== '' || waitingPackCourse.speed !== null
    || waitingPackCourse.sampleCount !== 2) {
  throw new Error(`Waiting pack course failed: ${JSON.stringify(waitingPackCourse)}`);
}
const stalePackCourse = calculatePackCourse([
  { at: 1000, centerX: 0, centerY: 0 },
  { at: 3000, centerX: 1, centerY: 0 },
  { at: 5000, centerX: 2, centerY: 0 }
], 60000);
if (stalePackCourse.state !== '' || stalePackCourse.sampleCount !== 0) {
  throw new Error(`Stale pack course failed: ${JSON.stringify(stalePackCourse)}`);
}
const emptyPack = calculatePackCohesion([], { x: 3, y: 14 });
if (emptyPack.friendCount !== 0 || emptyPack.center !== null
    || emptyPack.radius !== null || emptyPack.spread !== null) {
  throw new Error(`Empty pack geometry failed: ${JSON.stringify(emptyPack)}`);
}
const soloPack = calculatePackCohesion([
  { name: 'Solo', x: 7, y: 9 }
], null);
if (soloPack.friendCount !== 1 || soloPack.center.x !== 7 || soloPack.center.y !== 9
    || soloPack.radius !== 0 || soloPack.spread !== 0
    || soloPack.centerDistance !== null || soloPack.farthestName !== '') {
  throw new Error(`Solo pack geometry failed: ${JSON.stringify(soloPack)}`);
}
const twoFriendPack = calculatePackCohesion([
  { name: 'Alpha', x: 0, y: 0 },
  { name: 'Bravo', x: 6, y: 8 }
], { x: 3, y: 14 });
if (twoFriendPack.friendCount !== 2
    || Math.abs(twoFriendPack.center.x - 3) > 0.000001
    || Math.abs(twoFriendPack.center.y - 4) > 0.000001
    || Math.abs(twoFriendPack.radius - 5) > 0.000001
    || Math.abs(twoFriendPack.spread - 10) > 0.000001
    || Math.abs(twoFriendPack.centerDistance - 10) > 0.000001
    || Math.abs(twoFriendPack.centerBearing) > 0.000001
    || twoFriendPack.centerCardinal !== 'N'
    || twoFriendPack.farthestName !== 'Alpha'
    || Math.abs(twoFriendPack.farthestDistance - 5) > 0.000001
    || twoFriendPack.farthestPoint.name !== 'Alpha') {
  throw new Error(`Two-friend pack geometry failed: ${JSON.stringify(twoFriendPack)}`);
}
const outlierPack = calculatePackCohesion([
  { name: 'Near left', x: 0, y: 0 },
  { name: 'Near right', x: 2, y: 0 },
  { name: 'Straggler', x: 20, y: 0 }
], { x: -100, y: 0 });
if (outlierPack.farthestName !== 'Straggler'
    || Math.abs(outlierPack.farthestDistance - (38 / 3)) > 0.000001
    || outlierPack.farthestPoint.x !== 20
    || outlierPack.farthestPoint.y !== 0) {
  throw new Error(`Pack outlier geometry failed: ${JSON.stringify(outlierPack)}`);
}
const standardFriendStyle = resolveMarkerStyle('standard', 'friend');
const standardOtherStyle = resolveMarkerStyle('standard', 'other');
const contrastFriendStyle = resolveMarkerStyle('contrast', 'friend');
const contrastOtherStyle = resolveMarkerStyle('contrast', 'other');
const shapeFriendStyle = resolveMarkerStyle('shapes', 'friend');
const shapeOtherStyle = resolveMarkerStyle('shapes', 'other');
const fallbackSelfStyle = resolveMarkerStyle('unknown', 'self');
if (standardFriendStyle.shape !== 'native' || standardFriendStyle.fill !== '#34d399'
    || standardOtherStyle.shape !== 'native' || standardOtherStyle.fill !== '#fbbf24'
    || contrastFriendStyle.shape !== 'circle' || contrastFriendStyle.fill !== '#f8fafc'
    || contrastOtherStyle.shape !== 'diamond' || contrastOtherStyle.fill !== '#fde047'
    || shapeFriendStyle.shape !== 'circle-plus'
    || shapeOtherStyle.shape !== 'diamond-alert'
    || fallbackSelfStyle.mode !== 'standard' || fallbackSelfStyle.shape !== 'self') {
  throw new Error(`Marker-style resolution failed: ${JSON.stringify({
    standardFriendStyle,
    standardOtherStyle,
    contrastFriendStyle,
    contrastOtherStyle,
    shapeFriendStyle,
    shapeOtherStyle,
    fallbackSelfStyle
  })}`);
}
for (const style of [
  standardFriendStyle,
  standardOtherStyle,
  contrastFriendStyle,
  contrastOtherStyle,
  shapeFriendStyle,
  shapeOtherStyle,
  fallbackSelfStyle
]) {
  if (!/^#[0-9a-f]{6}$/i.test(style.fill)
      || ['#000000', '#020617', '#000'].includes(style.fill.toLowerCase())) {
    throw new Error(`Marker style can fall back to a black or invalid fill: ${JSON.stringify(style)}`);
  }
}
const offlineEncounter = calculateEncounterAwareness([
  { x: 0, y: -5 }, { x: 6, y: 8 }
], null);
if (offlineEncounter.trackableCount !== 2
    || offlineEncounter.nearestDistance !== null
    || offlineEncounter.nearestBearing !== null
    || offlineEncounter.within10 !== 0
    || offlineEncounter.within25 !== 0
    || offlineEncounter.within50 !== 0) {
  throw new Error(`Offline encounter geometry failed: ${JSON.stringify(offlineEncounter)}`);
}
const emptyEncounter = calculateEncounterAwareness([], { x: 0, y: 0 });
if (emptyEncounter.trackableCount !== 0 || emptyEncounter.nearestDistance !== null
    || emptyEncounter.within10 !== 0 || emptyEncounter.within50 !== 0) {
  throw new Error(`Empty encounter geometry failed: ${JSON.stringify(emptyEncounter)}`);
}
const encounterFormation = calculateEncounterAwareness([
  { x: 0, y: -5 },
  { x: 6, y: 8 },
  { x: 0, y: 30 },
  { x: -40, y: 0 },
  { x: 'invalid', y: 2 }
], { x: 0, y: 0 });
if (encounterFormation.trackableCount !== 4
    || Math.abs(encounterFormation.nearestDistance - 5) > 0.000001
    || Math.abs(encounterFormation.nearestBearing) > 0.000001
    || encounterFormation.nearestCardinal !== 'N'
    || encounterFormation.within10 !== 2
    || encounterFormation.within25 !== 2
    || encounterFormation.within50 !== 4) {
  throw new Error(`Encounter radius geometry failed: ${JSON.stringify(encounterFormation)}`);
}
const closingEncounterMotion = calculateEncounterMotion([
  { at: 4000, distance: 30 },
  { at: 6000, distance: 28 },
  { at: 8000, distance: 26 },
  { at: 10000, distance: 24 }
], 10000);
if (closingEncounterMotion.state !== 'closing'
    || Math.abs(closingEncounterMotion.relativeSpeed - 60) > 0.000001
    || Math.abs(closingEncounterMotion.interceptSeconds - 24) > 0.000001
    || closingEncounterMotion.sampleCount !== 4) {
  throw new Error(`Closing encounter motion failed: ${JSON.stringify(closingEncounterMotion)}`);
}
const openingEncounterMotion = calculateEncounterMotion([
  { at: 4000, distance: 20 },
  { at: 6000, distance: 22 },
  { at: 8000, distance: 24 },
  { at: 10000, distance: 26 }
], 10000);
if (openingEncounterMotion.state !== 'opening'
    || Math.abs(openingEncounterMotion.relativeSpeed + 60) > 0.000001
    || openingEncounterMotion.interceptSeconds !== null) {
  throw new Error(`Opening encounter motion failed: ${JSON.stringify(openingEncounterMotion)}`);
}
const steadyEncounterMotion = calculateEncounterMotion([
  { at: 4000, distance: 20 },
  { at: 6000, distance: 20.02 },
  { at: 8000, distance: 20.01 },
  { at: 10000, distance: 20 }
], 10000);
if (steadyEncounterMotion.state !== 'steady'
    || steadyEncounterMotion.relativeSpeed !== 0
    || steadyEncounterMotion.interceptSeconds !== null) {
  throw new Error(`Steady encounter motion failed: ${JSON.stringify(steadyEncounterMotion)}`);
}
const waitingEncounterMotion = calculateEncounterMotion([
  { at: 8000, distance: 20 },
  { at: 10000, distance: 19 }
], 10000);
if (waitingEncounterMotion.state !== ''
    || waitingEncounterMotion.relativeSpeed !== null
    || waitingEncounterMotion.sampleCount !== 2) {
  throw new Error(`Waiting encounter motion failed: ${JSON.stringify(waitingEncounterMotion)}`);
}
const encounterWithMotion = calculateEncounterAwareness([
  { x: 0, y: -8, motion: closingEncounterMotion },
  { x: 30, y: 0, motion: openingEncounterMotion }
], { x: 0, y: 0 });
if (encounterWithMotion.nearestMotion?.state !== 'closing'
    || Math.abs(encounterWithMotion.nearestMotion.relativeSpeed - 60) > 0.000001) {
  throw new Error(`Nearest encounter motion selection failed: ${JSON.stringify(encounterWithMotion)}`);
}
const encounterMemoryFixture = summarizeEncounterMemory([
  {
    name: 'Live contact', lastSeenAt: 999000,
    samples: [{ x: 0, y: -4, at: 999000 }]
  },
  {
    name: 'Recent west', lastSeenAt: 970000,
    samples: [{ x: -12, y: 0, at: 970000 }]
  },
  {
    name: 'Recent south', lastSeenAt: 900000,
    samples: [{ x: 0, y: 20, at: 900000 }]
  },
  {
    name: 'Expired', lastSeenAt: 600000,
    samples: [{ x: 1, y: 1, at: 600000 }]
  },
  {
    name: 'Invalid', lastSeenAt: 999000,
    samples: [{ x: 'bad', y: 1, at: 999000 }]
  }
], ['Live contact'], 300, 1000000, { x: 0, y: 0 });
if (encounterMemoryFixture.trackCount !== 3
    || encounterMemoryFixture.rememberedCount !== 2
    || encounterMemoryFixture.newestAgeMs !== 30000
    || Math.abs(encounterMemoryFixture.nearestDistance - 12) > 0.000001
    || Math.abs(encounterMemoryFixture.nearestBearing - 270) > 0.000001
    || encounterMemoryFixture.nearestCardinal !== 'W') {
  throw new Error(`Encounter-memory summary failed: ${JSON.stringify(encounterMemoryFixture)}`);
}
const offlineEncounterMemory = summarizeEncounterMemory([
  { name: 'Remembered', lastSeenAt: 950000, samples: [{ x: 5, y: 5, at: 950000 }] }
], [], 120, 1000000, null);
if (offlineEncounterMemory.trackCount !== 1
    || offlineEncounterMemory.rememberedCount !== 1
    || offlineEncounterMemory.nearestDistance !== null
    || offlineEncounterMemory.nearestBearing !== null) {
  throw new Error(`Offline encounter-memory summary failed: ${JSON.stringify(offlineEncounterMemory)}`);
}
const disabledEncounterMemory = summarizeEncounterMemory([
  { name: 'Remembered', lastSeenAt: 999000, samples: [{ x: 5, y: 5, at: 999000 }] }
], [], 0, 1000000, { x: 0, y: 0 });
if (disabledEncounterMemory.trackCount !== 0 || disabledEncounterMemory.rememberedCount !== 0) {
  throw new Error(`Disabled encounter memory retained contacts: ${JSON.stringify(disabledEncounterMemory)}`);
}
for (const point of [
  { x: -450000, y: 310000 },
  { x: 0, y: 0 },
  { x: 311234.5, y: -489876.25 }
]) {
  const mapPoint = worldToMap(calibrationFixture, point.x, point.y);
  const roundTrip = mapToWorld(calibrationFixture, mapPoint.x, mapPoint.y);
  if (Math.abs(roundTrip.x - point.x) > 0.000001
      || Math.abs(roundTrip.y - point.y) > 0.000001) {
    throw new Error(`Pin calibration round-trip failed for ${JSON.stringify(point)}`);
  }
}
const gatewayCalibrationFixture = {
  a: { worldX: -607000, worldY: -505000, u: 0, v: 0 },
  b: { worldX: 509000, worldY: 607000, u: 1, v: 1 },
  swapAxes: true
};
for (const [worldPoint, expectedMapPoint] of [
  [{ x: -607000, y: -505000 }, { x: 0, y: 0 }],
  [{ x: 509000, y: 607000 }, { x: 1000, y: 1000 }],
  [{ x: -49000, y: 51000 }, { x: 500, y: 500 }]
]) {
  const mapPoint = worldToMap(gatewayCalibrationFixture, worldPoint.x, worldPoint.y);
  const roundTrip = mapToWorld(gatewayCalibrationFixture, mapPoint.x, mapPoint.y);
  if (Math.abs(mapPoint.x - expectedMapPoint.x) > 0.000001
      || Math.abs(mapPoint.y - expectedMapPoint.y) > 0.000001
      || Math.abs(roundTrip.x - worldPoint.x) > 0.000001
      || Math.abs(roundTrip.y - worldPoint.y) > 0.000001) {
    throw new Error(`Gateway axis calibration failed for ${JSON.stringify(worldPoint)}`);
  }
}
const breadcrumbFixture = Array.from({ length: 21 }, (_, index) => ({
  x: index * 5,
  y: index < 10 ? 0 : (index - 10) * 3,
  at: index * 2000
}));
const breadcrumbStops = buildBreadcrumbRouteStops(
  breadcrumbFixture,
  breadcrumbFixture.at(-1),
  10);
if (breadcrumbStops.length < 2 || breadcrumbStops.length > 12) {
  throw new Error(`Breadcrumb simplification produced ${breadcrumbStops.length} stops`);
}
const breadcrumbStart = breadcrumbFixture[0];
const breadcrumbFinalStop = breadcrumbStops.at(-1);
if (Math.hypot(
  breadcrumbFinalStop.x - breadcrumbStart.x,
  breadcrumbFinalStop.y - breadcrumbStart.y) > 0.001) {
  throw new Error('Breadcrumb simplification did not retain the session start');
}
const longBreadcrumbFixture = Array.from({ length: 1000 }, (_, index) => ({
  x: index * 0.75,
  y: Math.sin(index / 20) * 18,
  at: index * 250
}));
longBreadcrumbFixture.splice(420, 0, { x: Number.NaN, y: 12, at: 1 });
const simplifiedTrailPoints = simplifyBreadcrumbTrailPoints(longBreadcrumbFixture, 360);
if (simplifiedTrailPoints.length > 360
    || simplifiedTrailPoints.length < 2
    || simplifiedTrailPoints[0].x !== longBreadcrumbFixture[0].x
    || simplifiedTrailPoints[0].y !== longBreadcrumbFixture[0].y
    || simplifiedTrailPoints.at(-1).x !== longBreadcrumbFixture.at(-1).x
    || simplifiedTrailPoints.at(-1).y !== longBreadcrumbFixture.at(-1).y
    || simplifiedTrailPoints.some(point => !Number.isFinite(point.x) || !Number.isFinite(point.y))) {
  throw new Error(`Session trail simplification failed: ${JSON.stringify({
    input: longBreadcrumbFixture.length,
    output: simplifiedTrailPoints.length,
    first: simplifiedTrailPoints[0],
    last: simplifiedTrailPoints.at(-1)
  })}`);
}
const liveDeathPoint = selectDeathMarkerPoint(
  { x: 120.5, y: 220.25 },
  { x: 700, y: 800 });
const fallbackDeathPoint = selectDeathMarkerPoint(
  null,
  { x: -20, y: 1200 });
const missingDeathPoint = selectDeathMarkerPoint(null, null);
if (liveDeathPoint?.source !== 'live'
    || liveDeathPoint.x !== 120.5
    || liveDeathPoint.y !== 220.25
    || fallbackDeathPoint?.source !== 'last'
    || fallbackDeathPoint.x !== 0
    || fallbackDeathPoint.y !== 1000
    || missingDeathPoint !== null) {
  throw new Error(`Death-marker point selection failed: ${JSON.stringify({
    liveDeathPoint,
    fallbackDeathPoint,
    missingDeathPoint
  })}`);
}
const normalizedExploration = normalizeExplorationSectors(
  [-1, 0, 0, 399, 400, '7', 2.5, null],
  20);
if (normalizedExploration.join(',') !== '0,7,399') {
  throw new Error(`Exploration-sector normalization failed: ${normalizedExploration.join(',')}`);
}
for (const [point, expected] of [
  [{ x: 0, y: 0 }, 0],
  [{ x: 49.999, y: 49.999 }, 0],
  [{ x: 50, y: 0 }, 1],
  [{ x: 999.999, y: 999.999 }, 399],
  [{ x: -50, y: 1200 }, 380]
]) {
  const actual = explorationSectorIndex(point, 20);
  if (actual !== expected) {
    throw new Error(`Exploration-sector mapping failed for ${JSON.stringify(point)}: ${actual}`);
  }
}
if (explorationSectorIndex({ x: Number.NaN, y: 20 }, 20) !== null) {
  throw new Error('Exploration-sector mapping accepted a non-finite point');
}
const shouldOfferRecoveryPrompt = state => state.markerWasAvailable
  && !state.markerAvailable
  && state.lastPositionAvailable
  && !state.promptDismissed
  && !state.streamerMode;
const recoveryPromptFixtures = [
  [{ markerWasAvailable: false, markerAvailable: false, lastPositionAvailable: true, promptDismissed: false, streamerMode: false }, false],
  [{ markerWasAvailable: true, markerAvailable: false, lastPositionAvailable: true, promptDismissed: false, streamerMode: false }, true],
  [{ markerWasAvailable: true, markerAvailable: true, lastPositionAvailable: true, promptDismissed: false, streamerMode: false }, false],
  [{ markerWasAvailable: true, markerAvailable: false, lastPositionAvailable: false, promptDismissed: false, streamerMode: false }, false],
  [{ markerWasAvailable: true, markerAvailable: false, lastPositionAvailable: true, promptDismissed: true, streamerMode: false }, false],
  [{ markerWasAvailable: true, markerAvailable: false, lastPositionAvailable: true, promptDismissed: false, streamerMode: true }, false]
];
for (const [state, expected] of recoveryPromptFixtures) {
  if (shouldOfferRecoveryPrompt(state) !== expected) {
    throw new Error(`Recovery-prompt transition failed: ${JSON.stringify(state)}`);
  }
}
for (const [x, y, expected] of [
  [0, 0, 'A1'],
  [350, 350, 'H8'],
  [999.999, 999.999, 'T20'],
  [-50, 1200, 'A20']
]) {
  const actual = mapPointToGridReference(x, y);
  if (actual !== expected) {
    throw new Error(`Grid reference mismatch for ${x},${y}: ${actual} !== ${expected}`);
  }
}
const gridFixture = resolveGridReference('Grid D4');
if (!gridFixture || gridFixture.x !== 175 || gridFixture.y !== 175
    || gridFixture.gridReference !== 'D4') {
  throw new Error(`Grid reference resolution failed: ${JSON.stringify(gridFixture)}`);
}
const sharedRouteFixture = parseSharedRouteTokens(
  'Isley route | 1: D4 -> 2: 1200, -3400 -> 3: Highlands | 492.8 MU planned');
if (JSON.stringify(sharedRouteFixture) !== JSON.stringify(['D4', '1200, -3400', 'Highlands'])) {
  throw new Error(`Shared route parsing failed: ${JSON.stringify(sharedRouteFixture)}`);
}
const pasteReadyRouteFixture = parseSharedRouteTokens(
  'Isley route | D4 > F5 > Highlands | 492.8 MU planned');
if (JSON.stringify(pasteReadyRouteFixture) !== JSON.stringify(['D4', 'F5', 'Highlands'])) {
  throw new Error(`Paste-ready route parsing failed: ${JSON.stringify(pasteReadyRouteFixture)}`);
}
const terrainCourseRouteFixture = parseSharedRouteTokens(
  'Isley road/trail course | -1234.5, 678.25 > 70, -90 | 1,842.6 MU planned');
if (JSON.stringify(terrainCourseRouteFixture)
    !== JSON.stringify(['-1234.5, 678.25', '70, -90'])) {
  throw new Error(`Road/trail course parsing failed: ${JSON.stringify(terrainCourseRouteFixture)}`);
}
const breadcrumbRouteFixture = parseSharedRouteTokens(
  'The Isle Mapper breadcrumb return | A1; D4; J10 | 120.0 MU planned');
if (JSON.stringify(breadcrumbRouteFixture) !== JSON.stringify(['A1', 'D4', 'J10'])) {
  throw new Error(`Breadcrumb route parsing failed: ${JSON.stringify(breadcrumbRouteFixture)}`);
}
const manualNewlineRouteFixture = parseSharedRouteTokens('D4\nF5\nHighlands');
if (JSON.stringify(manualNewlineRouteFixture) !== JSON.stringify(['D4', 'F5', 'Highlands'])) {
  throw new Error(`Newline route parsing failed: ${JSON.stringify(manualNewlineRouteFixture)}`);
}
for (const [label, fixture] of [
  ['single stop', 'D4'],
  ['too many stops', Array.from({ length: 13 }, (_, index) => `A${index + 1}`).join(' > ')],
  ['oversized input', `Isley route | ${'A'.repeat(1601)} > D4`],
  ['control character', 'D4 > F5\u0001'],
  ['oversized token', `${'A'.repeat(97)} > D4`],
  ['unexpected pipe', 'D4 > F5 | hidden']
]) {
  const parsed = parseSharedRouteTokens(fixture);
  if (parsed.length !== 0) {
    throw new Error(`Unsafe shared route was accepted (${label}): ${JSON.stringify(parsed)}`);
  }
}
const validMapAction = {
  pointerId: 7,
  startedAt: 1000,
  mapInteractionRevision: 12,
  x: 240,
  y: 180,
  outside: false
};
const validMapRelease = {
  type: 'pointerup',
  isTrusted: true,
  button: 0,
  isPrimary: true,
  pointerId: 7,
  clientX: 242,
  clientY: 181
};
const validMapContext = {
  now: 1300,
  focused: true,
  hidden: false,
  inside: true,
  mapInteractionRevision: 12
};
if (!evaluateMapActionRelease(validMapAction, validMapRelease, validMapContext)) {
  throw new Error('A valid same-pointer in-map action release was rejected');
}
for (const [label, action, release, context] of [
  ['untrusted release', validMapAction, { ...validMapRelease, isTrusted: false }, validMapContext],
  ['secondary pointer', validMapAction, { ...validMapRelease, isPrimary: false }, validMapContext],
  ['wrong pointer', validMapAction, { ...validMapRelease, pointerId: 8 }, validMapContext],
  ['lost focus', validMapAction, validMapRelease, { ...validMapContext, focused: false }],
  ['hidden document', validMapAction, validMapRelease, { ...validMapContext, hidden: true }],
  ['outside map', validMapAction, validMapRelease, { ...validMapContext, inside: false }],
  ['crossed map edge', { ...validMapAction, outside: true }, validMapRelease, validMapContext],
  ['map replaced', validMapAction, validMapRelease, { ...validMapContext, mapInteractionRevision: 13 }],
  ['stale gesture', validMapAction, validMapRelease, { ...validMapContext, now: 6001 }],
  ['dragged release', validMapAction, { ...validMapRelease, clientX: 250 }, validMapContext],
  ['cancel event', validMapAction, { ...validMapRelease, type: 'pointercancel' }, validMapContext]
]) {
  if (evaluateMapActionRelease(action, release, context)) {
    throw new Error(`Unsafe map action release was accepted (${label})`);
  }
}
const floatingPanelNear = positionFloatingPanel(100, 100, 180, 120, 500, 500);
const floatingPanelEdge = positionFloatingPanel(480, 480, 180, 120, 500, 500);
if (floatingPanelNear.left !== 112 || floatingPanelNear.top !== 112
    || floatingPanelEdge.left !== 288 || floatingPanelEdge.top !== 348) {
  throw new Error(`Floating tactical panel placement failed: ${JSON.stringify({
    floatingPanelNear,
    floatingPanelEdge
  })}`);
}
const waypointCueBounds = { left: 0, top: 0, right: 500, bottom: 400 };
const waypointCueInsets = { left: 30, top: 50, right: 30, bottom: 70 };
const waypointCueVisible = calculateOffscreenWaypointCue(
  250, 190, waypointCueBounds, waypointCueInsets);
const waypointCueNearEdgeVisible = calculateOffscreenWaypointCue(
  480, 190, waypointCueBounds, waypointCueInsets);
const waypointCueRight = calculateOffscreenWaypointCue(
  700, 190, waypointCueBounds, waypointCueInsets);
const waypointCueTop = calculateOffscreenWaypointCue(
  250, -100, waypointCueBounds, waypointCueInsets);
const waypointCueDiagonal = calculateOffscreenWaypointCue(
  700, -100, waypointCueBounds, waypointCueInsets);
if (waypointCueVisible.visible || waypointCueNearEdgeVisible.visible
    || !waypointCueRight.visible || waypointCueRight.side !== 'right'
    || Math.abs(waypointCueRight.x - 470) > 0.000001
    || Math.abs(waypointCueRight.y - 190) > 0.000001
    || Math.abs(waypointCueRight.angle - 90) > 0.000001
    || !waypointCueTop.visible || waypointCueTop.side !== 'top'
    || Math.abs(waypointCueTop.x - 250) > 0.000001
    || Math.abs(waypointCueTop.y - 50) > 0.000001
    || Math.abs(waypointCueTop.angle) > 0.000001
    || !waypointCueDiagonal.visible || waypointCueDiagonal.side !== 'top'
    || waypointCueDiagonal.x <= 250 || waypointCueDiagonal.x > 470
    || Math.abs(waypointCueDiagonal.y - 50) > 0.000001) {
  throw new Error(`Off-screen waypoint cue geometry failed: ${JSON.stringify({
    waypointCueVisible,
    waypointCueNearEdgeVisible,
    waypointCueRight,
    waypointCueTop,
    waypointCueDiagonal
  })}`);
}
const nearestLandmarkFixture = selectNearestLandmark([
  { x: 110, y: 100, label: 'East Ridge' },
  { x: 90, y: 90, label: 'Northwest Basin' },
  { x: 400, y: 400, label: 'Far Highlands' }
], { x: 100, y: 100 });
if (!nearestLandmarkFixture
    || nearestLandmarkFixture.label !== 'East Ridge'
    || Math.abs(nearestLandmarkFixture.distance - 10) > 0.000001
    || Math.abs(nearestLandmarkFixture.bearing - 90) > 0.000001
    || nearestLandmarkFixture.cardinal !== 'E') {
  throw new Error(`Nearest landmark selection failed: ${JSON.stringify(nearestLandmarkFixture)}`);
}
let recentRouteFixture = [];
recentRouteFixture = recordRecentRoute(recentRouteFixture, { x: 100, y: 120 }, 'Highlands', 1000, 3);
recentRouteFixture = recordRecentRoute(recentRouteFixture, { x: 400, y: 420 }, 'Radio Tower', 2000, 3);
recentRouteFixture = recordRecentRoute(recentRouteFixture, { x: 100.2, y: 120.1 }, 'Highlands return', 3000, 3);
recentRouteFixture = recordRecentRoute(recentRouteFixture, { x: 700, y: 710 }, 'South Plains', 4000, 3);
if (recentRouteFixture.length !== 3
    || recentRouteFixture[0].label !== 'South Plains'
    || recentRouteFixture[1].label !== 'Highlands return'
    || recentRouteFixture[2].label !== 'Radio Tower'
    || recentRouteFixture[1].x !== 100.2
    || recentRouteFixture.some(route => route.label === 'Highlands')) {
  throw new Error(`Recent route ordering/deduplication failed: ${JSON.stringify(recentRouteFixture)}`);
}
const sessionStatsFixture = calculateSessionStats(120, 120000, 300000, 75);
if (sessionStatsFixture.sessionElapsedMs !== 300000
    || sessionStatsFixture.sessionMovingMs !== 120000
    || Math.abs(sessionStatsFixture.sessionAverageSpeed - 60) > 0.000001
    || sessionStatsFixture.sessionMaxSpeed !== 75) {
  throw new Error(`Session activity calculation failed: ${JSON.stringify(sessionStatsFixture)}`);
}
const nearestDangerFixture = selectNearestDangerPin([
  { id: 'safe-1', type: 'safe', distance: 2, bearing: 0 },
  { id: 'danger-far', type: 'danger', distance: 40, bearing: 180 },
  { id: 'danger-near', type: 'danger', distance: 12, bearing: 315 },
  { id: 'danger-offline', type: 'danger', distance: null, bearing: null }
]);
if (!nearestDangerFixture || nearestDangerFixture.id !== 'danger-near') {
  throw new Error(`Danger proximity selection failed: ${JSON.stringify(nearestDangerFixture)}`);
}
const publicTerrainDangerRoster = buildCommunityTerrainDangerRoster([
  { id: 'community-terrain-hazard-1', label: 'Public terrain danger 1', x: 100, y: 80 },
  { id: 'community-terrain-hazard-2', label: 'Public terrain danger 2', x: 130, y: 100 },
  { id: 'invalid', label: 'Invalid terrain danger', x: 2000, y: 100 }
], { x: 100, y: 100 }, true);
const nearestCombinedDangerFixture = selectNearestDangerPin([
  { id: 'saved-danger', type: 'danger', distance: 25, bearing: 180 },
  ...publicTerrainDangerRoster
]);
if (publicTerrainDangerRoster.length !== 2
    || publicTerrainDangerRoster[0].distance !== 20
    || publicTerrainDangerRoster[0].bearing !== 0
    || publicTerrainDangerRoster[0].cardinal !== 'N'
    || publicTerrainDangerRoster[0].source !== 'community-terrain'
    || nearestCombinedDangerFixture?.id !== 'community-terrain-hazard-1'
    || buildCommunityTerrainDangerRoster(
      publicTerrainDangerRoster, { x: 100, y: 100 }, false).length !== 0) {
  throw new Error(`Public terrain danger proximity failed: ${JSON.stringify({
    publicTerrainDangerRoster,
    nearestCombinedDangerFixture
  })}`);
}
const landmarkLabelFixture = [
  { id: 'center', label: 'Highlands', left: 190, top: 140, width: 58, height: 14 },
  { id: 'overlap', label: 'Center Jungle', left: 205, top: 144, width: 82, height: 14 },
  { id: 'duplicate', label: 'Highlands', left: 20, top: 20, width: 58, height: 14 },
  { id: 'south', label: 'South Plains', left: 20, top: 260, width: 76, height: 14 },
  { id: 'cape', label: 'NE Cape', left: 355, top: 20, width: 48, height: 14 }
];
const autoLandmarkLabels = selectLandmarkLabels(landmarkLabelFixture, 'auto', 436, 320);
const focusLandmarkLabels = selectLandmarkLabels(landmarkLabelFixture, 'focus', 436, 320);
const fullLandmarkLabels = selectLandmarkLabels(landmarkLabelFixture, 'all', 436, 320);
const spacedLandmarkFixture = Array.from({ length: 24 }, (_, index) => ({
  id: `spaced-${index}`,
  label: `Place ${index}`,
  left: (index % 6) * 120 + 10,
  top: Math.floor(index / 6) * 90 + 10,
  width: 54,
  height: 14
}));
const spacedAutoLabels = selectLandmarkLabels(spacedLandmarkFixture, 'auto', 800, 600);
const spacedFocusLabels = selectLandmarkLabels(spacedLandmarkFixture, 'focus', 800, 600);
if (!autoLandmarkLabels.includes('center')
    || autoLandmarkLabels.includes('overlap')
    || autoLandmarkLabels.includes('duplicate')
    || !autoLandmarkLabels.includes('south')
    || !autoLandmarkLabels.includes('cape')
    || focusLandmarkLabels.length > autoLandmarkLabels.length
    || fullLandmarkLabels.length !== landmarkLabelFixture.length
    || spacedAutoLabels.length !== 24
    || spacedFocusLabels.length !== 16) {
  throw new Error(`Landmark label density failed: ${JSON.stringify({
    autoLandmarkLabels, focusLandmarkLabels, fullLandmarkLabels,
    spacedAutoLabels, spacedFocusLabels
  })}`);
}
const nearestAlertZoneFixture = selectNearestAlertZone([
  { id: 'marker-only', alertRadius: 0, distance: 1, bearing: 0, insideAlertZone: false, distanceToAlertZone: null },
  { id: 'outside-near', alertRadius: 25, distance: 30, bearing: 90, insideAlertZone: false, distanceToAlertZone: 5 },
  { id: 'inside-deep', alertRadius: 50, distance: 12, bearing: 180, insideAlertZone: true, distanceToAlertZone: 0 },
  { id: 'inside-edge', alertRadius: 25, distance: 24, bearing: 270, insideAlertZone: true, distanceToAlertZone: 0 }
]);
const nearestAlertZoneState = buildAlertZoneState([
  {
    id: 'inside-deep', label: 'Nest perimeter', alertRadius: 50,
    distance: 12, bearing: 180, cardinal: 'S', insideAlertZone: true, distanceToAlertZone: 0
  }
]);
if (!nearestAlertZoneFixture || nearestAlertZoneFixture.id !== 'inside-deep'
    || nearestAlertZoneState.nearestAlertZonePinId !== 'inside-deep'
    || nearestAlertZoneState.nearestAlertZoneRadius !== 50
    || nearestAlertZoneState.insideAlertZone !== true) {
  throw new Error(`Alert-zone selection failed: ${JSON.stringify({ nearestAlertZoneFixture, nearestAlertZoneState })}`);
}
const directRouteClearFixture = calculateDirectRouteObstacleRisk(
  { x: 0, y: 0 }, { x: 100, y: 0 }, []);
const directRouteCircleFixture = calculateDirectRouteObstacleRisk(
  { x: 0, y: 0 }, { x: 100, y: 0 }, [{ x: 50, y: 0, radius: 10 }]);
const directRoutePolygonFixture = calculateDirectRouteObstacleRisk(
  { x: 0, y: 0 }, { x: 100, y: 0 }, [{
    kind: 'polygon',
    points: [
      { x: 40, y: -8 }, { x: 60, y: -8 },
      { x: 60, y: 8 }, { x: 40, y: 8 }
    ]
  }]);
const directRouteInsideFixture = calculateDirectRouteObstacleRisk(
  { x: 50, y: 0 }, { x: 100, y: 0 }, [{ x: 50, y: 0, radius: 10 }]);
const directRouteInvalidFixture = calculateDirectRouteObstacleRisk(
  { x: Number.NaN, y: 0 }, { x: 100, y: 0 }, []);
if (!directRouteClearFixture.valid || directRouteClearFixture.crossingObstacleCount !== 0
    || directRouteClearFixture.insideObstacleCount !== 0
    || directRouteCircleFixture.crossingObstacleCount !== 1
    || directRoutePolygonFixture.crossingObstacleCount !== 1
    || directRouteInsideFixture.insideObstacleCount !== 1
    || directRouteInsideFixture.crossingObstacleCount !== 0
    || directRouteInvalidFixture.valid) {
  throw new Error(`Direct-route obstacle risk failed: ${JSON.stringify({
    directRouteClearFixture, directRouteCircleFixture, directRoutePolygonFixture,
    directRouteInsideFixture, directRouteInvalidFixture
  })}`);
}
const directEscapeFixture = calculateEscapeRoute(
  { x: 500, y: 500 }, 0, [], 75, 8);
const edgeEscapeFixture = calculateEscapeRoute(
  { x: 500, y: 990 }, 0, [], 75, 8);
const circleEscapeFixture = calculateEscapeRoute(
  { x: 500, y: 500 }, 0, [{ x: 500, y: 540, radius: 10 }], 75, 8);
const polygonEscapeFixture = calculateEscapeRoute(
  { x: 500, y: 500 }, 0, [{
    kind: 'polygon', id: 'ridge',
    points: [
      { x: 490, y: 530 }, { x: 510, y: 530 },
      { x: 510, y: 550 }, { x: 490, y: 550 }
    ]
  }], 75, 8);
const exitMarkedZoneEscapeFixture = calculateEscapeRoute(
  { x: 500, y: 500 }, 0, [{ x: 500, y: 500, radius: 10 }], 75, 8);
const surroundedEscapeFixture = calculateEscapeRoute(
  { x: 500, y: 500 }, 0, [{ x: 500, y: 500, radius: 90 }], 75, 8);
const invalidEscapeFixture = calculateEscapeRoute(
  { x: Number.NaN, y: 500 }, 0, [], 75, 8);
if (!directEscapeFixture.ok
    || directEscapeFixture.cardinal !== 'S'
    || Math.abs(directEscapeFixture.x - 500) > 0.000001
    || Math.abs(directEscapeFixture.y - 575) > 0.000001
    || directEscapeFixture.deflection !== 0
    || !edgeEscapeFixture.ok
    || edgeEscapeFixture.x < 8 || edgeEscapeFixture.x > 992
    || edgeEscapeFixture.y < 8 || edgeEscapeFixture.y > 992
    || edgeEscapeFixture.distance < 25
    || !circleEscapeFixture.ok || circleEscapeFixture.deflection <= 0
    || !polygonEscapeFixture.ok || polygonEscapeFixture.deflection <= 0
    || !exitMarkedZoneEscapeFixture.ok
    || exitMarkedZoneEscapeFixture.exitedObstacleCount !== 1
    || surroundedEscapeFixture.ok || surroundedEscapeFixture.reason !== 'NO_CLEAR_ROUTE'
    || invalidEscapeFixture.ok || invalidEscapeFixture.reason !== 'INVALID_INPUT') {
  throw new Error(`Escape-route planning failed: ${JSON.stringify({
    directEscapeFixture, edgeEscapeFixture, circleEscapeFixture,
    polygonEscapeFixture, exitMarkedZoneEscapeFixture,
    surroundedEscapeFixture, invalidEscapeFixture
  })}`);
}
const learnedNow = 2_000_000_000_000;
const learnedTrailFixture = buildLearnedPassageFromTrail(
  Array.from({ length: 20 }, (_, index) => ({ x: index * 5, y: 40 })),
  'roads-v9',
  learnedNow,
  12);
const learnedTooShortFixture = buildLearnedPassageFromTrail(
  Array.from({ length: 8 }, (_, index) => ({ x: index, y: 40 })),
  'roads-v9',
  learnedNow,
  12);
const learnedNormalizedFixture = normalizeLearnedPassageLibrary([
  {
    ...learnedTrailFixture.passage,
    id: 'unsafe id /<>',
    label: '  Ridge\u0000 passage  ',
    points: [
      { x: -5, y: 40 },
      ...learnedTrailFixture.passage.points.slice(1),
      { x: 1005, y: 40 }
    ]
  },
  {
    ...learnedTrailFixture.passage,
    id: 'expired',
    createdAt: learnedNow - 181 * 24 * 60 * 60 * 1000
  }
], learnedNow, 12, 120, 180 * 24 * 60 * 60 * 1000);
if (!learnedTrailFixture.ok
    || learnedTrailFixture.passage.points.length > 12
    || learnedTrailFixture.passage.points[0].x !== 0
    || learnedTrailFixture.passage.points.at(-1).x !== 95
    || learnedTooShortFixture.ok
    || learnedTooShortFixture.reason !== 'PASSAGE_TOO_SHORT'
    || learnedNormalizedFixture.length !== 1
    || learnedNormalizedFixture[0].id !== 'unsafeid'
    || learnedNormalizedFixture[0].label !== 'Ridge passage'
    || learnedNormalizedFixture[0].points[0].x !== 0
    || learnedNormalizedFixture[0].points.at(-1).x !== 1000
    || !learnedPassageIsCurrent(
      learnedNormalizedFixture[0], 'roads-v9', learnedNow, 90 * 24 * 60 * 60 * 1000)
    || learnedPassageIsCurrent(
      learnedNormalizedFixture[0], 'roads-v10', learnedNow, 90 * 24 * 60 * 60 * 1000)
    || learnedPassageIsCurrent(
      learnedNormalizedFixture[0],
      'roads-v9',
      learnedNow + 91 * 24 * 60 * 60 * 1000,
      90 * 24 * 60 * 60 * 1000)) {
  throw new Error(`Learned-passage privacy/freshness bounds failed: ${JSON.stringify({
    learnedTrailFixture,
    learnedTooShortFixture,
    learnedNormalizedFixture
  })}`);
}
const terrainRoadFixture = [{
  label: 'Mountain bypass trail',
  type: 'trail',
  points: [
    { x: 0, y: 0 },
    { x: 0, y: 20 },
    { x: 100, y: 20 },
    { x: 100, y: 0 }
  ]
}];
const terrainCourseFixture = calculateTerrainRoadCourse(
  { x: 0, y: 0 },
  { x: 100, y: 0 },
  terrainRoadFixture,
  [{ x: 50, y: 0, radius: 10 }],
  12);
if (!terrainCourseFixture.ok
    || terrainCourseFixture.reason !== ''
    || terrainCourseFixture.avoidedZoneCount !== 1
    || Math.abs(terrainCourseFixture.directDistance - 100) > 0.000001
    || Math.abs(terrainCourseFixture.courseDistance - 140) > 0.000001
    || Math.abs(terrainCourseFixture.trailDistance - 100) > 0.000001
    || Math.abs(terrainCourseFixture.unknownDistance - 40) > 0.000001
    || Math.abs(terrainCourseFixture.longestUnknownDistance - 20) > 0.000001
    || terrainCourseFixture.unknownSegmentCount !== 2
    || terrainCourseFixture.segments.length !== 3
    || terrainCourseFixture.segments.filter(segment => segment.kind === 'trail').length !== 1
    || terrainCourseFixture.segments.filter(
      segment => ['connector', 'endpoint'].includes(segment.kind)).length !== 2
    || terrainCourseFixture.segments.some(segment =>
      ![segment.x1, segment.y1, segment.x2, segment.y2].every(Number.isFinite))
    || Math.abs(terrainCourseFixture.mappedPercent - (100 / 140 * 100)) > 0.000001
    || terrainCourseFixture.stops.length !== 4
    || terrainCourseFixture.stops[1].y !== 20
    || terrainCourseFixture.stops.at(-1).x !== 100) {
  throw new Error(`Obstacle-aware road course failed: ${JSON.stringify(terrainCourseFixture)}`);
}
const learnedCourseFixture = calculateTerrainRoadCourse(
  { x: 0, y: 40 },
  { x: 95, y: 40 },
  [{
    label: 'Player-traveled passage',
    type: 'learned',
    points: learnedTrailFixture.passage.points
  }],
  [],
  12);
if (!learnedCourseFixture.ok
    || learnedCourseFixture.learnedDistance < 80
    || learnedCourseFixture.roadDistance !== 0
    || learnedCourseFixture.trailDistance !== 0
    || learnedCourseFixture.mappedPercent < 80
    || !learnedCourseFixture.segments.some(segment => segment.kind === 'learned')
    || learnedCourseFixture.segments.some(segment =>
      !['learned', 'endpoint'].includes(segment.kind))) {
  throw new Error(
    `Player-traveled passage routing failed: ${JSON.stringify(learnedCourseFixture)}`);
}
const makeFakeSvgNode = tag => ({
  tag,
  attributes: {},
  children: [],
  setAttribute(name, value) {
    this.attributes[name] = String(value);
  },
  appendChild(child) {
    this.children.push(child);
    return child;
  }
});
const fakeSvgDocument = {
  createElementNS(_namespace, tag) {
    return makeFakeSvgNode(tag);
  }
};
const rendererSegments = [
  { kind: 'road', x1: 0, y1: 0, x2: 10, y2: 0 },
  { kind: 'trail', x1: 10, y1: 0, x2: 20, y2: 10 },
  { kind: 'learned', x1: 20, y1: 10, x2: 30, y2: 10 },
  { kind: 'endpoint', x1: 30, y1: 10, x2: 40, y2: 10 }
];
const drawTypedTerrainFixture = compileArrowBetweenWithBindings(
  'drawTypedTerrainCourse',
  'drawRoutePlan',
  {
    terrainRouteEvidenceVisible: true,
    terrainCourseSegments: rendererSegments,
    routePlanSource: 'terrain',
    document: fakeSvgDocument,
    routePlanComplete: false
  });
const typedRouteRoot = makeFakeSvgNode('g');
const typedRouteDrawn = drawTypedTerrainFixture(typedRouteRoot);
const typedRouteStrokes = typedRouteRoot.children
  .slice(1)
  .map(child => child.attributes.stroke);
const typedRouteDashes = typedRouteRoot.children
  .slice(1)
  .map(child => child.attributes['stroke-dasharray'] || '');
const drawTypedTerrainOffFixture = compileArrowBetweenWithBindings(
  'drawTypedTerrainCourse',
  'drawRoutePlan',
  {
    terrainRouteEvidenceVisible: false,
    terrainCourseSegments: rendererSegments,
    routePlanSource: 'terrain',
    document: fakeSvgDocument,
    routePlanComplete: false
  });
const hiddenTypedRouteRoot = makeFakeSvgNode('g');
if (!typedRouteDrawn
    || typedRouteRoot.children.length !== 5
    || typedRouteRoot.children[0].attributes.stroke !== '#03131b'
    || typedRouteStrokes.join('|') !== '#2dd4bf|#60a5fa|#c084fc|#f59e0b'
    || typedRouteDashes.join('|') !== '|8 4|10 4 2 4|3 4'
    || drawTypedTerrainOffFixture(hiddenTypedRouteRoot)
    || hiddenTypedRouteRoot.children.length !== 0) {
  throw new Error(`Typed terrain renderer failed: ${JSON.stringify({
    typedRouteDrawn,
    childCount: typedRouteRoot.children.length,
    typedRouteStrokes,
    typedRouteDashes,
    hiddenChildCount: hiddenTypedRouteRoot.children.length
  })}`);
}
const blockedPassageFixture = buildBlockedPassageArea(
  { x: 500, y: 500 }, { x: 560, y: 500 }, 2, 123456, 8);
const blockedPassageTooClose = buildBlockedPassageArea(
  { x: 500, y: 500 }, { x: 510, y: 500 }, 0, 123456, 8);
const blockedPassageAtLimit = buildBlockedPassageArea(
  { x: 500, y: 500 }, { x: 560, y: 500 }, 8, 123456, 8);
const blockedPassageOutside = buildBlockedPassageArea(
  { x: 995, y: 500 }, { x: 995, y: 560 }, 0, 123456, 8);
if (!blockedPassageFixture.ok
    || blockedPassageFixture.area.id !== 'blocked-123456-2'
    || blockedPassageFixture.area.points.length !== 4
    || !routeSegmentIntersectsPolygon(
      { x: 500, y: 500 }, { x: 560, y: 500 }, blockedPassageFixture.area.points, 0)
    || routePointInPolygon({ x: 500, y: 500 }, blockedPassageFixture.area.points, 0)
    || blockedPassageTooClose.ok || blockedPassageTooClose.reason !== 'PASSAGE_TOO_CLOSE'
    || blockedPassageAtLimit.ok || blockedPassageAtLimit.reason !== 'AREA_LIMIT'
    || blockedPassageOutside.ok || blockedPassageOutside.reason !== 'OUTSIDE_MAP') {
  throw new Error(`Blocked-passage geometry failed: ${JSON.stringify({
    blockedPassageFixture,
    blockedPassageTooClose,
    blockedPassageAtLimit,
    blockedPassageOutside
  })}`);
}
const measuredSlopeFixture = buildMeasuredSlopeArea(
  { x: 400, y: 500 }, { x: 460, y: 500 }, 2, 'Measured descent 22%', 456789, 8);
const measuredSlopeTooShort = buildMeasuredSlopeArea(
  { x: 400, y: 500 }, { x: 400.1, y: 500 }, 0, 'Tiny', 456789, 8);
const measuredSlopeAtLimit = buildMeasuredSlopeArea(
  { x: 400, y: 500 }, { x: 460, y: 500 }, 8, 'Limit', 456789, 8);
const measuredSlopeOutside = buildMeasuredSlopeArea(
  { x: 1, y: 500 }, { x: 30, y: 500 }, 0, 'Edge', 456789, 8);
if (!measuredSlopeFixture.ok
    || measuredSlopeFixture.area.id !== 'slope-456789-2'
    || measuredSlopeFixture.area.label !== 'Measured descent 22%'
    || measuredSlopeFixture.area.points.length !== 4
    || measuredSlopeFixture.width < 8
    || !routeSegmentIntersectsPolygon(
      { x: 400, y: 500 }, { x: 460, y: 500 }, measuredSlopeFixture.area.points, 0)
    || !routePointInPolygon({ x: 430, y: 500 }, measuredSlopeFixture.area.points, 0)
    || measuredSlopeTooShort.ok || measuredSlopeTooShort.reason !== 'SEGMENT_TOO_SHORT'
    || measuredSlopeAtLimit.ok || measuredSlopeAtLimit.reason !== 'AREA_LIMIT'
    || measuredSlopeOutside.ok || measuredSlopeOutside.reason !== 'OUTSIDE_MAP') {
  throw new Error(`Measured-slope geometry failed: ${JSON.stringify({
    measuredSlopeFixture,
    measuredSlopeTooShort,
    measuredSlopeAtLimit,
    measuredSlopeOutside
  })}`);
}
const insideObstacleCourse = calculateTerrainRoadCourse(
  { x: 50, y: 0 },
  { x: 100, y: 0 },
  terrainRoadFixture,
  [{ x: 50, y: 0, radius: 10 }],
  12);
const polygonCourseFixture = calculateTerrainRoadCourse(
  { x: 0, y: 0 },
  { x: 100, y: 0 },
  terrainRoadFixture,
  [{
    kind: 'polygon', id: 'ridge', label: 'Mountain ridge',
    points: [
      { x: 40, y: -8 }, { x: 60, y: -8 },
      { x: 60, y: 8 }, { x: 40, y: 8 }
    ]
  }],
  12);
const insidePolygonCourse = calculateTerrainRoadCourse(
  { x: 50, y: 0 },
  { x: 100, y: 0 },
  terrainRoadFixture,
  [{
    kind: 'polygon', id: 'ridge', label: 'Mountain ridge',
    points: [
      { x: 40, y: -8 }, { x: 60, y: -8 },
      { x: 60, y: 8 }, { x: 40, y: 8 }
    ]
  }],
  12);
const distantRoadCourse = calculateTerrainRoadCourse(
  { x: 0, y: 0 },
  { x: 500, y: 0 },
  [{ label: 'Short road', type: 'road', points: [{ x: 0, y: 0 }, { x: 0, y: 20 }] }],
  [],
  12);
if (insideObstacleCourse.ok || insideObstacleCourse.reason !== 'START_INSIDE_OBSTACLE'
    || !polygonCourseFixture.ok || polygonCourseFixture.avoidedAreaCount !== 1
    || polygonCourseFixture.avoidedZoneCount !== 1 || polygonCourseFixture.stops[1].y !== 20
    || insidePolygonCourse.ok || insidePolygonCourse.reason !== 'START_INSIDE_OBSTACLE'
    || insidePolygonCourse.obstacleKind !== 'polygon' || insidePolygonCourse.obstacleId !== 'ridge'
    || distantRoadCourse.ok || distantRoadCourse.reason !== 'NO_ROAD_NEAR_DESTINATION') {
  throw new Error(`Terrain-course refusal failed: ${JSON.stringify({
    insideObstacleCourse, polygonCourseFixture, insidePolygonCourse, distantRoadCourse
  })}`);
}
const routeStyleFixture = [
  {
    label: 'Direct trail',
    type: 'trail',
    points: [{ x: 0, y: 0 }, { x: 300, y: 0 }]
  },
  {
    label: 'Road bypass',
    type: 'road',
    points: [
      { x: 0, y: 20 }, { x: 0, y: 35 },
      { x: 300, y: 35 }, { x: 300, y: 20 }
    ]
  }
];
const balancedStyleCourse = calculateTerrainRoadCourse(
  { x: 0, y: 10 }, { x: 300, y: 10 }, routeStyleFixture, [], 12, 'balanced');
const roadFirstStyleCourse = calculateTerrainRoadCourse(
  { x: 0, y: 10 }, { x: 300, y: 10 }, routeStyleFixture, [], 12, 'road-first');
const shortestStyleCourse = calculateTerrainRoadCourse(
  { x: 0, y: 10 }, { x: 300, y: 10 }, routeStyleFixture, [], 12, 'shortest');
if (!balancedStyleCourse.ok || balancedStyleCourse.routeStyle !== 'balanced'
    || Math.abs(balancedStyleCourse.courseDistance - 320) > 0.000001
    || !roadFirstStyleCourse.ok || roadFirstStyleCourse.routeStyle !== 'road-first'
    || Math.abs(roadFirstStyleCourse.courseDistance - 350) > 0.000001
    || !roadFirstStyleCourse.stops.some(point => point.y === 35)
    || !shortestStyleCourse.ok || shortestStyleCourse.routeStyle !== 'shortest'
    || Math.abs(shortestStyleCourse.courseDistance - 320) > 0.000001) {
  throw new Error(`Terrain route-style weighting failed: ${JSON.stringify({
    balancedStyleCourse, roadFirstStyleCourse, shortestStyleCourse
  })}`);
}
const gapPolicyFixture = [{
  label: 'Gap policy road',
  type: 'road',
  points: [{ x: 60, y: 0 }, { x: 120, y: 0 }]
}];
const strictGapCourse = calculateTerrainRoadCourse(
  { x: 0, y: 0 }, { x: 120, y: 0 }, gapPolicyFixture, [], 12, 'balanced', 'strict');
const balancedGapCourse = calculateTerrainRoadCourse(
  { x: 0, y: 0 }, { x: 120, y: 0 }, gapPolicyFixture, [], 12, 'balanced', 'balanced');
const broadGapFixture = [{
  label: 'Broad gap road',
  type: 'road',
  points: [{ x: 100, y: 0 }, { x: 180, y: 0 }]
}];
const refusedBroadGapCourse = calculateTerrainRoadCourse(
  { x: 0, y: 0 }, { x: 180, y: 0 }, broadGapFixture, [], 12, 'balanced', 'balanced');
const flexibleGapCourse = calculateTerrainRoadCourse(
  { x: 0, y: 0 }, { x: 180, y: 0 }, broadGapFixture, [], 12, 'balanced', 'flexible');
if (strictGapCourse.ok || strictGapCourse.reason !== 'NO_ROAD_NEAR_START'
    || !balancedGapCourse.ok || balancedGapCourse.gapPolicy !== 'balanced'
    || balancedGapCourse.maximumConnectorDistance !== 80
    || refusedBroadGapCourse.ok || refusedBroadGapCourse.reason !== 'NO_ROAD_NEAR_START'
    || !flexibleGapCourse.ok || flexibleGapCourse.gapPolicy !== 'flexible'
    || flexibleGapCourse.maximumConnectorDistance !== 125) {
  throw new Error(`Terrain gap-policy limits failed: ${JSON.stringify({
    strictGapCourse, balancedGapCourse, refusedBroadGapCourse, flexibleGapCourse
  })}`);
}
const simplifiedCourseFixture = simplifyTerrainCoursePoints(
  Array.from({ length: 20 }, (_, index) => ({ x: index, y: index % 2 ? 10 : 0 })),
  5);
if (simplifiedCourseFixture.length !== 5
    || simplifiedCourseFixture[0].x !== 0
    || simplifiedCourseFixture.at(-1).x !== 19) {
  throw new Error(`Terrain-course simplification failed: ${JSON.stringify(simplifiedCourseFixture)}`);
}
const mapScaleFixtures = [
  [436, 1, 100, 43.6],
  [436, 6, 25, 65.4],
  [436, 20, 10, 87.2]
];
for (const [width, scale, expectedUnits, expectedPixels] of mapScaleFixtures) {
  const actual = chooseMapScaleBar(width, scale);
  if (actual.scaleBarUnits !== expectedUnits
      || Math.abs(actual.scaleBarPixels - expectedPixels) > 0.1) {
    throw new Error(`Map scale selection failed for ${width}px @ ${scale}x: ${JSON.stringify(actual)}`);
  }
}
const placeScoreFixtures = [
  ['Highlands', 'Highlands', 0],
  ['high', 'Highlands', 1],
  ['lands', 'Highlands', 2],
  ['east rid', 'East River Ridge', 3]
];
for (const [query, label, expectedScore] of placeScoreFixtures) {
  const actual = scoreMapLabel(query, label);
  if (actual !== expectedScore) {
    throw new Error(`Place search score mismatch for ${query}/${label}: ${actual} !== ${expectedScore}`);
  }
}
const typoScore = scoreMapLabel('highlnd', 'Highland');
if (typoScore === null || typoScore < 4 || typoScore >= 5
    || scoreMapLabel('highlnd', 'Coastal Plain') !== null) {
  throw new Error(`Typo-tolerant place scoring failed: ${JSON.stringify({ typoScore })}`);
}
const rankedPlaceFixture = rankNamedPlaces([
  { x: 800, y: 800, label: 'Highland' },
  { x: 120, y: 130, label: 'Highland' },
  { x: 400, y: 400, label: 'Highlands' },
  { x: 600, y: 600, label: 'Coastal Plain' }
], 'highlnd', { x: 100, y: 100 }, 5);
if (rankedPlaceFixture.length !== 1
    || rankedPlaceFixture[0].label !== 'Highland'
    || rankedPlaceFixture[0].gridReference !== 'C3'
    || Math.abs(rankedPlaceFixture[0].distance - Math.hypot(20, 30)) > 0.000001) {
  throw new Error(`Place search ranking failed: ${JSON.stringify(rankedPlaceFixture)}`);
}
const rankedSavedFixture = rankSavedDestinations([
  { id: 'near', type: 'safe', label: 'Home Nest', x: 120, y: 120, favorite: false, createdAt: 1000 },
  { id: 'favorite', type: 'nest', label: 'Home Nest', x: 700, y: 700, favorite: true, createdAt: 900 },
  { id: 'other', type: 'food', label: 'Home Food', x: 130, y: 130, favorite: false, createdAt: 1100 }
], 'home nest', { x: 100, y: 100 }, 5);
if (rankedSavedFixture.length !== 2
    || rankedSavedFixture[0].id !== 'favorite'
    || rankedSavedFixture[0].favorite !== true
    || rankedSavedFixture[1].id !== 'near'
    || rankedSavedFixture[0].gridReference !== 'O15') {
  throw new Error(`Saved destination favorite ranking failed: ${JSON.stringify(rankedSavedFixture)}`);
}
const recentEtaFixture = calculateNavigationEta(
  10, 100, 10, 20, [9, 10, 11, 200], 60000, true);
if (!recentEtaFixture.navigationEtaActive
    || recentEtaFixture.navigationEtaSource !== 'RECENT'
    || recentEtaFixture.navigationEtaDistance !== 100
    || Math.abs(recentEtaFixture.navigationEtaPace - 12.4) > 0.000001
    || Math.abs(recentEtaFixture.navigationEtaMinutes - (100 / 12.4)) > 0.000001) {
  throw new Error(`Recent-pace route ETA failed: ${JSON.stringify(recentEtaFixture)}`);
}
const liveEtaFixture = calculateNavigationEta(
  60, null, 15, 0, [12, 13, 14, 15, 16, 17], 5000, false);
if (!liveEtaFixture.navigationEtaActive
    || liveEtaFixture.navigationEtaSource !== 'LIVE'
    || liveEtaFixture.navigationEtaDistance !== 60
    || Math.abs(liveEtaFixture.navigationEtaPace - 14.5) > 0.000001
    || Math.abs(liveEtaFixture.navigationEtaMinutes - (60 / 14.5)) > 0.000001) {
  throw new Error(`Live-pace waypoint ETA failed: ${JSON.stringify(liveEtaFixture)}`);
}
const tripEtaFixture = calculateNavigationEta(45, null, 0, 15, [], 30000, false);
const waitingEtaFixture = calculateNavigationEta(45, null, 0, 0, [], 0, false);
if (tripEtaFixture.navigationEtaSource !== 'TRIP'
    || Math.abs(tripEtaFixture.navigationEtaMinutes - 3) > 0.000001
    || waitingEtaFixture.navigationEtaActive
    || waitingEtaFixture.navigationEtaMinutes !== null) {
  throw new Error(`ETA fallback states failed: ${JSON.stringify({ tripEtaFixture, waitingEtaFixture })}`);
}
const closingApproachFixture = calculateWaypointApproach([
  { distance: 100, at: 0 },
  { distance: 96, at: 5000 },
  { distance: 90, at: 10000 }
], 100, 90, 10000);
const awayApproachFixture = calculateWaypointApproach([
  { distance: 100, at: 0 },
  { distance: 104, at: 5000 },
  { distance: 110, at: 10000 }
], 100, 110, 10000);
const steadyApproachFixture = calculateWaypointApproach([
  { distance: 100, at: 0 },
  { distance: 100.4, at: 5000 },
  { distance: 100.2, at: 10000 }
], 100, 100.2, 10000);
const waitingApproachFixture = calculateWaypointApproach([
  { distance: 100, at: 7000 },
  { distance: 98, at: 10000 }
], 100, 98, 10000);
if (closingApproachFixture.waypointTrend !== 'closing'
    || Math.abs(closingApproachFixture.waypointClosingRate - 60) > 0.000001
    || Math.abs(closingApproachFixture.waypointProgressPercent - 10) > 0.000001
    || awayApproachFixture.waypointTrend !== 'away'
    || Math.abs(awayApproachFixture.waypointClosingRate + 60) > 0.000001
    || awayApproachFixture.waypointProgressPercent !== 0
    || steadyApproachFixture.waypointTrend !== 'steady'
    || waitingApproachFixture.waypointTrend !== 'waiting'
    || waitingApproachFixture.waypointClosingRate !== null) {
  throw new Error(`Waypoint approach intelligence failed: ${JSON.stringify({
    closingApproachFixture,
    awayApproachFixture,
    steadyApproachFixture,
    waitingApproachFixture
  })}`);
}
const expiryPartitionFixture = partitionPinsByExpiry([
  { id: 'permanent', expiresAt: 0 },
  { id: 'future', expiresAt: 3000 },
  { id: 'expired', expiresAt: 2000 }
], 2000);
if (expiryPartitionFixture.activePins.length !== 2
    || expiryPartitionFixture.activePins[0].id !== 'permanent'
    || expiryPartitionFixture.activePins[1].id !== 'future'
    || expiryPartitionFixture.expiredPinIds.length !== 1
    || expiryPartitionFixture.expiredPinIds[0] !== 'expired') {
  throw new Error(`Timed-pin partition failed: ${JSON.stringify(expiryPartitionFixture)}`);
}
const pinBackupText = buildPinLibraryBackup([
  {
    id: 'existing', type: 'nest', label: 'River\nNest', x: 120, y: 130,
    favorite: true, expiresAt: 301000, expiryMinutes: 5, alertRadius: 50, createdAt: 900
  },
  { id: 'new', type: 'danger', label: 'Raptor pass', x: 420, y: 530, createdAt: 950 }
], calibrationFixture, 1000, [{
  id: 'ridge', label: 'North ridge', createdAt: 850,
  points: [
    { x: 300, y: 300 }, { x: 360, y: 300 },
    { x: 360, y: 340 }, { x: 300, y: 340 }
  ]
}]);
const pinBackupJson = JSON.parse(pinBackupText);
if (pinBackupJson.schema !== 'the-isle-mapper-pins'
    || pinBackupJson.version !== 2
    || pinBackupJson.pins.length !== 2
    || pinBackupJson.pins[0].label !== 'River Nest'
    || pinBackupJson.pins[0].favorite !== true
    || pinBackupJson.pins[0].expiresAt !== 301000
    || pinBackupJson.pins[0].expiryMinutes !== 5
    || pinBackupJson.pins[0].alertRadius !== 50
    || !Number.isFinite(pinBackupJson.pins[0].worldX)
    || !Number.isFinite(pinBackupJson.pins[0].worldY)
    || pinBackupJson.noGoAreas.length !== 1
    || pinBackupJson.noGoAreas[0].label !== 'North ridge'
    || pinBackupJson.noGoAreas[0].points.length !== 4
    || !Number.isFinite(pinBackupJson.noGoAreas[0].points[0].worldX)) {
  throw new Error(`Pin backup serialization failed: ${pinBackupText}`);
}
const parsedPinBackup = parsePinLibraryBackup(pinBackupText, calibrationFixture, 1000);
if (!parsedPinBackup.valid || parsedPinBackup.pins[0].favorite !== true
    || parsedPinBackup.pins[0].expiresAt !== 301000
    || parsedPinBackup.pins[0].expiryMinutes !== 5
    || parsedPinBackup.pins[0].alertRadius !== 50
    || parsedPinBackup.expiredCount !== 0
    || parsedPinBackup.noGoAreas.length !== 1
    || parsedPinBackup.noGoAreas[0].label !== 'North ridge') {
  throw new Error(`Pin backup favorite round trip failed: ${JSON.stringify(parsedPinBackup)}`);
}
const survivalMarkerBackup = buildPinLibraryBackup([
  { id: 'water', type: 'water', label: 'River drink', x: 200, y: 300, createdAt: 900 },
  { id: 'rally', type: 'rally', label: 'Pack rally', x: 400, y: 500, createdAt: 910 },
  { id: 'death', type: 'death', label: 'Last death', x: 600, y: 700, createdAt: 920 }
], calibrationFixture, 1000);
const parsedSurvivalMarkerBackup = parsePinLibraryBackup(
  survivalMarkerBackup,
  calibrationFixture,
  1000);
if (!parsedSurvivalMarkerBackup.valid
    || parsedSurvivalMarkerBackup.pins.length !== 3
    || parsedSurvivalMarkerBackup.pins.map(pin => pin.type).join(',') !== 'water,rally,death') {
  throw new Error(`Expanded marker backup failed: ${JSON.stringify(parsedSurvivalMarkerBackup)}`);
}
const pinImportPlan = buildPinLibraryImportPlan([
  { id: 'local', type: 'nest', label: 'River Nest', x: 120, y: 130, createdAt: 900 }
], pinBackupText, calibrationFixture, 1100, [{
  id: 'local-ridge', label: 'North ridge', createdAt: 800,
  points: [
    { x: 300, y: 300 }, { x: 360, y: 300 },
    { x: 360, y: 340 }, { x: 300, y: 340 }
  ]
}]);
if (!pinImportPlan.valid
    || pinImportPlan.addedCount !== 1
    || pinImportPlan.duplicateCount !== 1
    || pinImportPlan.addedAreaCount !== 0
    || pinImportPlan.duplicateAreaCount !== 1
    || pinImportPlan.trimmedCount !== 0
    || pinImportPlan.resultPins.length !== 2
    || pinImportPlan.resultPins[1].label !== 'Raptor pass'
    || Math.abs(pinImportPlan.resultPins[1].x - 420) > 0.000001
    || Math.abs(pinImportPlan.resultPins[1].y - 530) > 0.000001) {
  throw new Error(`Pin backup import planning failed: ${JSON.stringify(pinImportPlan)}`);
}
const malformedPinBackup = JSON.stringify({
  schema: 'the-isle-mapper-pins',
  version: 1,
  pins: [{ type: 'unknown', label: 'Bad', x: 10, y: 10 }]
});
const malformedPinPlan = buildPinLibraryImportPlan([], malformedPinBackup, calibrationFixture, 1100);
if (malformedPinPlan.valid || malformedPinPlan.resultPins.length !== 0) {
  throw new Error(`Malformed pin backup was not rejected atomically: ${JSON.stringify(malformedPinPlan)}`);
}
const expiredPinBackup = JSON.stringify({
  schema: 'the-isle-mapper-pins',
  version: 1,
  pins: [{
    type: 'danger', label: 'Old sighting', x: 220, y: 330,
    expiresAt: 1000, expiryMinutes: 5, createdAt: 900
  }]
});
const expiredPinPlan = buildPinLibraryImportPlan([], expiredPinBackup, calibrationFixture, 1100);
if (!expiredPinPlan.valid || expiredPinPlan.addedCount !== 0
    || expiredPinPlan.expiredCount !== 1 || expiredPinPlan.resultPins.length !== 0) {
  throw new Error(`Expired pin backup was not skipped safely: ${JSON.stringify(expiredPinPlan)}`);
}
console.log(`Embedded map controller syntax: PASS (${body.length} chars)`);
console.log(`Embedded navigation contracts: PASS (${requiredContracts.length} checks)`);
console.log('Independent live-data cadence: PASS (250ms full-mode file check, 1s Lite controller, 10s freshness refusal)');
console.log(`Lite Mode contracts: PASS (${requiredLiteModeContracts.length} checks)`);
console.log(`Onboarding tutorial contracts: PASS (${requiredOnboardingTutorialContracts.length} checks)`);
console.log(`Desktop guidance contracts: PASS (${requiredUiContracts.length} checks)`);
console.log(`Minimized Core Vitals contracts: PASS (${requiredMinimizedVitalsContracts.length} checks)`);
console.log(`Public server status contracts: PASS (${requiredServerStatusContracts.length} checks)`);
console.log(`Official Patch Watch contracts: PASS (${requiredPatchWatchContracts.length} checks)`);
console.log(`Visual comfort contracts: PASS (${requiredVisualComfortContracts.length} checks)`);
console.log(`HUD detail contracts: PASS (${requiredHudDetailContracts.length} checks)`);
console.log(`Smart HUD contracts: PASS (${requiredHudPriorityContracts.length} checks)`);
console.log(`Responsive overlay contracts: PASS (${requiredResponsiveLayoutContracts.length} checks)`);
console.log(`Gateway Resource Finder contracts: PASS (${requiredGatewayResourceContracts.length} checks)`);
console.log(`Tactical log contracts: PASS (${requiredTacticalLogContracts.length} checks)`);
console.log(`Tactical brief contracts: PASS (${requiredTacticalBriefContracts.length} checks)`);
console.log(`Tactical brief privacy: PASS (identity fields omitted, exact coordinates omitted, ${commandCatalogCount} commands)`);
console.log(`Server session contracts: PASS (${requiredServerSessionContracts.length} checks)`);
console.log(`Field Guide contracts: PASS (${requiredFieldGuideContracts.length} checks)`);
console.log(`Next Move contracts: PASS (${requiredNextMoveContracts.length} checks)`);
console.log(`Life Run contracts: PASS (${requiredLifeRunContracts.length} checks)`);
console.log(`Elder lineage contracts: PASS (${requiredElderLineageContracts.length} checks)`);
console.log(`Growth Clock contracts: PASS (${requiredGrowthPlannerContracts.length} checks)`);
console.log(`Live Growth bridge contracts: PASS (${requiredLiveGrowthBridgeContracts.length} checks)`);
console.log(`Live Species bridge contracts: PASS (${requiredLiveSpeciesBridgeContracts.length} checks)`);
console.log(`Life Transition contracts: PASS (${requiredLifeTransitionContracts.length} checks)`);
console.log(`Growth Gate Watch contracts: PASS (${requiredGrowthGateWatchContracts.length} checks)`);
console.log(`Approach Brief contracts: PASS (${requiredApproachBriefContracts.length} checks)`);
console.log(`Nest Planner contracts: PASS (${requiredNestPlannerContracts.length} checks)`);
console.log(`Mutation planner contracts: PASS (${requiredMutationPlannerContracts.length} checks)`);
console.log(`Mutation unlock contracts: PASS (${requiredMutationUnlockContracts.length} checks)`);
console.log(`Survival assistant contracts: PASS (${requiredSurvivalAssistantContracts.length} checks)`);
console.log(`Rest & Recovery Monitor contracts: PASS (${requiredRecoveryMonitorContracts.length} checks)`);
console.log(`Core Vitals contracts: PASS (${requiredCoreVitalsContracts.length} checks)`);
console.log(`Player snapshot contracts: PASS (${requiredPlayerSnapshotContracts.length} checks)`);
console.log(`Vitals trend contracts: PASS (${requiredVitalsTrendContracts.length} checks)`);
console.log(`Field conditions contracts: PASS (${requiredFieldConditionsContracts.length} checks)`);
console.log(`Safe Logout Guard contracts: PASS (${requiredSafeLogoutContracts.length} checks)`);
console.log(`Server Restart Watch contracts: PASS (${requiredServerRestartWatchContracts.length} checks)`);
console.log(`Hotkey Studio contracts: PASS (${requiredHotkeyBindingContracts.length} checks)`);
console.log(`Track Finder contracts: PASS (${requiredSoundFinderContracts.length} checks)`);
console.log(`Marker accessibility contracts: PASS (${requiredMarkerAccessibilityContracts.length} checks)`);
console.log('Marker accessibility styles: PASS (standard, contrast, shapes, fallback, and non-black fills)');
console.log(`Smart Follow contracts: PASS (${requiredSmartFollowContracts.length} checks)`);
console.log(`Smart Follow geometry: PASS (centered, cardinal, heading-up, speed-band, and hysteresis fixtures)`);
console.log(`Play Focus contracts: PASS (${requiredPlayFocusContracts.length} checks)`);
console.log(`Overlay z-order contracts: PASS (${requiredOverlayZOrderContracts.length} checks)`);
{
  const hubStart = xamlSource.indexOf('x:Name="HubToolsPanel"');
  let index = hubStart;
  let depth = 0;
  let hubEnd = -1;
  while (hubStart >= 0 && index >= 0 && index < xamlSource.length) {
    if (xamlSource.startsWith('<StackPanel', index)) {
      depth += 1;
      index += 10;
      continue;
    }
    if (xamlSource.startsWith('</StackPanel>', index)) {
      depth -= 1;
      if (depth === 0) {
        hubEnd = index;
        break;
      }
      index += 13;
      continue;
    }
    index += 1;
  }
  const hubChunk = hubStart >= 0 && hubEnd > hubStart
    ? xamlSource.slice(hubStart, hubEnd)
    : '';
  if (!hubChunk.includes('Text="ISLEY UPDATES"')
      || !hubChunk.includes('CheckForIsleyUpdateButton')
      || !source.includes('check-updates')) {
    throw new Error('Isley Updates must stay discoverable under Tools → MORE with a Check for updates command');
  }
  console.log('Isley Updates discoverability: PASS (Hub/MORE placement + check-updates command)');
}
console.log(`Pack cohesion contracts: PASS (${requiredPackCohesionContracts.length} checks)`);
console.log('Pack cohesion geometry: PASS (empty, solo, formation, and true-outlier fixtures)');
console.log('Pack dynamics: PASS (spreading, regrouping, steady, waiting, and stale fixtures)');
console.log('Pack course: PASS (moving NE, stationary, waiting, and stale fixtures)');
console.log(`Encounter awareness contracts: PASS (${requiredEncounterAwarenessContracts.length} checks)`);
console.log('Encounter awareness geometry: PASS (empty, offline, boundary, and radius-bucket fixtures)');
console.log('Encounter motion: PASS (closing, opening, steady, waiting, and nearest-contact fixtures)');
console.log('Encounter memory: PASS (live suppression, expiry, nearest last-known, offline, and disabled fixtures)');
console.log('Pin calibration round-trip: PASS (3 legacy fixtures, 3 Gateway axis fixtures)');
console.log(`Breadcrumb return simplification: PASS (${breadcrumbStops.length} stops)`);
console.log(`Private session trail simplification: PASS (${simplifiedTrailPoints.length}/360 rendered points)`);
console.log('Death-marker recovery selection: PASS (live, last-known, and unavailable fixtures)');
console.log('Recovery prompt transition: PASS (startup, confirmed loss, reacquire, unavailable, dismissed, and streamer fixtures)');
console.log('Private exploration mapping: PASS (normalization, bounds, 20x20 sectors, and invalid-point refusal)');
console.log('Gateway-scale tactical grid references: PASS (A1-T20, 4 map points, 1 route fixture)');
console.log('Shared route parsing: PASS (route, terrain, breadcrumb, newline, and refusal fixtures)');
console.log('Tactical interaction placement: PASS (12 release-safety, 2 viewport fixtures)');
console.log('Off-screen waypoint geometry: PASS (5 viewport fixtures)');
console.log('Nearest official landmark: PASS (3-place fixture)');
console.log('Adaptive place-label density: PASS (collision, duplicate, focus, and full-detail fixtures)');
console.log('Session route history: PASS (MRU, deduplication, and limit fixture)');
console.log('Session activity metrics: PASS (distance/time fixture)');
console.log('Danger proximity selection: PASS (4-pin fixture)');
console.log('Saved alert-zone selection: PASS (inside-first and nearest-boundary fixtures)');
console.log('Direct-route obstacle risk: PASS (clear, circle, polygon, inside, and invalid fixtures)');
console.log('Escape route: PASS (direct, edge-clamped, circle/polygon avoidance, zone exit, refusal, and invalid-input fixtures)');
console.log('Road/trail course: PASS (shortest path, Danger-zone and polygon avoidance, refusal, and 12-stop simplification)');
console.log('Dynamic map scale: PASS (3 zoom fixtures)');
console.log('Smart destination search: PASS (6 score fixtures, 4-place ranking fixture)');
console.log('Favorite destination ranking: PASS (3 saved-pin fixtures)');
console.log('Adaptive navigation ETA: PASS (recent, live, trip, and waiting fixtures)');
console.log('Waypoint approach intelligence: PASS (closing, away, steady, and waiting fixtures)');
console.log('Timed destinations: PASS (active/future/expired partition fixture)');
console.log('Portable pin library: PASS (seven marker types, no-go areas, timed/alert-zone round trip, duplicate merge, expired skip, malformed rejection)');
