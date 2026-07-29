using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.WebSockets;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Isley.Telemetry;
using Microsoft.Web.WebView2.Core;

namespace Isley;

public partial class MainWindow : Window
{
    private const int WmHotKey = 0x0312;
    private const int MaximumSharedRouteClipboardCharacters = 1600;
    private const string LocalMapHost = "isley.local";
    private const string LocalMapUri = "https://isley.local/map/index.html";
    private const int CurrentPlayerSyncSetupVersion = 1;

    private readonly DispatcherTimer _gamePollTimer;
    private readonly DispatcherTimer _playFocusTimer;
    private readonly DispatcherTimer _serverStatusTimer;
    private readonly DispatcherTimer _officialPatchTimer;
    private readonly DispatcherTimer _isleyUpdateTimer;
    private readonly DispatcherTimer _survivalTimerTick;
    private readonly DispatcherTimer _voiceStatusTimer;
    private readonly NativeMethods.LowLevelKeyboardProc _voiceKeyboardHookProc;
    private readonly double[] _opacityLevels = [0.92, 1.0, 0.78];
    private readonly double[] _zoomPresets = [2.5, 6.0, 12.0, 20.0];
    private readonly int[] _trailDurations = [0, 15, 30, 60, 120];
    private readonly double[] _arrivalAlertDistances = [0, 5, 10, 20];
    private readonly double[] _dangerAlertDistances = [0, 10, 25, 50, 100];
    private readonly (int Inner, int Outer)[] _rangeRingModes = [(0, 0), (10, 25), (25, 50), (50, 100)];
    private readonly double[] _packSpreadAlertDistances = [0, 25, 50, 100];
    private readonly double[] _encounterAlertDistances = [0, 10, 25, 50];
    private readonly int[] _encounterMemoryDurations = [0, 120, 300, 600];
    private readonly string[] _markerStyleModes = ["standard", "contrast", "shapes"];
    private readonly string[] _markerStyleLabels = ["Standard", "High contrast", "Shape coded"];
    private readonly string[] _landmarkLabelDensityModes = ["auto", "focus", "all"];
    private readonly string[] _mapLightModeLabels = ["Day", "Dim", "Night"];
    private readonly double[] _mapLightModeOpacities = [0, 0.18, 0.34];
    private readonly string[] _hudDetailModeLabels = ["Full", "Essential", "Clean"];
    private readonly string[] _lifeRunStageLabels = ["Hatchling", "Juvenile", "Subadult", "Adult", "Elder"];
    private readonly string[] _lifeRunStageShortLabels = ["HATCH", "JUV", "SUB", "ADULT", "ELDER"];
    private nint _voiceKeyboardHook;
    private nint _windowHandle;
    private bool _clickThrough;
    private bool _playFocusEnabled;
    private bool _visibilityRequested = true;
    private bool _playFocusSuppressed;
    private bool _playFocusInteractionOverride;
    private bool _playFocusRestoreClickThrough;
    private bool _expanded;
    private bool _isDocked;
    private bool _overlayLocked;
    private IsleyDockWindow? _dockWindow;
    private double _dockRestoreWidth = 472;
    private double _dockRestoreHeight = 560;
    private double _dockRestoreLeft = double.NaN;
    private double _dockRestoreTop = double.NaN;
    private bool _navigationHudVisible = true;
    private bool _vitalsHudVisible = true;
    private bool _survivalHudVisible = true;
    private bool _alertHudVisible = true;
    private bool _quickKeysHudVisible;
    private int _quickKeysModeIndex;
    private string _quickKeysUiSignature = string.Empty;
    private bool _aimGuideEnabled;
    private int _aimGuideGrowthIndex = AimCalibrationLogic.DefaultGrowthIndex;
    private bool _aimGuideGrowthSyncEnabled = true;
    private int _aimGuideCameraIndex = AimCalibrationLogic.DefaultCameraIndex;
    private int _aimGuideModeIndex = 1;
    private double _aimGuideSize = 220;
    private double _aimGuideDepthScale = AimCalibrationLogic.DefaultDepthScale;
    private double _aimGuideHorizontalOffset;
    private double _aimGuideVerticalOffset;
    private int _aimGuideConfirmedMatches;
    private int _aimGuideInsideMisses;
    private int _aimGuideOutsideHits;
    private bool _aimGuideAreaVisible = true;
    private bool _aimGuideCenterCueVisible = true;
    private bool _aimGuideUncertaintyVisible = true;
    private bool _aimGuideLabelVisible = true;
    private int _aimGuideAttackIndex;
    private readonly List<AimCalibrationProfile> _aimCalibrationProfiles = [];
    private string _aimGuideAppliedSpeciesId = string.Empty;
    private int _aimGuideAppliedGrowthIndex = -1;
    private AimGuideWindow? _aimGuideWindow;
    private bool _initializingMap;
    private readonly Dictionary<string, HotkeyBinding> _hotkeyBindings =
        HotkeyBindingLogic.DefaultBindings().ToDictionary(binding => binding.ActionId, StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _hotkeyRegistrationStates = new(StringComparer.Ordinal);
    private string _hotkeyCaptureActionId = string.Empty;
    private string _hotkeyCaptureMessage = string.Empty;
    private string _hotkeyStudioUiSignature = string.Empty;
    private long _lastDeathMarkerRequestTick;
    private bool _followControllerInstalled;
    private int _hotkeyToastRevision;
    private bool _playerLabelsVisible = true;
    private bool _friendOnly;
    private int _markerStyleIndex;
    private bool _headingUp;
    private bool _lookAheadEnabled = true;
    private bool _smartZoomEnabled = true;
    private bool _smartZoomSuspended;
    private bool _streamerMode;
    private bool _staleAlertActive;
    private bool _staleSoundEnabled = true;
    private bool _rememberLastPosition = true;
    private bool _markerAvailable;
    private bool _recoveryPromptVisible;
    private bool _recoveryPromptPending;
    private bool _recoveryPromptDismissed;
    private int _recoveryPromptRevision;
    private DateTimeOffset? _markerLostAt;
    private string _deathMarkerActionStatus = string.Empty;
    private DateTimeOffset _deathMarkerActionAt;
    private int _deathMarkerAttemptCount;
    private bool? _lastDeathMarkerAttemptSucceeded;
    private bool _waypointActive;
    private double? _currentWaypointDistance;
    private double? _currentWaypointBearing;
    private string _waypointKind = string.Empty;
    private bool _waypointArmed;
    private bool _routePlanArmed;
    private bool _routePlanActive;
    private bool _routePlanComplete;
    private string _routePlanSource = string.Empty;
    private bool _terrainNetworkReady;
    private int _terrainNetworkPathCount;
    private int _terrainNetworkPointCount;
    private string _terrainNetworkSourceVersion = string.Empty;
    private DateTimeOffset? _terrainNetworkLoadedAt;
    private double? _terrainCourseDirectDistance;
    private double? _terrainCourseDistance;
    private double? _terrainCourseDetourPercent;
    private int _terrainCourseAvoidedZoneCount;
    private bool _terrainCourseAvoidedWater;
    private double _terrainCourseRoadDistance;
    private double _terrainCourseTrailDistance;
    private double _terrainCourseLearnedDistance;
    private double _terrainCourseUnknownDistance;
    private double _terrainCourseLongestUnknown;
    private int _terrainCourseUnknownSegmentCount;
    private string _terrainRouteStyle = TerrainRouteStyleLogic.BalancedId;
    private string _terrainGapPolicy = TerrainGapPolicyLogic.BalancedId;
    private bool _terrainRouteConfidenceVisible = true;
    private bool _terrainWaterSafetyEnabled = true;
    private string _terrainWaterMaskStatus = "loading";
    private string _terrainWaterMaskSourceVersion = string.Empty;
    private bool _terrainCommunityHazardsEnabled = true;
    private string _terrainCommunityHazardStatus = "loading";
    private int _terrainCommunityHazardCount;
    private string _terrainCommunityHazardSourceVersion = string.Empty;
    private DateTimeOffset? _terrainCommunityHazardLoadedAt;
    private string _terrainCourseStatus = "loading";
    private bool _learnedPassageRoutingEnabled = true;
    private bool _learnedPassageVisible = true;
    private int _learnedPassageCount;
    private int _learnedPassageActiveCount;
    private int _learnedPassageStaleCount;
    private int _learnedPassagePointCount;
    private bool _clearLearnedPassagesConfirmationPending;
    private int _clearLearnedPassagesConfirmationRevision;
    private int _tripRouteObstacleCount;
    private bool _tripRouteInsideObstacle;
    private TerrainRoadNetwork? _terrainRoadNetwork;
    private CancellationTokenSource? _terrainRoadNetworkCancellation;
    private GatewayResourceNetwork? _gatewayResourceNetwork;
    private CancellationTokenSource? _gatewayResourceCancellation;
    private string _resourceFinderStatus = "loading";
    private string _resourceFinderQuery = "salt";
    private int _resourceFinderResultIndex;
    private ResourceFinderSelection? _resourceFinderSelection;
    private string _resourceFinderUiSignature = string.Empty;
    private string _activeResourceRouteId = string.Empty;
    private string _activeResourceRouteLabel = string.Empty;
    private bool _measurementArmed;
    private bool _measurementHasStart;
    private bool _measurementActive;
    private int _measurementMarkedBoundaryCount;
    private bool _measurementInsideMarkedBoundary;
    private bool _waterCrossingCheckActive;
    private string _waterCrossingUiSignature = string.Empty;
    private string _waterCrossingLoggedDecisionKey = string.Empty;
    private bool _shorelineCheckActive;
    private DateTimeOffset _shorelineCheckStartedAt;
    private string _shorelineCheckUiSignature = string.Empty;
    private string _shorelineCheckLoggedDecisionKey = string.Empty;
    private bool _shorelineCheckExpirationLogged;
    private bool _rangeRingsVisible;
    private int _rangeRingModeIndex;
    private bool _mapGridVisible;
    private int _landmarkLabelDensityIndex;
    private int _visibleLandmarkCount;
    private bool _breadcrumbTrailVisible = true;
    private bool _clearBreadcrumbConfirmationPending;
    private int _clearBreadcrumbConfirmationRevision;
    private bool _explorationEnabled;
    private int _explorationVisitedCount;
    private int _explorationTotalSectors = 400;
    private bool _clearExplorationConfirmationPending;
    private int _clearExplorationConfirmationRevision;
    private string _currentGridReference = string.Empty;
    private bool _friendRadarVisible = true;
    private bool _encounterHudVisible = true;
    private bool _nearestPlaceVisible = true;
    private bool _alwaysOnTop = true;
    private bool _toolsOpen;
    private string _toolsSection = "map";
    private FocusModeSnapshotSettings? _focusModeRestoreSnapshot;
    private string _activeFocusModeId = string.Empty;
    private bool _commandPaletteOpen;
    private int _commandPaletteResultIndex;
    private readonly List<CommandPaletteActionInfo> _commandPaletteMatches = [];
    private readonly List<string> _commandFavoriteActionIds = [];
    private readonly List<string> _commandRecentActionIds = [];
    private string _mapToolsJumpSection = "navigation";
    private string _nextMoveUiSignature = string.Empty;
    private string _tripReadinessUiSignature = string.Empty;
    private string _fightCheckUiSignature = string.Empty;
    private bool _manualSightingExpanded;
    private ManualSightingDirection _manualSightingDraftDirection = ManualSightingDirection.Ahead;
    private ManualSightingRange _manualSightingDraftRange = ManualSightingRange.Near;
    private ManualSightingDirection _manualSightingReportedDirection;
    private ManualSightingRange _manualSightingReportedRange;
    private DateTimeOffset? _manualSightingReportedAt;
    private string _manualSightingUiSignature = string.Empty;
    private ManualSightingState _manualSightingPreviousState = ManualSightingState.Ready;
    private int _manualSightingPreviousRemainingSeconds = -1;
    private string _settingsPersistenceError = string.Empty;
    private string _activeSettingsPath = string.Empty;
    private DateTimeOffset? _settingsLastSavedAt;
    private readonly List<SurvivalTimer> _survivalTimers = [];
    private string _survivalTimerUiSignature = string.Empty;
    private SafeLogoutGuardState _safeLogoutGuardState = SafeLogoutGuardState.Ready;
    private DateTimeOffset _safeLogoutGuardStartedAt = DateTimeOffset.UtcNow;
    private int _safeLogoutDurationIndex;
    private string _safeLogoutUiSignature = string.Empty;
    private bool _serverRestartWatchActive;
    private DateTimeOffset _serverRestartWatchStartedAt = DateTimeOffset.UtcNow;
    private int _serverRestartWatchDurationSeconds = 600;
    private int _serverRestartWatchNoticeLevel;
    private bool _serverRestartWatchPulsing;
    private string _serverRestartWatchUiSignature = string.Empty;
    private string _survivalIncidentId = string.Empty;
    private DateTimeOffset _survivalIncidentStartedAt = DateTimeOffset.UtcNow;
    private int _survivalIncidentAdditionalSeconds;
    private bool _survivalIncidentEstimateCompletionAnnounced = true;
    private bool _survivalIncidentFinalMinutePulsing;
    private bool _survivalIncidentPickerOpen;
    private bool _survivalIncidentHudCollapsed;
    private string _survivalIncidentUiSignature = string.Empty;
    private string _recoveryMonitorIncidentId = string.Empty;
    private DateTimeOffset? _recoveryMonitorStillSince;
    private RecoveryMovementState _recoveryMonitorState = RecoveryMovementState.Hidden;
    private int _recoveryMonitorRestSeconds;
    private string _recoveryMonitorPriorityOverride = string.Empty;
    private bool _recoveryMonitorRestQualified;
    private ReportedHealthState _reportedHealthState = ReportedHealthState.Unknown;
    private DateTimeOffset _reportedHealthReportedAt;
    private ReportedVitalState _reportedFoodState;
    private DateTimeOffset _reportedFoodReportedAt;
    private ReportedVitalState _reportedWaterState;
    private DateTimeOffset _reportedWaterReportedAt;
    private ReportedVitalState _reportedStaminaState;
    private DateTimeOffset _reportedStaminaReportedAt;
    private string _woundObservationId = string.Empty;
    private bool _woundCheckExpanded;
    private string _coreVitalsUiSignature = string.Empty;
    private string _coreVitalsDecisionSignature = string.Empty;
    private bool _visibleHudSensorEnabled;
    private VisibleHudSensorSample? _visibleHudSensorSample;
    private VisibleHudCalibration _visibleHudCalibration = VisibleHudCalibration.Default;
    private readonly Queue<VisibleHudSensorSample> _visibleHudSensorSamples = [];
    private string _visibleHudSensorStatus = "OFF · enable to estimate visible HUD vitals";
    private PlayerSnapshotRaw? _playerSnapshot;
    private string _playerSnapshotTransportState = "unavailable";
    private LiveDinoSample? _lastLiveDinoSample;
    private LifeTransitionAnalysis? _lifeTransitionPending;
    private string _lifeTransitionUiSignature = string.Empty;
    private LiveGrowthGateSample? _lastGrowthGateSample;
    private GrowthGateWatchAnalysis? _growthGatePending;
    private string _growthGateUiSignature = string.Empty;
    private readonly List<VitalsTrendSample> _vitalsTrendSamples = [];
    private string _vitalsTrendUiSignature = string.Empty;
    private string _vitalsTrendWarningKey = string.Empty;
    private FieldWeather _fieldWeather;
    private DateTimeOffset _fieldWeatherReportedAt;
    private FieldLight _fieldLight;
    private DateTimeOffset _fieldLightReportedAt;
    private string _fieldConditionsUiSignature = string.Empty;
    private string _fieldConditionsDecisionSignature = string.Empty;
    private bool _timerSoundEnabled = true;
    private bool _clearTimersConfirmationPending;
    private bool _lifeRunActive;
    private DateTimeOffset _lifeRunStartedAt;
    private int _lifeRunStageIndex = 1;
    private bool _lifeRunHudVisible = true;
    private bool _lifeRunSanctuaryVisited;
    private bool _lifeRunPerfectDiet;
    private bool _lifeRunNestedIn;
    private bool _lifeRunRaisedYoung;
    private bool _spawnPlanCoverReady;
    private bool _spawnPlanScentChecked;
    private bool _spawnPlanWaterFound;
    private bool _spawnPlanFoodFound;
    private string _spawnPlanUiSignature = string.Empty;
    private int _zoneBriefIndex;
    private string _zoneBriefUiSignature = string.Empty;
    private int _lifeRunMigrationVisits;
    private int _lifeRunPatrolVisits;
    private bool _lifeRunMassMigrationVisited;
    private int _lifeRunFertilityStatus;
    private int _lifeRunSpasmStatus;
    private int _lifeRunSpeciesClass;
    private int _dietSpeciesIndex;
    private int _dietTargetIndex;
    private int _dietSlot1;
    private int _dietSlot2;
    private int _dietSlot3;
    private int _lifeRunGrowthPercent = 25;
    private int _growthServerMultiplierIndex = GrowthPlannerLogic.DefaultLiveMapMultiplierIndex;
    private bool _growthPaused;
    private string _growthPlannerUiSignature = string.Empty;
    private int _elderEntombCount;
    private bool _elderPrimeConfirmed;
    private bool _elderConfirmed;
    private bool _recordEntombConfirmationPending;
    private int _recordEntombConfirmationRevision;
    private string _elderLineageUiSignature = string.Empty;
    private bool _nestPlannerActive;
    private int _nestPhaseIndex;
    private bool _nestPartnerReady;
    private bool _nestSiteReady;
    private bool _nestDebrisReady;
    private bool _nestReservesReady;
    private int _nestAccessIndex;
    private int _nestEggTarget = 2;
    private int _nestEggsLaid;
    private int _nestEggsHatched;
    private int _nestYoungRaised;
    private int _nestTimerDurationIndex = 1;
    private bool _nestAutoHatchGuidanceEnabled = true;
    private bool _clearNestConfirmationPending;
    private int _clearNestConfirmationRevision;
    private string _nestPlannerUiSignature = string.Empty;
    private string _guideFilterId = "all";
    private string _guideSelectedSpeciesId = "allosaurus";
    private readonly List<string> _guideFavoriteSpeciesIds = [];
    private IReadOnlyList<FieldGuideSpeciesEntry> _guideSearchResults = [];
    private string _guideUiSignature = string.Empty;
    private readonly List<MutationLoadoutItem> _mutationLoadout = [];
    private IReadOnlyList<MutationCatalogEntry> _mutationSearchResults = [];
    private int _mutationSearchResultIndex;
    private int _mutationBuildFocusIndex;
    private string _mutationPlannerUiSignature = string.Empty;
    private int _mutationRemoveConfirmationSlot;
    private int _mutationRemoveConfirmationRevision;
    private readonly List<MutationUnlockProgress> _mutationUnlockProgress = [];
    private int _mutationUnlockSelectedIndex;
    private string _mutationUnlockUiSignature = string.Empty;
    private string _mutationUnlockResetConfirmationId = string.Empty;
    private int _mutationUnlockResetConfirmationRevision;
    private bool _newLifeRunConfirmationPending;
    private int _newLifeRunConfirmationRevision;
    private string _lifeRunUiSignature = string.Empty;
    private readonly List<LifeRunHistoryEntry> _lifeRunHistory = [];
    private string _lifeRunHistoryUiSignature = string.Empty;
    private bool _clearLifeRunHistoryConfirmationPending;
    private int _clearLifeRunHistoryConfirmationRevision;
    private readonly List<TacticalEventEntry> _tacticalEvents = [];
    private int _nextTacticalEventId;
    private bool _clearTacticalLogConfirmationPending;
    private int _clearTacticalLogConfirmationRevision;
    private bool _tacticalMapReadyLogged;
    private bool _gameStateInitialized;
    private bool _gameWasRunning;
    private bool _autoLocateOnGameStart = true;
    private bool _locationResumeInFlight;
    private bool _locationResumePendingToast;
    private DateTimeOffset? _lastAuthorizedSelfAppliedAt;
    private string _serverSessionProfileId = ServerSessionLogic.LiveMapId;
    private string _serverSessionName = "Any Isle server";
    private bool _suppressServerSessionNameChanges;
    private string _communityServerAddress = string.Empty;
    private bool _suppressCommunityServerAddressChanges;
    private bool _communityServerWatchEnabled;
    private bool _communityServerSlotAlertEnabled;
    private readonly List<CommunityServerProfileSettings> _communityServerProfiles = [];
    private string _selectedCommunityServerProfileId = string.Empty;
    private bool _universalCoordinateCaptureEnabled = true;
    private uint _universalCoordinateClipboardSequence;
    private UniversalCoordinatePoint? _universalCoordinatePoint;
    private UniversalCoordinatePoint? _universalCoordinatePreviousPoint;
    private UniversalCoordinateMovement? _universalCoordinateMovement;
    private readonly List<UniversalTrackSample> _universalCoordinateTrack = [];
    private UniversalTrackEstimate? _universalTrackEstimate;
    private double _universalCoordinateHeadingDegrees;
    private bool _universalCoordinateHeadingAvailable;
    private DateTimeOffset? _universalCoordinateCapturedAt;
    private DateTimeOffset? _universalCoordinatePreviousCapturedAt;
    private int _universalCoordinateCaptureCount;
    private string _universalCoordinateUiSignature = string.Empty;
    private bool _liveDataRefreshInFlight;
    private DateTime _liveDataLastWriteUtc;
    private long _liveDataLastLength = -1;
    private string _liveDataAppliedSignature = string.Empty;
    private DateTimeOffset? _liveDataAppliedUpdatedAt;
    private readonly IsleyRelayClient _isleyRelayClient = new();
    private CancellationTokenSource? _isleyRelaySignInCancellation;
    private string _isleyRelayJoinLink = string.Empty;
    private IsleyRelayJoin? _isleyRelayJoin;
    private string _isleyRelayState = "disconnected";
    private string _isleyRelayDetail = "Optional · connect a participating server";
    private DateTimeOffset? _isleyRelaySnapshotAppliedAt;
    private readonly object _isleyRelaySnapshotGate = new();
    private ViewerTelemetrySnapshot? _isleyRelayPendingSnapshot;
    private bool _isleyRelaySnapshotDrainScheduled;
    private double? _isleyRelayLastUpdateRateHz;
    private double _isleyRelayLastRelayAgeMilliseconds;
    private string _isleyRelayConditionSignature = string.Empty;
    private double? _isleyRelayStaminaPercent;
    private bool _isleyRelayShareWithSteamFriends;
    private int _isleyRelayExplicitGrantCount;
    private bool _isleyRelayPrivacyRequestInFlight;
    private string _isleyRelayPrivacyDetail =
        "Sign in to control who can see your player node.";
    private double? _isleyRelayAgeMs;
    private double? _isleyRelayHz;
    private bool _isleyRelayConsentFiltered;
    private int _isleyRelayFriendCount;
    private string _liveHealthMapLabel = "—";
    private string _liveHealthAnnouncementSignature = string.Empty;
    private string _pendingFocusModeSuggestId = string.Empty;
    private bool _pressureCoachFirstDeathSeen;
    private bool _pressureCoachFirstNestSeen;
    private bool _pressureCoachConsentRosterSeen;
    private bool _pressureCoachPreStreamSeen;
    private string _whatsNewVersionSeen = string.Empty;
    private bool _preferBetaUpdates;
    private string _voiceNatCoachSignature = string.Empty;
    private bool _communityServerRemoveConfirmationPending;
    private int _communityServerRemoveConfirmationRevision;
    private bool? _communityServerWasFull;
    private IsleServerStatus? _lastCommunityServerStatus;
    private string _communityServerStatusError = string.Empty;
    private bool _serverStatusRefreshInFlight;
    private IsleServerStatus? _lastServerStatus = null;
    private readonly List<ServerPopulationSample> _serverPopulationSamples = [];
    private string _serverStatusError = string.Empty;
    private CancellationTokenSource? _serverStatusCancellation;
    private bool _officialPatchRefreshInFlight;
    private OfficialPatchSnapshot? _lastOfficialPatch;
    private string _officialPatchError = string.Empty;
    private string _officialPatchWarningAnnouncedVersion = string.Empty;
    private CancellationTokenSource? _officialPatchCancellation;
    private bool _automaticUpdatesEnabled = true;
    private bool _isleyUpdateRefreshInFlight;
    private bool _isleyUpdateDownloading;
    private IsleyRelease? _availableIsleyRelease;
    private string _isleyUpdateAnnouncedVersion = string.Empty;
    private string _isleyUpdateStatus = string.Empty;
    private DateTimeOffset _isleyUpdateSnoozedUntil = DateTimeOffset.MinValue;
    private CancellationTokenSource? _isleyUpdateCancellation;
    private int _opacityIndex;
    private int _mapLightModeIndex;
    private int _hudDetailModeIndex;
    private bool _smartHudEnabled = true;
    private string _smartHudUiSignature = string.Empty;
    private bool _liteModeEnabled;
    private int _onboardingTutorialVersionCompleted;
    private int _onboardingTutorialStepIndex;
    private bool _onboardingTutorialOpen;
    private string _responsiveLayoutUiSignature = string.Empty;
    private bool _hudDockMirrored;
    private string _hudDockUiSignature = string.Empty;
    private int _trailDurationIndex = 2;
    private int _zoomPresetIndex = 1;
    private int _arrivalAlertIndex = 2;
    private int _dangerAlertIndex = 2;
    private double _currentMapScale = 6;
    private double? _mapScaleBarUnits;
    private double? _mapScaleBarPixels;
    private double? _currentSelfX;
    private double? _currentSelfY;
    private double? _currentSelfMapX;
    private double? _currentSelfMapY;
    private double _currentSelfBearing;
    private double _currentSelfSpeed;
    private double _currentMarkerFreshnessAgeMs;
    private TrackFinderMode _trackFinderMode = TrackFinderMode.Sound;
    private ScentTargetKind _trackFinderScentTarget = ScentTargetKind.Water;
    private SoundBearingReading? _soundBearingFirst;
    private SoundBearingReading? _soundBearingSecond;
    private SoundFinderAnalysis _soundFinderAnalysis = SoundFinderLogic.Analyze(null, null, DateTimeOffset.UtcNow);
    private string _soundFinderUiSignature = string.Empty;
    private double _currentSessionDistance;
    private bool _sessionStatsActive;
    private double _sessionElapsedMs;
    private double _sessionMovingMs;
    private double _sessionAverageSpeed;
    private double _sessionMaxSpeed;
    private string _nearestFriendName = string.Empty;
    private double? _nearestFriendDistance;
    private double? _nearestFriendBearing;
    private string _nearestFriendCardinal = string.Empty;
    private int _packFriendCount;
    private double? _packSpread;
    private string _packSpreadMotion = string.Empty;
    private double? _packSpreadRate;
    private int _packSpreadMotionSampleCount;
    private string _packCourseState = string.Empty;
    private double? _packCourseSpeed;
    private double? _packCourseBearing;
    private string _packCourseCardinal = string.Empty;
    private int _packCourseSampleCount;
    private double? _packRadius;
    private double? _packCenterDistance;
    private double? _packCenterBearing;
    private string _packCenterCardinal = string.Empty;
    private string _packFarthestFriendName = string.Empty;
    private double? _packFarthestFriendDistance;
    private bool _packCenterAvailable;
    private bool _packRouteActive;
    private bool _packOutlierRouteActive;
    private int _packSpreadAlertIndex = 2;
    private bool _packSpreadAlertInitialized;
    private bool _packSpreadAlertActive;
    private int _encounterPlayerCount;
    private double? _nearestEncounterDistance;
    private double? _nearestEncounterBearing;
    private string _nearestEncounterCardinal = string.Empty;
    private string _nearestEncounterMotion = string.Empty;
    private double? _nearestEncounterRelativeSpeed;
    private double? _nearestEncounterInterceptSeconds;
    private int _nearestEncounterMotionSampleCount;
    private int _encounterWithin10;
    private int _encounterWithin25;
    private int _encounterWithin50;
    private int _encounterAlertIndex = 2;
    private bool _encounterAlertInitialized;
    private bool _encounterAlertActive;
    private int _encounterMemoryIndex = 2;
    private int _encounterMemoryTrackCount;
    private int _rememberedEncounterCount;
    private double? _rememberedEncounterNewestAgeMs;
    private double? _nearestRememberedEncounterDistance;
    private double? _nearestRememberedEncounterBearing;
    private string _nearestRememberedEncounterCardinal = string.Empty;
    private string _nearestPlaceName = string.Empty;
    private double? _nearestPlaceDistance;
    private double? _nearestPlaceBearing;
    private string _nearestPlaceCardinal = string.Empty;
    private int _officialLandmarkCount;
    private string _nearestDangerPinId = string.Empty;
    private string _nearestDangerLabel = string.Empty;
    private double? _nearestDangerDistance;
    private double? _nearestDangerBearing;
    private string _nearestDangerCardinal = string.Empty;
    private string _nearestAlertZonePinId = string.Empty;
    private string _nearestAlertZoneLabel = string.Empty;
    private double? _nearestAlertZoneDistance;
    private double? _nearestAlertZoneBearing;
    private string _nearestAlertZoneCardinal = string.Empty;
    private double _nearestAlertZoneRadius;
    private double? _nearestAlertZoneBoundaryDistance;
    private bool _insideAlertZone;
    private string _dangerAlertKey = string.Empty;
    private string _friendRouteName = string.Empty;
    private string _waypointLabel = string.Empty;
    private int _routeStopCount;
    private int _routeCurrentIndex;
    private double? _routePlanTotalDistance;
    private double? _routeRemainingDistance;
    private double? _navigationEtaMinutes;
    private double? _navigationEtaPace;
    private double? _navigationEtaDistance;
    private string _navigationEtaSource = string.Empty;
    private string _waypointTrend = "waiting";
    private double? _waypointClosingRate;
    private double? _waypointProgressPercent;
    private readonly List<RouteStopInfo> _routeStops = [];
    private double? _measurementDistance;
    private double? _measurementBearing;
    private string _measurementCardinal = string.Empty;
    private double? _measurementStartWorldX;
    private double? _measurementStartWorldY;
    private double? _measurementEndWorldX;
    private double? _measurementEndWorldY;
    private string _arrivalRouteKey = string.Empty;
    private bool _arrivalAlertTriggered;
    private string _approachBriefUiSignature = string.Empty;
    private string _approachBriefNoticeKey = string.Empty;
    private bool _sessionStartAvailable;
    private double? _sessionStartDistance;
    private double? _sessionStartBearing;
    private string _sessionStartCardinal = string.Empty;
    private bool _breadcrumbReturnAvailable;
    private int _breadcrumbPointCount;
    private double _breadcrumbDistance;
    private bool _lastPositionAvailable;
    private double _lastPositionAgeMs;
    private readonly List<FriendRouteInfo> _friendRoster = [];
    private string _friendRosterUiSignature = string.Empty;
    private readonly List<SteamFriendWatchEntry> _steamFriendWatchlist = [];
    private string _selectedSteamFriendWatchId = string.Empty;
    private string _autoFollowSteamFriendWatchId = string.Empty;
    private string _steamFriendWatchUiSignature = string.Empty;
    private string _steamFriendPickerSignature = string.Empty;
    private bool _updatingSteamFriendPicker;
    private bool _steamAutoFollowCommandPending;
    private DateTimeOffset _steamAutoFollowLastAttemptAt = DateTimeOffset.MinValue;
    private bool _removeSteamFriendWatchConfirmationPending;
    private int _removeSteamFriendWatchConfirmationRevision;
    private readonly List<RecentRouteInfo> _recentRoutes = [];
    private string _recentRoutesUiSignature = string.Empty;
    private bool _canRouteBack;
    private int _pinCount;
    private bool _pinArmed;
    private bool _clearPinsConfirmationPending;
    private bool _suppressDestinationSuggestions;
    private bool _suppressPinNameChanges;
    private int _destinationSearchRevision;
    private int _pinImportConfirmationRevision;
    private string _activePinId = string.Empty;
    private string _pinRemovalConfirmationId = string.Empty;
    private string _pinNameEditingId = string.Empty;
    private string _pendingPinImportText = string.Empty;
    private readonly List<PinRouteInfo> _pinRoster = [];
    private int _noGoAreaCount;
    private bool _noGoTraceActive;
    private int _noGoTraceVertexCount;
    private string _noGoSelectedAreaId = string.Empty;
    private string _noGoSelectedAreaLabel = string.Empty;
    private int _noGoSelectedAreaVertexCount;
    private string _noGoLastStatus = "ready";
    private readonly List<NoGoAreaRosterInfo> _noGoAreaRoster = [];
    private string _noGoAreaUiSignature = string.Empty;
    private string _noGoAreaRemovalConfirmationId = string.Empty;
    private int _noGoAreaRemovalConfirmationRevision;
    private readonly List<PlaceSearchSuggestion> _placeSuggestions = [];
    private string _pinRosterUiSignature = string.Empty;
    private string _pinType = "safe";
    private DateTimeOffset? _gameSessionStartedAt;
    private bool? _locationsLayer;
    private bool? _sanctuariesLayer;
    private bool? _migrationLayer;
    private bool? _patrolLayer;
    private bool? _foodLayer;
    private bool? _heatmapLayer;
    private bool? _officialSelfTrail;
    private bool? _officialFriendTrails;
    private bool? _shareLocation;
    private bool _voiceEnabled = true;
    private bool _voiceAutoOpen;
    private bool _voiceUserDisconnectedThisSession;
    private string _voiceProximityLobbyServerId = string.Empty;
    private bool _voiceAutoConnectInFlight;
    private bool _voiceHudVisible = true;
    private int _voicePttKeyIndex;
    private bool _voicePttHeld;
    private bool _voiceBridgeRunning;
    private bool _voiceConnecting;
    private bool _voiceDeafened;
    private bool _voiceNatAssist = true;
    private bool _voiceProximityEnabled = true;
    private int _voiceRangeIndex = 1;
    private bool _voiceEchoCancellation = true;
    private bool _voiceNoiseSuppression = true;
    private bool _voiceAutoGainControl = true;
    private bool _voiceMicMeterEnabled = true;
    private bool _voiceQualityMonitorEnabled = true;
    private int _voiceMicLevel;
    private bool _voiceMicClipped;
    private DateTimeOffset _voiceMicLevelAt;
    private int _voiceMicPresentedSeverity;
    private int _voiceQualityPeerCount;
    private int _voiceQualitySampleCount;
    private double? _voiceQualityRoundTripMilliseconds;
    private double? _voiceQualityJitterMilliseconds;
    private double? _voiceQualityPacketLossPercent;
    private DateTimeOffset _voiceQualityAt;
    private bool _voiceTurnRelayEnabled;
    private bool _voicePermissionArmed;
    private int _voicePermissionRevision;
    private bool _voiceEngineInitializing;
    private string _voiceEngineState = "READY";
    private string _voiceEngineDetail = "MICROPHONE OFF UNTIL CONNECT";
    private string _voiceNetworkState = "WAITING";
    private string _voiceNetworkRoute = string.Empty;
    private int _voiceParticipantCount;
    private string _voiceRoomSecret = string.Empty;
    private string _voicePeerId = string.Empty;
    private string _voiceServerUrl = "ws://127.0.0.1:5198/voice";
    private VoiceServerCheckState _voiceServerCheckState = VoiceServerCheckState.Unchecked;
    private VoiceServerReadinessSnapshot? _voiceServerReadiness;
    private string _voiceServerCheckedUrl = string.Empty;
    private bool _voiceServerCheckInFlight;
    private CancellationTokenSource? _voiceServerReadinessCancellation;
    private readonly List<VoiceInputDeviceInfo> _voiceInputDevices = [];
    private bool _suppressVoiceInputDeviceSelection;
    private string _voiceSelectedInputDeviceId = string.Empty;
    private string _voiceInputDeviceStatus = "CONNECT TO CHOOSE";
    private readonly List<VoiceOutputDeviceInfo> _voiceOutputDevices = [];
    private bool _suppressVoiceOutputDeviceSelection;
    private bool _voiceOutputSelectionSupported;
    private string _voiceSelectedOutputDeviceId = string.Empty;
    private string _voiceOutputDeviceStatus = "CONNECT TO CHOOSE";
    private readonly List<VoiceParticipantInfo> _voiceParticipants = [];
    private string _voiceParticipantRosterSignature = string.Empty;
    private VoiceRouteOffer? _pendingVoiceRouteOffer;
    private string _voiceRouteSendOfferId = string.Empty;
    private string _voiceRouteShareStatus =
        "START A ROUTE + CONNECT A PEER · EXPLICIT PEER-TO-PEER SHARE";
    private Process? _voiceLocalHostProcess;
    private string _voiceUiSignature = string.Empty;

    private sealed record FriendRouteInfo(
        string Name,
        double? Distance,
        double? Bearing,
        string Cardinal);

    private sealed record RecentRouteInfo(
        string Id,
        string Label,
        string GridReference,
        bool Active);

    private sealed record PinRouteInfo(
        string Id,
        string Type,
        string Label,
        double X,
        double Y,
        double? WorldX,
        double? WorldY,
        double? Distance,
        double? Bearing,
        string Cardinal,
        bool Favorite,
        double? ExpiresAt,
        double? ExpiresInMs,
        int ExpiryMinutes,
        int AlertRadius,
        bool InsideAlertZone,
        double? DistanceToAlertZone);

    private sealed record RouteStopInfo(
        int Index,
        double? WorldX,
        double? WorldY);

    private sealed record NoGoAreaRosterInfo(
        string Id,
        string Label,
        int VertexCount);

    private sealed record CommandPaletteActionInfo(
        string Id,
        string Title,
        string Detail,
        string Keywords);

    private sealed record ServerPopulationSample(DateTimeOffset CapturedAt, int Players);

    private sealed record TacticalEventEntry(
        int Id,
        DateTimeOffset OccurredAt,
        string Category,
        string Title,
        string Detail,
        bool Warning);

    private static readonly CommandPaletteActionInfo[] CommandPaletteActions =
    [
        new("next-move", "Open Next Move", "See the one highest-priority action from current Isley state", "next move coach recommendation priority urgent what should i do decision action context"),
        new("recenter", "Recenter and follow", "Resume following your authorized live marker", "player self center tracking follow"),
        new("death-marker", "Save latest Death marker", "Replace the previous Death marker using your latest authorized position", "death body lost recover last position respawn b"),
        new("quick-timer", "Start five-minute timer", "Create a Quick timer immediately", "timer countdown five 5m quick"),
        new("restart-watch", "Open Server Restart Watch", "Report a 30, 15, 10, or 5-minute in-game restart warning", "server restart reboot warning countdown safe logout report 30 15 10 5"),
        new("private-server-connect", "Connect to private server", "Paste your server's Isley link from the clipboard and connect in one step", "private server connect join link relay live network paste community isley lobby sign in"),
        new("map-pins-share", "Copy pin share code", "Copy a share code of your saved map pins to send to your pack", "pin share code copy export map pack send saved"),
        new("map-pins-import", "Import shared pins", "Add pins from a pack member's share code on your clipboard", "pin share code import paste add map pack receive"),
        new("encounter-history", "Copy encounter history", "Copy this session's recorded player encounters to the clipboard", "encounter history recent contacts players log copy session"),
        new("safe-logout", "Start Safe Logout Guard", "Run a truthful countdown and monitor authorized movement when available", "safe logout log out rest sleep hold h countdown movement monitor leave server"),
        new("timers", "Open activity and timers", "Jump directly to session stats and survival timers", "time nest patrol cooldown activity session"),
        new("life-run", "Open life run tracker", "Track the current dinosaur life, growth stage, zones, diet, and nesting", "life run prime growth sanctuary migration patrol diet nesting young survival objectives checklist"),
        new("spawn-plan", "Open Spawn Plan", "Guide the first minutes of this life through species, cover, scent, water, and food", "spawn plan first hour starter new life beginner cover scent water food hatchling survival checklist"),
        new("zone-brief", "Open Zone Brief", "Interpret the current Sanctuary, Migration, or Patrol signal for this life", "zone brief coach sanctuary migration patrol compass bees juvenile food yield personal group leader recent activity"),
        new("life-journal", "Open survival journal", "Archive outcomes and review private recent dinosaur lives", "life journal history archive death survived ended run stats average growth private"),
        new("growth-clock", "Open Growth Clock", "Estimate the next lifecycle gate from species, server rate, diet, and growth percent", "growth clock percent percentage eta time adult elder prime deadline multiplier 2x server diet nutrients lifecycle gate"),
        new("nest-planner", "Open Nest Planner", "Track readiness, nest phase, clutch progress, hatch timing, and raised young", "nest nesting court courting pair solo parthenogenesis egg eggs clutch gestation incubation hatch parent debris warmth young timer access public private"),
        new("survival-assistant", "Open survival assistant", "Triage bleeding, fractures, venom, sickness, thirst, and hunger", "survival injury status health bleed bleeding fracture venom sickness vomit bacteria blind thirst dehydrated starving emergency recovery"),
        new("core-vitals", "Open Core Vitals", "Report fresh health, food, water, and stamina bands without fabricated telemetry", "core vitals stats status health hp ekg food hunger water thirst stamina energy low empty manual report hud"),
        new("wound-check", "Open Wound Check", "Translate visible wounds and screen-edge splatter into a conservative manual HP band", "wound wounds blood splatter visual health hp estimate range ekg hurt critical no bar damage combat"),
        new("field-conditions", "Open Field Conditions", "Report weather and light with freshness, mutation windows, and tactical guidance", "field conditions environment weather rain storm fog clear day dusk night dawn forecast visibility sound mutation reabsorption nocturnal"),
        new("vomit-help", "Start vomit sickness recovery", "Start the five-minute estimate and open exact recovery instructions", "vomit vomiting puke sick sickness overeat overeating salt food warning cure recovery timer help"),
        new("diet-coach", "Open diet and growth coach", "Log nutrients, compare builds, and find current species foods", "diet nutrient nutrients protein carbs lipids food growth species balance endurance recovery nesting"),
        new("prime-planner", "Open Prime planner", "Track the ten guide conditions and the in-game fourth-slot verification step", "prime elder readiness fourth slot conditions sanctuary mass migration patrol infertility spasms species lifecycle"),
        new("elder-lineage", "Open Elder lineage", "Plan Prime Elder, verify Entomb, and carry mutations into the next run", "elder entomb lineage replication inheritance inherited mutation carry prime elder 100 percent endgame run"),
        new("mutation-planner", "Open mutation loadout", "Search the current catalog and plan active or carried mutations", "mutation mutations loadout build passive slot elder entomb carried active catalog search"),
        new("mutation-build-lab", "Open mutation Build Lab", "Analyze playstyle coverage, restriction-safe slots, synergies, and the next guide fit", "mutation build lab analyzer analysis synergy recommendation playstyle survival combat travel aquatic nesting stealth group coverage slot restriction"),
        new("mutation-unlocks", "Open mutation unlock tracker", "Track the seven task-gated mutations with counters and condition timers", "mutation unlock challenge progress night kills nutrients hunger stamina jumps saltwater bones lifestyle timer"),
        new("tactical-brief", "Copy tactical brief", "Copy an identity-free live callout for pack or voice coordination", "brief status callout copy share coordinate pack route contact danger server live"),
        new("tactical-log", "Open tactical log", "Review this session's routes, timers, warnings, and connection changes", "log timeline events history alert warning route timer connection session private"),
        new("session-trail", "Open session trail", "View, save as learned terrain evidence, hide, clear, or retrace your private path", "trail trace breadcrumb history path backtrack private learned passage terrain route"),
        new("exploration", "Open exploration map", "Track private visited sectors and long-term map coverage", "explore exploration coverage visited travel history fog map private"),
        new("navigation", "Open navigation controls", "Heading, zoom, grid, nearby places, and waypoints", "map nav compass zoom place waypoint"),
        new("sound-finder", "Open Sound Finder", "Triangulate a call or footstep from two player-facing bearings", "sound track finder audio call footstep roar noise bearing triangulate locate hunt listen hearing"),
        new("scent-finder", "Open Scent Finder", "Triangulate an in-game water, food, trail, or carcass scent clue", "scent sniff smell q track finder water food trail blood footprint carcass bearing triangulate locate forage"),
        new("resource-finder", "Open Resource Finder", "Search current public Gateway food, prey, Salt Lick, Mud Wallow, and Gastrolith sites", "resource finder site food prey ai herbivore salt lick mud wallow gastrolith stone route nearest public gateway spawn"),
        new("look-ahead", "Toggle look-ahead framing", "Show more terrain in the direction your animal is facing", "follow framing ahead direction travel flight navigation center"),
        new("smart-zoom", "Toggle Smart Zoom", "Adapt map scale to your current authorized movement pace", "automatic zoom pace speed sprint flight travel navigation"),
        new("routes", "Open routes and ruler", "Multi-stop planning and exact map measurement", "route plan stops ruler measure distance"),
        new("trip-check", "Open Trip Check", "Get one pre-departure GO, VERIFY, CAUTION, or HOLD decision from current Isley evidence", "trip check travel readiness go no go depart route safety commit vitals stamina weather contact danger risk"),
        new("water-crossing", "Start Water Crossing Check", "Measure both banks and combine exposure with current vitals, species, weather, contacts, and marked boundaries", "water crossing river swim banks ruler distance stamina health species deino beipi drown depth current exit threat"),
        new("shoreline-check", "Start Shoreline Check", "Run a 75-second drinking decision from current vitals, authorized contacts, warnings, weather, and species", "shoreline bank drink drinking water thirst hydrate hydration ambush deino crocodile scan listen scent exposure safety check"),
        new("fight-check", "Open Fight Check", "Combine fresh manual vitals, recovery, authorized contacts, pack cohesion, and the selected species abort cue", "fight check engagement readiness combat commit stamina health contact distance closing pack abort matchup posture"),
        new("sighting-check", "Open Sighting Check", "Report one temporary relative contact for any server without claiming live detection", "sighting contact manual report ahead behind left right close near far official community universal threat"),
        new("paste-route", "Paste shared route", "Validate and start an Isley route from the clipboard", "paste shared route course breadcrumb pack clipboard stops road trail"),
        new("route-clipboard-coords", "Route to clipboard coordinates", "Paste Asset Location or X,Y coordinates from the clipboard and plot a route to that point", "paste coordinates asset location clipboard route destination xyz goto go to coords"),
        new("terrain-course", "Plot road/trail course", "Follow current Gateway roads and trails around Danger zones and traced no-go areas", "terrain safe course road trail path obstacle mountain avoid danger destination route replan"),
        new("route-confidence", "Toggle route evidence", "Show mapped coverage, unknown connector distance, and terrain uncertainty", "route evidence confidence mapped unknown connector gap terrain trust coverage toggle"),
        new("terrain-danger", "Toggle public terrain danger", "Show current public Gateway danger points and keep enabled courses outside their marked areas", "terrain danger public community hazard ravine hole mountain cliff obstacle avoid route toggle vulnona"),
        new("block-passage", "Report blocked passage", "Save a reversible local obstacle ahead and immediately replan the active terrain course", "route blocked passage mountain cliff obstacle replan report local no go closure"),
        new("route-style", "Cycle route style", "Choose Balanced, Road-first, or Shortest routing without relaxing obstacles or water safety", "route style balanced road first shortest prefer trail connector obstacle water course"),
        new("route-gaps", "Cycle off-network gap limit", "Constrain how far a road/trail course may bridge across unknown terrain", "route gaps connector off network strict balanced flexible mountain slope cliff unknown terrain limit"),
        new("recovery", "Open recovery and arrival", "Breadcrumb return, session start, and arrival alerts", "safe backtrack return recover arrival logout"),
        new("players", "Open pack and player tools", "Friend routes, roster, labels, and trails", "pack friends social players roster trail"),
        new("steam-friends", "Add or track Steam friend", "Open Steam's add flow and follow their authorized live map marker", "steam friend friends add profile watch track auto follow route social pack map"),
        new("marker-style", "Cycle marker accessibility", "Switch among Standard, High Contrast, and Shape-coded player markers", "marker style accessibility colorblind colourblind contrast shapes friend player icon legend"),
        new("pack-center", "Route to pack center", "Follow the moving center of authorized live friends", "pack center regroup formation herd group route rally"),
        new("pack-outlier", "Route to pack outlier", "Follow the authorized friend farthest from the live pack center", "pack outlier straggler separated lost regroup formation route"),
        new("pack-alert", "Cycle pack spread alert", "Warn when the live authorized pack exceeds the selected width", "pack spread cohesion scattered straggler distance alert warning"),
        new("escape-route", "Plan escape route", "Create a bounded route away from the latest live authorized contact", "escape evade retreat flee threat contact danger route away obstacle no-go panic"),
        new("encounter-hud", "Toggle encounter HUD", "Show or hide authorized non-friend proximity context", "encounter player nearby outsider awareness radar hud"),
        new("encounter-alert", "Cycle encounter alert", "Warn once when an authorized non-friend enters the selected radius", "encounter nearby player proximity alert warning radius"),
        new("encounter-memory", "Cycle last-seen memory", "Retain fading session-only traces for disappeared authorized non-friends", "encounter memory contact last seen recent trace ghost player history private"),
        new("clear-encounter-memory", "Clear recent contacts", "Remove every session-only last-seen contact trace", "encounter memory clear erase contact last seen recent trace"),
        new("pins", "Open saved destinations", "Personal pins, search, history, and backup", "pins markers destination danger food nest safe water rally death"),
        new("alert-zones", "Open alert zones", "Add local proximity boundaries to saved destinations", "zone geofence radius boundary arrival ambush nest patrol event alert"),
        new("no-go-areas", "Open No-Go Areas", "Trace mountains, cliffs, lakes, and blocked passes for road/trail course avoidance", "no go terrain mountain range cliff lake obstacle blocked pass polygon trace boundary avoid route course"),
        new("layers", "Open bundled map layers", "Locations, zones, food, trails, and heatmap", "migration patrol sanctuary food heatmap layer"),
        new("app", "Open overlay settings", "Server compatibility, privacy, performance, window level, reload, and reset", "app settings server compatibility universal privacy performance reload layout"),
        new("tutorial", "Replay Isley quick start", "Five short steps for overlay controls, live follow, routes, survival tools, privacy, and Lite Mode", "tutorial tour onboarding getting started help learn first run quick start beginner controls recenter route voice privacy lite"),
        new("check-updates", "Check for Isley updates", "Open More tools and check the trusted stable release channel for Update & Restart", "update upgrades download release channel version patch check for updates"),
        new("aim-guide", "Toggle Aim Guide", "Show or hide the click-through external reticle or attack-area reference", "aim guide reticle attack area hitbox visual front rear arc crosshair calibrate toggle"),
        new("aim-calibration", "Open Aim Calibration", "Tune a species, attack, growth, and camera-specific external guide", "aim calibrate calibration hitbox area attack growth juvenile adult camera width depth offset margin match"),
        new("aim-growth-sync", "Toggle live aim growth", "Use fresh Live Map growth to select the matching Hatchling, Juvenile, Subadult, Adult, or Elder calibration", "aim growth sync live automatic hatchling juvenile subadult adult elder hitbox profile"),
        new("vitals-hud", "Toggle Core Vitals HUD", "Keep or hide the compact health, food, water, and stamina strip", "core vitals hud health hp food water stamina stats footer persistent toggle"),
        new("dock-overlay", "Dock Isley", "Collapse Isley into its small draggable and taskbar-accessible bar", "dock minimize collapse small tiny accessible taskbar open restore overlay"),
        new("hotkeys", "Open Hotkey Studio", "Rebind, disable, restore, and resolve global shortcut conflicts", "hotkey hotkeys shortcut shortcuts keybind key binding conflict customize keyboard ctrl shift alt"),
        new("server-session", "Choose server mode", "Use Live Map, Official, or Any Server compatibility without requiring private server data", "server session mode profile universal all any official community unofficial private password unlisted live map compatibility"),
        new("universal-coordinates", "Toggle Player Sync", "Place your map icon from Asset Location copies; two points also unlock Terrain Probe slope checks", "player sync terrain probe asset location coordinates clipboard capture hill slope grade elevation universal official community live map navigation"),
        new("save-slope-avoidance", "Save measured slope avoidance", "Convert the current Terrain Probe segment into a reversible local route obstacle", "terrain probe slope hill descent climb save avoid avoidance obstacle no go route course replan"),
        new("community-server-watch", "Open Any Server setup", "Optional local name, growth rate, and public status for community, private, or unlisted sessions", "any server community unofficial private password unlisted universal optional public population watch growth name address host port"),
        new("voice-chat", "Open Isley Voice", "Built-in private-room proximity voice, push-to-talk, deafen, and connection status", "voice chat proximity ptt push talk microphone room deafen built in"),
        new("voice-quality", "Open Voice Quality", "Monitor coarse WebRTC round trip, jitter, and packet loss without exposing peer addresses", "voice quality connection lag latency rtt jitter packet loss weak poor network call diagnostic"),
        new("join-voice-room", "Paste Isley Voice invite", "Validate a private-room invite from the clipboard without opening the microphone", "voice join room invite paste clipboard private encrypted ptt proximity"),
        new("voice-share-route", "Share route to voice room", "Offer the current route peer-to-peer; every recipient must accept explicitly", "voice route share send offer pack room peer course navigation accept consent"),
        new("field-guide", "Open Field Guide", "Search species roles, survival notes, controls, and current references", "guide species dinosaur controls survival role food diet tips favorite roster"),
        new("combat-guide", "Open species combat brief", "Signature action, positioning, abort condition, mutation search, and damage triage", "combat fight battle damage species signature attack position abort counter mutation survival triage"),
        new("play-focus", "Toggle Play Focus", "Stay visible over The Isle, pass clicks through, and hide over unrelated apps", "game focus overlay click through hide recovery interact"),
        new("server-status", "Check optional public server", "Population, online state, map, version, and source freshness", "server status online population players capacity gateway version source refresh"),
        new("patch-watch", "Open Official Patch Watch", "Compare Isley's guide baseline with the newest official public patch", "patch update version public branch official steam news notes guide current outdated refresh"),
        new("copy-server-address", "Copy optional public server address", "Copy the configured optional public server address for joining or sharing", "server ip address connect clipboard join copy"),
        new("map-lighting", "Cycle map lighting", "Switch terrain between Day, Dim, and Night without dimming HUD alerts", "night dark dim brightness low light glare visual comfort terrain"),
        new("hud-detail", "Cycle HUD detail", "Switch among Full, Essential, and Clean map overlays without hiding safety alerts", "hud detail density declutter clean minimal ambient cards visual comfort warning safety"),
        new("hud-surfaces", "Open HUD Surface Manager", "Individually show or hide ten map HUD surfaces without stopping their tools", "hud surfaces widgets visibility manager navigation vitals pack encounter survival voice alerts nearby aim keys toggle customize"),
        new("quick-keys", "Toggle Quick Keys", "Show or hide a click-through rail of rebindable default survival, combat, or call controls", "quick keys controls keybind binds survival combat calls reference hud toggle"),
        new("smart-hud", "Toggle Smart HUD", "Give urgent survival guidance priority over ambient cards on compact layouts", "smart adaptive automatic hud priority safety focus declutter compact survival voice pack navigation"),
        new("lite-mode", "Toggle Lite Mode", "Reduce Isley's background work and effects while keeping core live tools available", "lite lightweight low cpu gpu memory performance fps laptop potato background efficient mode"),
        new("hud-dock", "Mirror HUD dock", "Swap navigation and tactical cards between the left and right map rails", "hud dock layout mirror left right cards widgets position collision voice map clean"),
        new("focus-modes", "Open focus modes", "Switch the whole map among six purpose-built profiles", "focus profile preset balanced travel survival pack combat nest restore"),
        new("focus-combat", "Apply Combat focus", "Use high-contrast contacts, near rings, short trails, and a 25 MU encounter alert", "focus combat fight threat contact alert quick preset"),
        new("focus-nest", "Apply Nest focus", "Use wide perimeter rings, food and zone context, friend trails, and a 50 MU encounter alert", "focus nest nesting perimeter food zone friend alert preset"),
        new("hub", "Open player hub tools", "Dino, Prime, quests, skins, rewards, and guide", "dino prime quests battlepass garage skins hub"),
        new("heading", "Toggle north or heading up", "Rotate the map presentation around your live heading", "orientation north heading rotate"),
        new("grid", "Toggle tactical grid", "Show or hide the Gateway-scale A1-T20 reference grid", "grid cell coordinates tactical gateway"),
        new("place-labels", "Cycle place label detail", "Declutter overlapping bundled map names or reveal every label", "labels names places landmarks clarity density focus auto full declutter"),
        new("rings", "Cycle range rings", "Switch between Off, Near 10/25, Standard 25/50, and Wide 50/100 MU", "range radius distance rings close near wide tactical scale"),
        new("waypoint", "Place or clear waypoint", "Arm a map click or clear the active waypoint", "route target waypoint clear"),
        new("streamer", "Toggle streamer mode", "Hide live positions, identities, and sensitive context", "privacy stream hide redact"),
        new("reload", "Reload live map", "Refresh the local map shell and current Gateway layers", "refresh reconnect map reload"),
        new("preset-navigation", "Apply TRAVEL layers", "Place names plus sanctuary, migration, and patrol zones", "preset map layers zones navigation travel"),
        new("preset-survival", "Apply SURVIVAL layers", "Travel layers plus food sites and nearby live players", "preset food heatmap migration survival travel"),
        new("map-undo-clear", "Undo last map clear", "Restore the pins, route, no-go area, or measurement removed by the last clear", "undo clear pins route no-go measurement restore markers back"),
        new("map-routes-share", "Copy route share code", "Copy a share code of your active route plan to send to your pack", "route share code copy export map pack send plan stops"),
        new("map-routes-import", "Import shared route", "Start a route from a pack member's share code on your clipboard", "route share code import paste start map pack receive plan"),
        new("map-nogo-share", "Copy no-go share code", "Copy a share code of your no-go areas to send to your pack", "no-go area share code copy export map pack send zone avoid"),
        new("map-nogo-import", "Import shared no-go areas", "Add no-go areas from a pack member's share code on your clipboard", "no-go area share code import paste add map pack receive zone"),
        new("map-route-replan", "Toggle route auto-replan", "Re-plan the active route from your position when you stray off it", "route auto replan deviation off course toggle reroute stray")
    ];

    private sealed class PlaceSearchSuggestion
    {
        public string Kind { get; set; } = string.Empty;
        public string PinId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool Favorite { get; set; }
        public double? ExpiresInMs { get; set; }
        public string Label { get; set; } = string.Empty;
        public string GridReference { get; set; } = string.Empty;
        public double? Distance { get; set; }
        public double? Bearing { get; set; }
        public string Cardinal { get; set; } = string.Empty;
    }

    private sealed class PinImportResult
    {
        public bool Valid { get; set; }
        public bool Imported { get; set; }
        public string Error { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int AddedCount { get; set; }
        public int DuplicateCount { get; set; }
        public int ExpiredCount { get; set; }
        public int TrimmedCount { get; set; }
        public int ResultCount { get; set; }
        public int TotalAreaCount { get; set; }
        public int AddedAreaCount { get; set; }
        public int DuplicateAreaCount { get; set; }
        public int TrimmedAreaCount { get; set; }
        public int ResultAreaCount { get; set; }
    }

    private sealed class SurvivalTimer
    {
        public string Id { get; init; } = string.Empty;
        public string Label { get; set; } = "Survival timer";
        public int DurationSeconds { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public double PausedRemainingSeconds { get; set; }
        public bool IsPaused { get; set; }
        public bool Completed { get; set; }
        public bool CompletionNotified { get; set; }
    }

    private sealed class SurvivalTimerSettings
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = "Survival timer";
        public int DurationSeconds { get; set; }
        public long EndsAtUnixMs { get; set; }
        public double PausedRemainingSeconds { get; set; }
        public bool IsPaused { get; set; }
        public bool Completed { get; set; }
    }

    private sealed class LifeRunSettings
    {
        public bool Active { get; set; }
        public long StartedAtUnixMs { get; set; }
        public int StageIndex { get; set; } = 1;
        public bool HudVisible { get; set; } = true;
        public bool SanctuaryVisited { get; set; }
        public bool PerfectDiet { get; set; }
        public bool NestedIn { get; set; }
        public bool RaisedYoung { get; set; }
        public bool SpawnCoverReady { get; set; }
        public bool SpawnScentChecked { get; set; }
        public bool SpawnWaterFound { get; set; }
        public bool SpawnFoodFound { get; set; }
        public int CurrentZoneIndex { get; set; }
        public int MigrationVisits { get; set; }
        public int PatrolVisits { get; set; }
        public bool MassMigrationVisited { get; set; }
        public int FertilityStatus { get; set; }
        public int SpasmStatus { get; set; }
        public int SpeciesClass { get; set; }
        public int DietSpeciesIndex { get; set; }
        public int DietTargetIndex { get; set; }
        public int DietSlot1 { get; set; }
        public int DietSlot2 { get; set; }
        public int DietSlot3 { get; set; }
        public int? GrowthPercent { get; set; }
        public int GrowthServerMultiplierIndex { get; set; } = GrowthPlannerLogic.DefaultLiveMapMultiplierIndex;
        public bool GrowthPaused { get; set; }
        public int ElderEntombCount { get; set; }
        public bool ElderPrimeConfirmed { get; set; }
        public bool ElderConfirmed { get; set; }
        public NestPlannerSettings NestPlanner { get; set; } = new();
        public List<MutationLoadoutSettings> MutationLoadout { get; set; } = [];
        public int MutationBuildFocusIndex { get; set; }
        public int MutationUnlockSelectedIndex { get; set; }
        public List<MutationUnlockProgressSettings> MutationUnlockProgress { get; set; } = [];
    }

    private sealed class NestPlannerSettings
    {
        public bool Active { get; set; }
        public int PhaseIndex { get; set; }
        public bool PartnerReady { get; set; }
        public bool SiteReady { get; set; }
        public bool DebrisReady { get; set; }
        public bool ReservesReady { get; set; }
        public int AccessIndex { get; set; }
        public int EggTarget { get; set; } = 2;
        public int EggsLaid { get; set; }
        public int EggsHatched { get; set; }
        public int YoungRaised { get; set; }
        public int TimerDurationIndex { get; set; } = 1;
        public bool AutoHatchGuidanceEnabled { get; set; } = true;
    }

    private sealed class MutationLoadoutSettings
    {
        public int Slot { get; set; }
        public string MutationId { get; set; } = string.Empty;
        public int Status { get; set; }
    }

    private sealed class MutationUnlockProgressSettings
    {
        public string ChallengeId { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private sealed class FocusModeSnapshotSettings
    {
        public bool PlayerLabelsVisible { get; set; }
        public bool FriendOnly { get; set; }
        public bool HeadingUp { get; set; }
        public bool RangeRingsVisible { get; set; }
        public int? RangeRingModeIndex { get; set; }
        public bool MapGridVisible { get; set; }
        public int LandmarkLabelDensityIndex { get; set; }
        public bool BreadcrumbTrailVisible { get; set; } = true;
        public bool FriendRadarVisible { get; set; } = true;
        public bool NearestPlaceVisible { get; set; } = true;
        public int TrailDurationIndex { get; set; } = 2;
        public int ArrivalAlertIndex { get; set; } = 2;
        public int DangerAlertIndex { get; set; } = 2;
        public int? MarkerStyleIndex { get; set; }
        public int? HudDetailModeIndex { get; set; }
        public bool? EncounterHudVisible { get; set; }
        public int? EncounterAlertIndex { get; set; }
        public int? EncounterMemoryIndex { get; set; }
        public bool? LocationsLayer { get; set; }
        public bool? SanctuariesLayer { get; set; }
        public bool? MigrationLayer { get; set; }
        public bool? PatrolLayer { get; set; }
        public bool? FoodLayer { get; set; }
        public bool? HeatmapLayer { get; set; }
        public bool? OfficialSelfTrail { get; set; }
        public bool? OfficialFriendTrails { get; set; }
    }

    private sealed class EscapeRouteResult
    {
        public bool Ok { get; set; }
        public string Reason { get; set; } = string.Empty;
        public double? Bearing { get; set; }
        public string Cardinal { get; set; } = string.Empty;
        public double? Distance { get; set; }
        public double? Deflection { get; set; }
        public int ConsideredObstacleCount { get; set; }
        public int ExitedObstacleCount { get; set; }
    }

    private static readonly JsonSerializerOptions MapperJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class AimCalibrationProfileSettings
    {
        public string SpeciesId { get; set; } = string.Empty;
        public string AttackId { get; set; } = "primary";
        public int? GrowthIndex { get; set; }
        public int? CameraIndex { get; set; }
        public int ModeIndex { get; set; } = AimCalibrationLogic.DefaultModeIndex;
        public double Size { get; set; } = AimCalibrationLogic.DefaultSize;
        public double? DepthScale { get; set; }
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
        public int ConfirmedMatches { get; set; }
        public int InsideMisses { get; set; }
        public int OutsideHits { get; set; }
        public long UpdatedAtUnixMs { get; set; }
    }

    private sealed class MapperSettings
    {
        public const int CurrentSchemaVersion = 1;

        public MapperSettings()
        {
        }

        public int SchemaVersion { get; set; }

        public double Width { get; set; } = 472;
        public double Height { get; set; } = 560;
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public int OpacityIndex { get; set; }
        public int MapLightModeIndex { get; set; }
        public int HudDetailModeIndex { get; set; }
        public bool SmartHudEnabled { get; set; } = true;
        public bool LiteModeEnabled { get; set; }
        public bool AutomaticUpdatesEnabled { get; set; } = true;
        public int OnboardingTutorialVersionCompleted { get; set; }
        public bool HudDockMirrored { get; set; }
        public int ZoomPresetIndex { get; set; } = 1;
        public int TrailDurationIndex { get; set; } = 2;
        public int ArrivalAlertIndex { get; set; } = 2;
        public int DangerAlertIndex { get; set; } = 2;
        public int PackSpreadAlertIndex { get; set; } = 2;
        public int EncounterAlertIndex { get; set; } = 2;
        public int EncounterMemoryIndex { get; set; } = 2;
        public int LandmarkLabelDensityIndex { get; set; }
        public int MarkerStyleIndex { get; set; }
        public bool PlayerLabelsVisible { get; set; } = true;
        public bool FriendOnly { get; set; }
        public bool HeadingUp { get; set; }
        public bool LookAheadEnabled { get; set; } = true;
        public bool SmartZoomEnabled { get; set; } = true;
        public bool RangeRingsVisible { get; set; }
        public int? RangeRingModeIndex { get; set; }
        public bool MapGridVisible { get; set; }
        public bool BreadcrumbTrailVisible { get; set; } = true;
        public bool ExplorationEnabled { get; set; }
        public string TerrainRouteStyle { get; set; } = TerrainRouteStyleLogic.BalancedId;
        public string TerrainGapPolicy { get; set; } = TerrainGapPolicyLogic.BalancedId;
        public bool TerrainRouteConfidenceVisible { get; set; } = true;
        public bool LearnedPassageRoutingEnabled { get; set; } = true;
        public bool LearnedPassageVisible { get; set; } = true;
        public bool FriendRadarVisible { get; set; } = true;
        public bool EncounterHudVisible { get; set; } = true;
        public bool NearestPlaceVisible { get; set; } = true;
        public bool StaleSoundEnabled { get; set; } = true;
        public bool TimerSoundEnabled { get; set; } = true;
        public bool RememberLastPosition { get; set; } = true;
        public bool AlwaysOnTop { get; set; } = true;
        public bool OverlayLocked { get; set; }
        public bool PlayFocusEnabled { get; set; }
        public bool NavigationHudVisible { get; set; } = true;
        public bool VitalsHudVisible { get; set; } = true;
        public bool SurvivalHudVisible { get; set; } = true;
        public bool AlertHudVisible { get; set; } = true;
        public bool QuickKeysHudVisible { get; set; }
        public int QuickKeysModeIndex { get; set; }
        public bool AimGuideEnabled { get; set; }
        public int AimGuideGrowthIndex { get; set; } = AimCalibrationLogic.DefaultGrowthIndex;
        public bool AimGuideGrowthSyncEnabled { get; set; } = true;
        public int AimGuideCameraIndex { get; set; } = AimCalibrationLogic.DefaultCameraIndex;
        public int AimGuideModeIndex { get; set; } = 1;
        public double AimGuideSize { get; set; } = 220;
        public double AimGuideDepthScale { get; set; } = AimCalibrationLogic.DefaultDepthScale;
        public double AimGuideHorizontalOffset { get; set; }
        public double AimGuideVerticalOffset { get; set; }
        public int AimGuideAttackIndex { get; set; }
        public bool AimGuideAreaVisible { get; set; } = true;
        public bool AimGuideCenterCueVisible { get; set; } = true;
        public bool AimGuideUncertaintyVisible { get; set; } = true;
        public bool AimGuideLabelVisible { get; set; } = true;
        public List<AimCalibrationProfileSettings> AimCalibrationProfiles { get; set; } = [];
        public string ServerSessionProfileId { get; set; } = ServerSessionLogic.LiveMapId;
        public string ServerSessionName { get; set; } = "Any Isle server";
        public string CommunityServerAddress { get; set; } = string.Empty;
        public bool CommunityServerWatchEnabled { get; set; }
        public bool CommunityServerSlotAlertEnabled { get; set; }
        public bool UniversalCoordinateCaptureEnabled { get; set; } = true;
        public bool VisibleHudSensorEnabled { get; set; }
        public double VisibleHudCalibrationScale { get; set; } = 1;
        public double VisibleHudCalibrationOffsetX { get; set; }
        public double VisibleHudCalibrationOffsetY { get; set; }
        public double VisibleHudCalibrationScore { get; set; }
        public bool AutoLocateOnGameStart { get; set; } = true;
        public int PlayerSyncSetupVersion { get; set; }
        public string SelectedCommunityServerProfileId { get; set; } = string.Empty;
        public List<CommunityServerProfileSettings> CommunityServerProfiles { get; set; } = [];
        public List<SteamFriendWatchEntry> SteamFriendWatchlist { get; set; } = [];
        public string SelectedSteamFriendWatchId { get; set; } = string.Empty;
        public string AutoFollowSteamFriendWatchId { get; set; } = string.Empty;
        public string IsleyRelayJoinLink { get; set; } = string.Empty;
        public List<HotkeyBindingSettings> HotkeyBindings { get; set; } = [];
        public List<string> CommandFavoriteActionIds { get; set; } = [];
        public List<string> CommandRecentActionIds { get; set; } = [];
        public bool VoiceEnabled { get; set; } = true;
        public bool VoiceAutoOpen { get; set; } = true;
        public bool VoiceHudVisible { get; set; } = true;
        public int VoicePttKeyIndex { get; set; }
        public string VoiceServerUrl { get; set; } = "ws://127.0.0.1:5198/voice";
        public bool VoiceNatAssist { get; set; } = true;
        public bool VoiceProximityEnabled { get; set; } = true;
        public int VoiceRangeIndex { get; set; } = 1;
        public bool VoiceEchoCancellation { get; set; } = true;
        public bool VoiceNoiseSuppression { get; set; } = true;
        public bool VoiceAutoGainControl { get; set; } = true;
        public bool VoiceMicMeterEnabled { get; set; } = true;
        public bool VoiceQualityMonitorEnabled { get; set; } = true;
        public string GuideSelectedSpeciesId { get; set; } = "allosaurus";
        public List<string> GuideFavoriteSpeciesIds { get; set; } = [];
        public List<SurvivalTimerSettings> SurvivalTimers { get; set; } = [];
        public string SurvivalIncidentId { get; set; } = string.Empty;
        public long SurvivalIncidentStartedAtUnixMs { get; set; }
        public int SurvivalIncidentAdditionalSeconds { get; set; }
        public bool SurvivalIncidentHudCollapsed { get; set; }
        public LifeRunSettings LifeRun { get; set; } = new();
        public List<LifeRunHistoryEntry> LifeRunHistory { get; set; } = [];
        public FocusModeSnapshotSettings? FocusModeRestoreSnapshot { get; set; }
        public string ActiveFocusModeId { get; set; } = string.Empty;
        public bool PressureCoachFirstDeathSeen { get; set; }
        public bool PressureCoachFirstNestSeen { get; set; }
        public bool PressureCoachConsentRosterSeen { get; set; }
        public bool PressureCoachPreStreamSeen { get; set; }
        public string WhatsNewVersionSeen { get; set; } = string.Empty;
        public bool PreferBetaUpdates { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();

        _voiceKeyboardHookProc = VoiceKeyboardHook;
        LoadSettings();
        _isleyRelayClient.SnapshotReceived += IsleyRelayClient_SnapshotReceived;
        _isleyRelayClient.StateChanged += IsleyRelayClient_StateChanged;
        EnsureCommunityServerProfiles();
        var performanceProfile = LiteModeLogic.Resolve(_liteModeEnabled);
        Opacity = _opacityLevels[_opacityIndex];
        Topmost = _alwaysOnTop;
        _gamePollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(performanceProfile.GamePollMilliseconds)
        };
        _gamePollTimer.Tick += (_, _) => RefreshGameState();
        _playFocusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(performanceProfile.PlayFocusMilliseconds)
        };
        _playFocusTimer.Tick += (_, _) =>
        {
            RefreshPlayFocus();
            EnsureOverlayPriority();
            RefreshAimGuideVisibility();
            RefreshUniversalCoordinateCapture();
            RefreshVisibleHudSensor();
            _ = RefreshIndependentLiveDataAsync();
        };
        _serverStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _serverStatusTimer.Tick += async (_, _) => await RefreshServerStatusAsync();
        _officialPatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _officialPatchTimer.Tick += async (_, _) => await RefreshOfficialPatchAsync();
        _isleyUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _isleyUpdateTimer.Tick += async (_, _) =>
            await RefreshIsleyUpdateAsync(userRequested: false);
        _survivalTimerTick = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(performanceProfile.SurvivalRefreshMilliseconds)
        };
        _survivalTimerTick.Tick += (_, _) =>
        {
            UpdateServerRestartWatch();
            UpdateSafeLogoutGuard();
            UpdateSurvivalTimers();
            UpdateCoreVitals();
            UpdateSurvivalAssistant();
            UpdateFieldConditions();
            UpdateWaterCrossingCheck();
            UpdateShorelineCheck();
            UpdateLifeRun();
            UpdateManualSighting();
            UpdateNextMove();
            UpdateTripReadiness();
            UpdateFightCheck();
            UpdateSoundFinder();
        };
        _voiceStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(performanceProfile.VoiceStatusMilliseconds)
        };
        _voiceStatusTimer.Tick += (_, _) => RefreshVoiceStatus();

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyControlStates();
        UpdateZoomDisplay();
        WindowSizeText.Text = $"{ActualWidth:0}x{ActualHeight:0}";
        RefreshGameState();
        _gamePollTimer.Start();
        RefreshPlayFocus();
        ResetUniversalCoordinateClipboardBaseline();
        _playFocusTimer.Start();
        UpdateServerSessionPresentation(animate: false);
        UpdateUniversalCoordinatePresentation(force: true);
        UpdateServerStatusPresentation();
        UpdateOfficialPatchPresentation();
        _officialPatchTimer.Start();
        _ = RefreshOfficialPatchAsync();
        UpdateIsleyUpdatePresentation();
        if (_automaticUpdatesEnabled)
        {
            _isleyUpdateTimer.Start();
            _ = CheckForIsleyUpdateAfterStartupAsync();
        }
        ConsumeUpdaterResult();
        if (ShouldPollServerStatus)
        {
            _serverStatusTimer.Start();
            _ = RefreshServerStatusAsync();
        }
        UpdateSurvivalTimers(force: true);
        UpdateServerRestartWatch(force: true);
        UpdateSafeLogoutGuard(force: true);
        UpdateCoreVitals(force: true);
        UpdateSurvivalAssistant(force: true);
        UpdateResponsiveOverlayLayout(force: true);
        UpdateVitalsHudControl();
        UpdateAimGuidePresentation();
        UpdateFieldConditions(force: true);
        UpdateWaterCrossingCheck(force: true);
        UpdateShorelineCheck(force: true);
        UpdateLifeRun(force: true);
        UpdateManualSighting(force: true);
        UpdateNextMove(force: true);
        UpdateTripReadiness(force: true);
        UpdateFightCheck(force: true);
        UpdateResourceFinder(force: true);
        _survivalTimerTick.Start();
        InitializeVoiceSessionFields();
        RefreshVoiceStatus();
        _voiceStatusTimer.Start();
        UpdateIsleyRelayPresentation();
        _ = InitializeIsleyRelayAsync();
        if (LiveMapServicesActive)
        {
            _ = LoadTerrainRoadNetworkAsync();
            _ = LoadGatewayResourceNetworkAsync();
        }
        if (OnboardingTutorialLogic.ShouldShow(_onboardingTutorialVersionCompleted))
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(OpenOnboardingTutorial));
        }
        // Persist the usable window geometry immediately. WebView startup can remain
        // busy while Steam restores a session, so settings must not wait on it.
        SaveSettings();
        if (LiveMapServicesActive)
        {
            await InitializeLiveMapAsync();
        }
        else
        {
            SetLoading(false, string.Empty);
        }
        SaveSettings();
        await TryAutoConnectProximityVoiceAsync();
        await Task.Delay(6500);
        if (IsLoaded)
        {
            HelpTipBorder.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_windowHandle)?.AddHook(WindowMessageHook);
        _voiceKeyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _voiceKeyboardHookProc,
            NativeMethods.GetModuleHandle(null),
            0);

        RegisterAllHotkeys();
        UpdateHotkeyStatus();
        UpdateHotkeyStudio(force: true);
        EnsureOverlayPriority(forceToggle: true);
    }

    protected override void OnClosed(EventArgs e)
    {
        _gamePollTimer.Stop();
        _playFocusTimer.Stop();
        _serverStatusTimer.Stop();
        _officialPatchTimer.Stop();
        _isleyUpdateTimer.Stop();
        _survivalTimerTick.Stop();
        _voiceStatusTimer.Stop();
        if (_voiceLocalHostProcess is { HasExited: false })
        {
            try { _voiceLocalHostProcess.Kill(entireProcessTree: true); } catch { }
        }
        _voiceLocalHostProcess?.Dispose();
        _voiceLocalHostProcess = null;
        _dockWindow?.CloseSilently();
        _dockWindow = null;
        _aimGuideWindow?.Close();
        _aimGuideWindow = null;
        _serverStatusCancellation?.Cancel();
        _serverStatusCancellation?.Dispose();
        _serverStatusCancellation = null;
        _officialPatchCancellation?.Cancel();
        _officialPatchCancellation?.Dispose();
        _officialPatchCancellation = null;
        _isleyUpdateCancellation?.Cancel();
        _isleyUpdateCancellation?.Dispose();
        _isleyUpdateCancellation = null;
        _voiceServerReadinessCancellation?.Cancel();
        _voiceServerReadinessCancellation?.Dispose();
        _voiceServerReadinessCancellation = null;
        _terrainRoadNetworkCancellation?.Cancel();
        _terrainRoadNetworkCancellation?.Dispose();
        _terrainRoadNetworkCancellation = null;
        _gatewayResourceCancellation?.Cancel();
        _gatewayResourceCancellation?.Dispose();
        _gatewayResourceCancellation = null;
        _isleyRelaySignInCancellation?.Cancel();
        _isleyRelaySignInCancellation?.Dispose();
        _isleyRelaySignInCancellation = null;
        _isleyRelayClient.SnapshotReceived -= IsleyRelayClient_SnapshotReceived;
        _isleyRelayClient.StateChanged -= IsleyRelayClient_StateChanged;
        _isleyRelayClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
        SaveSettings();

        if (_voiceKeyboardHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_voiceKeyboardHook);
            _voiceKeyboardHook = 0;
        }

        if (_windowHandle != 0)
        {
            UnregisterAllHotkeys();
        }

        LiveMapWebView.Dispose();
        VoiceWebView.Dispose();
        base.OnClosed(e);
    }

    private async Task LoadGatewayResourceNetworkAsync()
    {
        _gatewayResourceCancellation?.Cancel();
        _gatewayResourceCancellation?.Dispose();
        _gatewayResourceCancellation = new CancellationTokenSource();
        var cancellationToken = _gatewayResourceCancellation.Token;
        _resourceFinderStatus = "loading";
        _resourceFinderUiSignature = string.Empty;
        UpdateResourceFinder(force: true);
        try
        {
            var network = await GatewayResourceClient.FetchAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _gatewayResourceNetwork = network;
            _resourceFinderStatus = "ready";
            _resourceFinderResultIndex = 0;
            _resourceFinderUiSignature = string.Empty;
            UpdateResourceFinder(force: true);
            UpdateDietCoachControls();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _gatewayResourceNetwork = null;
            _resourceFinderStatus = "source-unavailable";
            _resourceFinderUiSignature = string.Empty;
            UpdateResourceFinder(force: true);
            UpdateDietCoachControls();
        }
    }

    private bool LiveMapServicesActive =>
        ServerSessionLogic.HasLiveMapServices(_serverSessionProfileId);

    private bool CommunitySessionActive =>
        string.Equals(_serverSessionProfileId, ServerSessionLogic.CommunityId, StringComparison.Ordinal);

    private bool CommunityServerAddressValid =>
        CommunityServerWatchLogic.TryNormalizeAddress(_communityServerAddress, out _);

    private bool ShouldPollServerStatus =>
        CommunitySessionActive && _communityServerWatchEnabled && CommunityServerAddressValid;

    private static string IndependentLiveDataPath
    {
        get
        {
            if (PortableModeEnabled)
            {
                return Path.Combine(PortableDataDirectory, "LiveData", "positions.json");
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Isley", "LiveData", "positions.json");
        }
    }

    private async Task<bool> AnnouncePatchReviewIfNeededAsync()
    {
        var guidance = CurrentPatchWatchGuidance();
        if (!guidance.NeedsReview
            || string.IsNullOrEmpty(guidance.ReviewVersion)
            || string.Equals(
                _officialPatchWarningAnnouncedVersion,
                guidance.ReviewVersion,
                StringComparison.Ordinal))
        {
            return false;
        }

        _officialPatchWarningAnnouncedVersion = guidance.ReviewVersion;
        var serverAhead = guidance.State == PatchWatchState.ServerAhead;
        var publicVersion = _lastOfficialPatch?.Version ?? "unavailable";
        AddTacticalEvent(
            "PATCH",
            serverAhead ? "Server build needs guide review" : "Official patch needs review",
            serverAhead
                ? $"Server {guidance.ReviewVersion} · public {publicVersion} · Isley guide baseline {IsleContentBaseline.PublicBranch}"
                : $"Public {guidance.ReviewVersion} · Isley guide baseline {IsleContentBaseline.PublicBranch}",
            warning: true);
        await ShowHotkeyToastAsync(
            serverAhead
                ? $"SERVER {guidance.ReviewVersion} · REVIEW GUIDES"
                : $"PATCH {guidance.ReviewVersion} · REVIEW GUIDES",
            false);
        return true;
    }

    private string ServerRestartBriefLabel()
    {
        var view = CurrentServerRestartWatchView();
        if (!view.Visible)
        {
            return string.Empty;
        }

        return view.Phase == ServerRestartWatchPhase.Verify
            ? "RESTART WINDOW ELAPSED · VERIFY"
            : $"RESTART {view.Countdown} · REPORTED";
    }

    private static string PrimarySettingsPath
    {
        get
        {
            if (PortableModeEnabled)
            {
                return Path.Combine(PortableDataDirectory, "settings.json");
            }
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                localAppData = Path.Combine(userProfile, "AppData", "Local");
            }
            return Path.Combine(localAppData, "Isley", "settings.json");
        }
    }

    private static string PortableSettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "Isley.settings.json");

    private static string LegacyMapperSettingsPath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                localAppData = Path.Combine(userProfile, "AppData", "Local");
            }
            return Path.Combine(localAppData, "TheIsleMapper", "settings.json");
        }
    }

    private static string LegacyPortableSettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "TheIsleMapper.settings.json");

    private static string PortableDataDirectory
    {
        get
        {
            var isleyDirectory = Path.Combine(AppContext.BaseDirectory, "IsleyData");
            var legacyDirectory = Path.Combine(AppContext.BaseDirectory, "TheIsleMapperData");
            return LegacyPortableModeEnabled && !File.Exists(Path.Combine(AppContext.BaseDirectory, "Isley.portable"))
                ? legacyDirectory
                : isleyDirectory;
        }
    }

    private static bool LegacyPortableModeEnabled =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "TheIsleMapper.portable"));

    private static bool PortableModeEnabled =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "Isley.portable"))
        || LegacyPortableModeEnabled;

    private enum PlayFocusForeground
    {
        Mapper,
        Game,
        Other
    }

    private static Version CurrentIsleyVersion =>
        typeof(MainWindow).Assembly.GetName().Version ?? new Version(1, 0, 0);

    private HudPriorityPresentation CurrentHudPriorityPresentation(
        bool voiceActive = false,
        bool voiceProblem = false) =>
        HudPriorityLogic.Resolve(new HudPriorityContext(
            _smartHudEnabled,
            ActualWidth > 0 ? ActualWidth : Width,
            ActualHeight > 0 ? ActualHeight : Height,
            SurvivalAssistantLogic.Find(_survivalIncidentId) is not null,
            _markerAvailable,
            voiceActive,
            voiceProblem));

    private async void CopyPositionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _currentSelfX is null || _currentSelfY is null)
        {
            return;
        }

        var originalContent = CopyPositionButton.Content;
        try
        {
            Clipboard.SetText(
                $"Isley position: X {_currentSelfX:0.##}, Y {_currentSelfY:0.##}, " +
                (string.IsNullOrWhiteSpace(_currentGridReference)
                    ? string.Empty
                    : $"grid {_currentGridReference}, ") +
                $"heading {ToCardinal(_currentSelfBearing)} {_currentSelfBearing:000} degrees");
            CopyPositionButton.Content = "Position copied";
        }
        catch
        {
            CopyPositionButton.Content = "Clipboard unavailable";
        }

        await Task.Delay(1400);
        if (IsLoaded)
        {
            CopyPositionButton.Content = originalContent;
        }
    }

    private (int Index, bool Live, int Percent) CurrentAimGrowthContext(string? speciesId = null)
    {
        var activeSpeciesId = speciesId ?? ResolveAimCalibrationSpeciesId();
        var snapshot = CurrentPlayerSnapshotEvaluation();
        var liveGrowthAvailable = LiveMapServicesActive
                                  && snapshot.LiveFresh
                                  && snapshot.SpeciesAvailable
                                  && string.Equals(
                                      snapshot.SpeciesId,
                                      activeSpeciesId,
                                      StringComparison.OrdinalIgnoreCase);
        var index = AimCalibrationLogic.ResolveGrowthIndex(
            liveGrowthAvailable,
            snapshot.GrowthPercent,
            _aimGuideGrowthSyncEnabled,
            _aimGuideGrowthIndex);
        return (
            index,
            liveGrowthAvailable && _aimGuideGrowthSyncEnabled,
            liveGrowthAvailable ? snapshot.GrowthPercent : 0);
    }

    // Wave-2 sidecar DTO for the bounded extras that live beside the main
    // preferences file (voice peer volume memory + Steam friend groups).
    // Serialized by the append-only helpers at the end of MainWindow.Settings.cs.
    private sealed class OverlayExtrasSettings
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<VoicePeerVolumeEntry> VoicePeerVolumes { get; set; } = [];
        public List<SteamFriendGroupEntry> SteamFriendGroups { get; set; } = [];
    }

}
