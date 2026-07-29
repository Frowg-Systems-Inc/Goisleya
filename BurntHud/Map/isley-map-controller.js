(() => {
  const existing = window.__isley ?? window.__theIsleMapper;
  if (existing?.version === 78) {
    existing.recenter();
    window.__isley = existing;
    window.__theIsleMapper = existing;
    return 'already-installed';
  }
  existing?.dispose?.();
  window.__isleyControllerInstallCount =
    Number(window.__isleyControllerInstallCount || 0) + 1;
  const controllerInstallCount = window.__isleyControllerInstallCount;

  let map = null;
  let layer = null;
  let timer = 0;
  let lastControllerWorkAt = 0;
  let drag = null;
  let pendingMapAction = null;
  let mapInteractionRevision = 0;
  let following = true;
  let markerAvailable = false;
  let initialZoomPending = true;
  let lastMessage = '';
  let setReactView = null;
  let lastDispatchedView = '';
  let freshnessAt = 0;
  let lastMarkerSignature = '';
  let resourceObserver = null;
  let originalFetch = null;
  let wrappedFetch = null;
  let playerSnapshotInFlight = false;
  const fullPlayerSnapshotIntervalMs = 2000;
  const litePlayerSnapshotIntervalMs = 5000;
  const playerSnapshotErrorRetryMs = 5000;
  const playerSnapshotMaximumErrorRetryMs = 60000;
  let playerSnapshotNextAt = Date.now() + 250;
  let playerSnapshotFailures = 0;
  let playerSnapshotAbortController = null;
  let playerSnapshotDisposed = false;
  let fastPollTimer = 0;
  let fastPollInFlight = false;
  let markerRequestUrl = '';
  const pagePollControl = window.__isleyPollControl || null;
  let fastPollIntervalMs = Number(pagePollControl?.targetDelayMs) || 500;
  let fastPollDelayMs = Number(pagePollControl?.delayMs) || fastPollIntervalMs;
  let markerFetchPromise = null;
  let lastSharedMarkerResponse = null;
  let lastMarkerRequestStartedAt = 0;
  let latestMarkerPlayers = null;
  let markerResponseCount = 0;
  let markerResponseStatus = 0;
  let markerResponseOk = false;
  let markerResponseSource = 'initial-model';
  let lastAcceptedResponseAt = 0;
  let lastResponseIntervalMs = 0;
  let lastFastPollDurationMs = 0;
  let markerNetworkCount = 0;
  const seenMarkerResources = new Set();
  let lastMarkerNetworkAt = 0;
  let selfPositionAt = 0;
  let selfX = null;
  let selfY = null;
  let selfPoseSource = 'dom';
  let lastRenderedSelfSignature = '';
  let centerErrorPx = null;
  let otherAnimalCount = 0;
  let playerLabelsVisible = true;
  let friendOnly = false;
  let markerStyle = 'standard';
  let liteMode = false;
  let headingUp = false;
  let lookAheadEnabled = true;
  let smartZoomEnabled = true;
  let smartZoomSuspended = false;
  let lastSmartZoomAt = 0;
  let streamerMode = false;
  let trailSeconds = 30;
  let selfHeading = 0;
  let selfSpeed = 0;
  let movementSpeedSamples = [];
  let sessionDistance = 0;
  let sessionStatsStartedAt = 0;
  let sessionMovingMs = 0;
  let sessionMaxSpeed = 0;
  let lastMotionSample = null;
  let lastMotionAt = 0;
  let friendAnimalCount = 0;
  let authorizedAnimalCount = 0;
  let rangeRingsVisible = false;
  let rangeRingRadii = [25, 50];
  let mapGridVisible = false;
  let mapGridRoot = null;
  let mapGridRenderSignature = '';
  let breadcrumbTrailVisible = true;
  let breadcrumbTrailRoot = null;
  let breadcrumbTrailRenderSignature = '';
  const learnedPassageStorageKey = 'isley-learned-passages-v1';
  const learnedPassageRoutingStorageKey = 'isley-learned-passage-routing-v1';
  const learnedPassageVisibilityStorageKey = 'isley-learned-passage-visibility-v1';
  const learnedPassageMaximumCount = 12;
  const learnedPassageMaximumPoints = 120;
  const learnedPassageActiveAgeMs = 90 * 24 * 60 * 60 * 1000;
  const learnedPassageRetentionMs = 180 * 24 * 60 * 60 * 1000;
  let learnedPassages = [];
  let learnedPassageRoutingEnabled = true;
  let learnedPassageVisible = true;
  let learnedPassageRoot = null;
  let learnedPassageRenderSignature = '';
  let soundFinderState = {
    mode: 'sound', target: 'water', first: null, second: null, estimate: null
  };
  let soundFinderRoot = null;
  let soundFinderRenderSignature = '';
  const explorationGridSize = 20;
  const explorationStorageKey = 'the-isle-mapper-exploration-v1';
  let explorationEnabled = false;
  let exploredSectors = new Set();
  let explorationRoot = null;
  let explorationRenderSignature = '';
  let tacticalUiRoot = null;
  let cursorInspector = null;
  let quickActionMenu = null;
  let quickActionPoint = null;
  let waypointEdgeCue = null;
  let waypointEdgeCueVisible = false;
  let waypointEdgeCueSide = '';
  let cursorInspectorFrame = 0;
  let cursorInspectorPosition = null;
  const officialLayers = {
    locations: null,
    sanctuaries: null,
    migration: null,
    patrol: null,
    food: null,
    heatmap: null,
    selfTrail: null,
    friendTrails: null,
    shareLocation: null
  };
  let waypoint = null;
  const waypointKinds = new Set([
    'safe', 'nest', 'food', 'danger', 'water', 'rally', 'death',
    'friend', 'pack', 'salt', 'mud', 'gastrolith', 'resource',
    'estimate', 'escape', 'recovery'
  ]);
  const normalizeWaypointKind = value => {
    const normalized = String(value || '')
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9-]/g, '')
      .slice(0, 24);
    return waypointKinds.has(normalized) ? normalized : '';
  };
  let waypointArmed = false;
  let waypointDistance = null;
  let waypointBearing = null;
  let waypointCardinal = '';
  let waypointApproachKey = '';
  let waypointApproachMotionAt = 0;
  let waypointApproachSamples = [];
  let waypointInitialDistance = null;
  let waypointTrend = 'waiting';
  let waypointClosingRate = null;
  let waypointProgressPercent = null;
  let routePlanArmed = false;
  let routePlanActive = false;
  let routePlanComplete = false;
  let routePlanSource = '';
  let routeStops = [];
  let routeCurrentIndex = 0;
  let routePlanRoot = null;
  let routeAdvanceDistance = 10;
  let routeAdvanceTimer = 0;
  let routeAutoReplanEnabled = true;
  let routeAutoReplanAt = 0;
  let routeAutoReplanTimer = 0;
  let terrainRoadNetwork = null;
  let terrainNetworkReady = false;
  let terrainRoadDisplayRoot = null;
  let terrainRoadDisplaySignature = '';
  const terrainRouteStyleStorageKey = 'isley-terrain-route-style-v1';
  let terrainRouteStyle = (() => {
    try {
      const stored = String(localStorage.getItem(terrainRouteStyleStorageKey) || '')
        .trim().toLowerCase();
      return ['balanced', 'road-first', 'shortest'].includes(stored)
        ? stored
        : 'balanced';
    } catch { return 'balanced'; }
  })();
  const terrainGapPolicyStorageKey = 'isley-terrain-gap-policy-v1';
  let terrainGapPolicy = (() => {
    try {
      const stored = String(localStorage.getItem(terrainGapPolicyStorageKey) || '')
        .trim().toLowerCase();
      return ['strict', 'balanced', 'flexible'].includes(stored)
        ? stored
        : 'balanced';
    } catch { return 'balanced'; }
  })();
  let terrainCourseDestination = null;
  let terrainCourseDirectDistance = null;
  let terrainCourseDistance = null;
  let terrainCourseDetourPercent = null;
  let terrainCourseAvoidedZoneCount = 0;
  let terrainCourseRoadDistance = 0;
  let terrainCourseTrailDistance = 0;
  let terrainCourseLearnedDistance = 0;
  let terrainCourseUnknownDistance = 0;
  let terrainCourseLongestUnknown = 0;
  let terrainCourseUnknownSegmentCount = 0;
  let terrainCourseSegments = [];
  let terrainRouteEvidenceVisible = true;
  let terrainCourseStatus = 'waiting-source';
  let terrainCourseReplanAt = 0;
  let terrainCourseReplanTimer = 0;
  const terrainWaterSafetyStorageKey = 'isley-terrain-water-safety-v1';
  let terrainWaterSafetyEnabled = (() => {
    try { return localStorage.getItem(terrainWaterSafetyStorageKey) !== 'off'; }
    catch { return true; }
  })();
  let terrainWaterMaskStatus = 'waiting-source';
  let terrainWaterMask = null;
  let terrainWaterMaskLoadRevision = 0;
  let terrainWaterVisual = null;
  let terrainCourseAvoidedWater = false;
  const terrainCommunityHazardStorageKey =
    'isley-terrain-community-hazards-v1';
  let terrainCommunityHazardsEnabled = (() => {
    try {
      return localStorage.getItem(terrainCommunityHazardStorageKey) !== 'off';
    } catch { return true; }
  })();
  let terrainCommunityHazards = [];
  let terrainCommunityHazardStatus = 'waiting-source';
  let terrainCommunityHazardSource = null;
  let terrainCommunityHazardRoot = null;
  let friendRouteName = '';
  let measurementArmed = false;
  let measurementStart = null;
  let measurement = null;
  let measurementRoot = null;
  let activePinId = '';
  const pinStorageKey = 'the-isle-mapper-saved-pins-v4';
  const pinTypes = {
    safe: { label: 'Safe', short: 'S', color: '#38bdf8' },
    nest: { label: 'Nest', short: 'N', color: '#a78bfa' },
    food: { label: 'Food', short: 'F', color: '#34d399' },
    danger: { label: 'Danger', short: '!', color: '#fb923c' },
    water: { label: 'Water', short: 'W', color: '#60a5fa' },
    rally: { label: 'Rally', short: 'R', color: '#facc15' },
    death: { label: 'Death', short: 'X', color: '#f87171' }
  };
  const pinExpiryMinutes = [0, 5, 15, 30, 60];
  const pinAlertRadii = [0, 10, 25, 50, 100];
  let pinType = 'safe';
  let pinArmed = false;
  let savedPins = [];
  let pinRoot = null;
  const noGoAreaStorageKey = 'isley-no-go-areas-v1';
  const noGoAreaMaximumCount = 8;
  const noGoAreaMaximumVertices = 12;
  let noGoAreas = [];
  let noGoTrace = null;
  let noGoAreaRoot = null;
  let noGoSelectedAreaId = '';
  let noGoLastStatus = 'ready';
  let noGoHighlightAreaId = '';
  let noGoHighlightTimer = 0;
  const lastPositionStorageKey = 'the-isle-mapper-last-live-position-v1';
  let rememberLastPositionEnabled = true;
  let lastLivePosition = null;
  let sessionStartPosition = null;
  let breadcrumbSamples = [];
  let breadcrumbDistance = 0;
  let lastPositionSavedAt = 0;
  let nearestFriendName = '';
  let nearestFriendDistance = null;
  let nearestFriendBearing = null;
  let nearestFriendCardinal = '';
  let friendRoster = [];
  let packFriendCount = 0;
  let packSpread = null;
  let packSpreadMotion = '';
  let packSpreadRate = null;
  let packSpreadMotionSampleCount = 0;
  let packCourseState = '';
  let packCourseSpeed = null;
  let packCourseBearing = null;
  let packCourseCardinal = '';
  let packCourseSampleCount = 0;
  let packSpreadMotionRosterKey = '';
  let packSpreadMotionSamples = [];
  let packRadius = null;
  let packCenterDistance = null;
  let packCenterBearing = null;
  let packCenterCardinal = '';
  let packFarthestFriendName = '';
  let packFarthestFriendDistance = null;
  let packOutlierPoint = null;
  let packCenterPoint = null;
  let packRouteActive = false;
  let packOutlierRouteActive = false;
  let encounterPlayerCount = 0;
  let nearestEncounterDistance = null;
  let nearestEncounterBearing = null;
  let nearestEncounterCardinal = '';
  let nearestEncounterMotion = '';
  let nearestEncounterRelativeSpeed = null;
  let nearestEncounterInterceptSeconds = null;
  let nearestEncounterMotionSampleCount = 0;
  const encounterMotionTracks = new Map();
  let encounterWithin10 = 0;
  let encounterWithin25 = 0;
  let encounterWithin50 = 0;
  let encounterMemorySeconds = 300;
  const encounterMemoryTracks = new Map();
  let encounterMemoryLiveNames = new Set();
  let encounterMemoryRoot = null;
  let encounterMemoryRenderSignature = '';
  let encounterMemoryTrackCount = 0;
  let rememberedEncounterCount = 0;
  let rememberedEncounterNewestAgeMs = null;
  let nearestRememberedEncounterDistance = null;
  let nearestRememberedEncounterBearing = null;
  let nearestRememberedEncounterCardinal = '';
  let nearestPlaceName = '';
  let nearestPlaceDistance = null;
  let nearestPlaceBearing = null;
  let nearestPlaceCardinal = '';
  let nearestPlacePoint = null;
  let officialLandmarkCatalog = [];
  let officialLandmarkCatalogUpdatedAt = 0;
  let landmarkLabelDensity = 'auto';
  let visibleLandmarkCount = 0;
  let landmarkLabelLayoutAt = 0;
  let landmarkLabelSequence = 0;
  let recentRoutes = [];
  let trailRoot = null;
  let playerStyleRoot = null;
  let playerStyleRenderSignature = '';
  const trailSamples = new Map();
  const view = { scale: 1, tx: 0, ty: 0 };

  const calculateFollowTarget = (
    width,
    height,
    heading = 0,
    isHeadingUp = false,
    lookAhead = true,
    speed = 0
  ) => {
    const resolvedWidth = Number(width);
    const resolvedHeight = Number(height);
    if (!Number.isFinite(resolvedWidth) || !Number.isFinite(resolvedHeight)
        || resolvedWidth <= 0 || resolvedHeight <= 0) {
      return null;
    }
    const centerX = resolvedWidth / 2;
    const centerY = resolvedHeight / 2;
    if (!lookAhead) return { x: centerX, y: centerY, offsetPx: 0 };
    const resolvedSpeed = Math.max(0, Number(speed) || 0);
    const speedFactor = Math.min(1, resolvedSpeed / 60);
    const offsetPx = Math.min(resolvedWidth, resolvedHeight)
      * (0.12 + speedFactor * 0.08);
    if (isHeadingUp) {
      return { x: centerX, y: centerY + offsetPx, offsetPx };
    }
    const radians = ((Number(heading) || 0) % 360) * Math.PI / 180;
    return {
      x: centerX - Math.sin(radians) * offsetPx,
      y: centerY + Math.cos(radians) * offsetPx,
      offsetPx
    };
  };

  const chooseSmartFollowScale = (speed, currentScale) => {
    const resolvedSpeed = Math.max(0, Number(speed) || 0);
    const resolvedScale = Number.isFinite(Number(currentScale))
      ? Math.min(25, Math.max(1, Number(currentScale)))
      : 6;
    if (resolvedSpeed >= 48) return 2.5;
    if (resolvedSpeed <= 0.2) return 6;
    if (resolvedScale <= 3.5 && resolvedSpeed >= 28) return 2.5;
    if (resolvedSpeed < 10) return 12;
    if (resolvedScale >= 9 && resolvedSpeed < 18) return 12;
    return 6;
  };

  const normalizeLearnedPassageLibrary = (
    values,
    now = Date.now(),
    maximumCount = learnedPassageMaximumCount,
    maximumPoints = learnedPassageMaximumPoints,
    retentionMs = learnedPassageRetentionMs
  ) => {
    const resolvedNow = Math.max(0, Number(now) || 0);
    const countLimit = Math.max(1, Math.min(24, Math.floor(Number(maximumCount) || 12)));
    const pointLimit = Math.max(2, Math.min(240, Math.floor(Number(maximumPoints) || 120)));
    const retention = Math.max(
      24 * 60 * 60 * 1000,
      Math.min(365 * 24 * 60 * 60 * 1000, Number(retentionMs) || learnedPassageRetentionMs));
    return (Array.isArray(values) ? values : [])
      .map((passage, index) => {
        const createdAt = Math.max(0, Math.floor(Number(passage?.createdAt) || 0));
        if (!createdAt || createdAt > resolvedNow + 5 * 60 * 1000
            || resolvedNow - createdAt > retention) return null;
        const points = (Array.isArray(passage?.points) ? passage.points : [])
          .filter(point => Number.isFinite(Number(point?.x))
            && Number.isFinite(Number(point?.y)))
          .map(point => ({
            x: Math.min(1000, Math.max(0, Number(point.x))),
            y: Math.min(1000, Math.max(0, Number(point.y)))
          }))
          .filter((point, pointIndex, all) => pointIndex === 0
            || Math.hypot(
              point.x - all[pointIndex - 1].x,
              point.y - all[pointIndex - 1].y) >= 0.25)
          .slice(0, pointLimit);
        if (points.length < 2) return null;
        const distance = points.slice(1).reduce((total, point, pointIndex) =>
          total + Math.hypot(
            point.x - points[pointIndex].x,
            point.y - points[pointIndex].y), 0);
        if (!Number.isFinite(distance) || distance < 20 || distance > 10000) return null;
        return {
          id: String(passage?.id || `learned-${createdAt}-${index}`)
            .replace(/[^a-zA-Z0-9_-]/g, '').slice(0, 64)
            || `learned-${createdAt}-${index}`,
          label: String(passage?.label || 'Player-traveled passage')
            .replace(/[\u0000-\u001f\u007f]+/g, ' ')
            .replace(/\s+/g, ' ').trim().slice(0, 48)
            || 'Player-traveled passage',
          createdAt,
          sourceVersion: String(passage?.sourceVersion || '')
            .replace(/[^a-zA-Z0-9._-]/g, '').slice(0, 24),
          distance,
          points
        };
      })
      .filter(Boolean)
      .sort((a, b) => a.createdAt - b.createdAt)
      .slice(-countLimit);
  };

  const learnedPassageIsCurrent = (
    passage,
    currentSourceVersion = '',
    now = Date.now(),
    activeAgeMs = learnedPassageActiveAgeMs
  ) => {
    const createdAt = Number(passage?.createdAt);
    const age = Number(now) - createdAt;
    if (!Number.isFinite(createdAt) || age < 0
        || age > Math.max(24 * 60 * 60 * 1000, Number(activeAgeMs) || 0)) {
      return false;
    }
    const savedSource = String(passage?.sourceVersion || '');
    const currentSource = String(currentSourceVersion || '');
    return !savedSource || !currentSource || savedSource === currentSource;
  };

  const buildLearnedPassageFromTrail = (
    samples,
    sourceVersion = '',
    createdAt = Date.now(),
    maximumPoints = learnedPassageMaximumPoints
  ) => {
    const valid = (Array.isArray(samples) ? samples : [])
      .filter(sample => Number.isFinite(Number(sample?.x))
        && Number.isFinite(Number(sample?.y)))
      .map(sample => ({
        x: Math.min(1000, Math.max(0, Number(sample.x))),
        y: Math.min(1000, Math.max(0, Number(sample.y)))
      }));
    if (valid.length < 8) return { ok: false, reason: 'NOT_ENOUGH_SAMPLES' };
    const limit = Math.max(8, Math.min(240, Math.floor(Number(maximumPoints) || 120)));
    const points = valid.length <= limit
      ? valid
      : (() => {
          const reduced = [valid[0]];
          const stride = Math.ceil((valid.length - 1) / (limit - 1));
          for (let index = stride;
            index < valid.length - 1 && reduced.length < limit - 1;
            index += stride) {
            reduced.push(valid[index]);
          }
          reduced.push(valid.at(-1));
          return reduced;
        })();
    const distance = points.slice(1).reduce((total, point, index) =>
      total + Math.hypot(
        point.x - points[index].x,
        point.y - points[index].y), 0);
    if (!Number.isFinite(distance) || distance < 30) {
      return { ok: false, reason: 'PASSAGE_TOO_SHORT' };
    }
    const timestamp = Math.max(0, Math.floor(Number(createdAt) || Date.now()));
    return {
      ok: true,
      reason: '',
      passage: {
        id: `learned-${timestamp}`,
        label: 'Player-traveled passage',
        createdAt: timestamp,
        sourceVersion: String(sourceVersion || '')
          .replace(/[^a-zA-Z0-9._-]/g, '').slice(0, 24),
        distance,
        points
      }
    };
  };

  const normalizeExplorationSectors = (values, gridSize = 20) => {
    const resolvedSize = Math.min(40, Math.max(4, Math.floor(Number(gridSize) || 20)));
    const total = resolvedSize * resolvedSize;
    return Array.from(new Set((Array.isArray(values) ? values : [])
      .map(value => Number(value))
      .filter(value => Number.isInteger(value) && value >= 0 && value < total)))
      .sort((a, b) => a - b)
      .slice(0, total);
  };

  const explorationSectorIndex = (point, gridSize = 20) => {
    const x = Number(point?.x);
    const y = Number(point?.y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
    const resolvedSize = Math.min(40, Math.max(4, Math.floor(Number(gridSize) || 20)));
    const column = Math.min(resolvedSize - 1, Math.max(0, Math.floor(x / 1000 * resolvedSize)));
    const row = Math.min(resolvedSize - 1, Math.max(0, Math.floor(y / 1000 * resolvedSize)));
    return row * resolvedSize + column;
  };

  const buildExplorationState = () => {
    const total = explorationGridSize * explorationGridSize;
    const visited = streamerMode ? 0 : exploredSectors.size;
    return {
      explorationEnabled,
      explorationVisitedCount: visited,
      explorationTotalSectors: total,
      explorationCoveragePercent: visited / total * 100
    };
  };

  const persistExploration = () => {
    try {
      localStorage.setItem(
        explorationStorageKey,
        JSON.stringify(normalizeExplorationSectors(
          Array.from(exploredSectors),
          explorationGridSize)));
      return true;
    } catch {
      return false;
    }
  };

  const recordExplorationSample = pose => {
    if (!explorationEnabled || streamerMode) return false;
    const sector = explorationSectorIndex(pose, explorationGridSize);
    if (sector == null || exploredSectors.has(sector)) return false;
    exploredSectors.add(sector);
    persistExploration();
    explorationRenderSignature = '';
    lastMessage = '';
    return true;
  };

  try {
    const storedPins = JSON.parse(localStorage.getItem(pinStorageKey) || '[]');
    if (Array.isArray(storedPins)) {
      const storedAt = Date.now();
      savedPins = storedPins
        .filter(pin => pin && pinTypes[pin.type]
          && Number.isFinite(Number(pin.x)) && Number.isFinite(Number(pin.y))
          && (!Number.isFinite(Number(pin.expiresAt))
            || Number(pin.expiresAt) <= 0 || Number(pin.expiresAt) > storedAt))
        .slice(-20)
        .map(pin => ({
          id: String(pin.id || `${Date.now()}-${Math.random()}`),
          type: pin.type,
          x: Math.min(1000, Math.max(0, Number(pin.x))),
          y: Math.min(1000, Math.max(0, Number(pin.y))),
          label: String(pin.label || pinTypes[pin.type].label).slice(0, 64),
          favorite: Boolean(pin.favorite),
          expiresAt: Number(pin.expiresAt) > storedAt ? Number(pin.expiresAt) : 0,
          expiryMinutes: pinExpiryMinutes.includes(Number(pin.expiryMinutes))
            ? Number(pin.expiryMinutes)
            : 0,
          alertRadius: pinAlertRadii.includes(Number(pin.alertRadius))
            ? Number(pin.alertRadius)
            : 0,
          createdAt: Number(pin.createdAt) || Date.now()
        }));
      try { localStorage.setItem(pinStorageKey, JSON.stringify(savedPins)); } catch {}
    }
  } catch {
    savedPins = [];
  }

  try {
    const storedExploration = JSON.parse(
      localStorage.getItem(explorationStorageKey) || '[]');
    exploredSectors = new Set(normalizeExplorationSectors(
      storedExploration,
      explorationGridSize));
  } catch {
    exploredSectors = new Set();
  }

  try {
    learnedPassages = normalizeLearnedPassageLibrary(
      JSON.parse(localStorage.getItem(learnedPassageStorageKey) || '[]'));
    learnedPassageRoutingEnabled =
      localStorage.getItem(learnedPassageRoutingStorageKey) !== 'off';
    learnedPassageVisible =
      localStorage.getItem(learnedPassageVisibilityStorageKey) !== 'off';
    localStorage.setItem(
      learnedPassageStorageKey,
      JSON.stringify(learnedPassages));
  } catch {
    learnedPassages = [];
    learnedPassageRoutingEnabled = true;
    learnedPassageVisible = true;
  }

  try {
    const storedPosition = JSON.parse(localStorage.getItem(lastPositionStorageKey) || 'null');
    const ageMs = Date.now() - Number(storedPosition?.at);
    const hasWorldPoint = Number.isFinite(Number(storedPosition?.worldX))
      && Number.isFinite(Number(storedPosition?.worldY));
    const hasMapPoint = Number.isFinite(Number(storedPosition?.mapX))
      && Number.isFinite(Number(storedPosition?.mapY));
    if (ageMs >= 0 && ageMs <= 30 * 24 * 60 * 60 * 1000
        && (hasWorldPoint || hasMapPoint)) {
      lastLivePosition = {
        worldX: hasWorldPoint ? Number(storedPosition.worldX) : null,
        worldY: hasWorldPoint ? Number(storedPosition.worldY) : null,
        mapX: hasMapPoint ? Math.min(1000, Math.max(0, Number(storedPosition.mapX))) : null,
        mapY: hasMapPoint ? Math.min(1000, Math.max(0, Number(storedPosition.mapY))) : null,
        heading: Number(storedPosition.heading) || 0,
        at: Number(storedPosition.at)
      };
    }
  } catch {
    lastLivePosition = null;
  }

  const resolveAnchorPoint = anchor => {
    if (!anchor) return null;
    const calibration = findReactMapProps()?.calibration;
    if (Number.isFinite(anchor.worldX) && Number.isFinite(anchor.worldY)) {
      const calibrated = worldToMapPoint(calibration, anchor.worldX, anchor.worldY);
      if (calibrated) return calibrated;
    }
    return Number.isFinite(anchor.mapX) && Number.isFinite(anchor.mapY)
      ? { x: anchor.mapX, y: anchor.mapY }
      : null;
  };

  const buildAnchorNavigation = anchor => {
    const point = resolveAnchorPoint(anchor);
    if (!point) return { available: false, distance: null, bearing: null, cardinal: '' };
    const liveSelfPose = markerAvailable && lastMotionSample ? lastMotionSample : null;
    if (!liveSelfPose) {
      return { available: true, distance: null, bearing: null, cardinal: '' };
    }
    const dx = point.x - liveSelfPose.x;
    const dy = point.y - liveSelfPose.y;
    const bearing = (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
    const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
    return {
      available: true,
      distance: Math.hypot(dx, dy),
      bearing,
      cardinal: cardinals[Math.round(bearing / 45) % 8]
    };
  };

  const selectDeathMarkerPoint = (livePose, lastPoint) => {
    const liveX = Number(livePose?.x);
    const liveY = Number(livePose?.y);
    if (Number.isFinite(liveX) && Number.isFinite(liveY)) {
      return {
        x: Math.min(1000, Math.max(0, liveX)),
        y: Math.min(1000, Math.max(0, liveY)),
        source: 'live'
      };
    }
    const lastX = Number(lastPoint?.x);
    const lastY = Number(lastPoint?.y);
    return Number.isFinite(lastX) && Number.isFinite(lastY)
      ? {
          x: Math.min(1000, Math.max(0, lastX)),
          y: Math.min(1000, Math.max(0, lastY)),
          source: 'last'
        }
      : null;
  };

  const recordBreadcrumbSample = pose => {
    if (!pose || !Number.isFinite(pose.x) || !Number.isFinite(pose.y)) return;
    const point = { x: Number(pose.x), y: Number(pose.y), at: Date.now() };
    const previous = breadcrumbSamples.at(-1);
    if (!previous) {
      breadcrumbSamples = [point];
      breadcrumbDistance = 0;
      breadcrumbTrailRenderSignature = '';
      lastMessage = '';
      return;
    }

    const distance = Math.hypot(point.x - previous.x, point.y - previous.y);
    if (distance > 100) {
      breadcrumbSamples = [point];
      breadcrumbDistance = 0;
      breadcrumbTrailRenderSignature = '';
      lastMessage = '';
      return;
    }
    if (distance < 2.5) return;

    breadcrumbSamples.push(point);
    breadcrumbDistance += distance;
    if (breadcrumbSamples.length > 2400) {
      const lastIndex = breadcrumbSamples.length - 1;
      breadcrumbSamples = breadcrumbSamples.filter((_, index) =>
        index === 0 || index === lastIndex || index % 2 === 0);
    }
    breadcrumbTrailRenderSignature = '';
    lastMessage = '';
  };

  const simplifyBreadcrumbTrailPoints = (samples = breadcrumbSamples, maxPoints = 360) => {
    const valid = (Array.isArray(samples) ? samples : [])
      .filter(sample => Number.isFinite(Number(sample?.x))
        && Number.isFinite(Number(sample?.y)))
      .map(sample => ({
        x: Number(sample.x),
        y: Number(sample.y),
        at: Number(sample.at) || 0
      }));
    const limit = Math.max(2, Math.min(1000, Math.floor(Number(maxPoints) || 360)));
    if (valid.length <= limit) return valid;

    const result = [valid[0]];
    const stride = Math.ceil((valid.length - 1) / (limit - 1));
    for (let index = stride;
      index < valid.length - 1 && result.length < limit - 1;
      index += stride) {
      result.push(valid[index]);
    }
    result.push(valid.at(-1));
    return result;
  };

  const buildBreadcrumbRouteStops = (
    samples = breadcrumbSamples,
    liveSample = lastMotionSample,
    advanceDistance = routeAdvanceDistance) => {
    if (samples.length < 2) return [];
    const points = samples.slice();
    if (liveSample) {
      const latest = points.at(-1);
      const liveGap = latest
        ? Math.hypot(liveSample.x - latest.x, liveSample.y - latest.y)
        : 0;
      if (liveGap >= 0.5 && liveGap <= 100) {
        points.push({
          x: liveSample.x,
          y: liveSample.y,
          at: liveSample.at
        });
      }
    }
    if (points.length < 3) return [];

    let totalDistance = 0;
    for (let index = 1; index < points.length; index += 1) {
      totalDistance += Math.hypot(
        points[index].x - points[index - 1].x,
        points[index].y - points[index - 1].y);
    }
    const legSpacing = Math.max(8, advanceDistance * 1.35, totalDistance / 12);
    if (totalDistance < legSpacing + 1) return [];

    const stops = [];
    let distanceFromCurrent = 0;
    let nextTarget = legSpacing;
    for (let index = points.length - 1; index > 0 && stops.length < 11; index -= 1) {
      const current = points[index];
      const previous = points[index - 1];
      distanceFromCurrent += Math.hypot(current.x - previous.x, current.y - previous.y);
      if (distanceFromCurrent + 0.001 < nextTarget) continue;
      stops.push({ x: previous.x, y: previous.y });
      nextTarget += legSpacing;
    }

    const sessionStart = points[0];
    const finalStop = stops.at(-1);
    if (!finalStop || Math.hypot(finalStop.x - sessionStart.x, finalStop.y - sessionStart.y) > 1) {
      if (stops.length >= 12) stops[11] = { x: sessionStart.x, y: sessionStart.y };
      else stops.push({ x: sessionStart.x, y: sessionStart.y });
    }
    return stops.length >= 2 ? stops.slice(0, 12) : [];
  };

  const persistLearnedPassages = () => {
    try {
      localStorage.setItem(
        learnedPassageStorageKey,
        JSON.stringify(learnedPassages));
      return true;
    } catch {
      return false;
    }
  };

  const activeLearnedPassages = (now = Date.now()) =>
    learnedPassages.filter(passage => learnedPassageIsCurrent(
      passage,
      terrainRoadNetwork?.sourceVersion || '',
      now,
      learnedPassageActiveAgeMs));

  const buildLearnedPassageState = () => {
    if (streamerMode) {
      return {
        learnedPassageRoutingEnabled: false,
        learnedPassageVisible: false,
        learnedPassageCount: 0,
        learnedPassageActiveCount: 0,
        learnedPassageStaleCount: 0,
        learnedPassagePointCount: 0
      };
    }
    const activeCount = activeLearnedPassages().length;
    return {
      learnedPassageRoutingEnabled,
      learnedPassageVisible,
      learnedPassageCount: learnedPassages.length,
      learnedPassageActiveCount: activeCount,
      learnedPassageStaleCount: Math.max(0, learnedPassages.length - activeCount),
      learnedPassagePointCount: learnedPassages.reduce(
        (total, passage) => total + passage.points.length, 0)
    };
  };

  const learnedPassageRoadPaths = () =>
    !learnedPassageRoutingEnabled || streamerMode
      ? []
      : activeLearnedPassages().map(passage => ({
          label: passage.label,
          type: 'learned',
          points: passage.points
        }));

  const saveCurrentSessionPassage = () => {
    if (streamerMode || !terrainNetworkReady) return false;
    const result = buildLearnedPassageFromTrail(
      breadcrumbSamples,
      terrainRoadNetwork?.sourceVersion || '',
      Date.now(),
      learnedPassageMaximumPoints);
    if (!result.ok) {
      lastMessage = '';
      notify(result.reason === 'PASSAGE_TOO_SHORT'
        ? 'learned-passage-too-short'
        : 'learned-passage-needs-movement');
      return false;
    }
    const previousPassages = learnedPassages;
    learnedPassages = normalizeLearnedPassageLibrary(
      [...learnedPassages, result.passage]);
    if (!persistLearnedPassages()) {
      learnedPassages = previousPassages;
      lastMessage = '';
      notify('learned-passage-save-failed');
      return false;
    }
    learnedPassageRenderSignature = '';
    drawLearnedPassages();
    if (routePlanSource === 'terrain' && terrainCourseDestination
        && learnedPassageRoutingEnabled) {
      window.setTimeout(() => startTerrainCourseInternal(
        terrainCourseDestination,
        'terrain-course-learned-passage-saved'), 0);
    }
    lastMessage = '';
    notify('learned-passage-saved');
    return true;
  };

  const setLearnedPassageRoutingEnabled = enabled => {
    const changed = learnedPassageRoutingEnabled !== Boolean(enabled);
    learnedPassageRoutingEnabled = Boolean(enabled);
    try {
      localStorage.setItem(
        learnedPassageRoutingStorageKey,
        learnedPassageRoutingEnabled ? 'on' : 'off');
    } catch { /* Native settings retain this preference. */ }
    if (changed && routePlanSource === 'terrain' && terrainCourseDestination) {
      terrainCourseStatus = 'rerouting';
      window.setTimeout(() => startTerrainCourseInternal(
        terrainCourseDestination,
        'terrain-course-learned-routing-changed'), 0);
    }
    lastMessage = '';
    notify(learnedPassageRoutingEnabled
      ? 'learned-passage-routing-on'
      : 'learned-passage-routing-off');
    return true;
  };

  const setLearnedPassageVisible = visible => {
    learnedPassageVisible = Boolean(visible);
    try {
      localStorage.setItem(
        learnedPassageVisibilityStorageKey,
        learnedPassageVisible ? 'on' : 'off');
    } catch { /* Native settings retain this preference. */ }
    learnedPassageRenderSignature = '';
    drawLearnedPassages();
    lastMessage = '';
    notify(learnedPassageVisible
      ? 'learned-passage-visible'
      : 'learned-passage-hidden');
    return true;
  };

  const clearLearnedPassages = () => {
    if (streamerMode || !learnedPassages.length) return false;
    const previousPassages = learnedPassages;
    learnedPassages = [];
    if (!persistLearnedPassages()) {
      learnedPassages = previousPassages;
      return false;
    }
    learnedPassageRenderSignature = '';
    drawLearnedPassages();
    if (routePlanSource === 'terrain' && terrainCourseDestination
        && learnedPassageRoutingEnabled) {
      window.setTimeout(() => startTerrainCourseInternal(
        terrainCourseDestination,
        'terrain-course-learned-passages-cleared'), 0);
    }
    lastMessage = '';
    notify('learned-passages-cleared');
    return true;
  };

  const buildRecoveryState = () => {
    if (streamerMode) {
      return {
        sessionStartAvailable: false,
        sessionStartDistance: null,
        sessionStartBearing: null,
        sessionStartCardinal: '',
        breadcrumbReturnAvailable: false,
        breadcrumbPointCount: 0,
        breadcrumbDistance: 0,
        lastPositionMemoryEnabled: rememberLastPositionEnabled,
        lastPositionAvailable: false,
        lastPositionAgeMs: 0
      };
    }
    const sessionStart = buildAnchorNavigation(sessionStartPosition);
    const lastPosition = rememberLastPositionEnabled
      ? buildAnchorNavigation(lastLivePosition)
      : { available: false };
    const breadcrumbRouteStops = buildBreadcrumbRouteStops();
    return {
      sessionStartAvailable: sessionStart.available,
      sessionStartDistance: sessionStart.distance ?? null,
      sessionStartBearing: sessionStart.bearing ?? null,
      sessionStartCardinal: sessionStart.cardinal || '',
      breadcrumbReturnAvailable: breadcrumbRouteStops.length >= 2,
      breadcrumbPointCount: breadcrumbSamples.length,
      breadcrumbDistance,
      lastPositionMemoryEnabled: rememberLastPositionEnabled,
      lastPositionAvailable: Boolean(lastPosition.available),
      lastPositionAgeMs: lastLivePosition?.at
        ? Math.max(0, Date.now() - lastLivePosition.at)
        : 0
    };
  };

  const buildMeasurementState = () => {
    const start = measurement?.start ?? measurementStart;
    const end = measurement?.end ?? null;
    if (streamerMode || !start) {
      return {
        measurementArmed: streamerMode ? false : measurementArmed,
        measurementHasStart: false,
        measurementActive: false,
        measurementDistance: null,
        measurementBearing: null,
        measurementCardinal: '',
        measurementStartWorldX: null,
        measurementStartWorldY: null,
        measurementEndWorldX: null,
        measurementEndWorldY: null,
        measurementMarkedBoundaryCount: 0,
        measurementInsideMarkedBoundary: false
      };
    }

    const calibration = findReactMapProps()?.calibration;
    const startWorld = mapToWorldPoint(calibration, start.x, start.y);
    const endWorld = end ? mapToWorldPoint(calibration, end.x, end.y) : null;
    const dx = end ? end.x - start.x : 0;
    const dy = end ? end.y - start.y : 0;
    const bearing = end
      ? (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360
      : null;
    const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
    const obstacleRisk = end
      ? calculateDirectRouteObstacleRisk(start, end, terrainCourseObstacles())
      : { valid: false, insideObstacleCount: 0, crossingObstacleCount: 0 };
    return {
      measurementArmed,
      measurementHasStart: true,
      measurementActive: Boolean(end),
      measurementDistance: end ? Math.hypot(dx, dy) : null,
      measurementBearing: bearing,
      measurementCardinal: bearing == null
        ? ''
        : cardinals[Math.round(bearing / 45) % 8],
      measurementStartWorldX: startWorld?.x ?? null,
      measurementStartWorldY: startWorld?.y ?? null,
      measurementEndWorldX: endWorld?.x ?? null,
      measurementEndWorldY: endWorld?.y ?? null,
      measurementMarkedBoundaryCount: obstacleRisk.valid
        ? obstacleRisk.crossingObstacleCount
        : 0,
      measurementInsideMarkedBoundary: obstacleRisk.valid
        ? obstacleRisk.insideObstacleCount > 0
        : false
    };
  };

  const routeDistanceBetween = (a, b) =>
    a && b ? Math.hypot(Number(b.x) - Number(a.x), Number(b.y) - Number(a.y)) : 0;

  const distancePointToSegment = (point, start, end) => {
    const px = Number(point?.x);
    const py = Number(point?.y);
    const ax = Number(start?.x);
    const ay = Number(start?.y);
    const bx = Number(end?.x);
    const by = Number(end?.y);
    if (![px, py, ax, ay, bx, by].every(Number.isFinite)) return Infinity;
    const dx = bx - ax;
    const dy = by - ay;
    const lengthSquared = dx * dx + dy * dy;
    if (lengthSquared <= 0.000001) return Math.hypot(px - ax, py - ay);
    const t = Math.min(1, Math.max(0, ((px - ax) * dx + (py - ay) * dy) / lengthSquared));
    return Math.hypot(px - (ax + t * dx), py - (ay + t * dy));
  };

  const segmentIntersectsCircle = (start, end, obstacle, padding = 0) => {
    const radius = Math.max(0, Number(obstacle?.radius) || 0)
      + Math.max(0, Number(padding) || 0);
    return radius > 0
      && distancePointToSegment(obstacle, start, end) <= radius;
  };

  const routeOrientation = (a, b, c) => {
    const cross = (Number(b?.x) - Number(a?.x)) * (Number(c?.y) - Number(a?.y))
      - (Number(b?.y) - Number(a?.y)) * (Number(c?.x) - Number(a?.x));
    return Math.abs(cross) <= 0.000001 ? 0 : cross > 0 ? 1 : -1;
  };

  const routePointOnSegment = (start, point, end) =>
    Number(point?.x) <= Math.max(Number(start?.x), Number(end?.x)) + 0.000001
    && Number(point?.x) + 0.000001 >= Math.min(Number(start?.x), Number(end?.x))
    && Number(point?.y) <= Math.max(Number(start?.y), Number(end?.y)) + 0.000001
    && Number(point?.y) + 0.000001 >= Math.min(Number(start?.y), Number(end?.y));

  const routeSegmentsIntersect = (a, b, c, d) => {
    const o1 = routeOrientation(a, b, c);
    const o2 = routeOrientation(a, b, d);
    const o3 = routeOrientation(c, d, a);
    const o4 = routeOrientation(c, d, b);
    return (o1 !== o2 && o3 !== o4)
      || (o1 === 0 && routePointOnSegment(a, c, b))
      || (o2 === 0 && routePointOnSegment(a, d, b))
      || (o3 === 0 && routePointOnSegment(c, a, d))
      || (o4 === 0 && routePointOnSegment(c, b, d));
  };

  const routePolygonArea = points => {
    const safePoints = Array.isArray(points) ? points : [];
    if (safePoints.length < 3) return 0;
    let doubledArea = 0;
    for (let index = 0; index < safePoints.length; index += 1) {
      const next = (index + 1) % safePoints.length;
      doubledArea += Number(safePoints[index].x) * Number(safePoints[next].y)
        - Number(safePoints[next].x) * Number(safePoints[index].y);
    }
    return Math.abs(doubledArea) / 2;
  };

  const routePolygonSelfIntersects = points => {
    const safePoints = Array.isArray(points) ? points : [];
    if (safePoints.length < 3) return false;
    for (let first = 0; first < safePoints.length; first += 1) {
      const firstNext = (first + 1) % safePoints.length;
      for (let second = first + 1; second < safePoints.length; second += 1) {
        const secondNext = (second + 1) % safePoints.length;
        if (first === second || firstNext === second || secondNext === first) continue;
        if (routeSegmentsIntersect(
            safePoints[first], safePoints[firstNext],
            safePoints[second], safePoints[secondNext])) return true;
      }
    }
    return false;
  };

  const routePointInPolygon = (point, points, padding = 0) => {
    const safePoints = Array.isArray(points) ? points : [];
    if (safePoints.length < 3) return false;
    const safePadding = Math.max(0, Number(padding) || 0);
    for (let index = 0; index < safePoints.length; index += 1) {
      const next = (index + 1) % safePoints.length;
      if (distancePointToSegment(point, safePoints[index], safePoints[next])
          <= safePadding + 0.000001) return true;
    }
    let inside = false;
    for (let current = 0, previous = safePoints.length - 1;
        current < safePoints.length; previous = current++) {
      const a = safePoints[current];
      const b = safePoints[previous];
      if ((Number(a.y) > Number(point.y)) !== (Number(b.y) > Number(point.y))
          && Number(point.x) < (Number(b.x) - Number(a.x))
            * (Number(point.y) - Number(a.y))
            / (Number(b.y) - Number(a.y)) + Number(a.x)) inside = !inside;
    }
    return inside;
  };

  const routeSegmentIntersectsPolygon = (start, end, points, padding = 0) => {
    const safePoints = Array.isArray(points) ? points : [];
    if (safePoints.length < 3) return false;
    const safePadding = Math.max(0, Number(padding) || 0);
    if (routePointInPolygon(start, safePoints, safePadding)
        || routePointInPolygon(end, safePoints, safePadding)) return true;
    for (let index = 0; index < safePoints.length; index += 1) {
      const next = (index + 1) % safePoints.length;
      if (routeSegmentsIntersect(start, end, safePoints[index], safePoints[next])
          || distancePointToSegment(safePoints[index], start, end) <= safePadding
          || distancePointToSegment(safePoints[next], start, end) <= safePadding
          || distancePointToSegment(start, safePoints[index], safePoints[next]) <= safePadding
          || distancePointToSegment(end, safePoints[index], safePoints[next]) <= safePadding) {
        return true;
      }
    }
    return false;
  };

  const calculateEscapeRoute = (
    selfPose,
    threatBearing,
    obstacles = [],
    preferredDistance = 75,
    mapMargin = 8
  ) => {
    const start = { x: Number(selfPose?.x), y: Number(selfPose?.y) };
    const bearing = Number(threatBearing);
    if (![start.x, start.y, bearing].every(Number.isFinite)
        || start.x < 0 || start.x > 1000 || start.y < 0 || start.y > 1000) {
      return { ok: false, reason: 'INVALID_INPUT' };
    }

    const distance = Math.max(25, Math.min(150, Number(preferredDistance) || 75));
    const margin = Math.max(0, Math.min(40, Number(mapMargin) || 0));
    const safeObstacles = (Array.isArray(obstacles) ? obstacles : [])
      .map(obstacle => {
        if (String(obstacle?.kind || '') === 'polygon') {
          const points = (Array.isArray(obstacle?.points) ? obstacle.points : [])
            .filter(point => Number.isFinite(Number(point?.x))
              && Number.isFinite(Number(point?.y)))
            .slice(0, noGoAreaMaximumVertices)
            .map(point => ({ x: Number(point.x), y: Number(point.y) }));
          return points.length >= 3 && routePolygonArea(points) >= 4
            && !routePolygonSelfIntersects(points)
            ? { kind: 'polygon', points, id: String(obstacle?.id || '') }
            : null;
        }
        return Number.isFinite(Number(obstacle?.x))
          && Number.isFinite(Number(obstacle?.y))
          && Number(obstacle?.radius) > 0
          ? {
              kind: 'circle', x: Number(obstacle.x), y: Number(obstacle.y),
              radius: Math.min(150, Number(obstacle.radius)),
              id: String(obstacle?.id || '')
            }
          : null;
      })
      .filter(Boolean);
    const contains = (point, obstacle, padding = 0) => obstacle.kind === 'polygon'
      ? routePointInPolygon(point, obstacle.points, padding)
      : routeDistanceBetween(point, obstacle) <= obstacle.radius + padding;
    const intersects = (a, b, obstacle, padding = 0) => obstacle.kind === 'polygon'
      ? routeSegmentIntersectsPolygon(a, b, obstacle.points, padding)
      : segmentIntersectsCircle(a, b, obstacle, padding);
    const startObstacles = new Set(
      safeObstacles.filter(obstacle => contains(start, obstacle, 2)));
    const oppositeBearing = (bearing + 180 + 360) % 360;
    const offsets = [0, -22.5, 22.5, -45, 45, -67.5, 67.5, -90, 90];
    const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
    const candidates = [];

    for (const offset of offsets) {
      const candidateBearing = (oppositeBearing + offset + 360) % 360;
      const radians = candidateBearing * Math.PI / 180;
      const target = {
        x: Math.min(1000 - margin, Math.max(margin,
          start.x + Math.sin(radians) * distance)),
        y: Math.min(1000 - margin, Math.max(margin,
          start.y - Math.cos(radians) * distance))
      };
      const achievedDistance = routeDistanceBetween(start, target);
      if (achievedDistance < Math.min(25, distance * 0.5)) continue;
      if (safeObstacles.some(obstacle => contains(target, obstacle, 3))) continue;
      if (safeObstacles.some(obstacle => !startObstacles.has(obstacle)
          && intersects(start, target, obstacle, 3))) continue;
      candidates.push({
        target,
        bearing: candidateBearing,
        cardinal: cardinals[Math.round(candidateBearing / 45) % 8],
        distance: achievedDistance,
        deflection: Math.abs(offset),
        score: achievedDistance - Math.abs(offset) * 0.18
      });
    }

    candidates.sort((left, right) => right.score - left.score
      || left.deflection - right.deflection
      || left.bearing - right.bearing);
    const selected = candidates[0];
    return selected
      ? {
          ok: true,
          reason: '',
          x: selected.target.x,
          y: selected.target.y,
          bearing: selected.bearing,
          cardinal: selected.cardinal,
          distance: selected.distance,
          requestedDistance: distance,
          deflection: selected.deflection,
          consideredObstacleCount: safeObstacles.length,
          exitedObstacleCount: startObstacles.size
        }
      : {
          ok: false,
          reason: 'NO_CLEAR_ROUTE',
          requestedDistance: distance,
          consideredObstacleCount: safeObstacles.length,
          exitedObstacleCount: startObstacles.size
        };
  };

  const simplifyTerrainCoursePoints = (
    points,
    maximumPoints = 12,
    segmentBlocked = null
  ) => {
    const valid = (Array.isArray(points) ? points : [])
      .filter(point => Number.isFinite(Number(point?.x))
        && Number.isFinite(Number(point?.y)))
      .map(point => ({ x: Number(point.x), y: Number(point.y) }));
    const limit = Math.max(2, Math.min(24, Math.floor(Number(maximumPoints) || 12)));
    if (valid.length <= limit) return valid;

    const selected = [0, valid.length - 1];
    while (selected.length < limit) {
      selected.sort((a, b) => a - b);
      let bestIndex = -1;
      let bestDistance = -1;
      let bestBlocked = false;
      for (let segment = 1; segment < selected.length; segment += 1) {
        const first = selected[segment - 1];
        const last = selected[segment];
        const blocked = typeof segmentBlocked === 'function'
          && segmentBlocked(valid[first], valid[last]);
        for (let index = first + 1; index < last; index += 1) {
          const distance = distancePointToSegment(
            valid[index], valid[first], valid[last]);
          if ((blocked && !bestBlocked)
              || (blocked === bestBlocked && distance > bestDistance + 0.000001)
              || (Math.abs(distance - bestDistance) <= 0.000001
                && blocked === bestBlocked
                && (bestIndex < 0 || index < bestIndex))) {
            bestDistance = distance;
            bestIndex = index;
            bestBlocked = blocked;
          }
        }
      }
      if (bestIndex < 0) break;
      selected.push(bestIndex);
    }
    const simplified = selected.sort((a, b) => a - b).map(index => valid[index]);
    if (typeof segmentBlocked === 'function'
        && simplified.some((point, index) => index > 0
          && segmentBlocked(simplified[index - 1], point))) return [];
    return simplified;
  };

  const terrainWaterPixelForPoint = point => {
    if (!terrainWaterMask?.pixels || !terrainWaterMask.width || !terrainWaterMask.height) {
      return null;
    }
    const calibration = findReactMapProps()?.calibration;
    const world = mapToWorldPoint(calibration, Number(point?.x), Number(point?.y));
    if (!world || !Number.isFinite(Number(world.x)) || !Number.isFinite(Number(world.y))) {
      return null;
    }

    // My Isle Map publishes its Gateway water mask in this stable
    // 1000 x 1003 coordinate space. Convert through world coordinates
    // so it remains aligned when the bundled map uses a different SVG ratio.
    const maskX = ((Number(world.y) / 1000 + 505) / 1112) * 1000;
    const maskY = ((Number(world.x) / 1000 + 607) / 1116) * 1003;
    if (maskX < 0 || maskX > 1000 || maskY < 0 || maskY > 1003) return null;
    return {
      x: Math.max(0, Math.min(terrainWaterMask.width - 1,
        Math.round(maskX / 1000 * (terrainWaterMask.width - 1)))),
      y: Math.max(0, Math.min(terrainWaterMask.height - 1,
        Math.round(maskY / 1003 * (terrainWaterMask.height - 1))))
    };
  };

  const isTerrainWaterPoint = point => {
    const pixel = terrainWaterPixelForPoint(point);
    if (!pixel) return false;
    const { pixels, width, height } = terrainWaterMask;
    let covered = 0;
    let sampled = 0;
    for (let y = Math.max(0, pixel.y - 1); y <= Math.min(height - 1, pixel.y + 1); y += 1) {
      for (let x = Math.max(0, pixel.x - 1); x <= Math.min(width - 1, pixel.x + 1); x += 1) {
        sampled += 1;
        if (pixels[(y * width + x) * 4 + 3] >= 48) covered += 1;
      }
    }
    return sampled > 0 && covered >= Math.min(2, sampled);
  };

  const segmentCrossesTerrainWater = (start, end) => {
    if (!terrainWaterSafetyEnabled || terrainWaterMaskStatus !== 'ready') return false;
    const distance = routeDistanceBetween(start, end);
    if (!Number.isFinite(distance)) return false;
    const sampleCount = Math.max(1, Math.min(1200, Math.ceil(distance / 0.75)));
    for (let index = 0; index <= sampleCount; index += 1) {
      const ratio = index / sampleCount;
      if (isTerrainWaterPoint({
        x: Number(start.x) + (Number(end.x) - Number(start.x)) * ratio,
        y: Number(start.y) + (Number(end.y) - Number(start.y)) * ratio
      })) return true;
    }
    return false;
  };

  const removeTerrainWaterVisual = () => {
    terrainWaterVisual?.remove();
    terrainWaterVisual = null;
  };

  const drawTerrainWaterVisual = (encoded, sourceVersion) => {
    const svg = getMapSvg();
    if (!svg || !encoded) {
      removeTerrainWaterVisual();
      return false;
    }
    if (!terrainWaterVisual?.isConnected || terrainWaterVisual.ownerSVGElement !== svg) {
      removeTerrainWaterVisual();
      terrainWaterVisual = document.createElementNS(
        'http://www.w3.org/2000/svg', 'image');
      terrainWaterVisual.dataset.isleyCurrentWaterMask = 'true';
      terrainWaterVisual.setAttribute('x', '0');
      terrainWaterVisual.setAttribute('y', '0');
      terrainWaterVisual.setAttribute('width', '1000');
      terrainWaterVisual.setAttribute('height', '1000');
      terrainWaterVisual.setAttribute('preserveAspectRatio', 'none');
      terrainWaterVisual.setAttribute('opacity', '0.36');
      terrainWaterVisual.setAttribute('pointer-events', 'none');
      terrainWaterVisual.style.mixBlendMode = 'screen';
      const anchor = svg.querySelector(':scope > #roads');
      svg.insertBefore(terrainWaterVisual, anchor || null);
    }
    terrainWaterVisual.dataset.sourceVersion =
      String(sourceVersion || 'live').slice(0, 24);
    terrainWaterVisual.setAttribute('href', `data:image/webp;base64,${encoded}`);
    return true;
  };

  const loadTerrainWaterMask = async payload => {
    const revision = ++terrainWaterMaskLoadRevision;
    terrainWaterMask = null;
    terrainWaterMaskStatus = payload ? 'loading' : 'unavailable';
    lastMessage = '';
    if (!payload || String(payload?.mediaType || '') !== 'image/webp') {
      removeTerrainWaterVisual();
      return false;
    }
    const encoded = String(payload?.dataBase64 || '');
    if (encoded.length < 16 || encoded.length > 360000
        || !/^[A-Za-z0-9+/]+={0,2}$/.test(encoded)) {
      terrainWaterMaskStatus = 'unavailable';
      removeTerrainWaterVisual();
      return false;
    }

    let objectUrl = '';
    try {
      const binary = atob(encoded);
      const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
      const blob = new Blob([bytes], { type: 'image/webp' });
      objectUrl = URL.createObjectURL(blob);
      const image = new Image();
      image.src = objectUrl;
      await image.decode();
      if (revision !== terrainWaterMaskLoadRevision
          || image.naturalWidth < 100 || image.naturalWidth > 4096
          || image.naturalHeight < 100 || image.naturalHeight > 4096) return false;
      const canvas = document.createElement('canvas');
      canvas.width = image.naturalWidth;
      canvas.height = image.naturalHeight;
      const context = canvas.getContext('2d', { willReadFrequently: true });
      if (!context) throw new Error('canvas-unavailable');
      context.drawImage(image, 0, 0);
      const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
      terrainWaterMask = {
        width: canvas.width,
        height: canvas.height,
        pixels,
        sourceVersion: String(payload?.sourceVersion || 'live').slice(0, 24),
        loadedAt: Number(payload?.loadedAt) || Date.now()
      };
      terrainWaterMaskStatus = 'ready';
      drawTerrainWaterVisual(encoded, payload?.sourceVersion);
      lastMessage = '';
      notify('terrain-water-mask-ready');
      return true;
    } catch {
      if (revision === terrainWaterMaskLoadRevision) {
        terrainWaterMask = null;
        terrainWaterMaskStatus = 'unavailable';
        removeTerrainWaterVisual();
        lastMessage = '';
        notify('terrain-water-mask-unavailable');
      }
      return false;
    } finally {
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    }
  };

  const setTerrainWaterSafety = enabled => {
    terrainWaterSafetyEnabled = Boolean(enabled);
    try {
      localStorage.setItem(
        terrainWaterSafetyStorageKey,
        terrainWaterSafetyEnabled ? 'on' : 'off');
    } catch { /* Session state still remains active. */ }
    lastMessage = '';
    notify(terrainWaterSafetyEnabled ? 'terrain-water-safety-on' : 'terrain-water-safety-off');
    if (routePlanSource === 'terrain' && terrainCourseDestination) {
      window.setTimeout(() => startTerrainCourseInternal(
        terrainCourseDestination,
        'terrain-course-water-safety-changed'), 0);
    }
    return terrainWaterSafetyEnabled;
  };

  const setTerrainCommunityHazardsEnabled = enabled => {
    terrainCommunityHazardsEnabled = Boolean(enabled);
    try {
      localStorage.setItem(
        terrainCommunityHazardStorageKey,
        terrainCommunityHazardsEnabled ? 'on' : 'off');
    } catch { /* Session state still remains active. */ }
    drawTerrainCommunityHazards();
    scheduleTerrainCourseForObstacleChange();
    lastMessage = '';
    notify(terrainCommunityHazardsEnabled
      ? 'terrain-community-hazards-on'
      : 'terrain-community-hazards-off');
    return terrainCommunityHazardsEnabled;
  };

  const normalizeTerrainRouteStyle = value => {
    const normalized = String(value || '').trim().toLowerCase();
    return ['balanced', 'road-first', 'shortest'].includes(normalized)
      ? normalized
      : 'balanced';
  };

  const setTerrainRouteStyle = value => {
    const nextStyle = normalizeTerrainRouteStyle(value);
    const changed = nextStyle !== terrainRouteStyle;
    terrainRouteStyle = nextStyle;
    try { localStorage.setItem(terrainRouteStyleStorageKey, terrainRouteStyle); }
    catch { /* Native settings still retain the preference. */ }
    lastMessage = '';
    notify(changed ? 'terrain-route-style-changed' : 'terrain-route-style-confirmed');
    if (changed && routePlanSource === 'terrain' && terrainCourseDestination) {
      terrainCourseStatus = 'rerouting';
      window.setTimeout(() => startTerrainCourseInternal(
        terrainCourseDestination,
        'terrain-course-style-changed'), 0);
    }
    return terrainRouteStyle;
  };

  const normalizeTerrainGapPolicy = value => {
    const normalized = String(value || '').trim().toLowerCase();
    return ['strict', 'balanced', 'flexible'].includes(normalized)
      ? normalized
      : 'balanced';
  };

  const terrainGapLimit = value => ({
    strict: 45,
    balanced: 80,
    flexible: 125
  }[normalizeTerrainGapPolicy(value)]);

  const setTerrainGapPolicy = value => {
    const nextPolicy = normalizeTerrainGapPolicy(value);
    const changed = nextPolicy !== terrainGapPolicy;
    terrainGapPolicy = nextPolicy;
    try { localStorage.setItem(terrainGapPolicyStorageKey, terrainGapPolicy); }
    catch { /* Native settings still retain the preference. */ }
    lastMessage = '';
    notify(changed ? 'terrain-gap-policy-changed' : 'terrain-gap-policy-confirmed');
    if (changed && routePlanSource === 'terrain' && terrainCourseDestination) {
      terrainCourseStatus = 'rerouting';
      window.setTimeout(() => startTerrainCourseInternal(
        terrainCourseDestination,
        'terrain-course-gap-policy-changed'), 0);
    }
    return terrainGapPolicy;
  };

  const buildBlockedPassageArea = (
    start,
    target,
    existingAreaCount,
    createdAt = Date.now(),
    maximumAreas = 8
  ) => {
    const startPoint = { x: Number(start?.x), y: Number(start?.y) };
    const targetPoint = { x: Number(target?.x), y: Number(target?.y) };
    if (![startPoint.x, startPoint.y, targetPoint.x, targetPoint.y]
        .every(Number.isFinite)) {
      return { ok: false, reason: 'INVALID_INPUT' };
    }
    if (Math.max(0, Math.floor(Number(existingAreaCount) || 0))
        >= Math.max(1, Math.floor(Number(maximumAreas) || 8))) {
      return { ok: false, reason: 'AREA_LIMIT' };
    }
    const dx = targetPoint.x - startPoint.x;
    const dy = targetPoint.y - startPoint.y;
    const distance = Math.hypot(dx, dy);
    if (!Number.isFinite(distance) || distance < 24) {
      return { ok: false, reason: 'PASSAGE_TOO_CLOSE' };
    }

    const direction = { x: dx / distance, y: dy / distance };
    const perpendicular = { x: -direction.y, y: direction.x };
    const centerDistance = Math.min(distance - 8, Math.max(14, distance * 0.42));
    const center = {
      x: startPoint.x + direction.x * centerDistance,
      y: startPoint.y + direction.y * centerDistance
    };
    const halfAlong = 4;
    const halfAcross = 12;
    const points = [
      { x: center.x - direction.x * halfAlong - perpendicular.x * halfAcross,
        y: center.y - direction.y * halfAlong - perpendicular.y * halfAcross },
      { x: center.x + direction.x * halfAlong - perpendicular.x * halfAcross,
        y: center.y + direction.y * halfAlong - perpendicular.y * halfAcross },
      { x: center.x + direction.x * halfAlong + perpendicular.x * halfAcross,
        y: center.y + direction.y * halfAlong + perpendicular.y * halfAcross },
      { x: center.x - direction.x * halfAlong + perpendicular.x * halfAcross,
        y: center.y - direction.y * halfAlong + perpendicular.y * halfAcross }
    ];
    if (points.some(point => point.x < 0 || point.x > 1000
        || point.y < 0 || point.y > 1000)) {
      return { ok: false, reason: 'OUTSIDE_MAP' };
    }
    const timestamp = Math.max(0, Math.floor(Number(createdAt) || 0));
    return {
      ok: true,
      reason: '',
      area: {
        id: `blocked-${timestamp}-${Math.max(0, Math.floor(Number(existingAreaCount) || 0))}`,
        label: 'Blocked passage',
        points,
        createdAt: timestamp
      },
      distanceAhead: centerDistance,
      width: halfAcross * 2
    };
  };

  const buildMeasuredSlopeArea = (
    start,
    end,
    existingAreaCount,
    requestedLabel,
    createdAt = Date.now(),
    maximumAreas = 8
  ) => {
    const startPoint = { x: Number(start?.x), y: Number(start?.y) };
    const endPoint = { x: Number(end?.x), y: Number(end?.y) };
    if (![startPoint.x, startPoint.y, endPoint.x, endPoint.y]
        .every(Number.isFinite)) {
      return { ok: false, reason: 'INVALID_INPUT' };
    }
    if (Math.max(0, Math.floor(Number(existingAreaCount) || 0))
        >= Math.max(1, Math.floor(Number(maximumAreas) || 8))) {
      return { ok: false, reason: 'AREA_LIMIT' };
    }
    const dx = endPoint.x - startPoint.x;
    const dy = endPoint.y - startPoint.y;
    const distance = Math.hypot(dx, dy);
    if (!Number.isFinite(distance) || distance < 0.5) {
      return { ok: false, reason: 'SEGMENT_TOO_SHORT' };
    }

    const direction = { x: dx / distance, y: dy / distance };
    const perpendicular = { x: -direction.y, y: direction.x };
    const center = {
      x: (startPoint.x + endPoint.x) / 2,
      y: (startPoint.y + endPoint.y) / 2
    };
    const halfAlong = distance / 2 + Math.max(2, Math.min(6, distance * 0.1));
    const halfAcross = Math.max(4, Math.min(12, distance * 0.25 + 3));
    const points = [
      { x: center.x - direction.x * halfAlong - perpendicular.x * halfAcross,
        y: center.y - direction.y * halfAlong - perpendicular.y * halfAcross },
      { x: center.x + direction.x * halfAlong - perpendicular.x * halfAcross,
        y: center.y + direction.y * halfAlong - perpendicular.y * halfAcross },
      { x: center.x + direction.x * halfAlong + perpendicular.x * halfAcross,
        y: center.y + direction.y * halfAlong + perpendicular.y * halfAcross },
      { x: center.x - direction.x * halfAlong + perpendicular.x * halfAcross,
        y: center.y - direction.y * halfAlong + perpendicular.y * halfAcross }
    ];
    if (points.some(point => point.x < 0 || point.x > 1000
        || point.y < 0 || point.y > 1000)) {
      return { ok: false, reason: 'OUTSIDE_MAP' };
    }
    const timestamp = Math.max(0, Math.floor(Number(createdAt) || 0));
    const label = sanitizePinLabel(requestedLabel)
      || `Measured slope ${Math.max(0, Math.floor(Number(existingAreaCount) || 0)) + 1}`;
    return {
      ok: true,
      reason: '',
      area: {
        id: `slope-${timestamp}-${Math.max(0, Math.floor(Number(existingAreaCount) || 0))}`,
        label,
        points,
        createdAt: timestamp
      },
      mappedDistance: distance,
      width: halfAcross * 2
    };
  };

  const calculateTerrainRoadCourse = (
    start,
    destination,
    roadPaths,
    obstacles = [],
    maximumStops = 12,
    routeStyle = 'balanced',
    gapPolicy = 'balanced'
  ) => {
    const startPoint = { x: Number(start?.x), y: Number(start?.y) };
    const destinationPoint = {
      x: Number(destination?.x), y: Number(destination?.y)
    };
    if (![startPoint.x, startPoint.y, destinationPoint.x, destinationPoint.y]
        .every(Number.isFinite)) {
      return { ok: false, reason: 'INVALID_ENDPOINT' };
    }
    const directDistance = routeDistanceBetween(startPoint, destinationPoint);
    if (directDistance < 1) {
      return { ok: false, reason: 'ALREADY_THERE', directDistance };
    }
    const normalizedRouteStyle = normalizeTerrainRouteStyle(routeStyle);
    const normalizedGapPolicy = normalizeTerrainGapPolicy(gapPolicy);
    const maximumConnectorDistance = terrainGapLimit(normalizedGapPolicy);
    const styleWeights = {
      balanced: {
        road: 1, trail: 1, learned: 0.9, connector: 1.2, endpoint: 1.25
      },
      'road-first': {
        road: 0.85, trail: 1.4, learned: 1.05, connector: 1.5, endpoint: 2
      },
      shortest: {
        road: 1, trail: 1, learned: 1, connector: 1, endpoint: 1
      }
    }[normalizedRouteStyle];

    const safeObstacles = (Array.isArray(obstacles) ? obstacles : [])
      .map(obstacle => {
        if (String(obstacle?.kind || '') === 'polygon') {
          const points = (Array.isArray(obstacle?.points) ? obstacle.points : [])
            .filter(point => Number.isFinite(Number(point?.x))
              && Number.isFinite(Number(point?.y)))
            .slice(0, noGoAreaMaximumVertices)
            .map(point => ({ x: Number(point.x), y: Number(point.y) }));
          return points.length >= 3 && routePolygonArea(points) >= 4
            && !routePolygonSelfIntersects(points)
            ? {
                kind: 'polygon', points,
                id: String(obstacle?.id || ''), label: String(obstacle?.label || '')
              }
            : null;
        }
        return Number.isFinite(Number(obstacle?.x))
          && Number.isFinite(Number(obstacle?.y))
          && Number(obstacle?.radius) > 0
          ? {
              kind: 'circle',
              sourceKind: String(obstacle?.kind || '') === 'community-hazard'
                ? 'community-hazard'
                : 'circle',
              x: Number(obstacle.x), y: Number(obstacle.y),
              radius: Math.min(150, Number(obstacle.radius)),
              id: String(obstacle?.id || ''), label: String(obstacle?.label || '')
            }
          : null;
      }).filter(Boolean);
    const contains = (point, obstacle, padding = 0) => obstacle.kind === 'polygon'
      ? routePointInPolygon(point, obstacle.points, padding)
      : routeDistanceBetween(point, obstacle) <= obstacle.radius + padding;
    const intersects = (a, b, obstacle, padding = 0) => obstacle.kind === 'polygon'
      ? routeSegmentIntersectsPolygon(a, b, obstacle.points, padding)
      : segmentIntersectsCircle(a, b, obstacle, padding);
    const startObstacle = safeObstacles.find(obstacle => contains(startPoint, obstacle, 2));
    if (startObstacle) {
      return {
        ok: false, reason: 'START_INSIDE_OBSTACLE', directDistance,
        obstacleId: startObstacle.id,
        obstacleKind: startObstacle.sourceKind || startObstacle.kind
      };
    }
    const destinationObstacle = safeObstacles.find(obstacle =>
      contains(destinationPoint, obstacle, 2));
    if (destinationObstacle) {
      return {
        ok: false, reason: 'DESTINATION_INSIDE_OBSTACLE', directDistance,
        obstacleId: destinationObstacle.id,
        obstacleKind: destinationObstacle.sourceKind || destinationObstacle.kind
      };
    }
    if (terrainWaterSafetyEnabled && terrainWaterMaskStatus === 'ready'
        && isTerrainWaterPoint(startPoint)) {
      return { ok: false, reason: 'START_IN_WATER', directDistance };
    }
    if (terrainWaterSafetyEnabled && terrainWaterMaskStatus === 'ready'
        && isTerrainWaterPoint(destinationPoint)) {
      return { ok: false, reason: 'DESTINATION_IN_WATER', directDistance };
    }

    const nodes = [];
    const adjacency = [];
    const addNode = (point, pathIndex = -1, pathType = '') => {
      const node = {
        x: Number(point.x), y: Number(point.y), pathIndex, pathType,
        id: nodes.length
      };
      nodes.push(node);
      adjacency.push([]);
      return node.id;
    };
    const blockedByMarkedObstacle = (a, b) => safeObstacles.some(obstacle =>
      intersects(a, b, obstacle, 3));
    const blocked = (a, b, allowMappedRoadWater = false) =>
      blockedByMarkedObstacle(a, b)
      || (!allowMappedRoadWater && segmentCrossesTerrainWater(a, b));
    const addEdge = (
      aIndex,
      bIndex,
      kind = 'connector',
      allowMappedRoadWater = false
    ) => {
      if (aIndex === bIndex) return;
      const a = nodes[aIndex];
      const b = nodes[bIndex];
      const distance = routeDistanceBetween(a, b);
      if (!Number.isFinite(distance) || distance <= 0.000001
          || blocked(a, b, allowMappedRoadWater)) return;
      const multiplier = Number(styleWeights[kind]) || styleWeights.connector;
      const cost = distance * multiplier;
      adjacency[aIndex].push({ to: bIndex, distance, cost, kind });
      adjacency[bIndex].push({ to: aIndex, distance, cost, kind });
    };

    for (const [pathIndex, path] of (Array.isArray(roadPaths) ? roadPaths : []).entries()) {
      const pathType = String(path?.type || '').trim().toLowerCase();
      if (!['road', 'trail', 'learned'].includes(pathType)) continue;
      const points = (Array.isArray(path?.points) ? path.points : [])
        .filter(point => Number.isFinite(Number(point?.x))
          && Number.isFinite(Number(point?.y)))
        .map(point => ({ x: Number(point.x), y: Number(point.y) }));
      if (points.length < 2) continue;
      let previous = -1;
      for (const point of points) {
        const current = addNode(point, pathIndex, pathType);
        // Published road/trail edges may be a bridge or intentional ford.
        // Player-traveled evidence and every unmapped connector remain
        // water-blocked whenever water safety is enabled.
        if (previous >= 0) {
          addEdge(
            previous,
            current,
            pathType,
            pathType === 'road' || pathType === 'trail');
        }
        previous = current;
      }
    }
    const roadNodeCount = nodes.length;
    if (roadNodeCount < 2) {
      return { ok: false, reason: 'NO_ROAD_DATA', directDistance };
    }

    const snapDistance = 4;
    const gridSize = snapDistance;
    const buckets = new Map();
    const bucketKey = (x, y) => `${Math.floor(x / gridSize)}:${Math.floor(y / gridSize)}`;
    for (let index = 0; index < roadNodeCount; index += 1) {
      const node = nodes[index];
      const cellX = Math.floor(node.x / gridSize);
      const cellY = Math.floor(node.y / gridSize);
      for (let dx = -1; dx <= 1; dx += 1) {
        for (let dy = -1; dy <= 1; dy += 1) {
          const candidates = buckets.get(`${cellX + dx}:${cellY + dy}`) || [];
          for (const otherIndex of candidates) {
            const other = nodes[otherIndex];
            if (other.pathIndex !== node.pathIndex
                && routeDistanceBetween(node, other) <= snapDistance) {
              addEdge(index, otherIndex, 'connector');
            }
          }
        }
      }
      const key = bucketKey(node.x, node.y);
      if (!buckets.has(key)) buckets.set(key, []);
      buckets.get(key).push(index);
    }

    const connectEndpoint = (point, reason) => {
      const endpointIndex = addNode(point, -2);
      const candidates = nodes.slice(0, roadNodeCount)
        .map((node, index) => ({ index, distance: routeDistanceBetween(point, node) }))
        .filter(candidate => candidate.distance <= maximumConnectorDistance)
        .sort((a, b) => a.distance - b.distance || a.index - b.index)
        .slice(0, 8);
      for (const candidate of candidates) {
        addEdge(endpointIndex, candidate.index, 'endpoint');
      }
      if (!adjacency[endpointIndex].length) {
        return { index: endpointIndex, error: reason };
      }
      return { index: endpointIndex, error: '' };
    };
    const startConnection = connectEndpoint(startPoint, 'NO_ROAD_NEAR_START');
    if (startConnection.error) {
      return { ok: false, reason: startConnection.error, directDistance };
    }
    const destinationConnection = connectEndpoint(
      destinationPoint, 'NO_ROAD_NEAR_DESTINATION');
    if (destinationConnection.error) {
      return { ok: false, reason: destinationConnection.error, directDistance };
    }

    const costs = Array(nodes.length).fill(Infinity);
    const distances = Array(nodes.length).fill(Infinity);
    const previous = Array(nodes.length).fill(-1);
    const previousEdgeKind = Array(nodes.length).fill('');
    const previousEdgeDistance = Array(nodes.length).fill(0);
    const visited = Array(nodes.length).fill(false);
    costs[startConnection.index] = 0;
    distances[startConnection.index] = 0;
    for (let iteration = 0; iteration < nodes.length; iteration += 1) {
      let current = -1;
      let currentCost = Infinity;
      for (let index = 0; index < nodes.length; index += 1) {
        if (!visited[index] && costs[index] < currentCost) {
          current = index;
          currentCost = costs[index];
        }
      }
      if (current < 0 || current === destinationConnection.index) break;
      visited[current] = true;
      for (const edge of adjacency[current]) {
        const candidateCost = currentCost + edge.cost;
        const candidateDistance = distances[current] + edge.distance;
        if (candidateCost + 0.000001 < costs[edge.to]
            || (Math.abs(candidateCost - costs[edge.to]) <= 0.000001
              && candidateDistance + 0.000001 < distances[edge.to])) {
          costs[edge.to] = candidateCost;
          distances[edge.to] = candidateDistance;
          previous[edge.to] = current;
          previousEdgeKind[edge.to] = edge.kind;
          previousEdgeDistance[edge.to] = edge.distance;
        }
      }
    }
    if (!Number.isFinite(distances[destinationConnection.index])) {
      return { ok: false, reason: 'NO_CONNECTED_COURSE', directDistance };
    }

    const indexes = [];
    for (let index = destinationConnection.index; index >= 0; index = previous[index]) {
      indexes.push(index);
      if (index === startConnection.index) break;
    }
    indexes.reverse();
    if (indexes[0] !== startConnection.index) {
      return { ok: false, reason: 'NO_CONNECTED_COURSE', directDistance };
    }
    const selectedEdges = indexes.slice(1).map((index, offset) => {
      const from = nodes[indexes[offset]];
      const to = nodes[index];
      return {
        kind: String(previousEdgeKind[index] || 'connector'),
        distance: Math.max(0, Number(previousEdgeDistance[index]) || 0),
        from: { x: Number(from.x), y: Number(from.y) },
        to: { x: Number(to.x), y: Number(to.y) }
      };
    });
    const roadDistance = selectedEdges
      .filter(edge => edge.kind === 'road')
      .reduce((total, edge) => total + edge.distance, 0);
    const trailDistance = selectedEdges
      .filter(edge => edge.kind === 'trail')
      .reduce((total, edge) => total + edge.distance, 0);
    const learnedDistance = selectedEdges
      .filter(edge => edge.kind === 'learned')
      .reduce((total, edge) => total + edge.distance, 0);
    const unknownEdges = selectedEdges.filter(edge =>
      edge.kind === 'connector' || edge.kind === 'endpoint');
    const unknownDistance = unknownEdges
      .reduce((total, edge) => total + edge.distance, 0);
    const longestUnknownDistance = unknownEdges
      .reduce((longest, edge) => Math.max(longest, edge.distance), 0);
    const selectedDistance = selectedEdges
      .reduce((total, edge) => total + edge.distance, 0);
    const mappedPercent = selectedDistance > 0
      ? Math.max(0, Math.min(100,
        (roadDistance + trailDistance + learnedDistance) / selectedDistance * 100))
      : 0;
    const fullPoints = indexes.map(index => ({ x: nodes[index].x, y: nodes[index].y }));
    const stops = simplifyTerrainCoursePoints(
      fullPoints,
      maximumStops,
      blockedByMarkedObstacle);
    if (stops.length < 2) {
      return { ok: false, reason: 'COURSE_TOO_COMPLEX', directDistance };
    }
    const avoidedZoneCount = safeObstacles.filter(obstacle =>
      intersects(startPoint, destinationPoint, obstacle, 3)).length;
    const avoidedAreaCount = safeObstacles.filter(obstacle => obstacle.kind === 'polygon'
      && intersects(startPoint, destinationPoint, obstacle, 3)).length;
    return {
      ok: true,
      reason: '',
      routeStyle: normalizedRouteStyle,
      gapPolicy: normalizedGapPolicy,
      maximumConnectorDistance,
      directDistance,
      courseDistance: distances[destinationConnection.index],
      avoidedZoneCount,
      avoidedAreaCount,
      roadDistance,
      trailDistance,
      learnedDistance,
      unknownDistance,
      longestUnknownDistance,
      unknownSegmentCount: unknownEdges.length,
      mappedPercent,
      segments: selectedEdges.length <= 5000
        ? selectedEdges.map(edge => ({
            kind: edge.kind,
            distance: edge.distance,
            x1: edge.from.x,
            y1: edge.from.y,
            x2: edge.to.x,
            y2: edge.to.y
          }))
        : [],
      avoidedWaterCrossing: terrainWaterSafetyEnabled
        && terrainWaterMaskStatus === 'ready'
        && segmentCrossesTerrainWater(startPoint, destinationPoint),
      waterSafetyApplied: terrainWaterSafetyEnabled
        && terrainWaterMaskStatus === 'ready',
      fullPointCount: fullPoints.length,
      stops
    };
  };

  const buildRoutePlanState = () => {
    const learnedState = buildLearnedPassageState();
    if (streamerMode) {
      return {
        routePlanArmed: false,
        routePlanActive: false,
        routePlanComplete: false,
        routePlanSource: '',
        routeStopCount: 0,
        routeCurrentIndex: 0,
        routePlanTotalDistance: null,
        routeRemainingDistance: null,
        terrainNetworkReady: false,
        terrainNetworkPathCount: 0,
        terrainNetworkPointCount: 0,
        terrainNetworkSourceVersion: '',
        terrainNetworkLoadedAt: null,
        terrainCourseDirectDistance: null,
        terrainCourseDistance: null,
        terrainCourseDetourPercent: null,
        terrainCourseAvoidedZoneCount: 0,
        terrainCourseAvoidedWater: false,
        terrainCourseRoadDistance: 0,
        terrainCourseTrailDistance: 0,
        terrainCourseLearnedDistance: 0,
        terrainCourseUnknownDistance: 0,
        terrainCourseLongestUnknown: 0,
        terrainCourseUnknownSegmentCount: 0,
        terrainRouteStyle,
        terrainGapPolicy,
        terrainWaterSafetyEnabled: false,
        terrainWaterMaskStatus: 'hidden',
        terrainWaterMaskSourceVersion: '',
        terrainCommunityHazardsEnabled: false,
        terrainCommunityHazardStatus: 'hidden',
        terrainCommunityHazardCount: 0,
        terrainCommunityHazardSourceVersion: '',
        terrainCommunityHazardLoadedAt: null,
        terrainCourseStatus: 'hidden',
        ...learnedState,
        routeStops: []
      };
    }

    let totalDistance = 0;
    for (let index = 1; index < routeStops.length; index += 1) {
      totalDistance += routeDistanceBetween(routeStops[index - 1], routeStops[index]);
    }

    let remainingDistance = routePlanComplete ? 0 : null;
    if (routeStops.length && (routePlanActive || routePlanComplete)) {
      remainingDistance = 0;
      if (routePlanActive) {
        const players = getPlayerMarkers();
        const selfPlayer = players.find(player => player.isSelf);
        const selfPose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
        if (selfPose) {
          remainingDistance += routeDistanceBetween(selfPose, routeStops[routeCurrentIndex]);
        }
        for (let index = routeCurrentIndex + 1; index < routeStops.length; index += 1) {
          remainingDistance += routeDistanceBetween(routeStops[index - 1], routeStops[index]);
        }
      }
    } else if (routePlanArmed && routeStops.length > 1) {
      remainingDistance = totalDistance;
    }

    const calibration = findReactMapProps()?.calibration;
    return {
      routePlanArmed,
      routePlanActive,
      routePlanComplete,
      routePlanSource,
      routeStopCount: routeStops.length,
      routeCurrentIndex,
      routePlanTotalDistance: routeStops.length > 1
        ? routePlanSource === 'terrain' && Number.isFinite(terrainCourseDistance)
          ? terrainCourseDistance
          : totalDistance
        : null,
      routeRemainingDistance: remainingDistance,
      terrainNetworkReady,
      terrainNetworkPathCount: Number(terrainRoadNetwork?.paths?.length) || 0,
      terrainNetworkPointCount: Number(terrainRoadNetwork?.pointCount) || 0,
      terrainNetworkSourceVersion: terrainRoadNetwork?.sourceVersion || '',
      terrainNetworkLoadedAt: terrainRoadNetwork?.loadedAt ?? null,
      terrainCourseDirectDistance,
      terrainCourseDistance,
      terrainCourseDetourPercent,
      terrainCourseAvoidedZoneCount,
      terrainCourseAvoidedWater,
      terrainCourseRoadDistance,
      terrainCourseTrailDistance,
      terrainCourseLearnedDistance,
      terrainCourseUnknownDistance,
      terrainCourseLongestUnknown,
      terrainCourseUnknownSegmentCount,
      terrainRouteStyle,
      terrainGapPolicy,
      terrainWaterSafetyEnabled,
      terrainWaterMaskStatus,
      terrainWaterMaskSourceVersion: terrainWaterMask?.sourceVersion || '',
      terrainCommunityHazardsEnabled,
      terrainCommunityHazardStatus,
      terrainCommunityHazardCount: terrainCommunityHazards.length,
      terrainCommunityHazardSourceVersion:
        terrainCommunityHazardSource?.sourceVersion || '',
      terrainCommunityHazardLoadedAt:
        terrainCommunityHazardSource?.loadedAt ?? null,
      terrainCourseStatus,
      ...learnedState,
      routeStops: routeStops.map((stop, index) => {
        const world = mapToWorldPoint(calibration, stop.x, stop.y);
        return {
          index,
          worldX: world?.x ?? null,
          worldY: world?.y ?? null
        };
      })
    };
  };

  const buildPinRoster = () => {
    const calibration = findReactMapProps()?.calibration;
    const now = Date.now();
    const liveSelfPose = markerAvailable && lastMotionSample
      ? lastMotionSample
      : null;
    return savedPins.slice().reverse().map(pin => {
      const world = mapToWorldPoint(calibration, pin.x, pin.y);
      const alertRadius = pinAlertRadii.includes(Number(pin.alertRadius))
        ? Number(pin.alertRadius)
        : 0;
      let distance = null;
      let bearing = null;
      let cardinal = '';
      if (liveSelfPose) {
        const dx = pin.x - liveSelfPose.x;
        const dy = pin.y - liveSelfPose.y;
        distance = Math.hypot(dx, dy);
        bearing = (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
        cardinal = cardinals[Math.round(bearing / 45) % 8];
      }
      return {
        id: pin.id,
        type: pin.type,
        label: pin.label,
        x: pin.x,
        y: pin.y,
        worldX: world?.x ?? null,
        worldY: world?.y ?? null,
        distance,
        bearing,
        cardinal,
        favorite: Boolean(pin.favorite),
        expiresAt: Number(pin.expiresAt) > now ? Number(pin.expiresAt) : null,
        expiresInMs: Number(pin.expiresAt) > now ? Number(pin.expiresAt) - now : null,
        expiryMinutes: pinExpiryMinutes.includes(Number(pin.expiryMinutes))
          ? Number(pin.expiryMinutes)
          : 0,
        alertRadius,
        insideAlertZone: distance !== null && alertRadius > 0 && distance <= alertRadius,
        distanceToAlertZone: distance === null || alertRadius <= 0
          ? null
          : Math.max(0, distance - alertRadius),
        createdAt: pin.createdAt
      };
    });
  };

  const recordRecentRoute = (
    routes,
    point,
    label,
    now = Date.now(),
    limit = 6) => {
    const x = Number(point?.x);
    const y = Number(point?.y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) {
      return Array.isArray(routes) ? routes.slice(0, Math.max(1, Number(limit) || 6)) : [];
    }
    const safeX = Math.min(1000, Math.max(0, x));
    const safeY = Math.min(1000, Math.max(0, y));
    const safeLabel = String(label || 'Map waypoint')
      .replace(/[\u0000-\u001f\u007f]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, 64) || 'Map waypoint';
    const requestedKind = String(point?.kind || '').trim().toLowerCase();
    const safeKind = [
      'safe', 'nest', 'food', 'danger', 'water', 'rally', 'death',
      'friend', 'pack', 'salt', 'mud', 'gastrolith', 'resource',
      'estimate', 'escape', 'recovery'
    ].includes(requestedKind) ? requestedKind : '';
    const usedAt = Number.isFinite(Number(now)) ? Number(now) : Date.now();
    const next = (Array.isArray(routes) ? routes : [])
      .filter(route => Number.isFinite(Number(route?.x))
        && Number.isFinite(Number(route?.y))
        && Math.hypot(Number(route.x) - safeX, Number(route.y) - safeY) > 0.75)
      .map(route => ({ ...route }));
    next.unshift({
      id: `recent-${Math.round(usedAt)}-${Math.round(safeX * 10)}-${Math.round(safeY * 10)}`,
      label: safeLabel,
      kind: safeKind,
      x: safeX,
      y: safeY,
      usedAt
    });
    return next.slice(0, Math.min(12, Math.max(1, Number(limit) || 6)));
  };

  const buildRecentRouteRoster = () => streamerMode
    ? []
    : recentRoutes.map(route => ({
        id: route.id,
        label: route.label,
        gridReference: mapPointToGridReference(route.x, route.y),
        active: Boolean(waypoint)
          && !friendRouteName
          && !packRouteActive
          && Math.hypot(waypoint.x - route.x, waypoint.y - route.y) <= 0.75
      }));

  const calculateSessionStats = (distance, movingMs, elapsedMs, maxSpeed) => {
    const safeDistance = Math.max(0, Number(distance) || 0);
    const safeMovingMs = Math.max(0, Number(movingMs) || 0);
    return {
      sessionElapsedMs: Math.max(0, Number(elapsedMs) || 0),
      sessionMovingMs: safeMovingMs,
      sessionAverageSpeed: safeMovingMs > 0
        ? safeDistance / (safeMovingMs / 60000)
        : 0,
      sessionMaxSpeed: Math.max(0, Number(maxSpeed) || 0)
    };
  };

  const chooseMapScaleBar = (mapWidthPixels, mapScale, targetPixels = 68) => {
    const width = Number(mapWidthPixels);
    const scale = Number(mapScale);
    if (!Number.isFinite(width) || width <= 0
        || !Number.isFinite(scale) || scale <= 0) {
      return { scaleBarUnits: null, scaleBarPixels: null };
    }
    const unitsPerPixel = 1000 / (width * scale);
    const candidates = [1, 2, 5, 10, 25, 50, 100, 250, 500];
    let best = null;
    for (const units of candidates) {
      const pixels = units / unitsPerPixel;
      if (pixels < 28 || pixels > 112) continue;
      const score = Math.abs(pixels - targetPixels);
      if (!best || score < best.score) best = { units, pixels, score };
    }
    return best
      ? { scaleBarUnits: best.units, scaleBarPixels: best.pixels }
      : { scaleBarUnits: null, scaleBarPixels: null };
  };

  const buildSessionStatsState = () => {
    if (streamerMode || !sessionStatsStartedAt) {
      return {
        sessionStatsActive: false,
        sessionElapsedMs: 0,
        sessionMovingMs: 0,
        sessionAverageSpeed: 0,
        sessionMaxSpeed: 0
      };
    }
    return {
      sessionStatsActive: true,
      ...calculateSessionStats(
        sessionDistance,
        sessionMovingMs,
        Date.now() - sessionStatsStartedAt,
        sessionMaxSpeed)
    };
  };

  const calculateNavigationEta = (
    waypointDistanceValue,
    routeRemainingDistanceValue,
    currentSpeed,
    averageSpeed,
    recentSpeeds,
    movingMs,
    routeActive) => {
    const nextStopDistance = Number(waypointDistanceValue);
    const remainingDistance = Number(routeRemainingDistanceValue);
    const distance = routeActive && Number.isFinite(remainingDistance)
      ? Math.max(0, remainingDistance)
      : Number.isFinite(nextStopDistance)
        ? Math.max(0, nextStopDistance)
        : null;
    if (distance === null) {
      return {
        navigationEtaActive: false,
        navigationEtaMinutes: null,
        navigationEtaPace: null,
        navigationEtaSource: '',
        navigationEtaDistance: null
      };
    }

    const samples = (Array.isArray(recentSpeeds) ? recentSpeeds : [])
      .map(Number)
      .filter(speed => Number.isFinite(speed) && speed >= 0.15 && speed <= 600)
      .sort((a, b) => a - b);
    const midpoint = Math.floor(samples.length / 2);
    const median = samples.length === 0
      ? null
      : samples.length % 2
        ? samples[midpoint]
        : (samples[midpoint - 1] + samples[midpoint]) / 2;
    const tripPace = Number(averageSpeed);
    const hasTripPace = Number(movingMs) >= 15000
      && Number.isFinite(tripPace) && tripPace >= 0.15 && tripPace <= 600;
    const livePace = Number(currentSpeed);
    let pace = null;
    let source = '';
    if (median !== null && samples.length >= 3) {
      pace = hasTripPace ? median * 0.8 + tripPace * 0.2 : median;
      source = samples.length >= 6 ? 'LIVE' : 'RECENT';
    } else if (Number.isFinite(livePace) && livePace >= 0.15 && livePace <= 600) {
      pace = livePace;
      source = 'LIVE';
    } else if (hasTripPace) {
      pace = tripPace;
      source = 'TRIP';
    }
    if (pace === null) {
      return {
        navigationEtaActive: false,
        navigationEtaMinutes: null,
        navigationEtaPace: null,
        navigationEtaSource: '',
        navigationEtaDistance: distance
      };
    }
    return {
      navigationEtaActive: true,
      navigationEtaMinutes: distance / pace,
      navigationEtaPace: pace,
      navigationEtaSource: source,
      navigationEtaDistance: distance
    };
  };

  const calculateWaypointApproach = (
    samples,
    initialDistance,
    currentDistance,
    now = Date.now()) => {
    const current = Number(currentDistance);
    const initial = Number(initialDistance);
    const progress = Number.isFinite(current) && Number.isFinite(initial) && initial > 0.5
      ? Math.min(100, Math.max(0, (initial - current) / initial * 100))
      : null;
    const recent = (Array.isArray(samples) ? samples : [])
      .map(sample => ({
        distance: Number(sample?.distance),
        at: Number(sample?.at)
      }))
      .filter(sample => Number.isFinite(sample.distance)
        && sample.distance >= 0
        && Number.isFinite(sample.at)
        && sample.at <= Number(now)
        && Number(now) - sample.at <= 20000)
      .sort((a, b) => a.at - b.at);
    if (!Number.isFinite(current) || recent.length < 3) {
      return {
        waypointTrend: 'waiting',
        waypointClosingRate: null,
        waypointProgressPercent: progress,
        waypointApproachSpanMs: 0
      };
    }

    const first = recent[0];
    const last = recent.at(-1);
    const spanMs = Math.max(0, last.at - first.at);
    if (spanMs < 4500) {
      return {
        waypointTrend: 'waiting',
        waypointClosingRate: null,
        waypointProgressPercent: progress,
        waypointApproachSpanMs: spanMs
      };
    }

    const distanceClosed = first.distance - last.distance;
    const closingRate = distanceClosed / spanMs * 60000;
    const threshold = Math.max(1.25, Math.min(4, current * 0.0125));
    const trend = distanceClosed >= threshold && closingRate >= 2
      ? 'closing'
      : distanceClosed <= -threshold && closingRate <= -2
        ? 'away'
        : 'steady';
    return {
      waypointTrend: trend,
      waypointClosingRate: Number.isFinite(closingRate) ? closingRate : null,
      waypointProgressPercent: progress,
      waypointApproachSpanMs: spanMs
    };
  };

  const buildNavigationEtaState = routePlanState => {
    const now = Date.now();
    movementSpeedSamples = movementSpeedSamples
      .filter(sample => now - Number(sample?.at) <= 45000)
      .slice(-30);
    return calculateNavigationEta(
      waypointDistance,
      routePlanState?.routeRemainingDistance,
      selfSpeed,
      buildSessionStatsState().sessionAverageSpeed,
      movementSpeedSamples.map(sample => sample.speed),
      sessionMovingMs,
      Boolean(routePlanState?.routePlanActive));
  };

  const resetWaypointApproach = (key = '') => {
    waypointApproachKey = key;
    waypointApproachMotionAt = 0;
    waypointApproachSamples = [];
    waypointInitialDistance = null;
    waypointTrend = 'waiting';
    waypointClosingRate = null;
    waypointProgressPercent = null;
  };

  const buildWaypointApproachKey = () => {
    if (!waypoint) return '';
    if (packRouteActive) return 'pack:center';
    if (packOutlierRouteActive) return `pack:outlier:${packFarthestFriendName}`;
    if (friendRouteName) return `friend:${friendRouteName}`;
    if (activePinId) return `pin:${activePinId}`;
    return `point:${Number(waypoint.x).toFixed(2)}:${Number(waypoint.y).toFixed(2)}`;
  };

  const updateWaypointApproach = distance => {
    const currentDistance = Number(distance);
    const key = buildWaypointApproachKey();
    if (!key || !Number.isFinite(currentDistance)) {
      resetWaypointApproach(key);
      return;
    }
    if (key !== waypointApproachKey) resetWaypointApproach(key);
    waypointInitialDistance ??= currentDistance;

    const motionAt = Number(lastMotionSample?.at) || 0;
    if (motionAt > 0 && motionAt !== waypointApproachMotionAt) {
      waypointApproachMotionAt = motionAt;
      waypointApproachSamples.push({ distance: currentDistance, at: motionAt });
      waypointApproachSamples = waypointApproachSamples
        .filter(sample => motionAt - Number(sample?.at) <= 20000)
        .slice(-12);
    }

    const state = calculateWaypointApproach(
      waypointApproachSamples,
      waypointInitialDistance,
      currentDistance,
      motionAt || Date.now());
    waypointTrend = state.waypointTrend;
    waypointClosingRate = state.waypointClosingRate;
    waypointProgressPercent = state.waypointProgressPercent;
  };

  const resetSessionStats = () => {
    sessionStatsStartedAt = markerAvailable ? Date.now() : 0;
    sessionMovingMs = 0;
    sessionDistance = 0;
    sessionMaxSpeed = 0;
    selfSpeed = 0;
    movementSpeedSamples = [];
    lastMotionSample = null;
    lastMotionAt = 0;
    lastMessage = '';
    notify('session-stats-reset');
    return true;
  };

  const buildCommunityTerrainDangerRoster = (
    hazards,
    selfPose,
    enabled = true
  ) => {
    if (!enabled
        || !Number.isFinite(Number(selfPose?.x))
        || !Number.isFinite(Number(selfPose?.y))) {
      return [];
    }
    const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
    return (Array.isArray(hazards) ? hazards : [])
      .slice(0, 64)
      .map((hazard, index) => {
        const x = Number(hazard?.x);
        const y = Number(hazard?.y);
        if (!Number.isFinite(x) || !Number.isFinite(y)
            || x < 0 || x > 1000 || y < 0 || y > 1000) {
          return null;
        }
        const dx = x - Number(selfPose.x);
        const dy = y - Number(selfPose.y);
        const distance = Math.hypot(dx, dy);
        const bearing = (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        return {
          id: String(hazard?.id || `community-terrain-hazard-${index + 1}`)
            .slice(0, 80),
          type: 'danger',
          source: 'community-terrain',
          label: String(hazard?.label || `Public terrain danger ${index + 1}`)
            .slice(0, 64),
          distance,
          bearing,
          cardinal: cardinals[Math.round(bearing / 45) % 8]
        };
      })
      .filter(Boolean);
  };

  const selectNearestDangerPin = pins => {
    if (!Array.isArray(pins)) return null;
    return pins
      .filter(pin => pin?.type === 'danger'
        && pin.distance != null
        && pin.bearing != null
        && Number.isFinite(Number(pin.distance))
        && Number.isFinite(Number(pin.bearing)))
      .sort((a, b) => Number(a.distance) - Number(b.distance))[0] || null;
  };

  const buildDangerState = (pins = buildPinRoster()) => {
    const publicTerrainDangers = buildCommunityTerrainDangerRoster(
      terrainCommunityHazards,
      markerAvailable ? lastMotionSample : null,
      !streamerMode
        && terrainCommunityHazardsEnabled
        && terrainCommunityHazardStatus === 'ready');
    const nearest = streamerMode
      ? null
      : selectNearestDangerPin([
          ...(Array.isArray(pins) ? pins : []),
          ...publicTerrainDangers
        ]);
    return {
      nearestDangerPinId: nearest?.id || '',
      nearestDangerLabel: nearest?.label || '',
      nearestDangerDistance: nearest?.distance ?? null,
      nearestDangerBearing: nearest?.bearing ?? null,
      nearestDangerCardinal: nearest?.cardinal || ''
    };
  };

  const selectNearestAlertZone = pins => {
    if (!Array.isArray(pins)) return null;
    return pins
      .filter(pin => Number(pin?.alertRadius) > 0
        && Number.isFinite(Number(pin?.distance))
        && Number.isFinite(Number(pin?.bearing)))
      .sort((a, b) => {
        if (Boolean(a.insideAlertZone) !== Boolean(b.insideAlertZone)) {
          return a.insideAlertZone ? -1 : 1;
        }
        return Number(a.distanceToAlertZone) - Number(b.distanceToAlertZone)
          || Number(a.distance) - Number(b.distance);
      })[0] || null;
  };

  const buildAlertZoneState = (pins = buildPinRoster()) => {
    const nearest = streamerMode ? null : selectNearestAlertZone(pins);
    return {
      nearestAlertZonePinId: nearest?.id || '',
      nearestAlertZoneLabel: nearest?.label || '',
      nearestAlertZoneDistance: nearest?.distance ?? null,
      nearestAlertZoneBearing: nearest?.bearing ?? null,
      nearestAlertZoneCardinal: nearest?.cardinal || '',
      nearestAlertZoneRadius: Number(nearest?.alertRadius) || 0,
      nearestAlertZoneBoundaryDistance: nearest?.distanceToAlertZone ?? null,
      insideAlertZone: Boolean(nearest?.insideAlertZone)
    };
  };

  const notify = reason => {
    const now = Date.now();
    purgeExpiredPins(now);
    const isolatedMapWidth = map?.getBoundingClientRect?.().width || 0;
    const scaleBarState = chooseMapScaleBar(isolatedMapWidth, view.scale);
    const selfGridReference = !streamerMode && markerAvailable && lastMotionSample
      ? mapPointToGridReference(lastMotionSample.x, lastMotionSample.y)
      : '';
    const freshnessKnown = freshnessAt > 0;
    const freshnessAgeMs = freshnessKnown ? Math.max(0, now - freshnessAt) : 0;
    const freshnessSecond = Math.floor(freshnessAgeMs / 1000);
    const centerErrorBucket = centerErrorPx == null ? 'n/a' : centerErrorPx.toFixed(1);
    const waypointDistanceBucket = waypointDistance == null ? 'n/a' : waypointDistance.toFixed(1);
    const waypointBearingBucket = waypointBearing == null ? 'n/a' : waypointBearing.toFixed(0);
    const waypointClosingRateBucket = waypointClosingRate == null
      ? 'n/a'
      : waypointClosingRate.toFixed(1);
    const waypointProgressBucket = waypointProgressPercent == null
      ? 'n/a'
      : waypointProgressPercent.toFixed(1);
    const nearestFriendDistanceBucket = nearestFriendDistance == null
      ? 'n/a'
      : nearestFriendDistance.toFixed(1);
    const nearestFriendBearingBucket = nearestFriendBearing == null
      ? 'n/a'
      : nearestFriendBearing.toFixed(0);
    const packSpreadBucket = packSpread == null ? 'n/a' : packSpread.toFixed(1);
    const packSpreadRateBucket = packSpreadRate == null ? 'n/a' : packSpreadRate.toFixed(1);
    const packCourseSpeedBucket = packCourseSpeed == null ? 'n/a' : packCourseSpeed.toFixed(1);
    const packCourseBearingBucket = packCourseBearing == null ? 'n/a' : packCourseBearing.toFixed(0);
    const packRadiusBucket = packRadius == null ? 'n/a' : packRadius.toFixed(1);
    const packCenterDistanceBucket = packCenterDistance == null
      ? 'n/a'
      : packCenterDistance.toFixed(1);
    const packCenterBearingBucket = packCenterBearing == null
      ? 'n/a'
      : packCenterBearing.toFixed(0);
    const packFarthestDistanceBucket = packFarthestFriendDistance == null
      ? 'n/a'
      : packFarthestFriendDistance.toFixed(1);
    const nearestEncounterDistanceBucket = nearestEncounterDistance == null
      ? 'n/a'
      : nearestEncounterDistance.toFixed(1);
    const nearestEncounterBearingBucket = nearestEncounterBearing == null
      ? 'n/a'
      : nearestEncounterBearing.toFixed(0);
    const nearestEncounterRelativeSpeedBucket = nearestEncounterRelativeSpeed == null
      ? 'n/a'
      : nearestEncounterRelativeSpeed.toFixed(1);
    const nearestEncounterInterceptBucket = nearestEncounterInterceptSeconds == null
      ? 'n/a'
      : Math.round(nearestEncounterInterceptSeconds);
    const rememberedEncounterAgeBucket = rememberedEncounterNewestAgeMs == null
      ? 'n/a'
      : Math.floor(rememberedEncounterNewestAgeMs / 1000);
    const nearestRememberedEncounterDistanceBucket = nearestRememberedEncounterDistance == null
      ? 'n/a'
      : nearestRememberedEncounterDistance.toFixed(1);
    const nearestRememberedEncounterBearingBucket = nearestRememberedEncounterBearing == null
      ? 'n/a'
      : nearestRememberedEncounterBearing.toFixed(0);
    const nearestPlaceDistanceBucket = nearestPlaceDistance == null
      ? 'n/a'
      : nearestPlaceDistance.toFixed(1);
    const nearestPlaceBearingBucket = nearestPlaceBearing == null
      ? 'n/a'
      : nearestPlaceBearing.toFixed(0);
    const friendRosterSignature = friendRoster.map(friend => [
      friend.name,
      friend.distance == null ? 'n/a' : friend.distance.toFixed(1),
      friend.bearing == null ? 'n/a' : friend.bearing.toFixed(0),
      friend.cardinal || ''
    ].join('~')).join('|');
    const pinRoster = buildPinRoster();
    const pinRosterSignature = pinRoster.map(pin => [
      pin.id,
      pin.favorite ? 'favorite' : 'standard',
      pin.label,
      pin.expiresInMs == null ? 'permanent' : Math.ceil(pin.expiresInMs / 60000),
      pin.alertRadius,
      pin.distance == null ? 'n/a' : pin.distance.toFixed(1),
      pin.bearing == null ? 'n/a' : pin.bearing.toFixed(0)
    ].join('~')).join('|');
    const recentRouteRoster = buildRecentRouteRoster();
    const recentRouteSignature = recentRouteRoster.map(route => [
      route.id,
      route.label,
      route.gridReference,
      route.active ? 'active' : 'inactive'
    ].join('~')).join('|');
    const sessionStatsState = buildSessionStatsState();
    const dangerState = buildDangerState(pinRoster);
    const alertZoneState = buildAlertZoneState(pinRoster);
    const recoveryState = buildRecoveryState();
    const explorationState = buildExplorationState();
    const measurementState = buildMeasurementState();
    const routePlanState = buildRoutePlanState();
    const navigationEtaState = buildNavigationEtaState(routePlanState);
    const selfMapX = markerAvailable && lastMotionSample
      ? Number(lastMotionSample.x)
      : null;
    const selfMapY = markerAvailable && lastMotionSample
      ? Number(lastMotionSample.y)
      : null;
    const recoverySignature = [
      recoveryState.sessionStartAvailable,
      recoveryState.sessionStartDistance == null
        ? 'n/a'
        : recoveryState.sessionStartDistance.toFixed(1),
      recoveryState.sessionStartBearing == null
        ? 'n/a'
        : recoveryState.sessionStartBearing.toFixed(0),
      recoveryState.lastPositionMemoryEnabled,
      recoveryState.lastPositionAvailable,
      Math.floor((recoveryState.lastPositionAgeMs || 0) / 1000),
      recoveryState.breadcrumbReturnAvailable,
      recoveryState.breadcrumbPointCount,
      Math.floor(recoveryState.breadcrumbDistance || 0)
    ].join('~');
    const officialLayerSignature = Object.values(officialLayers)
      .map(value => value == null ? '?' : value ? '1' : '0')
      .join('');
    const signature = [
      following, markerAvailable, reason, freshnessKnown, freshnessSecond,
      centerErrorBucket, otherAnimalCount, friendAnimalCount, authorizedAnimalCount,
      view.scale.toFixed(2), headingUp, lookAheadEnabled,
      smartZoomEnabled, smartZoomSuspended, friendOnly, markerStyle, trailSeconds,
      streamerMode, playerLabelsVisible, rangeRingsVisible, rangeRingRadii.join('/'),
      mapGridVisible, breadcrumbTrailVisible, selfGridReference,
      explorationState.explorationEnabled,
      explorationState.explorationVisitedCount,
      waypointArmed, Boolean(waypoint), waypointDistanceBucket,
      waypointBearingBucket, waypointCardinal, waypoint?.label || '',
      waypoint?.kind || '', friendRouteName,
      packRouteActive, packOutlierRouteActive,
      waypointTrend, waypointClosingRateBucket, waypointProgressBucket,
      markerResponseCount,
      markerResponseStatus, markerResponseOk, selfPositionAt,
      selfX == null ? 'n/a' : selfX.toFixed(2),
      selfY == null ? 'n/a' : selfY.toFixed(2), selfPoseSource,
      selfMapX == null ? 'n/a' : selfMapX.toFixed(2),
      selfMapY == null ? 'n/a' : selfMapY.toFixed(2),
      Boolean(soundFinderState.first), Boolean(soundFinderState.second),
      Boolean(soundFinderState.estimate),
      markerResponseSource, fastPollIntervalMs, fastPollDelayMs,
      lastResponseIntervalMs, Math.round(lastFastPollDurationMs), fastPollInFlight,
      Boolean(pagePollControl?.patched), markerNetworkCount,
      Number(pagePollControl?.activeCallbacks) || 0,
      Number(pagePollControl?.callbackRuns) || 0, controllerInstallCount,
      selfHeading.toFixed(1), selfSpeed.toFixed(2), sessionDistance.toFixed(2),
      sessionStatsState.sessionStatsActive,
      Math.floor(sessionStatsState.sessionElapsedMs / 1000),
      Math.floor(sessionStatsState.sessionMovingMs / 1000),
      sessionStatsState.sessionAverageSpeed.toFixed(2),
      sessionStatsState.sessionMaxSpeed.toFixed(2),
      scaleBarState.scaleBarUnits ?? 'n/a',
      scaleBarState.scaleBarPixels == null
        ? 'n/a'
        : scaleBarState.scaleBarPixels.toFixed(1),
      officialLayerSignature, pinArmed, pinType, savedPins.length,
      activePinId, pinRosterSignature,
      recentRouteSignature,
      dangerState.nearestDangerPinId,
      dangerState.nearestDangerDistance == null
        ? 'n/a'
        : dangerState.nearestDangerDistance.toFixed(1),
      dangerState.nearestDangerBearing == null
        ? 'n/a'
        : dangerState.nearestDangerBearing.toFixed(0),
      alertZoneState.nearestAlertZonePinId,
      alertZoneState.nearestAlertZoneRadius,
      alertZoneState.nearestAlertZoneBoundaryDistance == null
        ? 'n/a'
        : alertZoneState.nearestAlertZoneBoundaryDistance.toFixed(1),
      alertZoneState.insideAlertZone,
      nearestFriendName, nearestFriendDistanceBucket,
      nearestFriendBearingBucket, nearestFriendCardinal, friendRosterSignature,
      packFriendCount, packSpreadBucket, packSpreadMotion, packSpreadRateBucket,
      packSpreadMotionSampleCount, packCourseState, packCourseSpeedBucket,
      packCourseBearingBucket, packCourseCardinal, packCourseSampleCount, packRadiusBucket,
      packCenterDistanceBucket, packCenterBearingBucket, packCenterCardinal,
      packFarthestFriendName, packFarthestDistanceBucket, Boolean(packCenterPoint),
      encounterPlayerCount, nearestEncounterDistanceBucket,
      nearestEncounterBearingBucket, nearestEncounterCardinal,
      nearestEncounterMotion, nearestEncounterRelativeSpeedBucket,
      nearestEncounterInterceptBucket, nearestEncounterMotionSampleCount,
      encounterWithin10, encounterWithin25, encounterWithin50,
      encounterMemorySeconds, encounterMemoryTrackCount,
      rememberedEncounterCount, rememberedEncounterAgeBucket,
      nearestRememberedEncounterDistanceBucket,
      nearestRememberedEncounterBearingBucket,
      nearestRememberedEncounterCardinal,
      nearestPlaceName, nearestPlaceDistanceBucket,
      nearestPlaceBearingBucket, nearestPlaceCardinal,
      officialLandmarkCatalog.length, visibleLandmarkCount, landmarkLabelDensity,
      recoverySignature,
      measurementState.measurementArmed,
      measurementState.measurementHasStart,
      measurementState.measurementActive,
      measurementState.measurementDistance == null
        ? 'n/a'
        : measurementState.measurementDistance.toFixed(2),
      measurementState.measurementBearing == null
        ? 'n/a'
        : measurementState.measurementBearing.toFixed(1),
      routePlanState.routePlanArmed,
      routePlanState.routePlanActive,
      routePlanState.routePlanComplete,
      routePlanState.routePlanSource,
      routePlanState.routeStopCount,
      routePlanState.routeCurrentIndex,
      routePlanState.routeRemainingDistance == null
        ? 'n/a'
        : routePlanState.routeRemainingDistance.toFixed(1),
      routePlanState.terrainNetworkReady,
      routePlanState.terrainNetworkPathCount,
      routePlanState.terrainNetworkPointCount,
      routePlanState.terrainNetworkSourceVersion,
      routePlanState.terrainCourseStatus,
      routePlanState.terrainCourseDistance == null
        ? 'n/a'
        : routePlanState.terrainCourseDistance.toFixed(1),
      routePlanState.terrainCourseRoadDistance.toFixed(1),
      routePlanState.terrainCourseTrailDistance.toFixed(1),
      routePlanState.terrainCourseLearnedDistance.toFixed(1),
      routePlanState.terrainCourseUnknownDistance.toFixed(1),
      routePlanState.terrainCourseLongestUnknown.toFixed(1),
      routePlanState.terrainCourseUnknownSegmentCount,
      routePlanState.terrainCourseAvoidedZoneCount,
      routePlanState.terrainCourseAvoidedWater,
      routePlanState.terrainRouteStyle,
      routePlanState.terrainGapPolicy,
      routePlanState.terrainWaterSafetyEnabled,
      routePlanState.terrainWaterMaskStatus,
      routePlanState.terrainWaterMaskSourceVersion,
      routePlanState.learnedPassageRoutingEnabled,
      routePlanState.learnedPassageVisible,
      routePlanState.learnedPassageCount,
      routePlanState.learnedPassageActiveCount,
      routePlanState.learnedPassageStaleCount,
      routePlanState.learnedPassagePointCount,
      navigationEtaState.navigationEtaMinutes == null
        ? 'n/a'
        : navigationEtaState.navigationEtaMinutes.toFixed(2),
      navigationEtaState.navigationEtaPace == null
        ? 'n/a'
        : navigationEtaState.navigationEtaPace.toFixed(2),
      navigationEtaState.navigationEtaSource,
      routePlanState.routeStops.map(stop => [
        stop.worldX == null ? 'n/a' : stop.worldX.toFixed(0),
        stop.worldY == null ? 'n/a' : stop.worldY.toFixed(0)
      ].join(',')).join('|')
    ].join(':');
    if (signature === lastMessage) return;
    lastMessage = signature;
    window.chrome?.webview?.postMessage({
      type: 'isley-follow',
      following,
      markerAvailable,
      freshnessKnown,
      freshnessAgeMs,
      centerErrorPx,
      otherAnimalCount,
      friendAnimalCount,
      authorizedAnimalCount,
      headingUp,
      lookAheadEnabled,
      smartZoomEnabled,
      smartZoomSuspended,
      friendOnly,
      markerStyle,
      trailSeconds,
      streamerMode,
      playerLabelsVisible,
      rangeRingsVisible,
      rangeRingRadii: [...rangeRingRadii],
      mapGridVisible,
      breadcrumbTrailVisible,
      selfGridReference,
      ...explorationState,
      waypointArmed,
      waypointActive: Boolean(waypoint),
      waypointDistance,
      waypointBearing,
      waypointCardinal,
      waypointLabel: waypoint?.label || '',
      waypointKind: waypoint?.kind || '',
      waypointTrend,
      waypointClosingRate,
      waypointProgressPercent,
      friendRouteName,
      packRouteActive,
      packOutlierRouteActive,
      pinArmed,
      pinType,
      pinCount: savedPins.length,
      activePinId,
      pinRoster,
      ...buildNoGoAreaState(),
      recentRoutes: recentRouteRoster,
      canRouteBack: !streamerMode && recentRoutes.length > 1,
      ...scaleBarState,
      ...sessionStatsState,
      ...dangerState,
      ...alertZoneState,
      ...recoveryState,
      ...measurementState,
      ...routePlanState,
      ...navigationEtaState,
      nearestFriendName,
      nearestFriendDistance,
      nearestFriendBearing,
      nearestFriendCardinal,
      packFriendCount,
      packSpread,
      packSpreadMotion,
      packSpreadRate,
      packSpreadMotionSampleCount,
      packCourseState,
      packCourseSpeed,
      packCourseBearing,
      packCourseCardinal,
      packCourseSampleCount,
      packRadius,
      packCenterDistance,
      packCenterBearing,
      packCenterCardinal,
      packFarthestFriendName,
      packFarthestFriendDistance,
      packCenterAvailable: Boolean(packCenterPoint),
      encounterPlayerCount,
      nearestEncounterDistance,
      nearestEncounterBearing,
      nearestEncounterCardinal,
      nearestEncounterMotion,
      nearestEncounterRelativeSpeed,
      nearestEncounterInterceptSeconds,
      nearestEncounterMotionSampleCount,
      encounterWithin10,
      encounterWithin25,
      encounterWithin50,
      encounterMemorySeconds,
      encounterMemoryTrackCount,
      rememberedEncounterCount,
      rememberedEncounterNewestAgeMs,
      nearestRememberedEncounterDistance,
      nearestRememberedEncounterBearing,
      nearestRememberedEncounterCardinal,
      nearestPlaceName,
      nearestPlaceDistance,
      nearestPlaceBearing,
      nearestPlaceCardinal,
      officialLandmarkCount: officialLandmarkCatalog.length,
      visibleLandmarkCount,
      landmarkLabelDensity,
      friendRoster: friendRoster.map(friend => ({ ...friend })),
      markerResponseCount,
      markerResponseStatus,
      markerResponseOk,
      markerResponseSource,
      fastPollIntervalMs,
      fastPollDelayMs,
      lastResponseIntervalMs,
      lastFastPollDurationMs,
      fastPollInFlight,
      pollControlPatched: Boolean(pagePollControl?.patched),
      markerNetworkCount,
      pollCallbackCount: Number(pagePollControl?.activeCallbacks) || 0,
      pollCallbackRuns: Number(pagePollControl?.callbackRuns) || 0,
      controllerInstallCount,
      lastMarkerNetworkAt,
      selfPositionAt,
      selfX,
      selfY,
      selfMapX,
      selfMapY,
      soundFinderReadingCount: soundFinderState.second
        ? 2
        : soundFinderState.first ? 1 : 0,
      soundFinderEstimateAvailable: Boolean(soundFinderState.estimate),
      trackFinderMode: soundFinderState.mode,
      trackFinderTarget: soundFinderState.target,
      selfBearing: (selfHeading + 90 + 360) % 360,
      selfSpeed,
      sessionDistance,
      officialLayers: { ...officialLayers },
      isolationStylePresent: Boolean(document.getElementById('the-isle-mapper-style')),
      mapIsolated: Boolean(map?.dataset?.isleMapperMap === 'true'),
      isolationHiddenCount: document.querySelectorAll('.the-isle-mapper-hidden').length,
      isolatedMapWidth,
      selfPoseSource,
      reactSynchronized: Boolean(setReactView),
      scale: view.scale,
      reason
    });
  };

  const isMarkerRequest = name => {
    try {
      return new URL(name, location.href).pathname.endsWith('/map/markers');
    } catch {
      return false;
    }
  };

  const recordResourceFreshness = entry => {
    if (!isMarkerRequest(entry.name)) return;
    const resourceKey = `${entry.name}:${entry.startTime.toFixed(3)}`;
    if (!seenMarkerResources.has(resourceKey)) {
      seenMarkerResources.add(resourceKey);
      markerNetworkCount += 1;
    }
    markerRequestUrl = new URL(entry.name, location.href).href;
    lastMarkerNetworkAt = Math.max(lastMarkerNetworkAt, performance.timeOrigin + entry.responseEnd);
    lastMessage = '';
  };

  for (const entry of performance.getEntriesByType('resource')) {
    recordResourceFreshness(entry);
  }
  try {
    resourceObserver = new PerformanceObserver(list => {
      for (const entry of list.getEntries()) recordResourceFreshness(entry);
    });
    resourceObserver.observe({ type: 'resource', buffered: true });
  } catch { }

  const observeMarkerResponse = async (response, source) => {
    const receivedAt = Date.now();
    markerResponseStatus = Number(response?.status) || 0;
    markerResponseOk = false;
    markerResponseSource = source;
    lastMarkerNetworkAt = receivedAt;
    try {
      const payload = await response.clone().json();
      if (response.ok && payload?.ok && Array.isArray(payload.markers)) {
        latestMarkerPlayers = payload.markers;
        markerResponseCount += 1;
        markerResponseOk = true;
        lastResponseIntervalMs = lastAcceptedResponseAt > 0
          ? receivedAt - lastAcceptedResponseAt
          : 0;
        lastAcceptedResponseAt = receivedAt;
        freshnessAt = receivedAt;
        requestAnimationFrame(() => tick('marker-response'));
      }
    } catch { }
    if (pagePollControl && source === 'official-map') {
      if (markerResponseStatus === 429) {
        pagePollControl.delayMs = Math.min(
          60000,
          Math.max(15000, Number(pagePollControl.delayMs || fastPollIntervalMs) * 2));
      } else if (markerResponseOk) {
        pagePollControl.delayMs = fastPollIntervalMs;
      } else if (markerResponseStatus >= 500 || markerResponseStatus === 0) {
        pagePollControl.delayMs = Math.min(
          30000,
          Math.max(5000, Number(pagePollControl.delayMs || fastPollIntervalMs) * 1.75));
      }
      fastPollDelayMs = Number(pagePollControl.delayMs) || fastPollIntervalMs;
    }
    lastMessage = '';
    return markerResponseOk;
  };

  originalFetch = window.fetch.bind(window);
  wrappedFetch = async (...args) => {
    const request = args[0];
    const requestName = typeof request === 'string'
      ? request
      : request?.url || String(request || '');
    if (!isMarkerRequest(requestName)) return originalFetch(...args);

    markerRequestUrl = new URL(requestName, location.href).href;
    const now = performance.now();
    const minimumGapMs = Math.max(250, fastPollIntervalMs - 50);
    if (!markerFetchPromise
        && lastSharedMarkerResponse
        && now - lastMarkerRequestStartedAt < minimumGapMs) {
      return lastSharedMarkerResponse.clone();
    }

    let ownsRequest = false;
    if (!markerFetchPromise) {
      ownsRequest = true;
      lastMarkerRequestStartedAt = now;
      markerFetchPromise = originalFetch(...args);
    }
    const sharedPromise = markerFetchPromise;
    let response;
    try {
      response = await sharedPromise;
    } catch (error) {
      if (ownsRequest && markerFetchPromise === sharedPromise) markerFetchPromise = null;
      markerResponseOk = false;
      markerResponseStatus = 0;
      markerResponseSource = 'official-map';
      lastMessage = '';
      throw error;
    }
    if (ownsRequest) {
      lastSharedMarkerResponse = response;
      lastFastPollDurationMs = performance.now() - now;
      void observeMarkerResponse(response, 'official-map');
      window.setTimeout(() => {
        if (markerFetchPromise === sharedPromise) markerFetchPromise = null;
      }, 0);
    }
    return response.clone();
  };
  window.fetch = wrappedFetch;

  const postPlayerSnapshotState = state => {
    if (playerSnapshotDisposed) return;
    window.chrome?.webview?.postMessage({
      type: 'isley-player-snapshot',
      state
    });
  };

  const fetchPlayerSnapshot = async force => {
    const now = Date.now();
    if (playerSnapshotDisposed || playerSnapshotInFlight || streamerMode) {
      if (streamerMode) postPlayerSnapshotState('unavailable');
      return false;
    }
    if (!force && now < playerSnapshotNextAt) return false;
    if (document.hidden) {
      playerSnapshotNextAt = now + 5000;
      return false;
    }

    playerSnapshotInFlight = true;
    try {
      const vitals = window.__isleyLocalMap?.getVitals?.();
      if (!vitals || typeof vitals !== 'object') {
        postPlayerSnapshotState('unavailable');
        playerSnapshotNextAt = now + fullPlayerSnapshotIntervalMs;
        return false;
      }
      const snapshot = {
        type: 'isley-player-snapshot',
        state: 'live',
        speciesId: vitals.speciesId ?? null,
        growthPercent: Number(vitals.growthPercent),
        healthCurrent: Number(vitals.healthCurrent),
        healthMaximum: Number(vitals.healthMaximum),
        foodCurrent: Number(vitals.foodCurrent),
        foodMaximum: Number(vitals.foodMaximum),
        waterCurrent: Number(vitals.waterCurrent),
        waterMaximum: Number(vitals.waterMaximum),
        primeCompleted: null,
        primeRequired: null,
        primeTotal: null
      };
      if (playerSnapshotDisposed || streamerMode) return false;
      window.chrome?.webview?.postMessage(snapshot);
      playerSnapshotFailures = 0;
      playerSnapshotNextAt = Date.now() + (
        liteMode ? litePlayerSnapshotIntervalMs : fullPlayerSnapshotIntervalMs);
      return true;
    } catch (error) {
      if (!playerSnapshotDisposed && error?.name !== 'AbortError') {
        playerSnapshotFailures += 1;
        playerSnapshotNextAt = Date.now() + Math.min(
          playerSnapshotMaximumErrorRetryMs,
          playerSnapshotErrorRetryMs
            * Math.pow(2, Math.min(4, playerSnapshotFailures - 1)));
        postPlayerSnapshotState('error');
      }
      return false;
    } finally {
      playerSnapshotInFlight = false;
    }
  };

  const scheduleFastPoll = delayMs => {
    clearTimeout(fastPollTimer);
    fastPollTimer = window.setTimeout(runFastPoll, Math.max(100, delayMs));
  };

  const runFastPoll = async () => {
    if (pagePollControl?.patched) {
      fastPollDelayMs = Number(pagePollControl.delayMs) || fastPollIntervalMs;
      clearTimeout(fastPollTimer);
      return;
    }
    if (!markerRequestUrl || document.hidden || fastPollInFlight) {
      scheduleFastPoll(markerRequestUrl ? 1000 : 500);
      return;
    }

    const sinceLastResponse = Date.now() - lastMarkerNetworkAt;
    if (lastMarkerNetworkAt > 0 && sinceLastResponse < fastPollIntervalMs - 100) {
      scheduleFastPoll(fastPollIntervalMs - sinceLastResponse);
      return;
    }

    fastPollInFlight = true;
    const startedAt = performance.now();
    try {
      const response = await originalFetch(markerRequestUrl, {
        cache: 'no-store',
        credentials: 'same-origin'
      });
      const accepted = await observeMarkerResponse(response, 'fast-poll');
      lastFastPollDurationMs = performance.now() - startedAt;
      if (response.status === 429) {
        fastPollDelayMs = Math.min(60000, Math.max(15000, fastPollDelayMs * 2));
      } else if (accepted) {
        fastPollDelayMs = fastPollIntervalMs;
      } else {
        fastPollDelayMs = Math.min(30000, Math.max(5000, fastPollDelayMs * 1.75));
      }
    } catch {
      markerResponseOk = false;
      markerResponseStatus = 0;
      markerResponseSource = 'fast-poll';
      lastFastPollDurationMs = performance.now() - startedAt;
      fastPollDelayMs = Math.min(30000, Math.max(5000, fastPollDelayMs * 1.75));
      lastMessage = '';
    } finally {
      fastPollInFlight = false;
      scheduleFastPoll(fastPollDelayMs);
    }
  };

  // The document-start controller normally owns the cadence. This scheduler
  // becomes active only if a future page build no longer exposes the expected
  // marker timer; the shared single-flight fetch still prevents duplicates.
  scheduleFastPoll(250);

  const parseTransform = () => {
    if (!layer) return;
    const value = layer.style.transform || '';
    const match = value.match(/translate\(\s*(-?[\d.]+)px\s*,\s*(-?[\d.]+)px\s*\)\s*scale\(\s*([\d.]+)\s*\)/i);
    if (!match) return;
    view.tx = Number(match[1]);
    view.ty = Number(match[2]);
    view.scale = Number(match[3]);
  };

  const findReactViewDispatcher = () => {
    if (!map) return null;
    const fiberKey = Object.keys(map).find(key => key.startsWith('__reactFiber$'));
    let fiber = fiberKey ? map[fiberKey] : null;
    for (let depth = 0; fiber && depth < 16; depth += 1, fiber = fiber.return) {
      let hook = fiber.memoizedState;
      for (let index = 0; hook && index < 40; index += 1, hook = hook.next) {
        const state = hook.memoizedState;
        if (state
            && typeof state === 'object'
            && Number.isFinite(state.scale)
            && Number.isFinite(state.tx)
            && Number.isFinite(state.ty)
            && typeof hook.queue?.dispatch === 'function') {
          return hook.queue.dispatch;
        }
      }
    }
    return null;
  };

  const applyTransform = () => {
    if (!layer) return;
    layer.style.transform = `translate(${view.tx}px, ${view.ty}px) scale(${view.scale})`;
    layer.style.transformOrigin = '0 0';
    setReactView ??= findReactViewDispatcher();
    const signature = `${view.scale.toFixed(5)}:${view.tx.toFixed(3)}:${view.ty.toFixed(3)}`;
    if (setReactView && signature !== lastDispatchedView) {
      lastDispatchedView = signature;
      setReactView({ scale: view.scale, tx: view.tx, ty: view.ty });
    }
  };

  const readMarkerPose = marker => {
    let x = Number(marker?.getAttribute('cx'));
    let y = Number(marker?.getAttribute('cy'));
    let rotation = 0;
    if (marker?.matches('polygon')) {
      const sourceTransform = marker.getAttribute('transform') || '';
      const translation = sourceTransform.match(/translate\(\s*(-?[\d.]+)(?:px)?(?:\s*,\s*|\s+)(-?[\d.]+)(?:px)?\s*\)/i);
      const heading = sourceTransform.match(/rotate\(\s*(-?[\d.]+)/i);
      if (translation) {
        x = Number(translation[1]);
        y = Number(translation[2]);
      }
      if (heading) rotation = Number(heading[1]);
    }
    return Number.isFinite(x) && Number.isFinite(y)
      ? { x, y, rotation }
      : null;
  };

  const isCalibration = value => Boolean(
    value?.a && value?.b
    && Number.isFinite(value.a.worldX) && Number.isFinite(value.a.worldY)
    && Number.isFinite(value.a.u) && Number.isFinite(value.a.v)
    && Number.isFinite(value.b.worldX) && Number.isFinite(value.b.worldY)
    && Number.isFinite(value.b.u) && Number.isFinite(value.b.v));

  const findReactMapProps = () => {
    if (!map) return null;
    const fiberKey = Object.keys(map).find(key => key.startsWith('__reactFiber$'));
    let fiber = fiberKey ? map[fiberKey] : null;
    for (let depth = 0; fiber && depth < 20; depth += 1, fiber = fiber.return) {
      for (const candidate of [fiber, fiber.alternate]) {
        const props = candidate?.memoizedProps;
        if (isCalibration(props?.calibration) && Array.isArray(props?.players)) {
          return props;
        }
      }
    }
    return null;
  };

  const worldToMapPoint = (calibration, worldX, worldY) => {
    if (!isCalibration(calibration)
        || !Number.isFinite(worldX) || !Number.isFinite(worldY)) return null;
    const horizontal = calibration.swapAxes ? worldY : worldX;
    const vertical = calibration.swapAxes ? worldX : worldY;
    const horizontalA = calibration.swapAxes
      ? calibration.a.worldY
      : calibration.a.worldX;
    const horizontalB = calibration.swapAxes
      ? calibration.b.worldY
      : calibration.b.worldX;
    const verticalA = calibration.swapAxes
      ? calibration.a.worldX
      : calibration.a.worldY;
    const verticalB = calibration.swapAxes
      ? calibration.b.worldX
      : calibration.b.worldY;
    const worldWidth = horizontalB - horizontalA;
    const worldHeight = verticalB - verticalA;
    if (Math.abs(worldWidth) < 0.0001 || Math.abs(worldHeight) < 0.0001) return null;
    const u = calibration.a.u
      + ((horizontal - horizontalA) / worldWidth)
        * (calibration.b.u - calibration.a.u);
    const v = calibration.a.v
      + ((vertical - verticalA) / worldHeight)
        * (calibration.b.v - calibration.a.v);
    return { x: 1000 * u, y: 1000 * v };
  };

  const mapToWorldPoint = (calibration, mapX, mapY) => {
    if (!isCalibration(calibration)
        || !Number.isFinite(mapX) || !Number.isFinite(mapY)) return null;
    const mapWidth = calibration.b.u - calibration.a.u;
    const mapHeight = calibration.b.v - calibration.a.v;
    if (Math.abs(mapWidth) < 0.0001 || Math.abs(mapHeight) < 0.0001) return null;
    const u = mapX / 1000;
    const v = mapY / 1000;
    const horizontal = (calibration.swapAxes
      ? calibration.a.worldY
      : calibration.a.worldX)
        + ((u - calibration.a.u) / mapWidth)
          * ((calibration.swapAxes
            ? calibration.b.worldY
            : calibration.b.worldX)
            - (calibration.swapAxes
              ? calibration.a.worldY
              : calibration.a.worldX));
    const vertical = (calibration.swapAxes
      ? calibration.a.worldX
      : calibration.a.worldY)
        + ((v - calibration.a.v) / mapHeight)
          * ((calibration.swapAxes
            ? calibration.b.worldX
            : calibration.b.worldY)
            - (calibration.swapAxes
              ? calibration.a.worldX
              : calibration.a.worldY));
    return calibration.swapAxes
      ? { x: vertical, y: horizontal }
      : { x: horizontal, y: vertical };
  };

  const readModelSelfPose = () => {
    const props = findReactMapProps();
    const calibration = props?.calibration;
    const players = Array.isArray(latestMarkerPlayers)
      ? latestMarkerPlayers
      : props?.players;
    if (!isCalibration(calibration) || !Array.isArray(players)) return null;
    const self = players.find(player => player?.self === true)
      ?? players.find(player => String(player?.label || '').trim().toLowerCase() === 'you');
    const point = self ? worldToMapPoint(calibration, Number(self.x), Number(self.y)) : null;
    if (!self || !point) return null;

    let rotation = 0;
    if (Number.isFinite(Number(self.yaw))) {
      const radians = Number(self.yaw) * Math.PI / 180;
      const facingPoint = worldToMapPoint(
        calibration,
        Number(self.x) + 1000 * Math.cos(radians),
        Number(self.y) + 1000 * Math.sin(radians));
      if (facingPoint) {
        rotation = Math.atan2(facingPoint.y - point.y, facingPoint.x - point.x)
          * 180 / Math.PI;
      }
    }
    return {
      x: point.x,
      y: point.y,
      rotation,
      rawX: Number(self.x),
      rawY: Number(self.y),
      source: Array.isArray(latestMarkerPlayers) ? 'server-model' : 'react-model'
    };
  };

  const readSelfPose = player => {
    const modelPose = readModelSelfPose();
    if (modelPose) return modelPose;
    const domPose = player?.marker ? readMarkerPose(player.marker) : null;
    return domPose ? { ...domPose, source: 'dom' } : null;
  };

  const clearTerrainCourseState = (status = 'ready') => {
    terrainCourseDestination = null;
    terrainCourseDirectDistance = null;
    terrainCourseDistance = null;
    terrainCourseDetourPercent = null;
    terrainCourseAvoidedZoneCount = 0;
    terrainCourseAvoidedWater = false;
    terrainCourseRoadDistance = 0;
    terrainCourseTrailDistance = 0;
    terrainCourseLearnedDistance = 0;
    terrainCourseUnknownDistance = 0;
    terrainCourseLongestUnknown = 0;
    terrainCourseUnknownSegmentCount = 0;
    terrainCourseSegments = [];
    terrainCourseStatus = terrainNetworkReady ? status : 'waiting-source';
    terrainCourseReplanAt = 0;
    if (terrainCourseReplanTimer) clearTimeout(terrainCourseReplanTimer);
    terrainCourseReplanTimer = 0;
  };

  const loadTerrainCommunityHazards = (payload, calibration) => {
    terrainCommunityHazards = [];
    terrainCommunityHazardSource = null;
    terrainCommunityHazardStatus = 'unavailable';
    const points = Array.isArray(payload?.points) ? payload.points : null;
    if (!isCalibration(calibration) || !points || points.length > 64) {
      drawTerrainCommunityHazards();
      return false;
    }

    const radius = Math.min(40, Math.max(4, Number(payload?.radius) || 12));
    const transformed = [];
    for (const [index, point] of points.entries()) {
      const mapped = worldToMapPoint(
        calibration, Number(point?.x), Number(point?.y));
      if (!mapped || !Number.isFinite(mapped.x) || !Number.isFinite(mapped.y)
          || mapped.x < 0 || mapped.x > 1000
          || mapped.y < 0 || mapped.y > 1000) {
        terrainCommunityHazards = [];
        drawTerrainCommunityHazards();
        return false;
      }
      transformed.push({
        id: `community-terrain-hazard-${index + 1}`,
        label: `Public terrain danger ${index + 1}`,
        kind: 'community-hazard',
        x: mapped.x,
        y: mapped.y,
        radius
      });
    }

    terrainCommunityHazards = transformed;
    terrainCommunityHazardSource = {
      sourceUrl: String(payload?.sourceUrl || '').slice(0, 240),
      mapId: String(payload?.mapId || '').slice(0, 40),
      sourceVersion: String(payload?.sourceVersion || 'live').slice(0, 24),
      loadedAt: Number(payload?.loadedAt) || Date.now()
    };
    terrainCommunityHazardStatus = 'ready';
    drawTerrainCommunityHazards();
    return true;
  };

  const drawTerrainRoadNetwork = () => {
    const svg = getMapSvg();
    const paths = Array.isArray(terrainRoadNetwork?.paths)
      ? terrainRoadNetwork.paths
      : [];
    const signature = terrainNetworkReady
      ? `${terrainRoadNetwork?.sourceVersion || 'live'}:${paths.length}:` +
        `${terrainRoadNetwork?.pointCount || 0}`
      : 'unavailable';
    if (!svg || !terrainNetworkReady || !paths.length) {
      terrainRoadDisplayRoot?.remove();
      terrainRoadDisplayRoot = null;
      terrainRoadDisplaySignature = '';
      const fallback = svg?.querySelector(':scope > #roads');
      if (fallback) fallback.style.opacity = '1';
      return false;
    }
    if (!terrainRoadDisplayRoot?.isConnected
        || terrainRoadDisplayRoot.ownerSVGElement !== svg) {
      terrainRoadDisplayRoot?.remove();
      terrainRoadDisplayRoot = document.createElementNS(
        'http://www.w3.org/2000/svg', 'g');
      terrainRoadDisplayRoot.dataset.isleyCurrentTerrainNetwork = 'true';
      terrainRoadDisplayRoot.setAttribute('pointer-events', 'none');
      terrainRoadDisplayRoot.setAttribute('aria-label', 'Current public roads and trails');
      const anchor = svg.querySelector(':scope > #structures');
      svg.insertBefore(terrainRoadDisplayRoot, anchor || null);
      terrainRoadDisplaySignature = '';
    }
    if (signature === terrainRoadDisplaySignature
        && terrainRoadDisplayRoot.childElementCount) return true;

    terrainRoadDisplaySignature = signature;
    terrainRoadDisplayRoot.replaceChildren();
    for (const path of paths) {
      const points = Array.isArray(path?.points) ? path.points : [];
      if (points.length < 2) continue;
      const data = points.map((point, index) =>
        `${index ? 'L' : 'M'}${Number(point.x).toFixed(2)} ` +
        `${Number(point.y).toFixed(2)}`).join('');
      const casing = document.createElementNS(
        'http://www.w3.org/2000/svg', 'path');
      casing.setAttribute('d', data);
      casing.setAttribute('fill', 'none');
      casing.setAttribute('stroke', '#07100d');
      casing.setAttribute('stroke-width', path.type === 'road' ? '4.6' : '3.2');
      casing.setAttribute('stroke-linecap', 'round');
      casing.setAttribute('stroke-linejoin', 'round');
      casing.setAttribute('opacity', '0.64');
      casing.setAttribute('vector-effect', 'non-scaling-stroke');
      const line = document.createElementNS(
        'http://www.w3.org/2000/svg', 'path');
      line.setAttribute('d', data);
      line.setAttribute('fill', 'none');
      line.setAttribute('stroke', path.type === 'road' ? '#d5a96c' : '#d7cb95');
      line.setAttribute('stroke-width', path.type === 'road' ? '2.1' : '1.45');
      line.setAttribute('stroke-linecap', 'round');
      line.setAttribute('stroke-linejoin', 'round');
      line.setAttribute('opacity', path.type === 'road' ? '0.9' : '0.82');
      line.setAttribute('vector-effect', 'non-scaling-stroke');
      if (path.type === 'trail') line.setAttribute('stroke-dasharray', '6 5');
      line.dataset.terrainPathType = String(path.type || '');
      terrainRoadDisplayRoot.append(casing, line);
    }
    const fallback = svg.querySelector(':scope > #roads');
    if (fallback) fallback.style.opacity = '0.28';
    return terrainRoadDisplayRoot.childElementCount > 0;
  };

  const loadTerrainRoadNetwork = payload => {
    const calibration = findReactMapProps()?.calibration;
    const gatewayMapReady = Boolean(
      window.__isleyLocalMap?.setTerrainDataset?.(payload?.gatewayMap ?? null));
    const paths = Array.isArray(payload?.paths) ? payload.paths : [];
    if (!isCalibration(calibration) || paths.length < 1 || paths.length > 200) {
      terrainRoadNetwork = null;
      terrainNetworkReady = false;
      drawTerrainRoadNetwork();
      loadTerrainCommunityHazards(null, calibration);
      void loadTerrainWaterMask(null);
      clearTerrainCourseState('waiting-source');
      lastMessage = '';
      notify('terrain-network-unavailable');
      return false;
    }

    const transformed = [];
    let pointCount = 0;
    for (const path of paths) {
      const type = String(path?.type || '').trim().toLowerCase();
      if (!['road', 'trail'].includes(type)) continue;
      const worldPoints = Array.isArray(path?.points) ? path.points : [];
      if (worldPoints.length < 2 || worldPoints.length > 500) continue;
      const points = worldPoints.map(point => worldToMapPoint(
        calibration, Number(point?.x), Number(point?.y)))
        .filter(point => point && Number.isFinite(point.x) && Number.isFinite(point.y));
      if (points.length !== worldPoints.length) continue;
      pointCount += points.length;
      if (pointCount > 20000) break;
      transformed.push({
        label: String(path?.label || 'Road/trail').slice(0, 80),
        type,
        points
      });
    }
    if (!transformed.length || pointCount > 20000) {
      terrainRoadNetwork = null;
      terrainNetworkReady = false;
      drawTerrainRoadNetwork();
      loadTerrainCommunityHazards(null, calibration);
      void loadTerrainWaterMask(null);
      clearTerrainCourseState('waiting-source');
      lastMessage = '';
      notify('terrain-network-invalid');
      return false;
    }

    terrainRoadNetwork = {
      sourceUrl: String(payload?.sourceUrl || '').slice(0, 240),
      sourceVersion: String(payload?.sourceVersion || 'live').slice(0, 24),
      loadedAt: Number(payload?.loadedAt) || Date.now(),
      paths: transformed,
      pointCount
    };
    terrainNetworkReady = true;
    drawTerrainRoadNetwork();
    loadTerrainCommunityHazards(payload?.communityHazards ?? null, calibration);
    void loadTerrainWaterMask(payload?.waterMask ?? null);
    learnedPassageRenderSignature = '';
    drawLearnedPassages();
    if (routePlanSource !== 'terrain') clearTerrainCourseState('ready');
    lastMessage = '';
    notify(gatewayMapReady
      ? 'terrain-network-and-map-ready'
      : 'terrain-network-ready-map-unavailable');
    return true;
  };

  const terrainCourseObstacles = () => [
    ...savedPins
      .filter(pin => pin.type === 'danger' && Number(pin.alertRadius) > 0)
      .map(pin => ({
        kind: 'circle',
        x: Number(pin.x),
        y: Number(pin.y),
        radius: Number(pin.alertRadius),
        id: pin.id,
        label: pin.label
      })),
    ...noGoAreas.map(area => ({
      kind: 'polygon',
      points: area.points,
      id: area.id,
      label: area.label
    })),
    ...(terrainCommunityHazardsEnabled
      ? terrainCommunityHazards.map(hazard => ({
          kind: 'community-hazard',
          x: hazard.x,
          y: hazard.y,
          radius: hazard.radius,
          id: hazard.id,
          label: hazard.label
        }))
      : [])
  ];

  const calculateDirectRouteObstacleRisk = (start, destination, obstacles = []) => {
    const safeStart = { x: Number(start?.x), y: Number(start?.y) };
    const safeDestination = { x: Number(destination?.x), y: Number(destination?.y) };
    if (![safeStart.x, safeStart.y, safeDestination.x, safeDestination.y]
        .every(Number.isFinite)
        || safeStart.x < 0 || safeStart.x > 1000
        || safeStart.y < 0 || safeStart.y > 1000
        || safeDestination.x < 0 || safeDestination.x > 1000
        || safeDestination.y < 0 || safeDestination.y > 1000) {
      return { valid: false, insideObstacleCount: 0, crossingObstacleCount: 0 };
    }

    const safeObstacles = (Array.isArray(obstacles) ? obstacles : [])
      .map(obstacle => {
        if (String(obstacle?.kind || '') === 'polygon') {
          const points = (Array.isArray(obstacle?.points) ? obstacle.points : [])
            .filter(point => Number.isFinite(Number(point?.x))
              && Number.isFinite(Number(point?.y)))
            .slice(0, noGoAreaMaximumVertices)
            .map(point => ({ x: Number(point.x), y: Number(point.y) }));
          return points.length >= 3 && routePolygonArea(points) >= 4
            && !routePolygonSelfIntersects(points)
            ? { kind: 'polygon', points }
            : null;
        }
        return Number.isFinite(Number(obstacle?.x))
          && Number.isFinite(Number(obstacle?.y))
          && Number(obstacle?.radius) > 0
          ? {
              kind: 'circle',
              x: Number(obstacle.x),
              y: Number(obstacle.y),
              radius: Math.min(150, Number(obstacle.radius))
            }
          : null;
      })
      .filter(Boolean);
    const contains = obstacle => obstacle.kind === 'polygon'
      ? routePointInPolygon(safeStart, obstacle.points, 2)
      : routeDistanceBetween(safeStart, obstacle) <= obstacle.radius + 2;
    const intersects = obstacle => obstacle.kind === 'polygon'
      ? routeSegmentIntersectsPolygon(
          safeStart, safeDestination, obstacle.points, 3)
      : segmentIntersectsCircle(safeStart, safeDestination, obstacle, 3);
    const insideObstacles = new Set(safeObstacles.filter(contains));
    const crossingObstacleCount = safeObstacles.filter(
      obstacle => !insideObstacles.has(obstacle) && intersects(obstacle)).length;
    return {
      valid: true,
      insideObstacleCount: insideObstacles.size,
      crossingObstacleCount
    };
  };

  const buildTripRouteRiskState = () => {
    const hidden = {
      tripRouteObstacleCount: 0,
      tripRouteInsideObstacle: false
    };
    if (streamerMode || routePlanSource === 'terrain' || !waypoint) return hidden;
    const players = getPlayerMarkers();
    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = markerAvailable
      ? (selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose())
      : null;
    const risk = calculateDirectRouteObstacleRisk(
      selfPose, waypoint, terrainCourseObstacles());
    return risk.valid
      ? {
          tripRouteObstacleCount: risk.crossingObstacleCount,
          tripRouteInsideObstacle: risk.insideObstacleCount > 0
        }
      : hidden;
  };

  const startEscapeRoute = () => {
    if (streamerMode) {
      notify('escape-route-streamer-mode');
      return { ok: false, reason: 'STREAMER_MODE' };
    }
    const players = getPlayerMarkers();
    updateEncounterAwareness(players);
    if (encounterPlayerCount < 1 || !Number.isFinite(Number(nearestEncounterBearing))) {
      notify('escape-route-no-live-contact');
      return { ok: false, reason: 'NO_LIVE_CONTACT' };
    }
    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = markerAvailable
      ? (selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose())
      : null;
    if (!selfPose) {
      notify('escape-route-waiting-position');
      return { ok: false, reason: 'NO_SELF_POSITION' };
    }

    const plan = calculateEscapeRoute(
      selfPose,
      nearestEncounterBearing,
      terrainCourseObstacles(),
      75,
      8);
    if (!plan.ok) {
      notify(`escape-route-${String(plan.reason || 'unavailable').toLowerCase()}`);
      return plan;
    }
    const label = `Escape ${plan.cardinal} · ${Math.round(plan.distance)} MU`;
    if (!setStaticWaypoint({ x: plan.x, y: plan.y }, label, '', false, 'escape')) {
      notify('escape-route-unavailable');
      return { ...plan, ok: false, reason: 'ROUTE_UNAVAILABLE' };
    }
    lastMessage = '';
    notify('escape-route-started');
    return { ...plan, routed: true };
  };

  const terrainCourseFailureMessage = reason => ({
    INVALID_ENDPOINT: 'invalid-endpoint',
    ALREADY_THERE: 'already-at-destination',
    START_INSIDE_OBSTACLE: 'inside-danger-zone',
    DESTINATION_INSIDE_OBSTACLE: 'destination-inside-danger-zone',
    NO_ROAD_DATA: 'road-network-empty',
    NO_ROAD_NEAR_START: 'no-road-near-player',
    NO_ROAD_NEAR_DESTINATION: 'no-road-near-destination',
    NO_CONNECTED_COURSE: 'no-connected-road-course',
    START_IN_WATER: 'start-in-water',
    DESTINATION_IN_WATER: 'destination-in-water',
    COURSE_TOO_COMPLEX: 'course-too-complex'
  }[String(reason || '')] || 'road-course-unavailable');

  const startTerrainCourseInternal = (requestedDestination, reason = 'terrain-course-started') => {
    if (streamerMode || !terrainNetworkReady || !terrainRoadNetwork?.paths?.length) {
      terrainCourseStatus = terrainNetworkReady ? 'unavailable' : 'waiting-source';
      lastMessage = '';
      notify('terrain-course-unavailable');
      return false;
    }
    const players = getPlayerMarkers();
    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = markerAvailable
      ? (selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose())
      : null;
    const destination = requestedDestination && Number.isFinite(Number(requestedDestination.x))
      && Number.isFinite(Number(requestedDestination.y))
      ? {
          x: Math.min(1000, Math.max(0, Number(requestedDestination.x))),
          y: Math.min(1000, Math.max(0, Number(requestedDestination.y))),
          label: String(requestedDestination.label || 'Destination').slice(0, 64),
          kind: normalizeWaypointKind(requestedDestination.kind)
        }
      : null;
    if (!selfPose || !destination) {
      terrainCourseStatus = !selfPose ? 'waiting-position' : 'choose-destination';
      lastMessage = '';
      notify(!selfPose ? 'terrain-course-waiting-position' : 'terrain-course-needs-destination');
      return false;
    }

    const course = calculateTerrainRoadCourse(
      selfPose,
      destination,
      [...terrainRoadNetwork.paths, ...learnedPassageRoadPaths()],
      terrainCourseObstacles(),
      12,
      terrainRouteStyle,
      terrainGapPolicy);
    if (!course.ok || !Array.isArray(course.stops) || course.stops.length < 2) {
      terrainCourseStatus = terrainCourseFailureMessage(course.reason);
      if (course.obstacleKind === 'community-hazard') {
        terrainCourseStatus = course.reason === 'START_INSIDE_OBSTACLE'
          ? 'inside-community-terrain-hazard'
          : course.reason === 'DESTINATION_INSIDE_OBSTACLE'
            ? 'destination-inside-community-terrain-hazard'
            : terrainCourseStatus;
      }
      if (course.obstacleKind === 'polygon' && course.obstacleId) {
        terrainCourseStatus = course.reason === 'START_INSIDE_OBSTACLE'
          ? 'inside-no-go-area'
          : course.reason === 'DESTINATION_INSIDE_OBSTACLE'
            ? 'destination-inside-no-go-area'
            : terrainCourseStatus;
        highlightNoGoArea(course.obstacleId);
      }
      lastMessage = '';
      notify(`terrain-course-${terrainCourseStatus}`);
      return false;
    }

    resetRoutePlan(true);
    waypoint = null;
    waypointArmed = false;
    waypointDistance = null;
    waypointBearing = null;
    waypointCardinal = '';
    friendRouteName = '';
    packRouteActive = false;
    packOutlierRouteActive = false;
    activePinId = '';
    pinArmed = false;
    cancelMeasurementCapture();
    routePlanSource = 'terrain';
    routeStops = course.stops.map((stop, index) => ({
      x: stop.x,
      y: stop.y,
      label: index === course.stops.length - 1
        ? destination.label
        : `Course bend ${index + 1}`,
      kind: index === course.stops.length - 1 ? destination.kind : ''
    }));
    routeCurrentIndex = 0;
    routePlanActive = true;
    routePlanComplete = false;
    terrainCourseDestination = destination;
    terrainCourseDirectDistance = course.directDistance;
    terrainCourseDistance = course.courseDistance;
    terrainCourseDetourPercent = course.directDistance > 0
      ? Math.max(0, (course.courseDistance / course.directDistance - 1) * 100)
      : null;
    terrainCourseAvoidedZoneCount = Number(course.avoidedZoneCount) || 0;
    terrainCourseAvoidedWater = Boolean(course.avoidedWaterCrossing);
    terrainCourseRoadDistance = Math.max(0, Number(course.roadDistance) || 0);
    terrainCourseTrailDistance = Math.max(0, Number(course.trailDistance) || 0);
    terrainCourseLearnedDistance = Math.max(
      0, Number(course.learnedDistance) || 0);
    terrainCourseUnknownDistance = Math.max(0, Number(course.unknownDistance) || 0);
    terrainCourseLongestUnknown = Math.max(0, Number(course.longestUnknownDistance) || 0);
    terrainCourseUnknownSegmentCount = Math.max(
      0, Math.floor(Number(course.unknownSegmentCount) || 0));
    terrainCourseSegments = (Array.isArray(course.segments) ? course.segments : [])
      .filter(segment => ['road', 'trail', 'learned', 'connector', 'endpoint']
        .includes(String(segment?.kind || ''))
        && [segment?.x1, segment?.y1, segment?.x2, segment?.y2]
          .every(value => Number.isFinite(Number(value))))
      .slice(0, 5000)
      .map(segment => ({
        kind: String(segment.kind),
        distance: Math.max(0, Number(segment.distance) || 0),
        x1: Number(segment.x1),
        y1: Number(segment.y1),
        x2: Number(segment.x2),
        y2: Number(segment.y2)
      }));
    terrainCourseStatus = 'active';
    terrainCourseReplanAt = Date.now();
    setWaypointFromRouteStop();
    drawRoutePlan();
    updateWaypoint(players);
    lastMessage = '';
    notify(reason);
    return true;
  };

  const startTerrainCourse = () => {
    const destination = routePlanSource === 'terrain' && terrainCourseDestination
      ? terrainCourseDestination
      : waypoint;
    return startTerrainCourseInternal(destination, routePlanSource === 'terrain'
      ? 'terrain-course-replanned'
      : 'terrain-course-started');
  };

  const distanceToRemainingTerrainCourse = point => {
    if (!point || routePlanSource !== 'terrain' || routeStops.length < 2) return null;
    let nearest = Infinity;
    const startIndex = Math.max(1, routeCurrentIndex);
    for (let index = startIndex; index < routeStops.length; index += 1) {
      nearest = Math.min(nearest, distancePointToSegment(
        point, routeStops[index - 1], routeStops[index]));
    }
    return Number.isFinite(nearest) ? nearest : null;
  };

  // Manual/shared route auto-replan: mirrors the terrain off-course posture
  // (bounded cadence, debounced timer) for straight-line route plans.
  const distanceToRemainingRoutePlan = point => {
    if (!point || routeStops.length < 2) return null;
    if (routePlanSource !== 'manual' && routePlanSource !== 'shared') return null;
    let nearest = Infinity;
    const startIndex = Math.max(1, routeCurrentIndex);
    for (let index = startIndex; index < routeStops.length; index += 1) {
      nearest = Math.min(nearest, distancePointToSegment(
        point, routeStops[index - 1], routeStops[index]));
    }
    return Number.isFinite(nearest) ? nearest : null;
  };

  const replanActiveRouteFromPosition = (point, reason = 'route-auto-replanned') => {
    if (!routeAutoReplanEnabled || !routePlanActive) return false;
    if (routePlanSource !== 'manual' && routePlanSource !== 'shared') return false;
    const x = Number(point?.x);
    const y = Number(point?.y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return false;
    const remaining = routeStops
      .slice(Math.max(0, routeCurrentIndex))
      .map(stop => ({ ...stop }));
    if (!remaining.length) return false;
    routeAutoReplanAt = Date.now();
    routeStops = [{
      x: Math.min(1000, Math.max(0, x)),
      y: Math.min(1000, Math.max(0, y)),
      label: 'Replanned start',
      kind: ''
    }, ...remaining];
    routeCurrentIndex = 1;
    routePlanComplete = false;
    setWaypointFromRouteStop();
    drawRoutePlan();
    updateWaypoint(getPlayerMarkers());
    lastMessage = '';
    notify(reason);
    return true;
  };

  const applyMapOrientation = () => {
    if (!map) return;
    const rotation = headingUp ? -selfHeading : 0;
    map.style.setProperty('--isle-mapper-rotation', `${rotation}deg`);
    map.style.setProperty('--isle-mapper-cover-scale', headingUp ? '1.43' : '1');
  };

  const getPlayerMarkers = () => {
    const svg = layer?.querySelector('svg[viewBox="0 0 1000 1000"]');
    if (!svg) return [];

    const players = [];
    for (const label of svg.querySelectorAll('text')) {
      const name = (label.textContent || '').replace(/\s+/g, ' ').trim();
      if (!name) continue;
      const playerGroup = label.closest('g');
      if (!playerGroup) continue;
      const marker = Array.from(playerGroup.children)
        .find(child => child.matches?.('polygon, circle'));
      if (!marker) continue;
      const markerClass = marker.getAttribute('class') || '';
      const explicitRole = playerGroup.dataset?.isleyRole || '';
      const isSelf = explicitRole === 'self'
        || name.toLowerCase() === 'you'
        || /\bfill-amber-400\b/.test(markerClass);
      const isFriend = explicitRole === 'friend'
        || /\bfill-emerald-400\b/.test(markerClass);
      const isProviderAnimal = explicitRole === 'self'
        || explicitRole === 'friend'
        || explicitRole === 'other';
      if (!isProviderAnimal
          && !isSelf
          && !/\bfill-(?:amber|emerald)-400\b/.test(markerClass)) continue;
      players.push({ playerGroup, label, marker, name, isSelf, isFriend });
    }
    return players;
  };

  const scaleNumericAttribute = (element, attribute, factor, key) => {
    const current = Number(element.getAttribute(attribute));
    if (!Number.isFinite(current)) return;
    const lastApplied = Number(element.dataset[`${key}Applied`]);
    const storedBase = Number(element.dataset[`${key}Base`]);
    const base = Number.isFinite(lastApplied)
      && Number.isFinite(storedBase)
      && Math.abs(current - lastApplied) < 0.0001
        ? storedBase
        : current;
    const applied = base * factor;
    element.dataset[`${key}Base`] = String(base);
    element.dataset[`${key}Applied`] = String(applied);
    element.setAttribute(attribute, String(applied));
  };

  const updateSelfMotion = pose => {
    const now = Date.now();
    sessionStatsStartedAt ||= now;
    if (!lastMotionSample) {
      lastMotionSample = { x: pose.x, y: pose.y, at: now };
      lastMotionAt = now;
      lastMessage = '';
      return;
    }

    const distance = Math.hypot(pose.x - lastMotionSample.x, pose.y - lastMotionSample.y);
    if (distance >= 0.002) {
      const elapsedMs = Math.max(250, now - lastMotionSample.at);
      const instantSpeed = distance / elapsedMs * 60000;
      if (distance <= 100) {
        selfSpeed = selfSpeed > 0
          ? selfSpeed * 0.58 + instantSpeed * 0.42
          : instantSpeed;
        if (instantSpeed >= 0.15 && instantSpeed <= 600) {
          movementSpeedSamples.push({ speed: instantSpeed, at: now });
          movementSpeedSamples = movementSpeedSamples.slice(-30);
        }
        sessionDistance += distance;
        sessionMovingMs += Math.min(5000, elapsedMs);
        sessionMaxSpeed = Math.max(sessionMaxSpeed, selfSpeed);
        lastMotionAt = now;
      } else {
        selfSpeed = 0;
        movementSpeedSamples = [];
      }
      lastMotionSample = { x: pose.x, y: pose.y, at: now };
      lastMessage = '';
      return;
    } else if (lastMotionAt && now - lastMotionAt > 5000 && selfSpeed !== 0) {
      selfSpeed = 0;
      movementSpeedSamples = movementSpeedSamples
        .filter(sample => now - Number(sample?.at) <= 45000);
      lastMessage = '';
    }
    if (now - lastMotionSample.at >= 5000) {
      lastMotionSample = { x: pose.x, y: pose.y, at: now };
      lastMessage = '';
    }
  };

  const rememberLivePosition = pose => {
    if (!pose || !Number.isFinite(pose.x) || !Number.isFinite(pose.y)) return;
    const now = Date.now();
    const anchor = {
      worldX: Number.isFinite(pose.rawX) ? pose.rawX : null,
      worldY: Number.isFinite(pose.rawY) ? pose.rawY : null,
      mapX: pose.x,
      mapY: pose.y,
      heading: pose.rotation || 0,
      at: now
    };
    sessionStartPosition ??= { ...anchor };
    recordBreadcrumbSample(pose);
    recordExplorationSample(pose);
    if (!rememberLastPositionEnabled) return;
    lastLivePosition = anchor;
    if (!lastPositionSavedAt || now - lastPositionSavedAt >= 5000) {
      try {
        localStorage.setItem(lastPositionStorageKey, JSON.stringify(anchor));
        lastPositionSavedAt = now;
      } catch {
        // The in-memory recovery point remains available for this session.
      }
    }
  };

  const updateRangeRings = pose => {
    const svg = getMapSvg();
    if (!svg) return;
    let rings = svg.querySelector(':scope > g[data-isle-mapper-range-rings="true"]');
    if (!rings) {
      rings = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      rings.dataset.isleMapperRangeRings = 'true';
      rings.setAttribute('pointer-events', 'none');
      const navigationMarker = svg.querySelector(':scope > g[data-isle-mapper-self-navigation="true"]');
      svg.insertBefore(rings, navigationMarker || null);
    }
    const ringSignature = rangeRingRadii.join(':');
    const ringModeChanged = rings.dataset.isleMapperRangeSignature !== ringSignature;
    if (ringModeChanged) {
      rings.replaceChildren();
      rings.dataset.isleMapperRangeSignature = ringSignature;
      for (const [index, radius] of rangeRingRadii.entries()) {
        const ring = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        ring.setAttribute('cx', '0');
        ring.setAttribute('cy', '0');
        ring.setAttribute('r', String(radius));
        ring.setAttribute('fill', 'none');
        ring.setAttribute('stroke', '#38bdf8');
        ring.setAttribute('stroke-width', index === 0 ? '1.5' : '2');
        ring.setAttribute('stroke-dasharray', index === 0 ? '4 5' : '7 6');
        ring.setAttribute('vector-effect', 'non-scaling-stroke');
        ring.setAttribute('opacity', index === 0 ? '0.48' : '0.68');
        rings.appendChild(ring);
      }
    }
    rings.style.display = rangeRingsVisible && !streamerMode ? '' : 'none';
    rings.setAttribute('transform', `translate(${pose.x} ${pose.y})`);
    if (ringModeChanged && rangeRingsVisible && !streamerMode) {
      rings.animate(
        [{ opacity: 0.18 }, { opacity: 1 }],
        { duration: 220, easing: 'ease-out' });
    }
  };

  const resolveMarkerStyle = (requestedMode, requestedRelation) => {
    const mode = ['standard', 'contrast', 'shapes'].includes(requestedMode)
      ? requestedMode
      : 'standard';
    const relation = ['self', 'friend', 'other'].includes(requestedRelation)
      ? requestedRelation
      : 'other';
    if (relation === 'self') {
      return mode === 'contrast'
        ? {
            mode, relation, shape: 'self', fill: '#f8fafc',
            stroke: '#22d3ee', accent: '#1d4ed8', shadow: '#22d3ee'
          }
        : {
            mode, relation, shape: 'self', fill: '#f8fafc',
            stroke: '#38bdf8', accent: '#2563eb', shadow: '#38bdf8'
          };
    }
    if (mode === 'contrast') {
      return relation === 'friend'
        ? {
            mode, relation, shape: 'circle', fill: '#f8fafc',
            stroke: '#22c55e', accent: '#0f766e', shadow: '#f8fafc'
          }
        : {
            mode, relation, shape: 'diamond', fill: '#fde047',
            stroke: '#fb7185', accent: '#7c2d12', shadow: '#fde047'
          };
    }
    if (mode === 'shapes') {
      return relation === 'friend'
        ? {
            mode, relation, shape: 'circle-plus', fill: '#34d399',
            stroke: '#f8fafc', accent: '#f8fafc', shadow: '#34d399'
          }
        : {
            mode, relation, shape: 'diamond-alert', fill: '#fbbf24',
            stroke: '#f8fafc', accent: '#7c2d12', shadow: '#fbbf24'
          };
    }
    return relation === 'friend'
      ? {
          mode, relation, shape: 'native', fill: '#34d399',
          stroke: '#f8fafc', accent: '#f8fafc', shadow: '#34d399'
        }
      : {
          mode, relation, shape: 'native', fill: '#fbbf24',
          stroke: '#f8fafc', accent: '#f8fafc', shadow: '#fbbf24'
        };
  };

  const ensureSelfNavigationMarker = player => {
    const { playerGroup, marker, label } = player;
    const pose = readSelfPose(player);
    if (!pose) return null;
    selfHeading = pose.rotation;
    selfX = pose.rawX ?? pose.x;
    selfY = pose.rawY ?? pose.y;
    selfPoseSource = pose.source || 'dom';
    updateSelfMotion(pose);
    rememberLivePosition(pose);
    updateRangeRings(pose);
    applyMapOrientation();

    const svg = getMapSvg();
    if (!svg) return null;
    for (const legacyMarker of playerGroup.querySelectorAll(':scope > g[data-isle-mapper-self-navigation="true"]')) {
      legacyMarker.remove();
    }
    let navigationMarker = svg.querySelector(':scope > g[data-isle-mapper-self-navigation="true"]');
    if (!navigationMarker) {
      navigationMarker = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      navigationMarker.dataset.isleMapperSelfNavigation = 'true';
      navigationMarker.setAttribute('pointer-events', 'none');

      const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      circle.dataset.isleMapperSelfAnchor = 'true';
      circle.setAttribute('cx', '0');
      circle.setAttribute('cy', '0');
      circle.setAttribute('r', '17');
      circle.setAttribute('fill', '#f8fafc');
      circle.setAttribute('fill-opacity', '1');
      circle.setAttribute('stroke', '#38bdf8');
      circle.setAttribute('stroke-width', '2.5');
      circle.style.filter = 'drop-shadow(0 0 4px #38bdf8)';

      const arrow = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
      arrow.dataset.isleMapperHeadingArrow = 'true';
      arrow.setAttribute('points', '-5,-8 21,0 -5,8 -1.5,0');
      arrow.setAttribute('fill', '#2563eb');
      arrow.setAttribute('stroke', '#1e3a8a');
      arrow.setAttribute('stroke-width', '1.5');
      arrow.setAttribute('stroke-linejoin', 'round');

      navigationMarker.append(circle, arrow);
      svg.appendChild(navigationMarker);
    }

    const selfStyle = resolveMarkerStyle(markerStyle, 'self');
    const selfCircle = navigationMarker.querySelector(
      '[data-isle-mapper-self-anchor="true"]');
    const selfArrow = navigationMarker.querySelector(
      '[data-isle-mapper-heading-arrow="true"]');
    if (selfCircle) {
      selfCircle.setAttribute('fill', selfStyle.fill);
      selfCircle.setAttribute('stroke', selfStyle.stroke);
      selfCircle.setAttribute('stroke-width', markerStyle === 'contrast' ? '3.2' : '2.5');
      selfCircle.style.filter = liteMode
        ? 'none'
        : `drop-shadow(0 0 4px ${selfStyle.shadow})`;
    }
    if (selfArrow) {
      selfArrow.setAttribute('fill', selfStyle.accent);
      selfArrow.setAttribute('stroke', '#1e3a8a');
    }

    if (navigationMarker !== svg.lastElementChild) svg.appendChild(navigationMarker);

    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    const markerRotation = pose.rotation + (headingUp ? -90 : 0);
    navigationMarker.setAttribute(
      'transform',
      `translate(${pose.x} ${pose.y}) rotate(${markerRotation}) scale(${inverseScale})`);
    const renderedSignature = `${pose.x.toFixed(3)}:${pose.y.toFixed(3)}:${pose.rotation.toFixed(2)}`;
    if (lastRenderedSelfSignature && renderedSignature !== lastRenderedSelfSignature) {
      selfPositionAt = Date.now();
    } else if (!selfPositionAt) {
      selfPositionAt = Date.now();
    }
    lastRenderedSelfSignature = renderedSignature;
    marker.style.opacity = '0';
    scaleNumericAttribute(label, 'font-size', 1.3, 'isleMapperFont');
    label.style.fontWeight = '900';
    label.style.paintOrder = 'stroke';
    label.style.fill = '#f8fafc';
    label.style.stroke = '#020617';
    label.style.strokeWidth = '2px';
    label.style.visibility = playerLabelsVisible && !streamerMode ? 'visible' : 'hidden';
    return navigationMarker.querySelector('[data-isle-mapper-self-anchor="true"]');
  };

  const enhanceMarker = player => {
    const { playerGroup, marker, label, isSelf, isFriend } = player;
    for (const nativeTrail of playerGroup.querySelectorAll(':scope > path, :scope > polyline')) {
      nativeTrail.style.opacity = '0';
    }
    if (isSelf) {
      playerGroup.style.display = '';
      ensureSelfNavigationMarker(player);
      return;
    }

    playerGroup.style.display = friendOnly && !isFriend ? 'none' : '';

    const markerFactor = 1.4;
    if (marker.matches('polygon')) {
      const currentTransform = marker.getAttribute('transform') || '';
      const previousApplied = marker.dataset.isleMapperAppliedTransform || '';
      const baseTransform = currentTransform === previousApplied
        ? marker.dataset.isleMapperBaseTransform || currentTransform
        : currentTransform;
      const appliedTransform = `${baseTransform} scale(${markerFactor})`;
      marker.dataset.isleMapperBaseTransform = baseTransform;
      marker.dataset.isleMapperAppliedTransform = appliedTransform;
      marker.setAttribute('transform', appliedTransform);
    } else {
      scaleNumericAttribute(marker, 'r', markerFactor, 'isleMapperRadius');
    }

    scaleNumericAttribute(label, 'font-size', 1.15, 'isleMapperFont');
    const style = resolveMarkerStyle(markerStyle, isFriend ? 'friend' : 'other');
    marker.setAttribute('fill', style.fill);
    marker.setAttribute('stroke', style.stroke);
    marker.setAttribute('vector-effect', 'non-scaling-stroke');
    marker.setAttribute('stroke-width', '1.75');
    marker.style.fill = style.fill;
    marker.style.stroke = style.stroke;
    marker.style.opacity = style.shape === 'native' ? '1' : '0';
    marker.style.filter = liteMode
      ? 'none'
      : `drop-shadow(0 0 3px ${style.shadow})`;
    label.style.fontWeight = '700';
    label.style.paintOrder = 'stroke';
    label.style.fill = '#f8fafc';
    label.style.stroke = '#020617';
    label.style.strokeWidth = '2px';
    label.style.visibility = playerLabelsVisible && !streamerMode ? 'visible' : 'hidden';
  };

  const getMapSvg = () => layer?.querySelector('svg[viewBox="0 0 1000 1000"]');

  const ensurePlayerStyleRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!playerStyleRoot?.isConnected || playerStyleRoot.ownerSVGElement !== svg) {
      playerStyleRoot = svg.querySelector(
        ':scope > g[data-isle-mapper-player-style="true"]');
    }
    if (!playerStyleRoot) {
      playerStyleRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      playerStyleRoot.dataset.isleMapperPlayerStyle = 'true';
      playerStyleRoot.setAttribute('pointer-events', 'none');
      playerStyleRoot.setAttribute('aria-hidden', 'true');
      playerStyleRoot.style.transition = 'opacity 150ms ease-out';
      const selfMarker = svg.querySelector(
        ':scope > g[data-isle-mapper-self-navigation="true"]');
      svg.insertBefore(playerStyleRoot, selfMarker || null);
    }
    return playerStyleRoot;
  };

  const drawStyledPlayerMarkers = players => {
    if (markerStyle === 'standard' || streamerMode) {
      if (playerStyleRoot) {
        playerStyleRoot.replaceChildren();
        playerStyleRoot.style.display = 'none';
      }
      playerStyleRenderSignature = '';
      return;
    }

    const root = ensurePlayerStyleRoot();
    if (!root) return;
    const items = (Array.isArray(players) ? players : [])
      .filter(player => !player.isSelf && (!friendOnly || player.isFriend))
      .map(player => ({ player, pose: readMarkerPose(player.marker) }))
      .filter(item => item.pose);
    const renderSignature = [
      markerStyle,
      liteMode,
      friendOnly,
      view.scale.toFixed(3),
      headingUp,
      ...items.map(({ player, pose }) => [
        player.name,
        player.isFriend ? 'friend' : 'other',
        pose.x.toFixed(2),
        pose.y.toFixed(2)
      ].join('~'))
    ].join('|');
    if (renderSignature === playerStyleRenderSignature && root.childElementCount) return;
    playerStyleRenderSignature = renderSignature;
    const styleChanged = root.dataset.isleMapperMarkerStyle !== markerStyle;
    root.dataset.isleMapperMarkerStyle = markerStyle;
    root.style.display = '';
    root.replaceChildren();

    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    for (const { player, pose } of items) {
      const relation = player.isFriend ? 'friend' : 'other';
      const style = resolveMarkerStyle(markerStyle, relation);
      const group = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      group.dataset.isleMapperMarkerRelation = relation;
      group.setAttribute(
        'transform',
        `translate(${pose.x} ${pose.y}) scale(${inverseScale})`);
      group.style.filter = liteMode
        ? 'none'
        : `drop-shadow(0 0 3px ${style.shadow})`;
      const title = document.createElementNS('http://www.w3.org/2000/svg', 'title');
      title.textContent = relation === 'friend'
        ? 'Authorized friend marker'
        : 'Authorized non-friend marker';

      let body;
      if (style.shape.startsWith('circle')) {
        body = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        body.setAttribute('r', markerStyle === 'contrast' ? '9.5' : '9');
      } else {
        body = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
        body.setAttribute('points', '0,-10 10,0 0,10 -10,0');
        body.setAttribute('stroke-linejoin', 'round');
      }
      body.setAttribute('fill', style.fill);
      body.setAttribute('stroke', style.stroke);
      body.setAttribute('stroke-width', markerStyle === 'contrast' ? '3' : '2.25');
      body.setAttribute('vector-effect', 'non-scaling-stroke');
      group.append(title, body);

      if (style.shape === 'circle-plus') {
        for (const points of [[-4, 0, 4, 0], [0, -4, 0, 4]]) {
          const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
          line.setAttribute('x1', String(points[0]));
          line.setAttribute('y1', String(points[1]));
          line.setAttribute('x2', String(points[2]));
          line.setAttribute('y2', String(points[3]));
          line.setAttribute('stroke', style.accent);
          line.setAttribute('stroke-width', '2.2');
          line.setAttribute('stroke-linecap', 'round');
          group.appendChild(line);
        }
      } else if (style.shape === 'diamond-alert') {
        const stem = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        stem.setAttribute('x1', '0');
        stem.setAttribute('y1', '-4.5');
        stem.setAttribute('x2', '0');
        stem.setAttribute('y2', '2');
        stem.setAttribute('stroke', style.accent);
        stem.setAttribute('stroke-width', '2.3');
        stem.setAttribute('stroke-linecap', 'round');
        const dot = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        dot.setAttribute('cy', '5');
        dot.setAttribute('r', '1.35');
        dot.setAttribute('fill', style.accent);
        group.append(stem, dot);
      } else {
        const center = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        center.setAttribute('r', '2.6');
        center.setAttribute('fill', style.accent);
        group.appendChild(center);
      }
      root.appendChild(group);
    }

    if (styleChanged && root.childElementCount) {
      root.animate(
        [{ opacity: 0.2, transform: 'scale(0.985)' }, { opacity: 1, transform: 'scale(1)' }],
        { duration: 180, easing: 'ease-out' });
    }
  };

  const mapPointToGridReference = (mapX, mapY) => {
    const x = Number(mapX);
    const y = Number(mapY);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return '';
    const column = Math.min(19, Math.max(0, Math.floor(Math.min(999.999999, Math.max(0, x)) / 50)));
    const row = Math.min(19, Math.max(0, Math.floor(Math.min(999.999999, Math.max(0, y)) / 50)));
    return `${String.fromCharCode(65 + column)}${row + 1}`;
  };

  const resolveGridReference = query => {
    const match = String(query || '').trim().match(/^(?:grid\s*)?([a-t])\s*[-:]?\s*(20|1[0-9]|[1-9])$/i);
    if (!match) return null;
    const column = match[1].toUpperCase().charCodeAt(0) - 65;
    const row = Number(match[2]) - 1;
    const gridReference = `${String.fromCharCode(65 + column)}${row + 1}`;
    return {
      x: column * 50 + 25,
      y: row * 50 + 25,
      label: `Grid ${gridReference}`,
      gridReference
    };
  };

  const parseSharedRouteTokens = query => {
    let text = String(query || '').trim();
    if (!text || text.length > 1600
        || /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/.test(text)) return [];
    text = text
      .replace(/^(?:isley|the\s+isle\s+mapper)\s+(?:breadcrumb\s+return|road\s*\/\s*trail\s+course|route)\s*\|\s*/i, '')
      .replace(/\s*\|\s*[\d,]+(?:\.\d+)?\s*mu\s+planned.*$/i, '')
      .trim();
    if (!/(?:->|>|;|\r?\n)/.test(text)) return [];
    const tokens = text
      .split(/\s*(?:->|>|;|\r?\n)\s*/)
      .map(token => token.replace(/^\d+\s*:\s*/, '').trim())
      .filter(Boolean);
    return tokens.length >= 2 && tokens.length <= 12
        && tokens.every(token => token.length <= 96 && !token.includes('|'))
      ? tokens
      : [];
  };

  const ensureExplorationRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!explorationRoot?.isConnected || explorationRoot.ownerSVGElement !== svg) {
      explorationRoot = svg.querySelector(':scope > g[data-isle-mapper-exploration="true"]');
    }
    if (!explorationRoot) {
      explorationRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      explorationRoot.dataset.isleMapperExploration = 'true';
      explorationRoot.setAttribute('pointer-events', 'none');
      explorationRoot.setAttribute('aria-hidden', 'true');
      explorationRoot.style.transition = 'opacity 180ms ease-out';
      const overlayAnchor = svg.querySelector(
        ':scope > g[data-isle-mapper-grid="true"], :scope > g[data-isle-mapper-trails="true"], :scope > g[data-isle-mapper-self-navigation="true"]');
      svg.insertBefore(explorationRoot, overlayAnchor || null);
    }
    return explorationRoot;
  };

  const drawExplorationOverlay = () => {
    const root = ensureExplorationRoot();
    if (!root) return;
    const visible = explorationEnabled && !streamerMode && exploredSectors.size > 0;
    root.style.display = visible ? '' : 'none';
    root.style.opacity = visible ? '1' : '0';
    if (!visible) return;

    const sectors = normalizeExplorationSectors(
      Array.from(exploredSectors),
      explorationGridSize);
    const renderSignature = sectors.join(',');
    if (renderSignature === explorationRenderSignature && root.childElementCount) return;
    explorationRenderSignature = renderSignature;
    root.replaceChildren();
    const sectorSize = 1000 / explorationGridSize;
    for (const sector of sectors) {
      const column = sector % explorationGridSize;
      const row = Math.floor(sector / explorationGridSize);
      const cell = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
      cell.setAttribute('x', String(column * sectorSize + 1.5));
      cell.setAttribute('y', String(row * sectorSize + 1.5));
      cell.setAttribute('width', String(sectorSize - 3));
      cell.setAttribute('height', String(sectorSize - 3));
      cell.setAttribute('rx', '3');
      cell.setAttribute('ry', '3');
      cell.setAttribute('fill', '#38bdf8');
      cell.setAttribute('fill-opacity', '0.11');
      cell.setAttribute('stroke', '#7dd3fc');
      cell.setAttribute('stroke-opacity', '0.30');
      cell.setAttribute('stroke-width', '0.75');
      cell.setAttribute('vector-effect', 'non-scaling-stroke');
      root.appendChild(cell);
    }
  };

  const ensureMapGridRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!mapGridRoot?.isConnected) {
      mapGridRoot = svg.querySelector(':scope > g[data-isle-mapper-grid="true"]');
    }
    if (!mapGridRoot) {
      mapGridRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      mapGridRoot.dataset.isleMapperGrid = 'true';
      mapGridRoot.setAttribute('pointer-events', 'none');
      mapGridRoot.setAttribute('aria-hidden', 'true');
      mapGridRoot.style.transition = 'opacity 160ms ease-out';
      const overlayAnchor = svg.querySelector(
        ':scope > g[data-isle-mapper-trails="true"], :scope > g[data-isle-mapper-self-navigation="true"]');
      svg.insertBefore(mapGridRoot, overlayAnchor || null);
    }
    return mapGridRoot;
  };

  const appendMapGridLabel = (root, text, x, y, inverseScale, opacity, fontSize = 9) => {
    const labelGroup = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    labelGroup.setAttribute(
      'transform',
      `translate(${x} ${y}) rotate(${headingUp ? selfHeading : 0}) scale(${inverseScale})`);
    const label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
    label.setAttribute('x', '0');
    label.setAttribute('y', '3');
    label.setAttribute('text-anchor', 'middle');
    label.setAttribute('font-family', 'Segoe UI, sans-serif');
    label.setAttribute('font-size', String(fontSize));
    label.setAttribute('font-weight', '800');
    label.setAttribute('fill', '#e0f2fe');
    label.setAttribute('stroke', '#03131b');
    label.setAttribute('stroke-width', '2.5');
    label.setAttribute('paint-order', 'stroke');
    label.setAttribute('opacity', String(opacity));
    label.textContent = text;
    labelGroup.appendChild(label);
    root.appendChild(labelGroup);
  };

  const drawMapGrid = () => {
    const root = ensureMapGridRoot();
    if (!root) return;
    const visible = mapGridVisible && !streamerMode;
    root.style.display = visible ? '' : 'none';
    root.style.opacity = visible ? '1' : '0';
    if (!visible) return;

    const renderSignature = [
      view.scale.toFixed(2),
      headingUp ? selfHeading.toFixed(1) : 'north'
    ].join(':');
    if (renderSignature === mapGridRenderSignature && root.childElementCount) return;
    mapGridRenderSignature = renderSignature;
    root.replaceChildren();

    for (let index = 0; index <= 20; index += 1) {
      const coordinate = index * 50;
      const major = index % 5 === 0;
      for (const vertical of [true, false]) {
        const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        line.setAttribute('x1', String(vertical ? coordinate : 0));
        line.setAttribute('y1', String(vertical ? 0 : coordinate));
        line.setAttribute('x2', String(vertical ? coordinate : 1000));
        line.setAttribute('y2', String(vertical ? 1000 : coordinate));
        line.setAttribute('stroke', '#7dd3fc');
        line.setAttribute('stroke-width', major ? '1.35' : '0.8');
        line.setAttribute('vector-effect', 'non-scaling-stroke');
        line.setAttribute('opacity', major ? '0.28' : '0.11');
        root.appendChild(line);
      }
    }

    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    for (let index = 0; index < 20; index += 1) {
      appendMapGridLabel(
        root, String.fromCharCode(65 + index), index * 50 + 25, 16,
        inverseScale, 0.72, 9);
      appendMapGridLabel(
        root, String.fromCharCode(65 + index), index * 50 + 25, 984,
        inverseScale, 0.72, 9);
      appendMapGridLabel(
        root, String(index + 1), 16, index * 50 + 25,
        inverseScale, 0.72, 9);
      appendMapGridLabel(
        root, String(index + 1), 984, index * 50 + 25,
        inverseScale, 0.72, 9);
    }

    if (view.scale >= 4) {
      for (let row = 0; row < 20; row += 1) {
        for (let column = 0; column < 20; column += 1) {
          appendMapGridLabel(
            root,
            `${String.fromCharCode(65 + column)}${row + 1}`,
            column * 50 + 25,
            row * 50 + 25,
            inverseScale,
            0.58,
            8);
        }
      }
    }
  };

  const ensureBreadcrumbTrailRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!breadcrumbTrailRoot?.isConnected) {
      breadcrumbTrailRoot = svg.querySelector(
        ':scope > g[data-isle-mapper-breadcrumb-trail="true"]');
    }
    if (!breadcrumbTrailRoot) {
      breadcrumbTrailRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      breadcrumbTrailRoot.dataset.isleMapperBreadcrumbTrail = 'true';
      breadcrumbTrailRoot.setAttribute('pointer-events', 'none');
      breadcrumbTrailRoot.setAttribute('aria-hidden', 'true');
      breadcrumbTrailRoot.style.transition = 'opacity 160ms ease-out';
      const overlayAnchor = svg.querySelector(
        ':scope > g[data-isle-mapper-trails="true"], :scope > g[data-isle-mapper-self-navigation="true"]');
      svg.insertBefore(breadcrumbTrailRoot, overlayAnchor || null);
    }
    return breadcrumbTrailRoot;
  };

  const drawBreadcrumbTrail = () => {
    const root = ensureBreadcrumbTrailRoot();
    if (!root) return;
    const visible = breadcrumbTrailVisible && !streamerMode && breadcrumbSamples.length >= 2;
    const hiddenSignature = `${visible}:${breadcrumbTrailVisible}:${streamerMode}:${breadcrumbSamples.length}`;
    root.style.display = visible ? '' : 'none';
    root.style.opacity = visible ? '1' : '0';
    if (!visible) {
      if (hiddenSignature !== breadcrumbTrailRenderSignature || root.childElementCount) {
        root.replaceChildren();
      }
      breadcrumbTrailRenderSignature = hiddenSignature;
      return;
    }

    const first = breadcrumbSamples[0];
    const last = breadcrumbSamples.at(-1);
    const renderSignature = [
      breadcrumbSamples.length,
      Number(first.x).toFixed(2),
      Number(first.y).toFixed(2),
      Number(last.x).toFixed(2),
      Number(last.y).toFixed(2),
      Number(last.at) || 0
    ].join(':');
    if (renderSignature === breadcrumbTrailRenderSignature && root.childElementCount) return;
    breadcrumbTrailRenderSignature = renderSignature;
    root.replaceChildren();

    const points = simplifyBreadcrumbTrailPoints(breadcrumbSamples, 360);
    if (points.length < 2) return;
    const pointText = samples => samples.map(point => `${point.x},${point.y}`).join(' ');
    const createPolyline = (samples, stroke, width, opacity) => {
      const trail = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
      trail.setAttribute('points', pointText(samples));
      trail.setAttribute('fill', 'none');
      trail.setAttribute('stroke', stroke);
      trail.setAttribute('stroke-width', String(width));
      trail.setAttribute('stroke-linecap', 'round');
      trail.setAttribute('stroke-linejoin', 'round');
      trail.setAttribute('vector-effect', 'non-scaling-stroke');
      trail.setAttribute('opacity', String(opacity));
      root.appendChild(trail);
    };

    createPolyline(points, '#03131b', 4.6, 0.38);
    const legCount = points.length - 1;
    const legsPerChunk = Math.max(1, Math.ceil(legCount / 12));
    for (let startIndex = 0; startIndex < legCount; startIndex += legsPerChunk) {
      const endIndex = Math.min(legCount, startIndex + legsPerChunk);
      const ageProgress = endIndex / legCount;
      createPolyline(
        points.slice(startIndex, endIndex + 1),
        '#38bdf8',
        2.15,
        0.16 + 0.58 * ageProgress);
    }

    const startMarker = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    startMarker.setAttribute('cx', String(points[0].x));
    startMarker.setAttribute('cy', String(points[0].y));
    startMarker.setAttribute('r', '4');
    startMarker.setAttribute('fill', '#07141d');
    startMarker.setAttribute('stroke', '#38bdf8');
    startMarker.setAttribute('stroke-width', '2');
    startMarker.setAttribute('vector-effect', 'non-scaling-stroke');
    startMarker.setAttribute('opacity', '0.92');
    root.appendChild(startMarker);
  };

  const ensureLearnedPassageRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!learnedPassageRoot?.isConnected) {
      learnedPassageRoot = svg.querySelector(
        ':scope > g[data-isley-learned-passages="true"]');
    }
    if (!learnedPassageRoot) {
      learnedPassageRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      learnedPassageRoot.dataset.isleyLearnedPassages = 'true';
      learnedPassageRoot.setAttribute('pointer-events', 'none');
      learnedPassageRoot.setAttribute('aria-hidden', 'true');
      learnedPassageRoot.style.transition = 'opacity 160ms ease-out';
      const overlayAnchor = svg.querySelector(
        ':scope > g[data-isle-mapper-breadcrumb-trail="true"], :scope > g[data-isle-mapper-trails="true"]');
      svg.insertBefore(learnedPassageRoot, overlayAnchor || null);
    }
    return learnedPassageRoot;
  };

  const drawLearnedPassages = () => {
    const root = ensureLearnedPassageRoot();
    if (!root) return;
    const visible = learnedPassageVisible && !streamerMode && learnedPassages.length > 0;
    root.style.display = visible ? '' : 'none';
    root.style.opacity = visible ? '1' : '0';
    if (!visible) {
      if (root.childElementCount) root.replaceChildren();
      learnedPassageRenderSignature = '';
      return;
    }

    const now = Date.now();
    const sourceVersion = terrainRoadNetwork?.sourceVersion || '';
    const signature = [
      sourceVersion,
      learnedPassages.length,
      ...learnedPassages.map(passage => [
        passage.id,
        passage.points.length,
        learnedPassageIsCurrent(
          passage, sourceVersion, now, learnedPassageActiveAgeMs),
        passage.points[0]?.x,
        passage.points[0]?.y,
        passage.points.at(-1)?.x,
        passage.points.at(-1)?.y
      ].join(':'))
    ].join('|');
    if (signature === learnedPassageRenderSignature && root.childElementCount) return;
    learnedPassageRenderSignature = signature;
    root.replaceChildren();

    for (const passage of learnedPassages) {
      const current = learnedPassageIsCurrent(
        passage, sourceVersion, now, learnedPassageActiveAgeMs);
      const underlay = document.createElementNS(
        'http://www.w3.org/2000/svg', 'polyline');
      underlay.setAttribute(
        'points',
        passage.points.map(point => `${point.x},${point.y}`).join(' '));
      underlay.setAttribute('fill', 'none');
      underlay.setAttribute('stroke', '#071018');
      underlay.setAttribute('stroke-width', '4.4');
      underlay.setAttribute('stroke-linecap', 'round');
      underlay.setAttribute('stroke-linejoin', 'round');
      underlay.setAttribute('vector-effect', 'non-scaling-stroke');
      underlay.setAttribute('opacity', current ? '0.55' : '0.32');
      root.appendChild(underlay);

      const path = underlay.cloneNode(false);
      path.setAttribute('stroke', current ? '#c084fc' : '#94a3b8');
      path.setAttribute('stroke-width', current ? '2.2' : '1.8');
      path.setAttribute('stroke-dasharray', current ? '10 4 2 4' : '3 6');
      path.setAttribute('opacity', current ? '0.9' : '0.48');
      root.appendChild(path);
    }
  };

  const normalizeSoundFinderPoint = value => {
    if (!value || typeof value !== 'object') return null;
    const x = Number(value.x);
    const y = Number(value.y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
    return {
      x: Math.min(1000, Math.max(0, x)),
      y: Math.min(1000, Math.max(0, y))
    };
  };

  const normalizeSoundFinderReading = value => {
    const point = normalizeSoundFinderPoint(value);
    const bearing = Number(value?.bearing);
    if (!point || !Number.isFinite(bearing)) return null;
    return {
      ...point,
      bearing: ((bearing % 360) + 360) % 360
    };
  };

  const normalizeSoundFinderState = value => {
    const mode = String(value?.mode || '').toLowerCase() === 'scent'
      ? 'scent'
      : 'sound';
    const requestedTarget = String(value?.target || '').toLowerCase();
    const target = ['water', 'food', 'trail', 'carcass'].includes(requestedTarget)
      ? requestedTarget
      : 'water';
    const first = normalizeSoundFinderReading(value?.first);
    const second = normalizeSoundFinderReading(value?.second);
    const estimatePoint = normalizeSoundFinderPoint(value?.estimate);
    const uncertainty = Number(value?.estimate?.uncertainty);
    const estimate = estimatePoint && Number.isFinite(uncertainty)
      ? {
          ...estimatePoint,
          uncertainty: Math.min(240, Math.max(4, uncertainty)),
          confidence: String(value?.estimate?.confidence || 'ROUGH').slice(0, 12)
        }
      : null;
    return { mode, target, first, second, estimate };
  };

  const ensureSoundFinderRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!soundFinderRoot?.isConnected) {
      soundFinderRoot = svg.querySelector(
        ':scope > g[data-isle-mapper-sound-finder="true"]');
    }
    if (!soundFinderRoot) {
      soundFinderRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      soundFinderRoot.dataset.isleMapperSoundFinder = 'true';
      soundFinderRoot.setAttribute('pointer-events', 'none');
      soundFinderRoot.setAttribute('aria-hidden', 'true');
      soundFinderRoot.style.transition = 'opacity 160ms ease-out';
      const overlayAnchor = svg.querySelector(
        ':scope > g[data-isle-mapper-self-navigation="true"]');
      svg.insertBefore(soundFinderRoot, overlayAnchor || null);
    }
    return soundFinderRoot;
  };

  const soundBearingRayEnd = reading => {
    if (!reading) return null;
    const radians = reading.bearing * Math.PI / 180;
    const dx = Math.sin(radians);
    const dy = -Math.cos(radians);
    const candidates = [];
    if (dx > 0.000001) candidates.push((1000 - reading.x) / dx);
    else if (dx < -0.000001) candidates.push((0 - reading.x) / dx);
    if (dy > 0.000001) candidates.push((1000 - reading.y) / dy);
    else if (dy < -0.000001) candidates.push((0 - reading.y) / dy);
    const distance = Math.min(...candidates.filter(value => value > 0));
    if (!Number.isFinite(distance)) return null;
    return {
      x: Math.min(1000, Math.max(0, reading.x + dx * distance)),
      y: Math.min(1000, Math.max(0, reading.y + dy * distance))
    };
  };

  const appendSoundBearing = (root, reading, label, inverseScale, opacity) => {
    const end = soundBearingRayEnd(reading);
    if (!end) return;
    const underlay = document.createElementNS('http://www.w3.org/2000/svg', 'line');
    underlay.setAttribute('x1', String(reading.x));
    underlay.setAttribute('y1', String(reading.y));
    underlay.setAttribute('x2', String(end.x));
    underlay.setAttribute('y2', String(end.y));
    underlay.setAttribute('stroke', '#03131b');
    underlay.setAttribute('stroke-width', '4.5');
    underlay.setAttribute('stroke-linecap', 'round');
    underlay.setAttribute('vector-effect', 'non-scaling-stroke');
    underlay.setAttribute('opacity', String(opacity * 0.58));
    root.appendChild(underlay);

    const ray = underlay.cloneNode();
    ray.setAttribute('stroke', '#22d3ee');
    ray.setAttribute('stroke-width', '2');
    ray.setAttribute('stroke-dasharray', '8 6');
    ray.setAttribute('opacity', String(opacity));
    root.appendChild(ray);

    const marker = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    marker.setAttribute(
      'transform',
      `translate(${reading.x} ${reading.y}) rotate(${headingUp ? selfHeading : 0}) scale(${inverseScale})`);
    const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    circle.setAttribute('r', '8');
    circle.setAttribute('fill', '#07141d');
    circle.setAttribute('stroke', '#22d3ee');
    circle.setAttribute('stroke-width', '2');
    const text = document.createElementNS('http://www.w3.org/2000/svg', 'text');
    text.setAttribute('x', '0');
    text.setAttribute('y', '3');
    text.setAttribute('text-anchor', 'middle');
    text.setAttribute('font-family', 'Segoe UI, sans-serif');
    text.setAttribute('font-size', '7');
    text.setAttribute('font-weight', '900');
    text.setAttribute('fill', '#cffafe');
    text.textContent = label;
    marker.append(circle, text);
    root.appendChild(marker);
  };

  const drawSoundFinder = () => {
    const root = ensureSoundFinderRoot();
    if (!root) return;
    const visible = !streamerMode && Boolean(soundFinderState.first);
    root.style.display = visible ? '' : 'none';
    root.style.opacity = visible ? '1' : '0';
    const stateSignature = JSON.stringify(soundFinderState);
    const renderSignature = [
      visible,
      stateSignature,
      view.scale.toFixed(2),
      headingUp ? selfHeading.toFixed(1) : 'north'
    ].join(':');
    if (renderSignature === soundFinderRenderSignature && root.childElementCount) return;
    soundFinderRenderSignature = renderSignature;
    root.replaceChildren();
    if (!visible) return;

    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    const bearingPrefix = soundFinderState.mode === 'scent' ? 'Q' : 'B';
    appendSoundBearing(
      root, soundFinderState.first, `${bearingPrefix}1`, inverseScale, 0.88);
    if (soundFinderState.second) {
      appendSoundBearing(
        root, soundFinderState.second, `${bearingPrefix}2`, inverseScale, 0.72);
    }
    const estimate = soundFinderState.estimate;
    if (!estimate) return;

    const area = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    area.setAttribute('cx', String(estimate.x));
    area.setAttribute('cy', String(estimate.y));
    area.setAttribute('r', String(estimate.uncertainty));
    area.setAttribute('fill', '#ffb24a');
    area.setAttribute('fill-opacity', '0.08');
    area.setAttribute('stroke', '#ffb24a');
    area.setAttribute('stroke-width', '2');
    area.setAttribute('stroke-dasharray', '5 5');
    area.setAttribute('vector-effect', 'non-scaling-stroke');
    area.setAttribute('opacity', '0.9');
    root.appendChild(area);

    const marker = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    marker.setAttribute(
      'transform',
      `translate(${estimate.x} ${estimate.y}) rotate(${headingUp ? selfHeading : 0}) scale(${inverseScale})`);
    const halo = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    halo.setAttribute('r', '11');
    halo.setAttribute('fill', '#07141d');
    halo.setAttribute('fill-opacity', '0.82');
    halo.setAttribute('stroke', '#ffb24a');
    halo.setAttribute('stroke-width', '2.5');
    const horizontal = document.createElementNS('http://www.w3.org/2000/svg', 'line');
    horizontal.setAttribute('x1', '-6');
    horizontal.setAttribute('y1', '0');
    horizontal.setAttribute('x2', '6');
    horizontal.setAttribute('y2', '0');
    const vertical = document.createElementNS('http://www.w3.org/2000/svg', 'line');
    vertical.setAttribute('x1', '0');
    vertical.setAttribute('y1', '-6');
    vertical.setAttribute('x2', '0');
    vertical.setAttribute('y2', '6');
    for (const line of [horizontal, vertical]) {
      line.setAttribute('stroke', '#ffb24a');
      line.setAttribute('stroke-width', '2.3');
      line.setAttribute('stroke-linecap', 'round');
    }
    marker.append(halo, horizontal, vertical);
    root.appendChild(marker);
  };

  const recordTrailSamples = players => {
    const now = Date.now();
    const retentionCutoff = now - 120000;
    const liveNames = new Set();
    for (const player of players) {
      const pose = player.isSelf
        ? readSelfPose(player)
        : readMarkerPose(player.marker);
      if (!pose) continue;
      liveNames.add(player.name);
      const samples = trailSamples.get(player.name) || [];
      const last = samples.at(-1);
      if (!last || now - last.at >= 1000 || Math.hypot(pose.x - last.x, pose.y - last.y) >= 0.35) {
        samples.push({ x: pose.x, y: pose.y, at: now });
      }
      while (samples.length && samples[0].at < retentionCutoff) samples.shift();
      trailSamples.set(player.name, samples);
    }
    for (const [name, samples] of trailSamples) {
      if (!liveNames.has(name) && (!samples.length || samples.at(-1).at < retentionCutoff)) {
        trailSamples.delete(name);
      }
    }
  };

  const drawTrails = players => {
    const svg = getMapSvg();
    if (!svg) return;
    trailRoot ??= svg.querySelector(':scope > g[data-isle-mapper-trails="true"]');
    if (!trailRoot) {
      trailRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      trailRoot.dataset.isleMapperTrails = 'true';
      trailRoot.setAttribute('pointer-events', 'none');
      const markerAnchor = svg.querySelector(
        ':scope > g[data-isle-mapper-player-style="true"], :scope > g[data-isle-mapper-self-navigation="true"]');
      svg.insertBefore(trailRoot, markerAnchor || null);
    }
    trailRoot.replaceChildren();
    if (trailSeconds <= 0 || streamerMode) return;

    const cutoff = Date.now() - trailSeconds * 1000;
    for (const player of players) {
      if (!player.isSelf && friendOnly && !player.isFriend) continue;
      const samples = (trailSamples.get(player.name) || []).filter(sample => sample.at >= cutoff);
      if (samples.length < 2) continue;
      const trail = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
      trail.setAttribute('points', samples.map(sample => `${sample.x},${sample.y}`).join(' '));
      trail.setAttribute('fill', 'none');
      trail.setAttribute('stroke', player.isSelf ? '#38bdf8' : player.isFriend ? '#34d399' : '#fbbf24');
      trail.setAttribute('stroke-width', player.isSelf ? '3' : '2.25');
      trail.setAttribute('stroke-linecap', 'round');
      trail.setAttribute('stroke-linejoin', 'round');
      trail.setAttribute('vector-effect', 'non-scaling-stroke');
      trail.setAttribute('opacity', player.isSelf ? '0.9' : '0.72');
      trailRoot.appendChild(trail);
    }
  };

  const ensureEncounterMemoryRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!encounterMemoryRoot?.isConnected) {
      encounterMemoryRoot = svg.querySelector(
        ':scope > g[data-isle-mapper-encounter-memory="true"]');
    }
    if (!encounterMemoryRoot) {
      encounterMemoryRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      encounterMemoryRoot.dataset.isleMapperEncounterMemory = 'true';
      encounterMemoryRoot.setAttribute('pointer-events', 'none');
      encounterMemoryRoot.setAttribute('aria-hidden', 'true');
      encounterMemoryRoot.style.transition = 'opacity 160ms ease-out';
      const overlayAnchor = svg.querySelector(
        ':scope > g[data-isle-mapper-trails="true"], :scope > g[data-isle-mapper-self-navigation="true"]');
      svg.insertBefore(encounterMemoryRoot, overlayAnchor || null);
    }
    return encounterMemoryRoot;
  };

  const clearEncounterMemoryInternal = (announce = false) => {
    const hadTracks = encounterMemoryTracks.size > 0;
    encounterMemoryTracks.clear();
    encounterMemoryLiveNames = new Set();
    encounterMemoryTrackCount = 0;
    rememberedEncounterCount = 0;
    rememberedEncounterNewestAgeMs = null;
    nearestRememberedEncounterDistance = null;
    nearestRememberedEncounterBearing = null;
    nearestRememberedEncounterCardinal = '';
    encounterMemoryRenderSignature = '';
    if (encounterMemoryRoot) {
      encounterMemoryRoot.replaceChildren();
      encounterMemoryRoot.style.display = 'none';
    }
    lastMessage = '';
    if (announce && hadTracks) notify('encounter-memory-cleared');
    return hadTracks;
  };

  const updateEncounterMemory = players => {
    if (streamerMode || encounterMemorySeconds <= 0) {
      clearEncounterMemoryInternal(false);
      return;
    }

    const now = Date.now();
    const retentionCutoff = now - encounterMemorySeconds * 1000;
    const liveNames = new Set();
    const friendNames = new Set();
    for (const player of Array.isArray(players) ? players : []) {
      const name = String(player?.name || '').slice(0, 64);
      if (!name || player?.isSelf) continue;
      if (player.isFriend) {
        friendNames.add(name);
        continue;
      }
      const pose = readMarkerPose(player.marker);
      if (!pose) continue;
      liveNames.add(name);
      const track = encounterMemoryTracks.get(name) || {
        name,
        lastSeenAt: now,
        samples: []
      };
      const last = track.samples.at(-1);
      if (!last || now - Number(last.at) >= 2000
          || Math.hypot(pose.x - last.x, pose.y - last.y) >= 0.35) {
        track.samples.push({ x: pose.x, y: pose.y, at: now });
      }
      track.lastSeenAt = now;
      track.samples = track.samples
        .filter(sample => Number(sample?.at) >= retentionCutoff)
        .slice(-300);
      encounterMemoryTracks.set(name, track);
    }

    for (const name of friendNames) encounterMemoryTracks.delete(name);
    for (const [name, track] of encounterMemoryTracks) {
      track.samples = (Array.isArray(track.samples) ? track.samples : [])
        .filter(sample => Number(sample?.at) >= retentionCutoff)
        .slice(-300);
      if (!track.samples.length || Number(track.lastSeenAt) < retentionCutoff) {
        encounterMemoryTracks.delete(name);
      }
    }

    encounterMemoryLiveNames = liveNames;
    const selfPlayer = (Array.isArray(players) ? players : [])
      .find(player => player.isSelf);
    const selfPose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
    const summary = summarizeEncounterMemory(
      Array.from(encounterMemoryTracks.values()),
      Array.from(liveNames),
      encounterMemorySeconds,
      now,
      selfPose);
    encounterMemoryTrackCount = summary.trackCount;
    rememberedEncounterCount = summary.rememberedCount;
    rememberedEncounterNewestAgeMs = summary.newestAgeMs;
    nearestRememberedEncounterDistance = summary.nearestDistance;
    nearestRememberedEncounterBearing = summary.nearestBearing;
    nearestRememberedEncounterCardinal = summary.nearestCardinal;
  };

  const drawEncounterMemory = () => {
    const root = ensureEncounterMemoryRoot();
    if (!root) return;
    const visible = !streamerMode && !friendOnly
      && encounterMemorySeconds > 0 && rememberedEncounterCount > 0;
    root.style.display = visible ? '' : 'none';
    root.style.opacity = visible ? '1' : '0';
    if (!visible) {
      if (root.childElementCount) root.replaceChildren();
      encounterMemoryRenderSignature = '';
      return;
    }

    const now = Date.now();
    const retentionMs = encounterMemorySeconds * 1000;
    const rememberedTracks = Array.from(encounterMemoryTracks.values())
      .filter(track => !encounterMemoryLiveNames.has(track.name)
        && now - Number(track.lastSeenAt) <= retentionMs);
    const renderSignature = [
      encounterMemorySeconds,
      view.scale.toFixed(3),
      headingUp,
      ...rememberedTracks.map(track => {
        const last = track.samples.at(-1);
        return [
          track.name,
          track.samples.length,
          Math.floor((now - Number(track.lastSeenAt)) / 5000),
          Number(last?.x).toFixed(2),
          Number(last?.y).toFixed(2)
        ].join('~');
      })
    ].join('|');
    if (renderSignature === encounterMemoryRenderSignature && root.childElementCount) return;
    encounterMemoryRenderSignature = renderSignature;
    root.replaceChildren();

    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    for (const track of rememberedTracks) {
      const ageMs = Math.max(0, now - Number(track.lastSeenAt));
      const freshness = Math.max(0, Math.min(1, 1 - ageMs / retentionMs));
      const opacity = 0.12 + 0.42 * freshness;
      const points = simplifyBreadcrumbTrailPoints(track.samples, 72);
      if (points.length >= 2) {
        const trail = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
        trail.setAttribute('points', points.map(point => `${point.x},${point.y}`).join(' '));
        trail.setAttribute('fill', 'none');
        trail.setAttribute('stroke', '#fbbf24');
        trail.setAttribute('stroke-width', '1.75');
        trail.setAttribute('stroke-linecap', 'round');
        trail.setAttribute('stroke-linejoin', 'round');
        trail.setAttribute('stroke-dasharray', '4 5');
        trail.setAttribute('vector-effect', 'non-scaling-stroke');
        trail.setAttribute('opacity', String(opacity));
        root.appendChild(trail);
      }

      const last = points.at(-1) || track.samples.at(-1);
      if (!last) continue;
      const marker = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      marker.setAttribute(
        'transform',
        `translate(${last.x} ${last.y}) scale(${inverseScale})`);
      marker.setAttribute('opacity', String(Math.min(0.78, opacity + 0.18)));
      const title = document.createElementNS('http://www.w3.org/2000/svg', 'title');
      title.textContent = `Last authorized sighting · ${Math.floor(ageMs / 1000)}s ago`;
      const ring = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      ring.setAttribute('r', '7');
      ring.setAttribute('fill', '#07141d');
      ring.setAttribute('fill-opacity', '0.72');
      ring.setAttribute('stroke', '#fbbf24');
      ring.setAttribute('stroke-width', '1.8');
      const dot = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      dot.setAttribute('r', '2.2');
      dot.setAttribute('fill', '#fbbf24');
      marker.append(title, ring, dot);
      root.appendChild(marker);
    }
  };

  const persistSavedPins = () => {
    try {
      localStorage.setItem(pinStorageKey, JSON.stringify(savedPins));
      return true;
    } catch {
      return false;
    }
  };

  // Pin share codes: a compact base64 payload packs can paste to each other.
  const pinShareCodePrefix = 'ISLEYPINS1.';
  const exportPinShareCode = () => {
    const payload = savedPins
      .filter(pin => pin && Object.hasOwn(pinTypes, pin.type))
      .slice(-20)
      .map(pin => ({
        t: pin.type,
        x: Math.round(Math.min(1000, Math.max(0, Number(pin.x))) * 10) / 10,
        y: Math.round(Math.min(1000, Math.max(0, Number(pin.y))) * 10) / 10,
        l: String(pin.label || '').slice(0, 64)
      }));
    if (!payload.length) return '';
    try {
      return pinShareCodePrefix + btoa(unescape(encodeURIComponent(JSON.stringify(payload))));
    } catch {
      return '';
    }
  };

  const importPinShareCode = code => {
    const text = String(code || '').trim();
    if (!text.startsWith(pinShareCodePrefix) || text.length > 8192) return -1;
    let entries;
    try {
      entries = JSON.parse(decodeURIComponent(escape(atob(text.slice(pinShareCodePrefix.length)))));
    } catch {
      return -1;
    }
    if (!Array.isArray(entries)) return -1;
    let added = 0;
    for (const entry of entries.slice(0, 20)) {
      if (!entry || typeof entry.t !== 'string' || !Object.hasOwn(pinTypes, entry.t)) continue;
      const x = Number(entry.x);
      const y = Number(entry.y);
      if (!Number.isFinite(x) || !Number.isFinite(y)) continue;
      const clampedX = Math.min(1000, Math.max(0, x));
      const clampedY = Math.min(1000, Math.max(0, y));
      const duplicate = savedPins.some(pin => pin.type === entry.t
        && Math.abs(pin.x - clampedX) < 0.5 && Math.abs(pin.y - clampedY) < 0.5);
      if (duplicate) continue;
      if (savedPins.length >= 20) savedPins.shift();
      savedPins.push({
        id: `${Date.now()}-${Math.random()}`,
        type: entry.t,
        x: clampedX,
        y: clampedY,
        label: String(entry.l || pinTypes[entry.t].label).slice(0, 64),
        favorite: false,
        expiresAt: 0,
        expiryMinutes: 0,
        alertRadius: 0,
        createdAt: Date.now()
      });
      added += 1;
    }
    if (added > 0) {
      persistSavedPins();
      drawSavedPins();
    }
    return added;
  };

  const partitionPinsByExpiry = (pins, now = Date.now()) => {
    const activePins = [];
    const expiredPinIds = [];
    for (const pin of Array.isArray(pins) ? pins : []) {
      const expiresAt = Number(pin?.expiresAt);
      if (Number.isFinite(expiresAt) && expiresAt > 0 && expiresAt <= Number(now)) {
        expiredPinIds.push(String(pin?.id || ''));
      } else {
        activePins.push(pin);
      }
    }
    return { activePins, expiredPinIds };
  };

  const purgeExpiredPins = (now = Date.now()) => {
    const partition = partitionPinsByExpiry(savedPins, now);
    if (!partition.expiredPinIds.length) return false;
    const expiredIds = new Set(partition.expiredPinIds);
    savedPins = partition.activePins;
    if (activePinId && expiredIds.has(activePinId)) {
      activePinId = '';
      waypoint = null;
      waypointArmed = false;
      waypointDistance = null;
      waypointBearing = null;
      waypointCardinal = '';
      updateWaypoint(getPlayerMarkers());
    }
    persistSavedPins();
    drawSavedPins();
    lastMessage = '';
    return true;
  };

  // One-step undo for destructive map-tool clears. Each collection keeps a
  // single snapshot taken just before its clear; undoLastClear restores the
  // most recently cleared collection unless a specific one is requested.
  const mapClearUndoState = { pins: null, route: null, noGo: null, measurement: null };
  let mapClearUndoLastKind = '';

  const snapshotMapClear = (kind, snapshot) => {
    if (!Object.hasOwn(mapClearUndoState, kind)) return;
    mapClearUndoState[kind] = snapshot;
    mapClearUndoLastKind = kind;
  };

  const undoLastClear = (requestedKind = '') => {
    const kind = String(requestedKind || mapClearUndoLastKind || '');
    if (!Object.hasOwn(mapClearUndoState, kind)) return '';
    const snapshot = mapClearUndoState[kind];
    if (!snapshot) return '';
    if (kind === 'pins') {
      const restoredPins = (Array.isArray(snapshot.pins) ? snapshot.pins : [])
        .filter(pin => pin && Object.hasOwn(pinTypes, pin.type))
        .slice(-20)
        .map(pin => ({ ...pin }));
      if (!restoredPins.length) return '';
      const existingPinIds = new Set(savedPins.map(pin => pin.id));
      for (const pin of restoredPins) {
        if (existingPinIds.has(pin.id)) continue;
        if (savedPins.length >= 20) savedPins.shift();
        savedPins.push(pin);
      }
      if (snapshot.activePinId && !activePinId) {
        const activePin = savedPins.find(pin => pin.id === snapshot.activePinId);
        if (activePin) {
          activePinId = activePin.id;
          waypoint = {
            x: activePin.x,
            y: activePin.y,
            label: String(
              activePin.label || `${pinTypes[activePin.type]?.label || 'Saved'} marker`)
              .slice(0, 64),
            kind: normalizeWaypointKind(activePin.type)
          };
          updateWaypoint(getPlayerMarkers());
        }
      }
      persistSavedPins();
      drawSavedPins();
    } else if (kind === 'route') {
      if (routePlanArmed || routePlanActive || routeStops.length) return '';
      const stops = (Array.isArray(snapshot.stops) ? snapshot.stops : [])
        .filter(stop => Number.isFinite(Number(stop?.x)) && Number.isFinite(Number(stop?.y)))
        .slice(0, 12)
        .map(stop => ({ ...stop }));
      if (!stops.length) return '';
      routePlanArmed = Boolean(snapshot.armed);
      routePlanActive = Boolean(snapshot.active);
      routePlanComplete = Boolean(snapshot.complete);
      routePlanSource = String(snapshot.source || 'manual');
      routeStops = stops;
      routeCurrentIndex = Math.min(
        stops.length - 1,
        Math.max(0, Number(snapshot.currentIndex) || 0));
      routeAutoReplanAt = Date.now();
      if (routePlanActive) {
        setWaypointFromRouteStop();
        updateWaypoint(getPlayerMarkers());
      }
      drawRoutePlan();
    } else if (kind === 'noGo') {
      const area = normalizeNoGoArea(snapshot.area, noGoAreas.length);
      if (!area) return '';
      if (noGoAreas.length >= noGoAreaMaximumCount) return '';
      if (noGoAreas.some(existing => existing.id === area.id)) return '';
      const index = Math.min(
        noGoAreas.length,
        Math.max(0, Number(snapshot.index) || 0));
      noGoAreas.splice(index, 0, area);
      noGoSelectedAreaId = area.id;
      noGoLastStatus = 'area-restored';
      persistNoGoAreas();
      drawNoGoAreas();
      scheduleTerrainCourseForObstacleChange();
    } else if (kind === 'measurement') {
      if (measurementArmed || measurementStart || measurement) return '';
      measurementArmed = Boolean(snapshot.armed);
      measurementStart = snapshot.start ? { ...snapshot.start } : null;
      measurement = snapshot.measurement
        ? {
            start: { ...snapshot.measurement.start },
            end: { ...snapshot.measurement.end }
          }
        : null;
      drawMeasurement();
    } else {
      return '';
    }
    mapClearUndoState[kind] = null;
    if (mapClearUndoLastKind === kind) mapClearUndoLastKind = '';
    lastMessage = '';
    notify(`${kind}-clear-undone`);
    return kind;
  };

  // Route share codes: the same bounded base64 posture as pin share codes.
  const routeShareCodePrefix = 'ISLEYROUTE1.';
  const exportRouteShareCode = () => {
    const payload = routeStops
      .filter(stop => Number.isFinite(Number(stop?.x)) && Number.isFinite(Number(stop?.y)))
      .slice(0, 12)
      .map(stop => ({
        x: Math.round(Math.min(1000, Math.max(0, Number(stop.x))) * 10) / 10,
        y: Math.round(Math.min(1000, Math.max(0, Number(stop.y))) * 10) / 10,
        l: String(stop.label || '').slice(0, 64)
      }));
    if (payload.length < 2) return '';
    try {
      return routeShareCodePrefix + btoa(unescape(encodeURIComponent(JSON.stringify(payload))));
    } catch {
      return '';
    }
  };

  const importRouteShareCode = code => {
    const text = String(code || '').trim();
    if (!text.startsWith(routeShareCodePrefix) || text.length > 8192) return -1;
    let entries;
    try {
      entries = JSON.parse(decodeURIComponent(escape(atob(text.slice(routeShareCodePrefix.length)))));
    } catch {
      return -1;
    }
    if (!Array.isArray(entries) || streamerMode) return -1;
    const stops = [];
    for (const entry of entries.slice(0, 12)) {
      if (!entry || typeof entry !== 'object') continue;
      const x = Number(entry.x);
      const y = Number(entry.y);
      if (!Number.isFinite(x) || !Number.isFinite(y)) continue;
      const clampedX = Math.min(1000, Math.max(0, x));
      const clampedY = Math.min(1000, Math.max(0, y));
      const previous = stops.at(-1);
      if (previous
          && Math.abs(previous.x - clampedX) < 0.5
          && Math.abs(previous.y - clampedY) < 0.5) continue;
      stops.push({
        x: clampedX,
        y: clampedY,
        label: String(entry.l || `Route stop ${stops.length + 1}`).slice(0, 64),
        kind: ''
      });
    }
    if (stops.length < 2) return 0;
    const identical = stops.length === routeStops.length
      && stops.every((stop, index) => Math.abs(routeStops[index].x - stop.x) < 0.5
        && Math.abs(routeStops[index].y - stop.y) < 0.5);
    if (identical) return 0;
    resetRoutePlan(true);
    waypoint = null;
    waypointArmed = false;
    waypointDistance = null;
    waypointBearing = null;
    waypointCardinal = '';
    friendRouteName = '';
    packRouteActive = false;
    packOutlierRouteActive = false;
    activePinId = '';
    pinArmed = false;
    cancelMeasurementCapture();
    routePlanSource = 'shared';
    routeStops = stops;
    routeCurrentIndex = 0;
    routePlanActive = true;
    routePlanComplete = false;
    routeAutoReplanAt = Date.now();
    setWaypointFromRouteStop();
    drawRoutePlan();
    updateWaypoint(getPlayerMarkers());
    lastMessage = '';
    notify('route-share-imported');
    return stops.length;
  };

  // No-go share codes: whole avoidance areas under the same codec posture.
  const noGoShareCodePrefix = 'ISLEYNOGO1.';
  const exportNoGoShareCode = () => {
    const payload = noGoAreas
      .slice(0, noGoAreaMaximumCount)
      .map(area => ({
        l: String(area.label || '').slice(0, 64),
        p: (Array.isArray(area.points) ? area.points : [])
          .slice(0, noGoAreaMaximumVertices)
          .map(point => [
            Math.round(Math.min(1000, Math.max(0, Number(point.x))) * 10) / 10,
            Math.round(Math.min(1000, Math.max(0, Number(point.y))) * 10) / 10
          ])
      }))
      .filter(area => area.p.length >= 3);
    if (!payload.length) return '';
    try {
      return noGoShareCodePrefix + btoa(unescape(encodeURIComponent(JSON.stringify(payload))));
    } catch {
      return '';
    }
  };

  const importNoGoShareCode = code => {
    const text = String(code || '').trim();
    if (!text.startsWith(noGoShareCodePrefix) || text.length > 8192) return -1;
    let entries;
    try {
      entries = JSON.parse(decodeURIComponent(escape(atob(text.slice(noGoShareCodePrefix.length)))));
    } catch {
      return -1;
    }
    if (!Array.isArray(entries) || streamerMode) return -1;
    let added = 0;
    for (const entry of entries.slice(0, noGoAreaMaximumCount)) {
      if (noGoAreas.length >= noGoAreaMaximumCount) break;
      if (!entry || typeof entry !== 'object') continue;
      const sourcePoints = Array.isArray(entry.p) ? entry.p : [];
      if (sourcePoints.length < 3 || sourcePoints.length > noGoAreaMaximumVertices) continue;
      const points = [];
      let pointsValid = true;
      for (const sourcePoint of sourcePoints) {
        const x = Number(Array.isArray(sourcePoint) ? sourcePoint[0] : sourcePoint?.x);
        const y = Number(Array.isArray(sourcePoint) ? sourcePoint[1] : sourcePoint?.y);
        if (!Number.isFinite(x) || !Number.isFinite(y)) {
          pointsValid = false;
          break;
        }
        points.push({
          x: Math.min(1000, Math.max(0, x)),
          y: Math.min(1000, Math.max(0, y))
        });
      }
      if (!pointsValid) continue;
      const area = normalizeNoGoArea({
        label: String(entry.l || '').slice(0, 64),
        points,
        createdAt: Date.now()
      }, noGoAreas.length);
      if (!area) continue;
      const duplicate = noGoAreas.some(existing => existing.id === area.id
        || (existing.points.length === area.points.length
          && existing.points.every((point, index) =>
            Math.abs(point.x - area.points[index].x) < 0.5
            && Math.abs(point.y - area.points[index].y) < 0.5)));
      if (duplicate) continue;
      noGoAreas.push(area);
      noGoSelectedAreaId = area.id;
      added += 1;
    }
    if (added > 0) {
      noGoLastStatus = 'areas-imported';
      persistNoGoAreas();
      drawNoGoAreas();
      scheduleTerrainCourseForObstacleChange();
      lastMessage = '';
      notify('no-go-share-imported');
    }
    return added;
  };

  const sanitizePinLabel = value => String(value || '')
    .replace(/[\u0000-\u001f\u007f]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, 40);

  const buildPinLibraryBackup = (
    pins, calibration, exportedAt = Date.now(), areas = []) => {
    const exportTime = Number(exportedAt) || Date.now();
    return JSON.stringify({
      schema: 'the-isle-mapper-pins',
      version: 2,
      exportedAt: exportTime,
      pins: (Array.isArray(pins) ? pins : [])
        .filter(pin => !Number.isFinite(Number(pin?.expiresAt))
          || Number(pin.expiresAt) <= 0 || Number(pin.expiresAt) > exportTime)
        .slice(-20).map(pin => {
        const world = mapToWorldPoint(calibration, Number(pin.x), Number(pin.y));
        const expiresAt = Number(pin.expiresAt);
        return {
          type: pinTypes[pin.type] ? pin.type : 'safe',
          label: sanitizePinLabel(pin.label)
            || pinTypes[pin.type]?.label || pinTypes.safe.label,
          x: Math.min(1000, Math.max(0, Number(pin.x))),
          y: Math.min(1000, Math.max(0, Number(pin.y))),
          worldX: world?.x ?? null,
          worldY: world?.y ?? null,
          favorite: Boolean(pin.favorite),
          expiresAt: Number.isFinite(expiresAt) && expiresAt > exportTime
            ? expiresAt
            : null,
          expiryMinutes: pinExpiryMinutes.includes(Number(pin.expiryMinutes))
            ? Number(pin.expiryMinutes)
            : 0,
          alertRadius: pinAlertRadii.includes(Number(pin.alertRadius))
            ? Number(pin.alertRadius)
            : 0,
          createdAt: Number(pin.createdAt) || exportTime
        };
      }),
      noGoAreas: (Array.isArray(areas) ? areas : [])
        .slice(0, noGoAreaMaximumCount)
        .map((area, areaIndex) => ({
          id: String(area?.id || `area-${areaIndex}`).slice(0, 80),
          label: sanitizePinLabel(area?.label) || 'No-go area',
          createdAt: Number(area?.createdAt) || exportTime,
          points: (Array.isArray(area?.points) ? area.points : [])
            .slice(0, noGoAreaMaximumVertices)
            .map(point => {
              const world = mapToWorldPoint(
                calibration, Number(point?.x), Number(point?.y));
              return {
                x: Math.min(1000, Math.max(0, Number(point?.x))),
                y: Math.min(1000, Math.max(0, Number(point?.y))),
                worldX: world?.x ?? null,
                worldY: world?.y ?? null
              };
            })
        }))
    });
  };

  const parsePinLibraryBackup = (text, calibration, now = Date.now()) => {
    const input = String(text || '').trim();
    if (!input || input.length > 20000) {
      return { valid: false, error: 'Backup is empty or too large', pins: [], noGoAreas: [], expiredCount: 0 };
    }
    let backup;
    try {
      backup = JSON.parse(input);
    } catch {
      return { valid: false, error: 'Clipboard text is not an Isley backup', pins: [], noGoAreas: [], expiredCount: 0 };
    }
    if (backup?.schema !== 'the-isle-mapper-pins' || ![1, 2].includes(backup?.version)
        || !Array.isArray(backup.pins) || backup.pins.length > 20
        || (backup.version === 2
          && (!Array.isArray(backup.noGoAreas)
            || backup.noGoAreas.length > noGoAreaMaximumCount))) {
      return { valid: false, error: 'Unsupported or malformed Isley backup', pins: [], noGoAreas: [], expiredCount: 0 };
    }
    const pins = [];
    const importTime = Number(now) || Date.now();
    let expiredCount = 0;
    for (const sourcePin of backup.pins) {
      const type = String(sourcePin?.type || '').toLowerCase();
      if (!pinTypes[type]) {
        return { valid: false, error: 'Backup contains an invalid marker type', pins: [], noGoAreas: [], expiredCount: 0 };
      }
      let point = null;
      const hasWorld = sourcePin?.worldX !== null && sourcePin?.worldX !== ''
        && sourcePin?.worldY !== null && sourcePin?.worldY !== ''
        && Number.isFinite(Number(sourcePin.worldX))
        && Number.isFinite(Number(sourcePin.worldY));
      if (hasWorld) {
        point = worldToMapPoint(
          calibration, Number(sourcePin.worldX), Number(sourcePin.worldY));
      }
      if (!point) {
        const x = Number(sourcePin?.x);
        const y = Number(sourcePin?.y);
        if (Number.isFinite(x) && Number.isFinite(y)) point = { x, y };
      }
      if (!point || !Number.isFinite(Number(point.x)) || !Number.isFinite(Number(point.y))
          || Number(point.x) < -1 || Number(point.x) > 1001
          || Number(point.y) < -1 || Number(point.y) > 1001) {
        return { valid: false, error: 'Backup contains coordinates outside this map', pins: [], noGoAreas: [], expiredCount: 0 };
      }
      const label = sanitizePinLabel(sourcePin?.label) || pinTypes[type].label;
      const createdAt = Number(sourcePin?.createdAt);
      const requestedExpiresAt = Number(sourcePin?.expiresAt);
      const expiresAt = Number.isFinite(requestedExpiresAt) && requestedExpiresAt > 0
        ? requestedExpiresAt
        : 0;
      if (expiresAt > 0 && expiresAt <= importTime) {
        expiredCount += 1;
        continue;
      }
      pins.push({
        type,
        label,
        x: Math.min(1000, Math.max(0, Number(point.x))),
        y: Math.min(1000, Math.max(0, Number(point.y))),
        favorite: Boolean(sourcePin?.favorite),
        expiresAt,
        expiryMinutes: pinExpiryMinutes.includes(Number(sourcePin?.expiryMinutes))
          ? Number(sourcePin.expiryMinutes)
          : 0,
        alertRadius: pinAlertRadii.includes(Number(sourcePin?.alertRadius))
          ? Number(sourcePin.alertRadius)
          : 0,
        createdAt: Number.isFinite(createdAt) && createdAt > 0
          ? Math.min(createdAt, importTime)
          : importTime
      });
    }
    const importedAreas = [];
    for (const [areaIndex, sourceArea] of (backup.version === 2
        ? backup.noGoAreas : []).entries()) {
      const sourcePoints = Array.isArray(sourceArea?.points) ? sourceArea.points : [];
      if (sourcePoints.length < 3 || sourcePoints.length > noGoAreaMaximumVertices) {
        return {
          valid: false, error: 'Backup contains an invalid no-go boundary',
          pins: [], noGoAreas: [], expiredCount: 0
        };
      }
      const points = [];
      for (const sourcePoint of sourcePoints) {
        let point = null;
        const hasWorld = sourcePoint?.worldX !== null && sourcePoint?.worldX !== ''
          && sourcePoint?.worldY !== null && sourcePoint?.worldY !== ''
          && Number.isFinite(Number(sourcePoint.worldX))
          && Number.isFinite(Number(sourcePoint.worldY));
        if (hasWorld) {
          point = worldToMapPoint(
            calibration, Number(sourcePoint.worldX), Number(sourcePoint.worldY));
        }
        if (!point) {
          const x = Number(sourcePoint?.x);
          const y = Number(sourcePoint?.y);
          if (Number.isFinite(x) && Number.isFinite(y)) point = { x, y };
        }
        if (!point || Number(point.x) < -1 || Number(point.x) > 1001
            || Number(point.y) < -1 || Number(point.y) > 1001) {
          return {
            valid: false, error: 'Backup contains a no-go area outside this map',
            pins: [], noGoAreas: [], expiredCount: 0
          };
        }
        points.push({
          x: Math.min(1000, Math.max(0, Number(point.x))),
          y: Math.min(1000, Math.max(0, Number(point.y)))
        });
      }
      if (routePolygonArea(points) < 4 || routePolygonSelfIntersects(points)) {
        return {
          valid: false, error: 'Backup contains a crossed or undersized no-go area',
          pins: [], noGoAreas: [], expiredCount: 0
        };
      }
      importedAreas.push({
        id: String(sourceArea?.id || `area-${importTime}-${areaIndex}`)
          .replace(/[^a-z0-9_-]/gi, '').slice(0, 80),
        label: sanitizePinLabel(sourceArea?.label) || 'No-go area',
        points,
        createdAt: Math.min(Number(sourceArea?.createdAt) || importTime, importTime)
      });
    }
    return {
      valid: true,
      error: '',
      pins,
      noGoAreas: importedAreas,
      expiredCount,
      totalCount: backup.pins.length,
      totalAreaCount: importedAreas.length
    };
  };

  const buildPinLibraryImportPlan = (
    currentPins, backupText, calibration, now = Date.now(), currentAreas = []) => {
    const parsed = parsePinLibraryBackup(backupText, calibration, now);
    if (!parsed.valid) {
      return {
        valid: false,
        error: parsed.error,
        totalCount: 0,
        addedCount: 0,
        duplicateCount: 0,
        expiredCount: 0,
        trimmedCount: 0,
        totalAreaCount: 0,
        addedAreaCount: 0,
        duplicateAreaCount: 0,
        trimmedAreaCount: 0,
        resultPins: Array.isArray(currentPins) ? currentPins.slice(-20) : [],
        resultNoGoAreas: Array.isArray(currentAreas)
          ? currentAreas.slice(0, noGoAreaMaximumCount) : []
      };
    }
    const existingPins = Array.isArray(currentPins) ? currentPins.slice(-20) : [];
    const importedPins = [];
    let duplicateCount = 0;
    for (const pin of parsed.pins) {
      const duplicate = [...existingPins, ...importedPins].some(candidate =>
        candidate.type === pin.type
        && sanitizePinLabel(candidate.label).toLowerCase() === pin.label.toLowerCase()
        && Math.hypot(Number(candidate.x) - pin.x, Number(candidate.y) - pin.y) <= 0.5);
      if (duplicate) {
        duplicateCount += 1;
        continue;
      }
      importedPins.push({
        ...pin,
        id: `import-${Number(now) || Date.now()}-${importedPins.length.toString(36)}`
      });
    }
    const combined = [...existingPins, ...importedPins];
    const resultPins = combined.slice(-20);
    const existingAreas = Array.isArray(currentAreas)
      ? currentAreas.slice(0, noGoAreaMaximumCount) : [];
    const importedAreas = [];
    let duplicateAreaCount = 0;
    for (const area of parsed.noGoAreas) {
      const duplicate = [...existingAreas, ...importedAreas].some(candidate =>
        sanitizePinLabel(candidate?.label).toLowerCase() === area.label.toLowerCase()
        && Array.isArray(candidate?.points)
        && candidate.points.length === area.points.length
        && candidate.points.every((point, index) =>
          routeDistanceBetween(point, area.points[index]) <= 0.5));
      if (duplicate) {
        duplicateAreaCount += 1;
        continue;
      }
      importedAreas.push({
        ...area,
        id: `import-area-${Number(now) || Date.now()}-${importedAreas.length.toString(36)}`
      });
    }
    const acceptedAreas = importedAreas.slice(
      0, Math.max(0, noGoAreaMaximumCount - existingAreas.length));
    const combinedAreas = [...existingAreas, ...acceptedAreas];
    const resultNoGoAreas = combinedAreas.slice(0, noGoAreaMaximumCount);
    return {
      valid: true,
      error: '',
      totalCount: Number(parsed.totalCount) || parsed.pins.length,
      addedCount: importedPins.length,
      duplicateCount,
      expiredCount: Number(parsed.expiredCount) || 0,
      trimmedCount: Math.max(0, combined.length - resultPins.length),
      totalAreaCount: Number(parsed.totalAreaCount) || parsed.noGoAreas.length,
      addedAreaCount: acceptedAreas.length,
      duplicateAreaCount,
      trimmedAreaCount: importedAreas.length - acceptedAreas.length,
      resultPins,
      resultNoGoAreas
    };
  };

  const addSavedPin = (
    x,
    y,
    requestedType = pinType,
    requestedLabel = '',
    requestedExpiryMinutes = 0) => {
    const resolvedX = Math.min(1000, Math.max(0, Number(x)));
    const resolvedY = Math.min(1000, Math.max(0, Number(y)));
    const resolvedType = pinTypes[requestedType] ? requestedType : 'safe';
    const resolvedExpiryMinutes = pinExpiryMinutes.includes(Number(requestedExpiryMinutes))
      ? Number(requestedExpiryMinutes)
      : 0;
    if (!Number.isFinite(resolvedX) || !Number.isFinite(resolvedY)) return false;
    savedPins.push({
      id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      type: resolvedType,
      x: resolvedX,
      y: resolvedY,
      label: String(requestedLabel || pinTypes[resolvedType].label).slice(0, 64),
      favorite: false,
      expiresAt: resolvedExpiryMinutes > 0
        ? Date.now() + resolvedExpiryMinutes * 60000
        : 0,
      expiryMinutes: resolvedExpiryMinutes,
      alertRadius: 0,
      createdAt: Date.now()
    });
    if (savedPins.length > 20) savedPins = savedPins.slice(-20);
    persistSavedPins();
    lastMessage = '';
    return true;
  };

  const ensurePinRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    pinRoot ??= svg.querySelector(':scope > g[data-isle-mapper-saved-pins="true"]');
    if (!pinRoot) {
      pinRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      pinRoot.dataset.isleMapperSavedPins = 'true';
      pinRoot.setAttribute('pointer-events', 'none');
      pinRoot.setAttribute('aria-hidden', 'true');
      svg.appendChild(pinRoot);
    }
    return pinRoot;
  };

  const drawSavedPins = () => {
    const root = ensurePinRoot();
    if (!root) return;
    root.replaceChildren();
    root.style.display = streamerMode ? 'none' : '';
    if (streamerMode) return;
    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    for (const pin of savedPins) {
      const alertRadius = pinAlertRadii.includes(Number(pin.alertRadius))
        ? Number(pin.alertRadius)
        : 0;
      if (alertRadius <= 0) continue;
      const style = pinTypes[pin.type] || pinTypes.safe;
      const zone = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      zone.dataset.isleMapperAlertZone = pin.id;
      zone.setAttribute('cx', String(pin.x));
      zone.setAttribute('cy', String(pin.y));
      zone.setAttribute('r', String(alertRadius));
      zone.setAttribute('fill', style.color);
      zone.setAttribute('fill-opacity', activePinId === pin.id ? '0.15' : '0.09');
      zone.setAttribute('stroke', style.color);
      zone.setAttribute('stroke-opacity', activePinId === pin.id ? '0.98' : '0.84');
      zone.setAttribute('stroke-width', activePinId === pin.id ? '2.6' : '1.85');
      zone.setAttribute('stroke-dasharray', '9 4');
      zone.setAttribute('stroke-linecap', 'round');
      zone.setAttribute('vector-effect', 'non-scaling-stroke');
      root.appendChild(zone);
    }
    for (const pin of savedPins) {
      const style = pinTypes[pin.type] || pinTypes.safe;
      const marker = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      marker.dataset.isleMapperSavedPin = pin.id;
      marker.setAttribute(
        'transform',
        `translate(${pin.x} ${pin.y}) rotate(${headingUp ? selfHeading : 0}) scale(${inverseScale})`);

      const halo = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      halo.setAttribute('r', '12');
      halo.setAttribute('fill', style.color);
      halo.setAttribute('fill-opacity', '0.2');
      halo.setAttribute('stroke', style.color);
      halo.setAttribute('stroke-width', '2.25');
      if (Number(pin.expiresAt) > Date.now()) {
        halo.setAttribute('stroke-dasharray', '3 2');
      }

      const core = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      core.setAttribute('r', '7');
      core.setAttribute('fill', '#071018');
      core.setAttribute('stroke', '#f8fafc');
      core.setAttribute('stroke-width', '1.25');

      const glyph = document.createElementNS('http://www.w3.org/2000/svg', 'text');
      glyph.setAttribute('x', '0');
      glyph.setAttribute('y', '3');
      glyph.setAttribute('text-anchor', 'middle');
      glyph.setAttribute('font-size', '8');
      glyph.setAttribute('font-weight', '900');
      glyph.setAttribute('fill', style.color);
      glyph.textContent = style.short;
      marker.append(halo, core, glyph);
      if (pin.favorite) {
        const favoriteGlyph = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        favoriteGlyph.setAttribute('x', '9');
        favoriteGlyph.setAttribute('y', '-8');
        favoriteGlyph.setAttribute('text-anchor', 'middle');
        favoriteGlyph.setAttribute('font-size', '9');
        favoriteGlyph.setAttribute('font-weight', '900');
        favoriteGlyph.setAttribute('fill', '#fbbf24');
        favoriteGlyph.setAttribute('stroke', '#071018');
        favoriteGlyph.setAttribute('stroke-width', '1.8');
        favoriteGlyph.setAttribute('paint-order', 'stroke');
        favoriteGlyph.textContent = '★';
        marker.appendChild(favoriteGlyph);
      }
      root.appendChild(marker);
    }
  };

  const clampMapPoint = point => {
    const x = Math.min(1000, Math.max(0, Number(point?.x)));
    const y = Math.min(1000, Math.max(0, Number(point?.y)));
    return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
  };

  const normalizeNoGoArea = (source, fallbackIndex = 0) => {
    const points = [];
    for (const sourcePoint of (Array.isArray(source?.points) ? source.points : [])) {
      const point = clampMapPoint(sourcePoint);
      if (!point) return null;
      const previous = points.at(-1);
      if (!previous || routeDistanceBetween(previous, point) > 0.000001) points.push(point);
      if (points.length > noGoAreaMaximumVertices) return null;
    }
    if (points.length > 1 && routeDistanceBetween(points[0], points.at(-1)) <= 0.000001) {
      points.pop();
    }
    if (points.length < 3 || points.length > noGoAreaMaximumVertices
        || routePolygonArea(points) < 4 || routePolygonSelfIntersects(points)) return null;
    const createdAt = Number(source?.createdAt) > 0 ? Number(source.createdAt) : Date.now();
    const label = sanitizePinLabel(source?.label) || 'No-go area';
    const id = String(source?.id || `area-${createdAt}-${fallbackIndex.toString(36)}`)
      .replace(/[^a-z0-9_-]/gi, '').slice(0, 80);
    return { id: id || `area-${createdAt}-${fallbackIndex}`, label, points, createdAt };
  };

  const validateNoGoTrace = trace => {
    const points = Array.isArray(trace?.points) ? trace.points : [];
    if (points.length < 3) return { valid: false, status: 'add-at-least-3-points' };
    if (points.length > noGoAreaMaximumVertices) {
      return { valid: false, status: 'maximum-12-points' };
    }
    if (routePolygonSelfIntersects(points)) {
      return { valid: false, status: 'boundary-lines-cross' };
    }
    if (routePolygonArea(points) < 4) {
      return { valid: false, status: 'trace-a-larger-area' };
    }
    return { valid: true, status: 'ready' };
  };

  const persistNoGoAreas = () => {
    try {
      localStorage.setItem(noGoAreaStorageKey, JSON.stringify(noGoAreas));
      return true;
    } catch {
      noGoLastStatus = 'save-failed';
      return false;
    }
  };

  const ensureTerrainCommunityHazardRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!terrainCommunityHazardRoot?.isConnected) {
      terrainCommunityHazardRoot = svg.querySelector(
        ':scope > g[data-isley-terrain-community-hazards="true"]');
    }
    if (!terrainCommunityHazardRoot) {
      terrainCommunityHazardRoot =
        document.createElementNS('http://www.w3.org/2000/svg', 'g');
      terrainCommunityHazardRoot.dataset.isleyTerrainCommunityHazards = 'true';
      terrainCommunityHazardRoot.setAttribute('pointer-events', 'none');
      terrainCommunityHazardRoot.setAttribute('aria-hidden', 'true');
      svg.appendChild(terrainCommunityHazardRoot);
    }
    return terrainCommunityHazardRoot;
  };

  const drawTerrainCommunityHazards = () => {
    const root = ensureTerrainCommunityHazardRoot();
    if (!root) return;
    root.replaceChildren();
    root.style.display = streamerMode || !terrainCommunityHazardsEnabled
      ? 'none'
      : '';
    if (streamerMode || !terrainCommunityHazardsEnabled) return;
    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    for (const hazard of terrainCommunityHazards) {
      const circle = document.createElementNS(
        'http://www.w3.org/2000/svg', 'circle');
      circle.dataset.isleyTerrainCommunityHazard = hazard.id;
      circle.setAttribute('cx', String(hazard.x));
      circle.setAttribute('cy', String(hazard.y));
      circle.setAttribute('r', String(hazard.radius));
      circle.setAttribute('fill', '#9f1239');
      circle.setAttribute('fill-opacity', '0.22');
      circle.setAttribute('stroke', '#fecdd3');
      circle.setAttribute('stroke-opacity', '0.98');
      circle.setAttribute('stroke-width', '3');
      circle.setAttribute('stroke-dasharray', '2 4');
      circle.setAttribute('stroke-linecap', 'round');
      circle.setAttribute('vector-effect', 'non-scaling-stroke');
      root.appendChild(circle);

      const marker = document.createElementNS(
        'http://www.w3.org/2000/svg', 'g');
      marker.setAttribute(
        'transform',
        `translate(${hazard.x} ${hazard.y}) ` +
        `rotate(${headingUp ? selfHeading : 0}) scale(${inverseScale})`);
      const label = document.createElementNS(
        'http://www.w3.org/2000/svg', 'text');
      label.setAttribute('x', '0');
      label.setAttribute('y', '3.5');
      label.setAttribute('text-anchor', 'middle');
      label.setAttribute('font-size', '8');
      label.setAttribute('font-weight', '900');
      label.setAttribute('letter-spacing', '0.04em');
      label.setAttribute('fill', '#fff1f2');
      label.setAttribute('stroke', '#4c0519');
      label.setAttribute('stroke-width', '2.4');
      label.setAttribute('paint-order', 'stroke');
      label.textContent = 'DANGER';
      marker.appendChild(label);
      root.appendChild(marker);
    }
  };

  const ensureNoGoAreaRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!noGoAreaRoot?.isConnected) {
      noGoAreaRoot = svg.querySelector(':scope > g[data-isley-no-go-areas="true"]');
    }
    if (!noGoAreaRoot) {
      noGoAreaRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      noGoAreaRoot.dataset.isleyNoGoAreas = 'true';
      noGoAreaRoot.setAttribute('pointer-events', 'none');
      noGoAreaRoot.setAttribute('aria-hidden', 'true');
      svg.appendChild(noGoAreaRoot);
    }
    return noGoAreaRoot;
  };

  const drawNoGoAreas = () => {
    const root = ensureNoGoAreaRoot();
    if (!root) return;
    root.replaceChildren();
    root.style.display = streamerMode ? 'none' : '';
    if (streamerMode) return;
    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    for (const area of noGoAreas) {
      const selected = area.id === noGoSelectedAreaId;
      const highlighted = area.id === noGoHighlightAreaId;
      const polygon = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
      polygon.dataset.isleyNoGoArea = area.id;
      polygon.setAttribute('points', area.points.map(point => `${point.x},${point.y}`).join(' '));
      polygon.setAttribute('fill', highlighted ? '#b91c1c' : selected ? '#92400e' : '#78350f');
      polygon.setAttribute('fill-opacity', highlighted ? '0.26' : selected ? '0.18' : '0.14');
      polygon.setAttribute('stroke', highlighted ? '#fca5a5' : selected ? '#fbbf24' : '#d97706');
      polygon.setAttribute('stroke-opacity', highlighted ? '1' : selected ? '0.97' : '0.9');
      polygon.setAttribute('stroke-width', highlighted ? '3.2' : selected ? '2.5' : '1.95');
      polygon.setAttribute('stroke-dasharray', highlighted ? '3 2' : '10 5');
      polygon.setAttribute('stroke-linejoin', 'round');
      polygon.setAttribute('vector-effect', 'non-scaling-stroke');
      root.appendChild(polygon);
    }
    if (!noGoTrace?.points?.length) return;
    const tracePoints = noGoTrace.points;
    const trace = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
    trace.dataset.isleyNoGoTrace = 'true';
    trace.setAttribute('points', tracePoints.map(point => `${point.x},${point.y}`).join(' '));
    trace.setAttribute('fill', 'none');
    trace.setAttribute('stroke', '#fdba74');
    trace.setAttribute('stroke-width', '2.25');
    trace.setAttribute('stroke-dasharray', '5 4');
    trace.setAttribute('stroke-linecap', 'round');
    trace.setAttribute('stroke-linejoin', 'round');
    trace.setAttribute('vector-effect', 'non-scaling-stroke');
    root.appendChild(trace);
    for (const [index, point] of tracePoints.entries()) {
      const marker = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      marker.setAttribute(
        'transform',
        `translate(${point.x} ${point.y}) rotate(${headingUp ? selfHeading : 0}) scale(${inverseScale})`);
      const dot = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      dot.setAttribute('r', '7');
      dot.setAttribute('fill', '#071018');
      dot.setAttribute('stroke', '#fdba74');
      dot.setAttribute('stroke-width', '2');
      const reveal = document.createElementNS('http://www.w3.org/2000/svg', 'animate');
      reveal.setAttribute('attributeName', 'r');
      reveal.setAttribute('from', '2');
      reveal.setAttribute('to', '7');
      reveal.setAttribute('dur', '0.16s');
      reveal.setAttribute('fill', 'freeze');
      dot.appendChild(reveal);
      const label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
      label.setAttribute('x', '0');
      label.setAttribute('y', '3');
      label.setAttribute('text-anchor', 'middle');
      label.setAttribute('font-size', '8');
      label.setAttribute('font-weight', '900');
      label.setAttribute('fill', '#ffedd5');
      label.textContent = String(index + 1);
      marker.append(dot, label);
      root.appendChild(marker);
    }
  };

  const buildNoGoAreaState = () => {
    const selected = noGoAreas.find(area => area.id === noGoSelectedAreaId) || null;
    return streamerMode ? {
      noGoAreaCount: 0,
      noGoTraceActive: false,
      noGoTraceVertexCount: 0,
      noGoSelectedAreaId: '',
      noGoSelectedAreaLabel: '',
      noGoSelectedAreaVertexCount: 0,
      noGoLastStatus: 'hidden',
      noGoAreaRoster: []
    } : {
      noGoAreaCount: noGoAreas.length,
      noGoTraceActive: Boolean(noGoTrace),
      noGoTraceVertexCount: noGoTrace?.points?.length || 0,
      noGoSelectedAreaId: selected?.id || '',
      noGoSelectedAreaLabel: selected?.label || '',
      noGoSelectedAreaVertexCount: selected?.points?.length || 0,
      noGoLastStatus,
      noGoAreaRoster: noGoAreas.map(area => ({
        id: area.id, label: area.label, vertexCount: area.points.length
      }))
    };
  };

  const scheduleTerrainCourseForObstacleChange = () => {
    if (routePlanSource !== 'terrain' || !terrainCourseDestination || !routePlanActive) return;
    window.setTimeout(() => startTerrainCourseInternal(
      terrainCourseDestination, 'terrain-course-obstacles-updated'), 0);
  };

  const beginNoGoTrace = requestedLabel => {
    if (streamerMode || noGoAreas.length >= noGoAreaMaximumCount) {
      noGoLastStatus = streamerMode ? 'hidden' : 'maximum-8-areas';
      notify('no-go-trace-refused');
      return false;
    }
    noGoTrace = {
      label: sanitizePinLabel(requestedLabel) || `No-go area ${noGoAreas.length + 1}`,
      points: []
    };
    routePlanArmed = false;
    waypointArmed = false;
    pinArmed = false;
    cancelMeasurementCapture();
    closeMapQuickActions();
    noGoLastStatus = 'click-map-boundary';
    drawNoGoAreas();
    lastMessage = '';
    notify('no-go-trace-started');
    return true;
  };

  const addNoGoTracePoint = point => {
    if (!noGoTrace || streamerMode) return false;
    const cleanPoint = clampMapPoint(point);
    if (!cleanPoint) return false;
    if (noGoTrace.points.length >= noGoAreaMaximumVertices) {
      noGoLastStatus = 'maximum-12-points';
      notify('no-go-trace-full');
      return false;
    }
    const previous = noGoTrace.points.at(-1);
    if (previous && routeDistanceBetween(previous, cleanPoint) < 1) {
      noGoLastStatus = 'move-farther-for-next-point';
      notify('no-go-trace-point-too-close');
      return false;
    }
    noGoTrace.points.push(cleanPoint);
    noGoLastStatus = noGoTrace.points.length < 3
      ? `add-${3 - noGoTrace.points.length}-more-point${noGoTrace.points.length === 2 ? '' : 's'}`
      : 'ready-to-finish';
    drawNoGoAreas();
    lastMessage = '';
    notify('no-go-trace-point-added');
    return true;
  };

  const undoNoGoTracePoint = () => {
    if (!noGoTrace?.points?.length || streamerMode) return false;
    noGoTrace.points.pop();
    noGoLastStatus = noGoTrace.points.length >= 3 ? 'ready-to-finish' : 'click-map-boundary';
    drawNoGoAreas();
    lastMessage = '';
    notify('no-go-trace-point-undone');
    return true;
  };

  const cancelNoGoTrace = () => {
    if (!noGoTrace) return false;
    noGoTrace = null;
    noGoLastStatus = 'trace-cancelled';
    drawNoGoAreas();
    lastMessage = '';
    notify('no-go-trace-cancelled');
    return true;
  };

  const finishNoGoTrace = () => {
    if (!noGoTrace || streamerMode) return false;
    const validation = validateNoGoTrace(noGoTrace);
    if (!validation.valid) {
      noGoLastStatus = validation.status;
      drawNoGoAreas();
      notify('no-go-trace-invalid');
      return false;
    }
    const createdAt = Date.now();
    const area = normalizeNoGoArea({
      id: `area-${createdAt}-${Math.random().toString(36).slice(2, 7)}`,
      label: noGoTrace.label,
      points: noGoTrace.points,
      createdAt
    }, noGoAreas.length);
    if (!area) {
      noGoLastStatus = 'invalid-boundary';
      notify('no-go-trace-invalid');
      return false;
    }
    noGoAreas.push(area);
    noGoSelectedAreaId = area.id;
    noGoTrace = null;
    noGoLastStatus = 'area-saved';
    persistNoGoAreas();
    drawNoGoAreas();
    scheduleTerrainCourseForObstacleChange();
    lastMessage = '';
    notify('no-go-area-saved');
    return true;
  };

  const reportBlockedTerrainPassage = () => {
    if (streamerMode) {
      notify('terrain-passage-hidden');
      return { ok: false, reason: 'STREAMER_MODE' };
    }
    if (routePlanSource !== 'terrain' || !routePlanActive
        || !terrainCourseDestination || routeStops.length < 2) {
      notify('terrain-passage-no-course');
      return { ok: false, reason: 'NO_ACTIVE_COURSE' };
    }
    if (noGoAreas.length >= noGoAreaMaximumCount) {
      noGoLastStatus = 'maximum-8-areas';
      notify('terrain-passage-area-limit');
      return { ok: false, reason: 'AREA_LIMIT' };
    }

    const players = getPlayerMarkers();
    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = markerAvailable
      ? (selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose())
      : null;
    if (!selfPose) {
      notify('terrain-passage-waiting-position');
      return { ok: false, reason: 'NO_SELF_POSITION' };
    }
    const target = routeStops
      .slice(Math.max(0, routeCurrentIndex))
      .find(stop => routeDistanceBetween(selfPose, stop) >= 24);
    if (!target) {
      notify('terrain-passage-too-close');
      return { ok: false, reason: 'PASSAGE_TOO_CLOSE' };
    }

    const built = buildBlockedPassageArea(
      selfPose,
      target,
      noGoAreas.length,
      Date.now(),
      noGoAreaMaximumCount);
    if (!built.ok || !built.area) {
      notify(`terrain-passage-${String(built.reason || 'unavailable').toLowerCase()}`);
      return built;
    }
    const blockedCount = noGoAreas.filter(area =>
      String(area?.label || '').startsWith('Blocked passage')).length + 1;
    const area = normalizeNoGoArea({
      ...built.area,
      label: `Blocked passage ${blockedCount}`
    }, noGoAreas.length);
    if (!area) {
      notify('terrain-passage-invalid-area');
      return { ok: false, reason: 'INVALID_AREA' };
    }

    noGoAreas.push(area);
    noGoSelectedAreaId = area.id;
    noGoLastStatus = 'blocked-passage-saved';
    persistNoGoAreas();
    drawNoGoAreas();
    terrainCourseStatus = 'rerouting';
    lastMessage = '';
    notify('terrain-passage-reported');
    const destination = { ...terrainCourseDestination };
    window.setTimeout(() => startTerrainCourseInternal(
      destination, 'terrain-course-passage-rerouted'), 0);
    return {
      ok: true,
      reason: '',
      areaId: area.id,
      label: area.label,
      distanceAhead: built.distanceAhead,
      width: built.width
    };
  };

  const saveMeasuredSlopeAvoidance = (
    worldStartX,
    worldStartY,
    worldEndX,
    worldEndY,
    requestedLabel
  ) => {
    if (streamerMode) {
      notify('measured-slope-hidden');
      return { ok: false, reason: 'STREAMER_MODE' };
    }
    if (noGoAreas.length >= noGoAreaMaximumCount) {
      noGoLastStatus = 'maximum-8-areas';
      notify('measured-slope-area-limit');
      return { ok: false, reason: 'AREA_LIMIT' };
    }
    const calibration = findReactMapProps()?.calibration;
    const start = worldToMapPoint(
      calibration, Number(worldStartX), Number(worldStartY));
    const end = worldToMapPoint(
      calibration, Number(worldEndX), Number(worldEndY));
    if (!start || !end) {
      notify('measured-slope-no-calibration');
      return { ok: false, reason: 'NO_CALIBRATION' };
    }
    const built = buildMeasuredSlopeArea(
      start,
      end,
      noGoAreas.length,
      requestedLabel,
      Date.now(),
      noGoAreaMaximumCount);
    if (!built.ok || !built.area) {
      notify(`measured-slope-${String(built.reason || 'unavailable').toLowerCase()}`);
      return built;
    }
    const area = normalizeNoGoArea(built.area, noGoAreas.length);
    if (!area) {
      notify('measured-slope-invalid-area');
      return { ok: false, reason: 'INVALID_AREA' };
    }

    noGoAreas.push(area);
    noGoSelectedAreaId = area.id;
    noGoLastStatus = 'measured-slope-saved';
    persistNoGoAreas();
    drawNoGoAreas();
    scheduleTerrainCourseForObstacleChange();
    lastMessage = '';
    notify('measured-slope-saved');
    return {
      ok: true,
      reason: '',
      areaId: area.id,
      label: area.label,
      mappedDistance: built.mappedDistance,
      width: built.width
    };
  };

  const selectNoGoArea = requestedId => {
    const id = String(requestedId || '');
    const selected = noGoAreas.find(area => area.id === id);
    if (!selected || streamerMode) return false;
    noGoSelectedAreaId = selected.id;
    noGoLastStatus = 'area-selected';
    drawNoGoAreas();
    lastMessage = '';
    notify('no-go-area-selected');
    return true;
  };

  const cycleNoGoArea = direction => {
    if (!noGoAreas.length || streamerMode) return false;
    const currentIndex = Math.max(0, noGoAreas.findIndex(area =>
      area.id === noGoSelectedAreaId));
    const nextIndex = (currentIndex + (Number(direction) < 0 ? -1 : 1)
      + noGoAreas.length) % noGoAreas.length;
    return selectNoGoArea(noGoAreas[nextIndex].id);
  };

  const removeNoGoArea = requestedId => {
    const id = String(requestedId || noGoSelectedAreaId || '');
    const index = noGoAreas.findIndex(area => area.id === id);
    if (index < 0 || streamerMode) return false;
    const removed = noGoAreas[index];
    snapshotMapClear('noGo', {
      area: {
        ...removed,
        points: removed.points.map(point => ({ ...point }))
      },
      index
    });
    noGoAreas.splice(index, 1);
    noGoSelectedAreaId = noGoAreas[Math.min(index, noGoAreas.length - 1)]?.id || '';
    noGoLastStatus = 'area-removed';
    persistNoGoAreas();
    drawNoGoAreas();
    scheduleTerrainCourseForObstacleChange();
    lastMessage = '';
    notify('no-go-area-removed');
    return true;
  };

  const highlightNoGoArea = requestedId => {
    const id = String(requestedId || '');
    if (!id || !noGoAreas.some(area => area.id === id)) return false;
    noGoHighlightAreaId = id;
    noGoSelectedAreaId = id;
    drawNoGoAreas();
    if (noGoHighlightTimer) clearTimeout(noGoHighlightTimer);
    noGoHighlightTimer = window.setTimeout(() => {
      noGoHighlightTimer = 0;
      noGoHighlightAreaId = '';
      drawNoGoAreas();
    }, 900);
    return true;
  };

  try {
    const storedNoGoAreas = JSON.parse(localStorage.getItem(noGoAreaStorageKey) || '[]');
    noGoAreas = (Array.isArray(storedNoGoAreas) ? storedNoGoAreas : [])
      .slice(0, noGoAreaMaximumCount)
      .map(normalizeNoGoArea)
      .filter(Boolean);
    noGoSelectedAreaId = noGoAreas[0]?.id || '';
    persistNoGoAreas();
  } catch {
    noGoAreas = [];
    noGoSelectedAreaId = '';
    noGoLastStatus = 'storage-reset';
  }

  const cancelMeasurementCapture = () => {
    measurementArmed = false;
    if (!measurement) measurementStart = null;
  };

  const ensureMeasurementRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!measurementRoot?.isConnected) {
      measurementRoot = svg.querySelector(':scope > g[data-isle-mapper-measurement="true"]');
    }
    if (!measurementRoot) {
      measurementRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      measurementRoot.dataset.isleMapperMeasurement = 'true';
      measurementRoot.setAttribute('pointer-events', 'none');
      measurementRoot.setAttribute('aria-hidden', 'true');
      svg.appendChild(measurementRoot);
    }
    return measurementRoot;
  };

  const drawMeasurementEndpoint = (root, point, label, inverseScale) => {
    const marker = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    marker.setAttribute(
      'transform',
      `translate(${point.x} ${point.y}) rotate(${headingUp ? selfHeading : 0}) scale(${inverseScale})`);

    const halo = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    halo.setAttribute('r', '10');
    halo.setAttribute('fill', '#22d3ee');
    halo.setAttribute('fill-opacity', '0.18');
    halo.setAttribute('stroke', '#67e8f9');
    halo.setAttribute('stroke-width', '2');

    const core = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    core.setAttribute('r', '6');
    core.setAttribute('fill', '#071018');
    core.setAttribute('stroke', '#f8fafc');
    core.setAttribute('stroke-width', '1.25');

    const glyph = document.createElementNS('http://www.w3.org/2000/svg', 'text');
    glyph.setAttribute('x', '0');
    glyph.setAttribute('y', '3');
    glyph.setAttribute('text-anchor', 'middle');
    glyph.setAttribute('font-size', '8');
    glyph.setAttribute('font-weight', '900');
    glyph.setAttribute('fill', '#67e8f9');
    glyph.textContent = label;
    marker.append(halo, core, glyph);
    root.appendChild(marker);
  };

  const drawMeasurement = () => {
    const root = ensureMeasurementRoot();
    if (!root) return;
    root.replaceChildren();
    const start = measurement?.start ?? measurementStart;
    const end = measurement?.end ?? null;
    root.style.display = streamerMode || !start ? 'none' : '';
    if (streamerMode || !start) return;

    if (end) {
      const underlay = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      underlay.setAttribute('x1', String(start.x));
      underlay.setAttribute('y1', String(start.y));
      underlay.setAttribute('x2', String(end.x));
      underlay.setAttribute('y2', String(end.y));
      underlay.setAttribute('stroke', '#03131b');
      underlay.setAttribute('stroke-width', '5');
      underlay.setAttribute('stroke-linecap', 'round');
      underlay.setAttribute('vector-effect', 'non-scaling-stroke');
      underlay.setAttribute('opacity', '0.78');

      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      line.setAttribute('x1', String(start.x));
      line.setAttribute('y1', String(start.y));
      line.setAttribute('x2', String(end.x));
      line.setAttribute('y2', String(end.y));
      line.setAttribute('stroke', '#22d3ee');
      line.setAttribute('stroke-width', '2.25');
      line.setAttribute('stroke-dasharray', '7 5');
      line.setAttribute('stroke-linecap', 'round');
      line.setAttribute('vector-effect', 'non-scaling-stroke');
      root.append(underlay, line);
    }

    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    drawMeasurementEndpoint(root, start, 'A', inverseScale);
    if (end) drawMeasurementEndpoint(root, end, 'B', inverseScale);
  };

  const ensureRoutePlanRoot = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    if (!routePlanRoot?.isConnected) {
      routePlanRoot = svg.querySelector(':scope > g[data-isle-mapper-route-plan="true"]');
    }
    if (!routePlanRoot) {
      routePlanRoot = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      routePlanRoot.dataset.isleMapperRoutePlan = 'true';
      routePlanRoot.setAttribute('pointer-events', 'none');
      routePlanRoot.setAttribute('aria-hidden', 'true');
      svg.appendChild(routePlanRoot);
    }
    return routePlanRoot;
  };

  const drawRouteStop = (root, stop, index, inverseScale) => {
    const completed = routePlanComplete || index < routeCurrentIndex;
    const current = !routePlanArmed && index === routeCurrentIndex;
    const terrainCourse = routePlanSource === 'terrain';
    const color = routePlanComplete
      ? '#34d399'
      : current ? '#ff6847' : completed ? '#64748b' : terrainCourse ? '#2dd4bf' : '#22d3ee';
    const marker = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    marker.setAttribute(
      'transform',
      `translate(${stop.x} ${stop.y}) rotate(${headingUp ? selfHeading : 0}) scale(${inverseScale})`);

    const halo = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    halo.setAttribute('r', current ? '12' : terrainCourse ? '6' : '10');
    halo.setAttribute('fill', color);
    halo.setAttribute('fill-opacity', current ? '0.28' : terrainCourse ? '0.24' : '0.18');
    halo.setAttribute('stroke', color);
    halo.setAttribute('stroke-width', current ? '2.5' : '2');

    const core = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    core.setAttribute('r', terrainCourse && !current ? '2.6' : '6.5');
    core.setAttribute('fill', '#071018');
    core.setAttribute('stroke', '#f8fafc');
    core.setAttribute('stroke-width', terrainCourse && !current ? '0.9' : '1.25');

    const glyph = document.createElementNS('http://www.w3.org/2000/svg', 'text');
    glyph.setAttribute('x', '0');
    glyph.setAttribute('y', '3');
    glyph.setAttribute('text-anchor', 'middle');
    glyph.setAttribute('font-size', index >= 9 ? '7' : '8');
    glyph.setAttribute('font-weight', '900');
    glyph.setAttribute('fill', color);
    glyph.textContent = terrainCourse
      ? index === routeStops.length - 1 ? 'GO' : ''
      : String(index + 1);
    marker.append(halo, core);
    if (glyph.textContent) marker.appendChild(glyph);
    root.appendChild(marker);
  };

  const drawTypedTerrainCourse = root => {
    if (!terrainRouteEvidenceVisible || !terrainCourseSegments.length
        || routePlanSource !== 'terrain') return false;
    const validSegments = terrainCourseSegments.filter(segment =>
      ['road', 'trail', 'learned', 'connector', 'endpoint'].includes(segment.kind)
      && [segment.x1, segment.y1, segment.x2, segment.y2]
        .every(Number.isFinite));
    if (!validSegments.length) return false;

    const pathData = segments => segments
      .map(segment =>
        `M ${segment.x1} ${segment.y1} L ${segment.x2} ${segment.y2}`)
      .join(' ');
    const underlay = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    underlay.setAttribute('d', pathData(validSegments));
    underlay.setAttribute('fill', 'none');
    underlay.setAttribute('stroke', '#03131b');
    underlay.setAttribute('stroke-width', '5.6');
    underlay.setAttribute('stroke-linecap', 'round');
    underlay.setAttribute('stroke-linejoin', 'round');
    underlay.setAttribute('vector-effect', 'non-scaling-stroke');
    underlay.setAttribute('opacity', '0.78');
    root.appendChild(underlay);

    const styles = [
      { kinds: ['road'], color: '#2dd4bf', width: '2.8', dash: '' },
      { kinds: ['trail'], color: '#60a5fa', width: '2.7', dash: '8 4' },
      { kinds: ['learned'], color: '#c084fc', width: '2.9', dash: '10 4 2 4' },
      {
        kinds: ['connector', 'endpoint'],
        color: '#f59e0b',
        width: '3.1',
        dash: '3 4'
      }
    ];
    for (const style of styles) {
      const segments = validSegments.filter(segment =>
        style.kinds.includes(segment.kind));
      if (!segments.length) continue;
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      path.setAttribute('d', pathData(segments));
      path.setAttribute('fill', 'none');
      path.setAttribute('stroke', routePlanComplete ? '#34d399' : style.color);
      path.setAttribute('stroke-width', style.width);
      if (!routePlanComplete && style.dash) {
        path.setAttribute('stroke-dasharray', style.dash);
      }
      path.setAttribute('stroke-linecap', 'round');
      path.setAttribute('stroke-linejoin', 'round');
      path.setAttribute('vector-effect', 'non-scaling-stroke');
      path.setAttribute('opacity', routePlanComplete ? '0.7' : '0.96');
      root.appendChild(path);
    }
    return true;
  };

  const drawRoutePlan = () => {
    const root = ensureRoutePlanRoot();
    if (!root) return;
    root.replaceChildren();
    root.style.display = streamerMode || !routeStops.length ? 'none' : '';
    if (streamerMode || !routeStops.length) return;

    const typedTerrainDrawn = drawTypedTerrainCourse(root);
    if (!typedTerrainDrawn) {
      for (let index = 1; index < routeStops.length; index += 1) {
        const start = routeStops[index - 1];
        const end = routeStops[index];
        const completed = routePlanComplete || index < routeCurrentIndex;
        const terrainCourse = routePlanSource === 'terrain';
        const color = routePlanComplete
          ? '#34d399'
          : completed ? '#64748b' : terrainCourse ? '#2dd4bf' : '#22d3ee';
        const underlay = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        underlay.setAttribute('x1', String(start.x));
        underlay.setAttribute('y1', String(start.y));
        underlay.setAttribute('x2', String(end.x));
        underlay.setAttribute('y2', String(end.y));
        underlay.setAttribute('stroke', '#03131b');
        underlay.setAttribute('stroke-width', '5');
        underlay.setAttribute('stroke-linecap', 'round');
        underlay.setAttribute('vector-effect', 'non-scaling-stroke');
        underlay.setAttribute('opacity', '0.72');

        const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        line.setAttribute('x1', String(start.x));
        line.setAttribute('y1', String(start.y));
        line.setAttribute('x2', String(end.x));
        line.setAttribute('y2', String(end.y));
        line.setAttribute('stroke', color);
        line.setAttribute(
          'stroke-width',
          completed ? '1.75' : terrainCourse ? '2.6' : '2.25');
        line.setAttribute(
          'stroke-dasharray',
          completed ? '4 6' : terrainCourse ? 'none' : '8 5');
        line.setAttribute('stroke-linecap', 'round');
        line.setAttribute('vector-effect', 'non-scaling-stroke');
        line.setAttribute('opacity', completed ? '0.58' : '0.9');
        root.append(underlay, line);
      }
    }

    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    routeStops.forEach((stop, index) => drawRouteStop(root, stop, index, inverseScale));
  };

  const clearRouteAdvanceTimer = () => {
    if (routeAdvanceTimer) clearTimeout(routeAdvanceTimer);
    routeAdvanceTimer = 0;
  };

  const resetRoutePlan = (clearOwnedWaypoint = true) => {
    const ownedWaypoint = routePlanActive || routePlanComplete;
    clearRouteAdvanceTimer();
    if (routeAutoReplanTimer) {
      clearTimeout(routeAutoReplanTimer);
      routeAutoReplanTimer = 0;
    }
    routePlanArmed = false;
    routePlanActive = false;
    routePlanComplete = false;
    routePlanSource = '';
    routeStops = [];
    routeCurrentIndex = 0;
    clearTerrainCourseState('ready');
    if (clearOwnedWaypoint && ownedWaypoint) {
      waypoint = null;
      waypointDistance = null;
      waypointBearing = null;
      waypointCardinal = '';
    }
    drawRoutePlan();
  };

  const setWaypointFromRouteStop = () => {
    const stop = routeStops[routeCurrentIndex];
    if (!stop) return false;
    friendRouteName = '';
    packRouteActive = false;
    packOutlierRouteActive = false;
    activePinId = '';
    waypointArmed = false;
    waypoint = {
      x: stop.x,
      y: stop.y,
      label: routePlanSource === 'breadcrumb'
        ? `Breadcrumb stop ${routeCurrentIndex + 1} of ${routeStops.length}`
        : routePlanSource === 'terrain'
          ? routeCurrentIndex === routeStops.length - 1
            ? `${terrainCourseDestination?.label || 'Destination'} · road/trail course`
            : `Road/trail bend ${routeCurrentIndex + 1} of ${routeStops.length - 1}`
        : routePlanSource === 'shared' && stop.label
          ? `${stop.label} · ${routeCurrentIndex + 1}/${routeStops.length}`
        : `Route stop ${routeCurrentIndex + 1} of ${routeStops.length}`,
      kind: normalizeWaypointKind(stop.kind)
    };
    return true;
  };

  const advanceRouteStopInternal = reason => {
    if (!routePlanActive || !routeStops.length) return false;
    clearRouteAdvanceTimer();
    if (routeCurrentIndex + 1 < routeStops.length) {
      routeCurrentIndex += 1;
      setWaypointFromRouteStop();
      drawRoutePlan();
      updateWaypoint(getPlayerMarkers());
      lastMessage = '';
      notify(reason || 'route-stop-advanced');
      return true;
    }

    routePlanActive = false;
    routePlanComplete = true;
    drawRoutePlan();
    lastMessage = '';
    notify(reason || 'route-complete');
    return true;
  };

  const summarizeEncounterMemory = (
    entries,
    liveNames = [],
    retentionSeconds = 300,
    now = Date.now(),
    selfPose = null
  ) => {
    const retentionMs = Math.max(0, Number(retentionSeconds) || 0) * 1000;
    const liveSet = new Set((Array.isArray(liveNames) ? liveNames : [])
      .map(name => String(name || '')));
    const currentTime = Number.isFinite(Number(now)) ? Number(now) : Date.now();
    const selfX = Number(selfPose?.x);
    const selfY = Number(selfPose?.y);
    const hasSelf = Number.isFinite(selfX) && Number.isFinite(selfY);
    let trackCount = 0;
    let rememberedCount = 0;
    let newestAgeMs = null;
    let nearestDistance = null;
    let nearestBearing = null;
    let nearestCardinal = '';
    const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];

    for (const entry of Array.isArray(entries) ? entries : []) {
      const name = String(entry?.name || '').slice(0, 64);
      const lastSeenAt = Number(entry?.lastSeenAt);
      const samples = Array.isArray(entry?.samples) ? entry.samples : [];
      const last = samples.at(-1);
      const x = Number(last?.x);
      const y = Number(last?.y);
      if (!name || !Number.isFinite(lastSeenAt)
          || !Number.isFinite(x) || !Number.isFinite(y)) continue;
      const ageMs = Math.max(0, currentTime - lastSeenAt);
      if (retentionMs <= 0 || ageMs > retentionMs) continue;
      trackCount += 1;
      if (liveSet.has(name)) continue;
      rememberedCount += 1;
      newestAgeMs = newestAgeMs == null ? ageMs : Math.min(newestAgeMs, ageMs);
      if (!hasSelf) continue;
      const dx = x - selfX;
      const dy = y - selfY;
      const distance = Math.hypot(dx, dy);
      if (nearestDistance == null || distance < nearestDistance) {
        nearestDistance = distance;
        nearestBearing = (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        nearestCardinal = cardinals[Math.round(nearestBearing / 45) % 8];
      }
    }

    return {
      trackCount,
      rememberedCount,
      newestAgeMs,
      nearestDistance,
      nearestBearing,
      nearestCardinal
    };
  };

  const calculateEncounterMotion = (samples, now = Date.now()) => {
    const currentTime = Number.isFinite(Number(now)) ? Number(now) : Date.now();
    const cleaned = (Array.isArray(samples) ? samples : [])
      .map(sample => ({
        at: Number(sample?.at),
        distance: Number(sample?.distance)
      }))
      .filter(sample => Number.isFinite(sample.at)
        && Number.isFinite(sample.distance)
        && sample.distance >= 0
        && sample.at <= currentTime + 1000
        && currentTime - sample.at <= 45000)
      .sort((left, right) => left.at - right.at)
      .filter((sample, index, list) => index === 0 || sample.at > list[index - 1].at)
      .slice(-8);
    const waiting = {
      state: '',
      relativeSpeed: null,
      interceptSeconds: null,
      sampleCount: cleaned.length,
      spanSeconds: cleaned.length >= 2
        ? Math.max(0, (cleaned.at(-1).at - cleaned[0].at) / 1000)
        : 0
    };
    if (cleaned.length < 3 || waiting.spanSeconds < 3) return waiting;

    const rates = [];
    for (let index = 1; index < cleaned.length; index += 1) {
      const elapsedSeconds = (cleaned[index].at - cleaned[index - 1].at) / 1000;
      if (elapsedSeconds < 0.75 || elapsedSeconds > 20) continue;
      const rate = (cleaned[index - 1].distance - cleaned[index].distance)
        / elapsedSeconds * 60;
      if (Number.isFinite(rate) && Math.abs(rate) <= 1200) rates.push(rate);
    }
    if (rates.length < 2) return waiting;

    rates.sort((left, right) => left - right);
    const middle = Math.floor(rates.length / 2);
    const medianRate = rates.length % 2
      ? rates[middle]
      : (rates[middle - 1] + rates[middle]) / 2;
    const stabilizedRate = Math.abs(medianRate) < 1.5 ? 0 : medianRate;
    const state = stabilizedRate > 0 ? 'closing' : stabilizedRate < 0 ? 'opening' : 'steady';
    const currentDistance = cleaned.at(-1).distance;
    const interceptSeconds = state === 'closing' && stabilizedRate >= 2
      ? currentDistance / stabilizedRate * 60
      : null;
    return {
      state,
      relativeSpeed: stabilizedRate,
      interceptSeconds: Number.isFinite(interceptSeconds) && interceptSeconds <= 900
        ? Math.max(0, interceptSeconds)
        : null,
      sampleCount: cleaned.length,
      spanSeconds: waiting.spanSeconds
    };
  };

  const calculateEncounterAwareness = (playerPoints, selfPose = null) => {
    const points = (Array.isArray(playerPoints) ? playerPoints : [])
      .map(point => ({
        x: Number(point?.x),
        y: Number(point?.y),
        motion: point?.motion ?? null
      }))
      .filter(point => Number.isFinite(point.x) && Number.isFinite(point.y));
    const selfX = Number(selfPose?.x);
    const selfY = Number(selfPose?.y);
    if (!Number.isFinite(selfX) || !Number.isFinite(selfY)) {
      return {
        trackableCount: points.length,
        nearestDistance: null,
        nearestBearing: null,
        nearestCardinal: '',
        nearestMotion: null,
        within10: 0,
        within25: 0,
        within50: 0
      };
    }

    let nearestDistance = null;
    let nearestBearing = null;
    let nearestCardinal = '';
    let nearestMotion = null;
    let within10 = 0;
    let within25 = 0;
    let within50 = 0;
    const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
    for (const point of points) {
      const dx = point.x - selfX;
      const dy = point.y - selfY;
      const distance = Math.hypot(dx, dy);
      if (distance <= 10) within10 += 1;
      if (distance <= 25) within25 += 1;
      if (distance <= 50) within50 += 1;
      if (nearestDistance == null || distance < nearestDistance) {
        nearestDistance = distance;
        nearestBearing = (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        nearestCardinal = cardinals[Math.round(nearestBearing / 45) % 8];
        nearestMotion = point.motion;
      }
    }

    return {
      trackableCount: points.length,
      nearestDistance,
      nearestBearing,
      nearestCardinal,
      nearestMotion,
      within10,
      within25,
      within50
    };
  };

  const resetPackSpreadMotion = (clearRoster = true) => {
    packSpreadMotion = '';
    packSpreadRate = null;
    packSpreadMotionSampleCount = 0;
    packCourseState = '';
    packCourseSpeed = null;
    packCourseBearing = null;
    packCourseCardinal = '';
    packCourseSampleCount = 0;
    packSpreadMotionSamples = [];
    if (clearRoster) packSpreadMotionRosterKey = '';
  };

  const calculatePackSpreadMotion = (samples, now = Date.now()) => {
    const observedAt = Number(now);
    const sanitized = (Array.isArray(samples) ? samples : [])
      .map(sample => ({
        at: Number(sample?.at),
        spread: Number(sample?.spread)
      }))
      .filter(sample => Number.isFinite(sample.at)
        && Number.isFinite(sample.spread)
        && sample.spread >= 0
        && sample.at <= observedAt + 1000
        && observedAt - sample.at <= 45000)
      .sort((left, right) => left.at - right.at)
      .filter((sample, index, all) => index === 0 || sample.at > all[index - 1].at)
      .slice(-8);
    const spanSeconds = sanitized.length >= 2
      ? (sanitized.at(-1).at - sanitized[0].at) / 1000
      : 0;
    if (sanitized.length < 3 || spanSeconds < 3) {
      return { state: '', rate: null, sampleCount: sanitized.length, spanSeconds };
    }

    const intervalRates = [];
    for (let index = 1; index < sanitized.length; index += 1) {
      const elapsedSeconds = (sanitized[index].at - sanitized[index - 1].at) / 1000;
      if (elapsedSeconds < 0.75 || elapsedSeconds > 20) continue;
      const rate = (sanitized[index].spread - sanitized[index - 1].spread)
        / elapsedSeconds * 60;
      if (Number.isFinite(rate) && Math.abs(rate) <= 1200) intervalRates.push(rate);
    }
    if (intervalRates.length < 2) {
      return { state: '', rate: null, sampleCount: sanitized.length, spanSeconds };
    }

    intervalRates.sort((left, right) => left - right);
    const middle = Math.floor(intervalRates.length / 2);
    let rate = intervalRates.length % 2
      ? intervalRates[middle]
      : (intervalRates[middle - 1] + intervalRates[middle]) / 2;
    if (Math.abs(rate) < 1.5) rate = 0;
    return {
      state: rate > 0 ? 'spreading' : rate < 0 ? 'regrouping' : 'steady',
      rate,
      sampleCount: sanitized.length,
      spanSeconds
    };
  };

  const calculatePackCourse = (samples, now = Date.now()) => {
    const observedAt = Number(now);
    const sanitized = (Array.isArray(samples) ? samples : [])
      .map(sample => ({
        at: Number(sample?.at),
        x: Number(sample?.centerX),
        y: Number(sample?.centerY)
      }))
      .filter(sample => Number.isFinite(sample.at)
        && Number.isFinite(sample.x)
        && Number.isFinite(sample.y)
        && sample.at <= observedAt + 1000
        && observedAt - sample.at <= 45000)
      .sort((left, right) => left.at - right.at)
      .filter((sample, index, all) => index === 0 || sample.at > all[index - 1].at)
      .slice(-8);
    const spanSeconds = sanitized.length >= 2
      ? (sanitized.at(-1).at - sanitized[0].at) / 1000
      : 0;
    if (sanitized.length < 3 || spanSeconds < 3) {
      return {
        state: '', speed: null, bearing: null, cardinal: '',
        sampleCount: sanitized.length, spanSeconds
      };
    }

    const intervalVectors = [];
    for (let index = 1; index < sanitized.length; index += 1) {
      const elapsedSeconds = (sanitized[index].at - sanitized[index - 1].at) / 1000;
      if (elapsedSeconds < 0.75 || elapsedSeconds > 20) continue;
      const x = (sanitized[index].x - sanitized[index - 1].x) / elapsedSeconds * 60;
      const y = (sanitized[index].y - sanitized[index - 1].y) / elapsedSeconds * 60;
      const speed = Math.hypot(x, y);
      if (Number.isFinite(x) && Number.isFinite(y) && speed <= 1200) {
        intervalVectors.push({ x, y });
      }
    }
    if (intervalVectors.length < 2) {
      return {
        state: '', speed: null, bearing: null, cardinal: '',
        sampleCount: sanitized.length, spanSeconds
      };
    }

    const median = values => {
      const sorted = [...values].sort((left, right) => left - right);
      const middle = Math.floor(sorted.length / 2);
      return sorted.length % 2
        ? sorted[middle]
        : (sorted[middle - 1] + sorted[middle]) / 2;
    };
    const x = median(intervalVectors.map(vector => vector.x));
    const y = median(intervalVectors.map(vector => vector.y));
    const speed = Math.hypot(x, y);
    if (speed < 1.5) {
      return {
        state: 'stationary', speed: 0, bearing: null, cardinal: '',
        sampleCount: sanitized.length, spanSeconds
      };
    }
    const bearing = (Math.atan2(x, -y) * 180 / Math.PI + 360) % 360;
    const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
    return {
      state: 'moving',
      speed,
      bearing,
      cardinal: cardinals[Math.round(bearing / 45) % 8],
      sampleCount: sanitized.length,
      spanSeconds
    };
  };

  const calculatePackCohesion = (friendPoints, selfPose = null) => {
    const points = (Array.isArray(friendPoints) ? friendPoints : [])
      .map(point => ({
        name: String(point?.name || 'Friend').slice(0, 64),
        x: Number(point?.x),
        y: Number(point?.y)
      }))
      .filter(point => Number.isFinite(point.x) && Number.isFinite(point.y));
    if (!points.length) {
      return {
        friendCount: 0,
        center: null,
        radius: null,
        spread: null,
        centerDistance: null,
        centerBearing: null,
        centerCardinal: '',
        farthestName: '',
        farthestDistance: null,
        farthestPoint: null
      };
    }

    const center = {
      x: points.reduce((sum, point) => sum + point.x, 0) / points.length,
      y: points.reduce((sum, point) => sum + point.y, 0) / points.length
    };
    const radius = points.reduce((maximum, point) =>
      Math.max(maximum, Math.hypot(point.x - center.x, point.y - center.y)), 0);
    let spread = 0;
    for (let left = 0; left < points.length; left += 1) {
      for (let right = left + 1; right < points.length; right += 1) {
        spread = Math.max(spread, Math.hypot(
          points[right].x - points[left].x,
          points[right].y - points[left].y));
      }
    }

    let centerDistance = null;
    let centerBearing = null;
    let centerCardinal = '';
    let farthestName = '';
    let farthestDistance = null;
    let farthestPoint = null;
    const selfX = Number(selfPose?.x);
    const selfY = Number(selfPose?.y);
    if (Number.isFinite(selfX) && Number.isFinite(selfY)) {
      const dx = center.x - selfX;
      const dy = center.y - selfY;
      centerDistance = Math.hypot(dx, dy);
      centerBearing = (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
      const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
      centerCardinal = cardinals[Math.round(centerBearing / 45) % 8];
    }
    if (points.length >= 2) {
      for (const point of points) {
        const distance = Math.hypot(point.x - center.x, point.y - center.y);
        if (farthestDistance == null || distance > farthestDistance) {
          farthestDistance = distance;
          farthestName = point.name;
          farthestPoint = { x: point.x, y: point.y, name: point.name };
        }
      }
    }

    return {
      friendCount: points.length,
      center,
      radius,
      spread,
      centerDistance,
      centerBearing,
      centerCardinal,
      farthestName,
      farthestDistance,
      farthestPoint
    };
  };

  const updateNearestFriend = players => {
    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
    const nextRoster = [];
    const friendPoints = [];
    for (const player of players) {
      if (!player.isFriend || player.isSelf) continue;
      const pose = readMarkerPose(player.marker);
      if (!pose) continue;
      friendPoints.push({
        name: String(player.name || 'Friend').slice(0, 64),
        x: pose.x,
        y: pose.y
      });
      let distance = null;
      let bearing = null;
      let cardinal = '';
      if (selfPose) {
        const dx = pose.x - selfPose.x;
        const dy = pose.y - selfPose.y;
        distance = Math.hypot(dx, dy);
        bearing = (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
        cardinal = cardinals[Math.round(bearing / 45) % 8];
      }
      nextRoster.push({
        name: String(player.name || 'Friend').slice(0, 64),
        distance,
        bearing,
        cardinal
      });
    }

    nextRoster.sort((a, b) => {
      if (a.distance != null && b.distance != null) return a.distance - b.distance;
      if (a.distance != null) return -1;
      if (b.distance != null) return 1;
      return a.name.localeCompare(b.name);
    });
    friendRoster = nextRoster.slice(0, 20);
    const nextPack = calculatePackCohesion(friendPoints, selfPose);
    const nextRosterKey = friendPoints
      .map(point => point.name)
      .sort((left, right) => left.localeCompare(right))
      .join('\u001f');
    if (nextRosterKey !== packSpreadMotionRosterKey) {
      resetPackSpreadMotion(false);
      packSpreadMotionRosterKey = nextRosterKey;
    }
    packFriendCount = nextPack.friendCount;
    packSpread = nextPack.spread;
    packRadius = nextPack.radius;
    packCenterDistance = nextPack.centerDistance;
    packCenterBearing = nextPack.centerBearing;
    packCenterCardinal = nextPack.centerCardinal;
    packFarthestFriendName = nextPack.farthestName;
    packFarthestFriendDistance = nextPack.farthestDistance;
    packOutlierPoint = nextPack.farthestPoint;
    packCenterPoint = nextPack.center;
    if (streamerMode || nextPack.friendCount < 2 || nextPack.spread == null) {
      resetPackSpreadMotion(!nextRosterKey);
    } else {
      const observedAt = Date.now();
      const responseToken = Number(markerResponseCount) || 0;
      const lastSample = packSpreadMotionSamples.at(-1);
      if (!lastSample || lastSample.responseToken !== responseToken) {
        packSpreadMotionSamples = [...packSpreadMotionSamples, {
          at: observedAt,
          spread: nextPack.spread,
          centerX: nextPack.center.x,
          centerY: nextPack.center.y,
          responseToken
        }]
          .filter(sample => observedAt - Number(sample.at) <= 45000)
          .slice(-12);
      }
      const motion = calculatePackSpreadMotion(packSpreadMotionSamples, observedAt);
      packSpreadMotion = motion.state;
      packSpreadRate = motion.rate;
      packSpreadMotionSampleCount = motion.sampleCount;
      const course = calculatePackCourse(packSpreadMotionSamples, observedAt);
      packCourseState = course.state;
      packCourseSpeed = course.speed;
      packCourseBearing = course.bearing;
      packCourseCardinal = course.cardinal;
      packCourseSampleCount = course.sampleCount;
    }
    const nearest = friendRoster.find(friend => friend.distance != null) || null;

    const nextName = nearest?.name || '';
    const nextDistance = nearest?.distance ?? null;
    const nextBearing = nearest?.bearing ?? null;
    const nextCardinal = nearest?.cardinal || '';
    if (nextName !== nearestFriendName
        || Math.abs((nextDistance ?? -1) - (nearestFriendDistance ?? -1)) >= 0.05
        || Math.abs((nextBearing ?? -1) - (nearestFriendBearing ?? -1)) >= 0.5
        || nextCardinal !== nearestFriendCardinal) {
      nearestFriendName = nextName;
      nearestFriendDistance = nextDistance;
      nearestFriendBearing = nextBearing;
      nearestFriendCardinal = nextCardinal;
      lastMessage = '';
    }
  };

  const updateEncounterAwareness = players => {
    if (streamerMode) {
      encounterPlayerCount = 0;
      nearestEncounterDistance = null;
      nearestEncounterBearing = null;
      nearestEncounterCardinal = '';
      nearestEncounterMotion = '';
      nearestEncounterRelativeSpeed = null;
      nearestEncounterInterceptSeconds = null;
      nearestEncounterMotionSampleCount = 0;
      encounterMotionTracks.clear();
      encounterWithin10 = 0;
      encounterWithin25 = 0;
      encounterWithin50 = 0;
      return;
    }

    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
    const otherPlayers = players.filter(player => !player.isSelf && !player.isFriend);
    const observedAt = Date.now();
    const liveContactKeys = new Set();
    const points = otherPlayers.map((player, index) => {
      const pose = readMarkerPose(player.marker);
      if (!pose) return null;
      const contactKey = String(
        player.name
        || player.marker?.getAttribute?.('data-player-id')
        || `anonymous-${index}`
      ).slice(0, 96);
      liveContactKeys.add(contactKey);
      let motion = null;
      if (selfPose) {
        const distance = Math.hypot(pose.x - selfPose.x, pose.y - selfPose.y);
        const previous = encounterMotionTracks.get(contactKey) || [];
        const last = previous.at(-1);
        const responseToken = Number(markerResponseCount) || 0;
        const sample = { at: observedAt, distance, responseToken };
        const nextSamples = last && Number(last.responseToken) === responseToken
          ? [...previous.slice(0, -1), sample]
          : [...previous, sample];
        const bounded = nextSamples
          .filter(sample => observedAt - Number(sample.at) <= 45000)
          .slice(-12);
        encounterMotionTracks.set(contactKey, bounded);
        motion = calculateEncounterMotion(bounded, observedAt);
      }
      return { x: pose.x, y: pose.y, motion };
    }).filter(Boolean);
    for (const key of encounterMotionTracks.keys()) {
      if (!liveContactKeys.has(key)) encounterMotionTracks.delete(key);
    }
    if (!selfPose) encounterMotionTracks.clear();
    const nextEncounter = calculateEncounterAwareness(points, selfPose);
    encounterPlayerCount = otherPlayers.length;
    nearestEncounterDistance = nextEncounter.nearestDistance;
    nearestEncounterBearing = nextEncounter.nearestBearing;
    nearestEncounterCardinal = nextEncounter.nearestCardinal;
    nearestEncounterMotion = nextEncounter.nearestMotion?.state || '';
    nearestEncounterRelativeSpeed = nextEncounter.nearestMotion?.relativeSpeed ?? null;
    nearestEncounterInterceptSeconds = nextEncounter.nearestMotion?.interceptSeconds ?? null;
    nearestEncounterMotionSampleCount = nextEncounter.nearestMotion?.sampleCount ?? 0;
    encounterWithin10 = nextEncounter.within10;
    encounterWithin25 = nextEncounter.within25;
    encounterWithin50 = nextEncounter.within50;
  };

  const setFriendRoute = requestedName => {
    if (streamerMode) return false;
    const name = String(requestedName || '').trim();
    if (!name) return false;
    const players = getPlayerMarkers();
    const friend = players.find(player => player.isFriend && player.name === name)
      ?? players.find(player => player.isFriend
        && player.name.toLowerCase() === name.toLowerCase());
    const pose = friend ? readMarkerPose(friend.marker) : null;
    if (!friend || !pose) return false;
    resetRoutePlan(false);
    friendRouteName = friend.name;
    packRouteActive = false;
    packOutlierRouteActive = false;
    activePinId = '';
    waypoint = { x: pose.x, y: pose.y, label: `Friend · ${friend.name}`, kind: 'friend' };
    waypointArmed = false;
    pinArmed = false;
    cancelMeasurementCapture();
    updateWaypoint(players);
    lastMessage = '';
    notify('friend-routed');
    return true;
  };

  const setPackCenterRoute = () => {
    if (streamerMode) return false;
    const players = getPlayerMarkers();
    updateNearestFriend(players);
    if (!packCenterPoint) return false;
    resetRoutePlan(false);
    friendRouteName = '';
    packRouteActive = true;
    packOutlierRouteActive = false;
    activePinId = '';
    waypoint = {
      x: packCenterPoint.x,
      y: packCenterPoint.y,
      label: 'Pack center',
      kind: 'pack'
    };
    waypointArmed = false;
    pinArmed = false;
    cancelMeasurementCapture();
    updateWaypoint(players);
    lastMessage = '';
    notify('pack-center-routed');
    return true;
  };

  const setPackOutlierRoute = () => {
    if (streamerMode) return false;
    const players = getPlayerMarkers();
    updateNearestFriend(players);
    if (packFriendCount < 2 || !packOutlierPoint || !packFarthestFriendName) return false;
    resetRoutePlan(false);
    friendRouteName = '';
    packRouteActive = false;
    packOutlierRouteActive = true;
    activePinId = '';
    waypoint = {
      x: packOutlierPoint.x,
      y: packOutlierPoint.y,
      label: `Pack outlier - ${packFarthestFriendName}`,
      kind: 'pack'
    };
    waypointArmed = false;
    pinArmed = false;
    cancelMeasurementCapture();
    updateWaypoint(players);
    lastMessage = '';
    notify('pack-outlier-routed');
    return true;
  };

  const normalizeMapLabel = value => String(value || '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();

  const mapLabelEditDistance = (left, right) => {
    const a = String(left || '');
    const b = String(right || '');
    if (!a.length) return b.length;
    if (!b.length) return a.length;
    let previous = Array.from({ length: b.length + 1 }, (_, index) => index);
    for (let row = 1; row <= a.length; row += 1) {
      const current = [row];
      for (let column = 1; column <= b.length; column += 1) {
        current[column] = Math.min(
          current[column - 1] + 1,
          previous[column] + 1,
          previous[column - 1] + (a[row - 1] === b[column - 1] ? 0 : 1));
      }
      previous = current;
    }
    return previous[b.length];
  };

  const scoreMapLabel = (query, label) => {
    const normalizedQuery = normalizeMapLabel(query);
    const normalizedLabel = normalizeMapLabel(label);
    if (normalizedQuery.length < 2 || !normalizedLabel) return null;
    if (normalizedLabel === normalizedQuery) return 0;
    if (normalizedLabel.startsWith(normalizedQuery)) return 1;
    if (normalizedLabel.includes(normalizedQuery)) return 2;
    const queryTokens = normalizedQuery.split(' ').filter(Boolean);
    const labelTokens = normalizedLabel.split(' ').filter(Boolean);
    if (queryTokens.length && queryTokens.every(queryToken =>
      labelTokens.some(labelToken => labelToken.startsWith(queryToken)))) return 3;
    if (normalizedQuery.length < 4) return null;
    const typoLimit = Math.max(1, Math.floor(normalizedQuery.length * 0.25));
    const editDistance = mapLabelEditDistance(normalizedQuery, normalizedLabel);
    return editDistance <= typoLimit
      ? 4 + editDistance / normalizedQuery.length
      : null;
  };

  const selectLandmarkLabels = (
    candidates,
    requestedMode = 'auto',
    viewportWidth = 0,
    viewportHeight = 0) => {
    const mode = ['auto', 'focus', 'all'].includes(String(requestedMode))
      ? String(requestedMode)
      : 'auto';
    const usable = (Array.isArray(candidates) ? candidates : [])
      .map(candidate => ({
        id: String(candidate?.id || ''),
        label: String(candidate?.label || '').replace(/\s+/g, ' ').trim(),
        left: Number(candidate?.left),
        top: Number(candidate?.top),
        width: Number(candidate?.width),
        height: Number(candidate?.height)
      }))
      .filter(candidate => candidate.id && candidate.label
        && Number.isFinite(candidate.left) && Number.isFinite(candidate.top)
        && Number.isFinite(candidate.width) && candidate.width > 0
        && Number.isFinite(candidate.height) && candidate.height > 0);
    if (mode === 'all') return usable.map(candidate => candidate.id);

    const width = Math.max(1, Number(viewportWidth) || 1);
    const height = Math.max(1, Number(viewportHeight) || 1);
    const compact = width < 560;
    const limit = mode === 'focus'
      ? compact ? 10 : 16
      : compact ? 32 : 54;
    const paddingX = mode === 'focus' ? 14 : compact ? 8 : 5;
    const paddingY = mode === 'focus' ? 7 : compact ? 4 : 3;
    const centerX = width / 2;
    const centerY = height / 2;
    usable.sort((left, right) => {
      const leftDistance = Math.hypot(
        left.left + left.width / 2 - centerX,
        left.top + left.height / 2 - centerY);
      const rightDistance = Math.hypot(
        right.left + right.width / 2 - centerX,
        right.top + right.height / 2 - centerY);
      return leftDistance - rightDistance
        || left.label.length - right.label.length
        || left.id.localeCompare(right.id);
    });

    const accepted = [];
    const acceptedBounds = [];
    const seenLabels = new Set();
    for (const candidate of usable) {
      if (accepted.length >= limit) break;
      const normalizedLabel = normalizeMapLabel(candidate.label);
      if (!normalizedLabel || seenLabels.has(normalizedLabel)) continue;
      const bounds = {
        left: candidate.left - paddingX,
        right: candidate.left + candidate.width + paddingX,
        top: candidate.top - paddingY,
        bottom: candidate.top + candidate.height + paddingY
      };
      if (acceptedBounds.some(existing =>
        bounds.left < existing.right && bounds.right > existing.left
        && bounds.top < existing.bottom && bounds.bottom > existing.top)) {
        continue;
      }
      accepted.push(candidate.id);
      acceptedBounds.push(bounds);
      seenLabels.add(normalizedLabel);
    }
    return accepted;
  };

  const selectNearestLandmark = (landmarks, selfPose) => {
    if (!selfPose || !Array.isArray(landmarks) || !landmarks.length) return null;
    let nearest = null;
    for (const landmark of landmarks) {
      const x = Number(landmark?.x);
      const y = Number(landmark?.y);
      if (!Number.isFinite(x) || !Number.isFinite(y)) continue;
      const dx = x - Number(selfPose.x);
      const dy = y - Number(selfPose.y);
      const distance = Math.hypot(dx, dy);
      if (!nearest || distance < nearest.distance
          || (distance === nearest.distance
            && String(landmark.label || '').length < nearest.label.length)) {
        const bearing = (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
        nearest = {
          x,
          y,
          label: String(landmark.label || 'Map place').slice(0, 64),
          distance,
          bearing,
          cardinal: cardinals[Math.round(bearing / 45) % 8]
        };
      }
    }
    return nearest;
  };

  const readSvgTextPose = text => {
    const svg = getMapSvg();
    if (!svg || !text) return null;
    try {
      const box = text.getBBox();
      const textScreenMatrix = text.getScreenCTM();
      const svgScreenMatrix = svg.getScreenCTM();
      if (!textScreenMatrix || !svgScreenMatrix) return null;
      const point = svg.createSVGPoint();
      point.x = box.x + box.width / 2;
      point.y = box.y + box.height / 2;
      const screenPoint = point.matrixTransform(textScreenMatrix);
      const rootPoint = screenPoint.matrixTransform(svgScreenMatrix.inverse());
      return Number.isFinite(rootPoint.x) && Number.isFinite(rootPoint.y)
        ? { x: rootPoint.x, y: rootPoint.y }
        : null;
    } catch {
      return null;
    }
  };

  const isOfficialMapLabel = (text, playerLabels) => {
    if (!text || playerLabels.has(text)
        || text.closest('[data-isle-mapper-saved-pins="true"]')
        || text.closest('[data-isle-mapper-route-plan="true"]')
        || text.closest('[data-isle-mapper-grid="true"]')
        || text.closest('[data-isle-mapper-measurement="true"]')
        || text.closest('[data-isle-mapper-waypoint="true"]')
        || text.closest('[data-isle-mapper-trails="true"]')
        || text.closest('[data-isle-mapper-sound-finder="true"]')
        || text.closest('[data-isle-mapper-self-navigation="true"]')) return false;
    const label = String(text.textContent || '').replace(/\s+/g, ' ').trim();
    const normalizedLabel = normalizeMapLabel(label);
    if (label.length < 2 || label.length > 64 || normalizedLabel.length < 2
        || !/[a-z]/i.test(label)) return false;
    try {
      const style = getComputedStyle(text);
      const hiddenByMapper = text.classList.contains('the-isle-mapper-landmark-hidden')
        || Boolean(text.closest('.the-isle-mapper-landmark-hidden'));
      if (style.display === 'none' || (!hiddenByMapper && style.visibility === 'hidden')
          || Number(style.opacity) === 0) return false;
      const bounds = text.getBoundingClientRect();
      if (bounds.width <= 0 || bounds.height <= 0) return false;
    } catch { }
    return true;
  };

  const resolveLandmarkLabelContainer = text => {
    const group = text?.closest?.('g');
    if (!group || group.querySelectorAll('text').length !== 1) return text;
    const mapGeometry = group.querySelector(
      'path, polygon, line, polyline, image, use, foreignObject');
    return mapGeometry ? text : group;
  };

  const getOfficialLandmarkCatalog = (force = false) => {
    const now = Date.now();
    if (!force && now - officialLandmarkCatalogUpdatedAt < 3000) {
      return officialLandmarkCatalog;
    }
    const svg = getMapSvg();
    officialLandmarkCatalogUpdatedAt = now;
    if (!svg) {
      officialLandmarkCatalog = [];
      return officialLandmarkCatalog;
    }
    const players = getPlayerMarkers();
    const playerLabels = new Set(players.map(player => player.label));
    const deduplicated = new Map();
    for (const text of svg.querySelectorAll('text')) {
      if (!isOfficialMapLabel(text, playerLabels)) continue;
      const label = String(text.textContent || '').replace(/\s+/g, ' ').trim();
      const pose = readSvgTextPose(text);
      if (!pose) continue;
      const key = `${normalizeMapLabel(label)}:${Math.round(pose.x / 6)}:${Math.round(pose.y / 6)}`;
      const existingLandmark = deduplicated.get(key);
      if (!existingLandmark || label.length < existingLandmark.label.length) {
        deduplicated.set(key, { ...pose, label });
      }
    }
    officialLandmarkCatalog = [...deduplicated.values()];
    return officialLandmarkCatalog;
  };

  const applyLandmarkLabelDensity = (force = false) => {
    const now = Date.now();
    if (!force && now - landmarkLabelLayoutAt < 400) return visibleLandmarkCount;
    landmarkLabelLayoutAt = now;
    const svg = getMapSvg();
    const mapRect = map?.getBoundingClientRect?.();
    if (!svg || !mapRect?.width || !mapRect?.height) {
      visibleLandmarkCount = 0;
      return visibleLandmarkCount;
    }

    for (const text of svg.querySelectorAll('text[data-isle-mapper-official-label="true"]')) {
      text.classList.remove('the-isle-mapper-landmark-hidden');
      text.closest('.the-isle-mapper-landmark-hidden')?.classList.remove(
        'the-isle-mapper-landmark-hidden');
    }
    const players = getPlayerMarkers();
    const playerLabels = new Set(players.map(player => player.label));
    const labels = [];
    for (const text of svg.querySelectorAll('text')) {
      if (!isOfficialMapLabel(text, playerLabels)) continue;
      text.dataset.isleMapperOfficialLabel = 'true';
      text.dataset.isleMapperLandmarkId ||= `landmark-${++landmarkLabelSequence}`;
      const bounds = text.getBoundingClientRect();
      if (bounds.width <= 0 || bounds.height <= 0
          || bounds.right <= mapRect.left || bounds.left >= mapRect.right
          || bounds.bottom <= mapRect.top || bounds.top >= mapRect.bottom) continue;
      labels.push({
        element: resolveLandmarkLabelContainer(text),
        id: text.dataset.isleMapperLandmarkId,
        label: String(text.textContent || ''),
        left: bounds.left - mapRect.left,
        top: bounds.top - mapRect.top,
        width: bounds.width,
        height: bounds.height
      });
    }

    const selectedIds = new Set(selectLandmarkLabels(
      labels,
      landmarkLabelDensity,
      mapRect.width,
      mapRect.height));
    for (const label of labels) {
      label.element.classList.toggle(
        'the-isle-mapper-landmark-hidden',
        !selectedIds.has(label.id));
    }
    const nextVisibleCount = landmarkLabelDensity === 'all'
      ? officialLandmarkCatalog.length || selectedIds.size
      : Math.min(
        officialLandmarkCatalog.length || selectedIds.size,
        selectedIds.size);
    if (nextVisibleCount !== visibleLandmarkCount) {
      visibleLandmarkCount = nextVisibleCount;
      lastMessage = '';
    }
    return visibleLandmarkCount;
  };

  const rankNamedPlaces = (catalog, query, selfPose = null, limit = 5) => {
    const requestedLimit = Math.min(8, Math.max(1, Number(limit) || 5));
    const hasSelfPose = Number.isFinite(Number(selfPose?.x))
      && Number.isFinite(Number(selfPose?.y));
    const deduplicated = new Map();
    for (const place of Array.isArray(catalog) ? catalog : []) {
      const x = Number(place?.x);
      const y = Number(place?.y);
      const label = String(place?.label || '').trim();
      const normalizedLabel = normalizeMapLabel(label);
      const score = scoreMapLabel(query, label);
      if (!normalizedLabel || score === null || !Number.isFinite(x) || !Number.isFinite(y)) {
        continue;
      }
      const distance = hasSelfPose
        ? Math.hypot(x - Number(selfPose.x), y - Number(selfPose.y))
        : null;
      const dx = hasSelfPose ? x - Number(selfPose.x) : null;
      const dy = hasSelfPose ? y - Number(selfPose.y) : null;
      const bearing = hasSelfPose
        ? (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360
        : null;
      const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
      const match = {
        x,
        y,
        label: label.slice(0, 64),
        score,
        distance,
        bearing,
        cardinal: bearing === null ? '' : cardinals[Math.round(bearing / 45) % 8],
        gridReference: mapPointToGridReference(x, y)
      };
      const existingMatch = deduplicated.get(normalizedLabel);
      if (!existingMatch || score < existingMatch.score
          || (score === existingMatch.score
            && distance !== null
            && (existingMatch.distance === null || distance < existingMatch.distance))) {
        deduplicated.set(normalizedLabel, match);
      }
    }
    return [...deduplicated.values()]
      .sort((a, b) => a.score - b.score
        || (a.distance ?? Number.POSITIVE_INFINITY)
          - (b.distance ?? Number.POSITIVE_INFINITY)
        || a.label.length - b.label.length
        || a.label.localeCompare(b.label))
      .slice(0, requestedLimit);
  };

  const rankSavedDestinations = (pins, query, selfPose = null, limit = 5) => {
    const requestedLimit = Math.min(8, Math.max(1, Number(limit) || 5));
    const now = Date.now();
    const hasSelfPose = Number.isFinite(Number(selfPose?.x))
      && Number.isFinite(Number(selfPose?.y));
    const matches = [];
    for (const pin of Array.isArray(pins) ? pins : []) {
      const x = Number(pin?.x);
      const y = Number(pin?.y);
      const label = String(pin?.label || '').trim();
      const score = scoreMapLabel(query, label);
      if (!label || score === null || !Number.isFinite(x) || !Number.isFinite(y)) {
        continue;
      }
      const dx = hasSelfPose ? x - Number(selfPose.x) : null;
      const dy = hasSelfPose ? y - Number(selfPose.y) : null;
      const distance = hasSelfPose ? Math.hypot(dx, dy) : null;
      const bearing = hasSelfPose
        ? (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360
        : null;
      const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
      matches.push({
        id: String(pin.id || ''),
        type: pinTypes[pin.type] ? pin.type : 'safe',
        label: label.slice(0, 64),
        x,
        y,
        favorite: Boolean(pin.favorite),
        expiresInMs: Number(pin.expiresAt) > now ? Number(pin.expiresAt) - now : null,
        createdAt: Number(pin.createdAt) || 0,
        score,
        distance,
        bearing,
        cardinal: bearing === null ? '' : cardinals[Math.round(bearing / 45) % 8],
        gridReference: mapPointToGridReference(x, y)
      });
    }
    return matches
      .sort((a, b) => a.score - b.score
        || Number(b.favorite) - Number(a.favorite)
        || (a.distance ?? Number.POSITIVE_INFINITY)
          - (b.distance ?? Number.POSITIVE_INFINITY)
        || b.createdAt - a.createdAt
        || a.label.localeCompare(b.label))
      .slice(0, requestedLimit);
  };

  const searchDestinations = (query, limit = 5) => {
    if (streamerMode || normalizeMapLabel(query).length < 2) return [];
    purgeExpiredPins(Date.now());
    const requestedLimit = Math.min(8, Math.max(1, Number(limit) || 5));
    const players = getPlayerMarkers();
    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = markerAvailable
      ? (selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose())
      : null;
    const placeMatches = rankNamedPlaces(
      getOfficialLandmarkCatalog(), query, selfPose, requestedLimit * 2)
      .map(match => ({ ...match, kind: 'place', sourceRank: 1, favorite: false }));
    const savedMatches = rankSavedDestinations(
      savedPins, query, selfPose, requestedLimit * 2)
      .map(match => ({ ...match, kind: 'pin', sourceRank: 0 }));
    return [...savedMatches, ...placeMatches]
      .sort((a, b) => a.score - b.score
        || a.sourceRank - b.sourceRank
        || Number(b.favorite) - Number(a.favorite)
        || (a.distance ?? Number.POSITIVE_INFINITY)
          - (b.distance ?? Number.POSITIVE_INFINITY)
        || a.label.length - b.label.length
        || a.label.localeCompare(b.label))
      .slice(0, requestedLimit)
      .map(match => ({
        kind: match.kind,
        pinId: match.id || '',
        type: match.type || '',
        favorite: Boolean(match.favorite),
        expiresInMs: match.expiresInMs ?? null,
        label: match.label,
        gridReference: match.gridReference,
        distance: match.distance,
        bearing: match.bearing,
        cardinal: match.cardinal
      }));
  };

  const updateNearestPlace = players => {
    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
    const nearest = streamerMode || !markerAvailable || !selfPose
      ? null
      : selectNearestLandmark(getOfficialLandmarkCatalog(), selfPose);
    const nextName = nearest?.label || '';
    const nextDistance = nearest?.distance ?? null;
    const nextBearing = nearest?.bearing ?? null;
    const nextCardinal = nearest?.cardinal || '';
    nearestPlacePoint = nearest
      ? { x: nearest.x, y: nearest.y, label: nearest.label }
      : null;
    if (nextName !== nearestPlaceName
        || Math.abs((nextDistance ?? -1) - (nearestPlaceDistance ?? -1)) >= 0.05
        || Math.abs((nextBearing ?? -1) - (nearestPlaceBearing ?? -1)) >= 0.5
        || nextCardinal !== nearestPlaceCardinal) {
      nearestPlaceName = nextName;
      nearestPlaceDistance = nextDistance;
      nearestPlaceBearing = nextBearing;
      nearestPlaceCardinal = nextCardinal;
      lastMessage = '';
    }
  };

  const findNamedPlace = query => {
    const normalizedQuery = normalizeMapLabel(query);
    if (normalizedQuery.length < 2) return null;
    const players = getPlayerMarkers();
    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
    return rankNamedPlaces(
      getOfficialLandmarkCatalog(true), query, selfPose, 1)[0] || null;
  };

  const findSavedDestination = query => {
    const normalizedQuery = normalizeMapLabel(query);
    if (normalizedQuery.length < 2) return null;
    const players = getPlayerMarkers();
    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = markerAvailable
      ? (selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose())
      : null;
    return rankSavedDestinations(savedPins, query, selfPose, 1)[0] || null;
  };

  const resolveWorldPoint = (worldX, worldY) => {
    const props = findReactMapProps();
    return worldToMapPoint(props?.calibration, Number(worldX), Number(worldY));
  };

  const resolveSharedRoute = query => {
    const tokens = parseSharedRouteTokens(query);
    if (!tokens.length) return null;
    const stops = [];
    for (const token of tokens) {
      let stop = resolveGridReference(token);
      if (!stop) {
        const cleaned = String(token || '')
          .replace(/^(?:ASSET\s+LOCATION\s*[:-]?\s*)/i, '')
          .trim();
        const numbers = [...cleaned.matchAll(/[-+]?(?:\d+(?:\.\d+)?|\.\d+)/g)]
          .map(match => Number(match[0]))
          .filter(Number.isFinite);
        if (numbers.length === 2 || numbers.length === 3) {
          const worldX = numbers[0];
          const worldY = numbers[1];
          const point = resolveWorldPoint(worldX, worldY);
          if (point) {
            stop = {
              ...point,
              label: `Coordinates ${worldX.toFixed(0)}, ${worldY.toFixed(0)}`
            };
          }
        }
      }
      stop ??= findSavedDestination(token);
      stop ??= findNamedPlace(token);
      if (!stop || !Number.isFinite(Number(stop.x)) || !Number.isFinite(Number(stop.y))) {
        return null;
      }
      stops.push({
        x: Math.min(1000, Math.max(0, Number(stop.x))),
        y: Math.min(1000, Math.max(0, Number(stop.y))),
        label: String(stop.label || token).slice(0, 64)
      });
    }
    return stops;
  };

  const startSharedRoute = query => {
    if (streamerMode) return false;
    const stops = resolveSharedRoute(query);
    if (!stops || stops.length < 2) return false;
    resetRoutePlan(true);
    waypoint = null;
    waypointArmed = false;
    waypointDistance = null;
    waypointBearing = null;
    waypointCardinal = '';
    friendRouteName = '';
    packRouteActive = false;
    packOutlierRouteActive = false;
    activePinId = '';
    pinArmed = false;
    cancelMeasurementCapture();
    routePlanSource = 'shared';
    routeStops = stops;
    routeCurrentIndex = 0;
    routePlanActive = true;
    routePlanComplete = false;
    routeAutoReplanAt = Date.now();
    setWaypointFromRouteStop();
    drawRoutePlan();
    updateWaypoint(getPlayerMarkers());
    lastMessage = '';
    notify('shared-route-started');
    return true;
  };

  const setStaticWaypoint = (
    point,
    label,
    pinId = '',
    remember = true,
    requestedKind = '') => {
    if (!point || streamerMode) return false;
    resetRoutePlan(false);
    friendRouteName = '';
    packRouteActive = false;
    packOutlierRouteActive = false;
    activePinId = String(pinId || '');
    waypoint = {
      x: Math.min(1000, Math.max(0, Number(point.x))),
      y: Math.min(1000, Math.max(0, Number(point.y))),
      label: String(label || 'Waypoint').slice(0, 64),
      kind: normalizeWaypointKind(requestedKind || point?.kind)
    };
    if (!Number.isFinite(waypoint.x) || !Number.isFinite(waypoint.y)) return false;
    if (remember) recentRoutes = recordRecentRoute(recentRoutes, waypoint, waypoint.label);
    waypointArmed = false;
    pinArmed = false;
    cancelMeasurementCapture();
    updateWaypoint(getPlayerMarkers());
    lastMessage = '';
    return true;
  };

  const setPinRoute = requestedId => {
    purgeExpiredPins(Date.now());
    const id = String(requestedId || '');
    const pin = savedPins.find(candidate => candidate.id === id);
    if (!pin || streamerMode) return false;
    const label = pin.label || `${pinTypes[pin.type]?.label || 'Saved'} marker`;
    if (!setStaticWaypoint(pin, label, pin.id, true, pin.type)) return false;
    notify('pin-routed');
    return true;
  };

  const routeToNearestPinType = requestedType => {
    purgeExpiredPins(Date.now());
    const type = String(requestedType || '').toLowerCase();
    if (!['safe', 'water', 'food', 'death'].includes(type) || streamerMode) return false;
    const matches = savedPins.filter(pin => pin.type === type);
    if (!matches.length) return false;
    const players = getPlayerMarkers();
    const selfPlayer = players.find(player => player.isSelf);
    const pose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
    const pin = pose
      ? matches.reduce((nearest, candidate) => {
          const nearestDistance = Math.hypot(nearest.x - pose.x, nearest.y - pose.y);
          const candidateDistance = Math.hypot(candidate.x - pose.x, candidate.y - pose.y);
          return candidateDistance < nearestDistance ? candidate : nearest;
        })
      : matches.at(-1);
    if (!pin || !setPinRoute(pin.id)) return false;
    notify('nearest-pin-routed');
    return true;
  };

  const renameSavedPin = (requestedId, requestedLabel) => {
    if (streamerMode) return false;
    const id = String(requestedId || '');
    const pin = savedPins.find(candidate => candidate.id === id);
    const label = sanitizePinLabel(requestedLabel);
    if (!pin || !label || pin.label === label) return false;
    pin.label = label;
    if (activePinId === id && waypoint) waypoint.label = label;
    persistSavedPins();
    drawSavedPins();
    updateWaypoint(getPlayerMarkers());
    lastMessage = '';
    notify('pin-renamed');
    return true;
  };

  const toggleSavedPinFavorite = requestedId => {
    if (streamerMode) return false;
    const id = String(requestedId || '');
    const pin = savedPins.find(candidate => candidate.id === id);
    if (!pin) return false;
    pin.favorite = !pin.favorite;
    persistSavedPins();
    drawSavedPins();
    lastMessage = '';
    notify('pin-favorite');
    return true;
  };

  const cycleSavedPinExpiry = requestedId => {
    if (streamerMode) return false;
    purgeExpiredPins(Date.now());
    const id = String(requestedId || '');
    const pin = savedPins.find(candidate => candidate.id === id);
    if (!pin) return false;
    const currentMinutes = pinExpiryMinutes.includes(Number(pin.expiryMinutes))
      ? Number(pin.expiryMinutes)
      : 0;
    const currentIndex = Math.max(0, pinExpiryMinutes.indexOf(currentMinutes));
    const nextMinutes = pinExpiryMinutes[(currentIndex + 1) % pinExpiryMinutes.length];
    pin.expiryMinutes = nextMinutes;
    pin.expiresAt = nextMinutes > 0 ? Date.now() + nextMinutes * 60000 : 0;
    persistSavedPins();
    drawSavedPins();
    lastMessage = '';
    notify('pin-expiry-changed');
    return true;
  };

  const cycleSavedPinAlertRadius = requestedId => {
    if (streamerMode) return false;
    purgeExpiredPins(Date.now());
    const id = String(requestedId || '');
    const pin = savedPins.find(candidate => candidate.id === id);
    if (!pin) return false;
    const currentRadius = pinAlertRadii.includes(Number(pin.alertRadius))
      ? Number(pin.alertRadius)
      : 0;
    const currentIndex = Math.max(0, pinAlertRadii.indexOf(currentRadius));
    pin.alertRadius = pinAlertRadii[(currentIndex + 1) % pinAlertRadii.length];
    persistSavedPins();
    drawSavedPins();
    lastMessage = '';
    notify('pin-alert-radius-changed');
    return true;
  };

  const publicPinImportResult = (plan, imported = false) => ({
    valid: Boolean(plan?.valid),
    imported: Boolean(imported),
    error: String(plan?.error || ''),
    totalCount: Number(plan?.totalCount) || 0,
    addedCount: Number(plan?.addedCount) || 0,
    duplicateCount: Number(plan?.duplicateCount) || 0,
    expiredCount: Number(plan?.expiredCount) || 0,
    trimmedCount: Number(plan?.trimmedCount) || 0,
    resultCount: Array.isArray(plan?.resultPins) ? plan.resultPins.length : savedPins.length,
    totalAreaCount: Number(plan?.totalAreaCount) || 0,
    addedAreaCount: Number(plan?.addedAreaCount) || 0,
    duplicateAreaCount: Number(plan?.duplicateAreaCount) || 0,
    trimmedAreaCount: Number(plan?.trimmedAreaCount) || 0,
    resultAreaCount: Array.isArray(plan?.resultNoGoAreas)
      ? plan.resultNoGoAreas.length : noGoAreas.length
  });

  const previewPinLibraryImport = backupText => {
    if (streamerMode) {
      return publicPinImportResult({ valid: false, error: 'Unavailable in streamer mode' });
    }
    return publicPinImportResult(buildPinLibraryImportPlan(
      savedPins, backupText, findReactMapProps()?.calibration, Date.now(), noGoAreas));
  };

  const importPinLibrary = backupText => {
    if (streamerMode) {
      return publicPinImportResult({ valid: false, error: 'Unavailable in streamer mode' });
    }
    const plan = buildPinLibraryImportPlan(
      savedPins, backupText, findReactMapProps()?.calibration, Date.now(), noGoAreas);
    if (!plan.valid || (plan.addedCount <= 0 && plan.addedAreaCount <= 0)) {
      return publicPinImportResult(plan);
    }
    savedPins = plan.resultPins;
    noGoAreas = plan.resultNoGoAreas;
    if (noGoSelectedAreaId
        && !noGoAreas.some(area => area.id === noGoSelectedAreaId)) {
      noGoSelectedAreaId = noGoAreas[0]?.id || '';
    } else if (!noGoSelectedAreaId && noGoAreas.length) {
      noGoSelectedAreaId = noGoAreas[0].id;
    }
    if (activePinId && !savedPins.some(pin => pin.id === activePinId)) {
      activePinId = '';
      waypoint = null;
      waypointArmed = false;
      waypointDistance = null;
      waypointBearing = null;
      waypointCardinal = '';
    }
    persistSavedPins();
    persistNoGoAreas();
    drawSavedPins();
    drawNoGoAreas();
    scheduleTerrainCourseForObstacleChange();
    updateWaypoint(getPlayerMarkers());
    lastMessage = '';
    notify('pin-library-imported');
    return publicPinImportResult(plan, true);
  };

  const routeToAnchor = (anchor, label) => {
    const point = resolveAnchorPoint(anchor);
    if (!point || streamerMode) return false;
    if (!setStaticWaypoint(point, label, '', false, 'recovery')) return false;
    notify('recovery-routed');
    return true;
  };

  const removeSavedPin = requestedId => {
    const id = String(requestedId || '');
    const index = savedPins.findIndex(pin => pin.id === id);
    if (index < 0) return false;
    const removingActiveRoute = activePinId === id;
    savedPins.splice(index, 1);
    if (removingActiveRoute) {
      activePinId = '';
      waypoint = null;
      waypointArmed = false;
      waypointDistance = null;
      waypointBearing = null;
      waypointCardinal = '';
      updateWaypoint(getPlayerMarkers());
    }
    persistSavedPins();
    drawSavedPins();
    lastMessage = '';
    notify('pin-removed');
    return true;
  };

  const routeToNamedPlace = query => {
    if (parseSharedRouteTokens(query).length) {
      return startSharedRoute(query);
    }
    const gridPoint = resolveGridReference(query);
    if (gridPoint) {
      if (!setStaticWaypoint(gridPoint, gridPoint.label)) return false;
      notify('grid-cell-routed');
      return true;
    }
    const savedDestination = findSavedDestination(query);
    if (savedDestination) return setPinRoute(savedDestination.id);
    const place = findNamedPlace(query);
    if (!place || !setStaticWaypoint(place, place.label)) return false;
    notify('place-routed');
    return true;
  };

  const routeToWorldCoordinates = (worldX, worldY) => {
    const point = resolveWorldPoint(worldX, worldY);
    const label = `Coordinates ${Number(worldX).toFixed(0)}, ${Number(worldY).toFixed(0)}`;
    if (!point || !setStaticWaypoint(point, label)) return false;
    notify('coordinates-routed');
    return true;
  };

  const saveNamedPlacePin = (query, type) => {
    if (streamerMode) return false;
    const place = resolveGridReference(query) || findNamedPlace(query);
    if (!place || !addSavedPin(place.x, place.y, type, place.label)) return false;
    drawSavedPins();
    notify(place.gridReference ? 'grid-cell-pin-saved' : 'place-pin-saved');
    return true;
  };

  const saveWorldCoordinatePin = (worldX, worldY, type) => {
    if (streamerMode) return false;
    const point = resolveWorldPoint(worldX, worldY);
    const label = `Coordinates ${Number(worldX).toFixed(0)}, ${Number(worldY).toFixed(0)}`;
    if (!point || !addSavedPin(point.x, point.y, type, label)) return false;
    drawSavedPins();
    notify('coordinates-pin-saved');
    return true;
  };

  const ensureWaypointMarker = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    let waypointMarker = svg.querySelector(':scope > g[data-isle-mapper-waypoint="true"]');
    if (!waypointMarker) {
      waypointMarker = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      waypointMarker.dataset.isleMapperWaypoint = 'true';
      waypointMarker.setAttribute('pointer-events', 'none');

      const ring = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      ring.setAttribute('cx', '0');
      ring.setAttribute('cy', '0');
      ring.setAttribute('r', '12');
      ring.setAttribute('fill', '#ff6847');
      ring.setAttribute('fill-opacity', '0.22');
      ring.setAttribute('stroke', '#ff6847');
      ring.setAttribute('stroke-width', '2.5');

      const core = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      core.setAttribute('cx', '0');
      core.setAttribute('cy', '0');
      core.setAttribute('r', '4');
      core.setAttribute('fill', '#ffffff');
      core.setAttribute('stroke', '#ff6847');
      core.setAttribute('stroke-width', '2');
      waypointMarker.append(ring, core);
      svg.appendChild(waypointMarker);
    }
    return waypointMarker;
  };

  const ensureWaypointRoute = () => {
    const svg = getMapSvg();
    if (!svg) return null;
    let route = svg.querySelector(':scope > line[data-isle-mapper-waypoint-route="true"]');
    if (!route) {
      route = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      route.dataset.isleMapperWaypointRoute = 'true';
      route.setAttribute('pointer-events', 'none');
      route.setAttribute('stroke', '#ff6847');
      route.setAttribute('stroke-width', '2');
      route.setAttribute('stroke-dasharray', '7 6');
      route.setAttribute('vector-effect', 'non-scaling-stroke');
      route.setAttribute('opacity', '0.78');
      const marker = svg.querySelector(':scope > g[data-isle-mapper-waypoint="true"]');
      svg.insertBefore(route, marker || null);
    }
    return route;
  };

  const updateWaypoint = players => {
    const waypointMarker = ensureWaypointMarker();
    const waypointRoute = ensureWaypointRoute();
    if (!waypointMarker || !waypointRoute) return;
    if (packRouteActive) {
      if (!packCenterPoint) {
        waypointMarker.style.display = 'none';
        waypointRoute.style.display = 'none';
        waypointDistance = null;
        waypointBearing = null;
        waypointCardinal = '';
        resetWaypointApproach();
        hideWaypointEdgeCue();
        return;
      }
      waypoint = {
        x: packCenterPoint.x,
        y: packCenterPoint.y,
        label: 'Pack center',
        kind: 'pack'
      };
    } else if (packOutlierRouteActive) {
      if (packFriendCount < 2 || !packOutlierPoint || !packFarthestFriendName) {
        waypointMarker.style.display = 'none';
        waypointRoute.style.display = 'none';
        waypointDistance = null;
        waypointBearing = null;
        waypointCardinal = '';
        resetWaypointApproach();
        hideWaypointEdgeCue();
        return;
      }
      waypoint = {
        x: packOutlierPoint.x,
        y: packOutlierPoint.y,
        label: `Pack outlier - ${packFarthestFriendName}`,
        kind: 'pack'
      };
    } else if (friendRouteName) {
      const friend = players.find(player => player.isFriend && player.name === friendRouteName);
      const friendPose = friend ? readMarkerPose(friend.marker) : null;
      if (!friendPose) {
        waypointMarker.style.display = 'none';
        waypointRoute.style.display = 'none';
        waypointDistance = null;
        waypointBearing = null;
        waypointCardinal = '';
        resetWaypointApproach();
        hideWaypointEdgeCue();
        return;
      }
      waypoint = {
        x: friendPose.x,
        y: friendPose.y,
        label: `Friend · ${friendRouteName}`,
        kind: 'friend'
      };
    }
    if (!waypoint || streamerMode) {
      waypointMarker.style.display = 'none';
      waypointRoute.style.display = 'none';
      waypointDistance = null;
      waypointBearing = null;
      waypointCardinal = '';
      resetWaypointApproach();
      hideWaypointEdgeCue();
      return;
    }

    waypointMarker.style.display = '';
    waypointRoute.style.display = '';
    const coverScale = headingUp ? 1.43 : 1;
    const inverseScale = 1 / Math.max(0.001, view.scale * coverScale);
    waypointMarker.setAttribute('transform', `translate(${waypoint.x} ${waypoint.y}) scale(${inverseScale})`);

    const selfPlayer = players.find(player => player.isSelf);
    const selfPose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
    if (!selfPose) {
      waypointRoute.style.display = 'none';
      waypointDistance = null;
      waypointBearing = null;
      waypointCardinal = '';
      resetWaypointApproach(buildWaypointApproachKey());
      updateWaypointEdgeCue();
      return;
    }

    const dx = waypoint.x - selfPose.x;
    const dy = waypoint.y - selfPose.y;
    waypointRoute.setAttribute('x1', String(selfPose.x));
    waypointRoute.setAttribute('y1', String(selfPose.y));
    waypointRoute.setAttribute('x2', String(waypoint.x));
    waypointRoute.setAttribute('y2', String(waypoint.y));
    waypointDistance = Math.hypot(dx, dy);
    waypointBearing = (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
    const cardinals = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
    waypointCardinal = cardinals[Math.round(waypointBearing / 45) % 8];
    updateWaypointApproach(waypointDistance);
    updateWaypointEdgeCue();
    if (routePlanSource === 'terrain' && routePlanActive && terrainCourseDestination) {
      const offCourseDistance = distanceToRemainingTerrainCourse(selfPose);
      const now = Date.now();
      if (offCourseDistance != null && offCourseDistance > 25
          && now - terrainCourseReplanAt >= 10000
          && !terrainCourseReplanTimer) {
        terrainCourseStatus = 'rerouting';
        terrainCourseReplanTimer = window.setTimeout(() => {
          terrainCourseReplanTimer = 0;
          startTerrainCourseInternal(
            terrainCourseDestination, 'terrain-course-auto-rerouted');
        }, 450);
      }
    }
    if (routeAutoReplanEnabled
        && routePlanActive
        && (routePlanSource === 'manual' || routePlanSource === 'shared')) {
      const offRouteDistance = distanceToRemainingRoutePlan(selfPose);
      const now = Date.now();
      if (offRouteDistance != null && offRouteDistance > 30
          && now - routeAutoReplanAt >= 8000
          && !routeAutoReplanTimer) {
        routeAutoReplanTimer = window.setTimeout(() => {
          routeAutoReplanTimer = 0;
          replanActiveRouteFromPosition(selfPose);
        }, 450);
      }
    }
    if (routePlanActive
        && waypointDistance <= routeAdvanceDistance
        && !routeAdvanceTimer) {
      routeAdvanceTimer = window.setTimeout(() => {
        routeAdvanceTimer = 0;
        if (routePlanActive && waypointDistance <= routeAdvanceDistance) {
          advanceRouteStopInternal(routeCurrentIndex + 1 < routeStops.length
            ? 'route-auto-advanced'
            : 'route-auto-complete');
        }
      }, 650);
    }
  };

  const enhanceLiveMarkers = () => {
    const players = getPlayerMarkers();
    for (const player of players) enhanceMarker(player);
    drawStyledPlayerMarkers(players);
    drawExplorationOverlay();
    drawMapGrid();
    drawBreadcrumbTrail();
    drawLearnedPassages();
    drawSoundFinder();
    recordTrailSamples(players);
    drawTrails(players);
    updateEncounterMemory(players);
    drawEncounterMemory();
    updateNearestFriend(players);
    updateEncounterAwareness(players);
    updateWaypoint(players);
    drawSavedPins();
    drawNoGoAreas();
    drawTerrainCommunityHazards();
    drawMeasurement();
    drawRoutePlan();
    getOfficialLandmarkCatalog();
    updateNearestPlace(players);
    const nextFriendAnimalCount = players.filter(player => !player.isSelf && player.isFriend).length;
    const nextAuthorizedAnimalCount = players.filter(player => !player.isSelf).length;
    const nextOtherAnimalCount = players.filter(player =>
      !player.isSelf && (!friendOnly || player.isFriend)).length;
    if (nextOtherAnimalCount !== otherAnimalCount
        || nextFriendAnimalCount !== friendAnimalCount
        || nextAuthorizedAnimalCount !== authorizedAnimalCount) {
      otherAnimalCount = nextOtherAnimalCount;
      friendAnimalCount = nextFriendAnimalCount;
      authorizedAnimalCount = nextAuthorizedAnimalCount;
      lastMessage = '';
    }
    return players;
  };

  const findSelfMarker = () => {
    const modelMarker = getMapSvg()
      ?.querySelector(':scope > g[data-isle-mapper-self-navigation="true"] [data-isle-mapper-self-anchor="true"]');
    if (modelMarker) return modelMarker;
    const players = getPlayerMarkers();
    const self = players.find(player => player.isSelf);
    if (!self) return null;
    return self.marker;
  };

  const observeMarkerFreshness = (marker, pose, playerGroup) => {
    if (!marker && !pose) return;
    const updatedAt = Number(playerGroup?.dataset?.isleyUpdatedAt);
    const signature = pose
      ? `${pose.rawX ?? pose.x}:${pose.rawY ?? pose.y}:${pose.rotation}:${updatedAt || 0}`
      : [
          marker.tagName,
          marker.getAttribute('transform') || '',
          marker.getAttribute('cx') || '',
          marker.getAttribute('cy') || '',
          updatedAt || 0
        ].join(':');
    if (signature === lastMarkerSignature) return;
    const now = Date.now();
    freshnessAt = Number.isFinite(updatedAt)
      && updatedAt > 0
      && Math.abs(now - updatedAt) < 86400000
        ? updatedAt
        : now;
    selfPositionAt = freshnessAt;
    lastMarkerSignature = signature;
    lastMessage = '';
  };

  // Keep LOCATION DATA freshness current even when follow/centering is paused.
  // Asset Location / live-provider acquires update the self marker via setSelf, but
  // they must still advance freshnessAt or the stale banner never clears.
  const refreshSelfMarkerFreshness = () => {
    const selfPlayer = getPlayerMarkers().find(player => player.isSelf);
    const selfPose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
    if (selfPlayer) ensureSelfNavigationMarker(selfPlayer);
    const marker = findSelfMarker();
    if (!marker || !selfPose) {
      return Boolean(marker);
    }
    observeMarkerFreshness(
      selfPlayer?.marker ?? marker,
      selfPose,
      selfPlayer?.playerGroup);
    return true;
  };

  const setMarkerAvailability = (available, reason) => {
    if (markerAvailable === available) return;
    markerAvailable = available;
    notify(reason);
  };

  const centerOnSelf = reason => {
    if (!map || !layer) {
      setMarkerAvailability(false, 'map-waiting');
      return false;
    }

    const selfPlayer = getPlayerMarkers().find(player => player.isSelf);
    const selfPose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
    if (selfPlayer) ensureSelfNavigationMarker(selfPlayer);
    const marker = findSelfMarker();
    if (!marker || !selfPose) {
      setMarkerAvailability(false, 'player-waiting');
      return false;
    }
    observeMarkerFreshness(
      selfPlayer?.marker ?? marker,
      selfPose,
      selfPlayer?.playerGroup);

    parseTransform();
    const mapRect = map.getBoundingClientRect();
    if (!map.clientWidth || !map.clientHeight || !mapRect.width || !mapRect.height) {
      setMarkerAvailability(false, 'player-waiting');
      return false;
    }

    setReactView ??= findReactViewDispatcher();
    if (initialZoomPending && view.scale < 2 && setReactView) {
      view.scale = 6;
    }
    initialZoomPending = false;
    if (smartZoomEnabled && !smartZoomSuspended
        && Date.now() - lastSmartZoomAt >= 1200) {
      const smartScale = chooseSmartFollowScale(selfSpeed, view.scale);
      if (Math.abs(smartScale - view.scale) >= 0.05) {
        view.scale = smartScale;
        lastSmartZoomAt = Date.now();
        lastMessage = '';
      }
    }
    applyTransform();
    applyMapOrientation();
    const svg = getMapSvg();
    const screenMatrix = svg?.getScreenCTM();
    if (!svg || !screenMatrix) {
      setMarkerAvailability(false, 'map-waiting');
      return false;
    }

    const svgPoint = svg.createSVGPoint();
    svgPoint.x = selfPose.x;
    svgPoint.y = selfPose.y;
    const markerScreenPoint = svgPoint.matrixTransform(screenMatrix);
    const followViewportWidth = map.clientWidth || mapRect.width;
    const followViewportHeight = map.clientHeight || mapRect.height;
    const followTarget = calculateFollowTarget(
      followViewportWidth,
      followViewportHeight,
      selfHeading,
      headingUp,
      lookAheadEnabled,
      selfSpeed) || {
        x: followViewportWidth / 2,
        y: followViewportHeight / 2,
        offsetPx: 0
      };
    const followOffsetX = followTarget.x - followViewportWidth / 2;
    const followOffsetY = followTarget.y - followViewportHeight / 2;
    const targetScreenX = mapRect.left + mapRect.width / 2 + followOffsetX;
    const targetScreenY = mapRect.top + mapRect.height / 2 + followOffsetY;
    const centerDelta = screenDeltaToMap(
      targetScreenX - markerScreenPoint.x,
      targetScreenY - markerScreenPoint.y
    );
    view.tx += centerDelta.x;
    view.ty += centerDelta.y;
    applyTransform();

    const centeredMarker = findSelfMarker() ?? marker;
    const centeredMarkerRect = centeredMarker.getBoundingClientRect();
    const centeredMapRect = map.getBoundingClientRect();
    centerErrorPx = Math.hypot(
      centeredMarkerRect.left + centeredMarkerRect.width / 2
        - (centeredMapRect.left + centeredMapRect.width / 2 + followOffsetX),
      centeredMarkerRect.top + centeredMarkerRect.height / 2
        - (centeredMapRect.top + centeredMapRect.height / 2 + followOffsetY)
    );
    setMarkerAvailability(true, reason);
    return true;
  };

  const isControl = target => target instanceof Element
    && Boolean(target.closest('button, a, input, select, textarea, [role="button"], [role="switch"]'));

  const screenDeltaToMap = (dx, dy) => {
    const coverScale = headingUp ? 1.43 : 1;
    if (!headingUp) return { x: dx / coverScale, y: dy / coverScale };
    const radians = selfHeading * Math.PI / 180;
    const scaledX = dx / coverScale;
    const scaledY = dy / coverScale;
    return {
      x: scaledX * Math.cos(radians) - scaledY * Math.sin(radians),
      y: scaledX * Math.sin(radians) + scaledY * Math.cos(radians)
    };
  };

  const clientToMapPoint = (clientX, clientY) => {
    if (!map) return null;
    const rect = map.getBoundingClientRect();
    const centerX = rect.left + rect.width / 2;
    const centerY = rect.top + rect.height / 2;
    const localDelta = screenDeltaToMap(clientX - centerX, clientY - centerY);
    const mapX = map.clientWidth / 2 + localDelta.x;
    const mapY = map.clientHeight / 2 + localDelta.y;
    const result = {
      mapX,
      mapY,
      x: (mapX - view.tx) / view.scale,
      y: (mapY - view.ty) / view.scale
    };
    try {
      const svg = getMapSvg();
      const matrix = svg?.getScreenCTM?.();
      if (svg && matrix) {
        const point = svg.createSVGPoint();
        point.x = clientX;
        point.y = clientY;
        const transformed = point.matrixTransform(matrix.inverse());
        if (Number.isFinite(transformed.x) && Number.isFinite(transformed.y)) {
          result.x = transformed.x;
          result.y = transformed.y;
        }
      }
    } catch {
      // The manual transform remains a safe fallback while the map is hydrating.
    }
    return result;
  };

  const positionFloatingPanel = (
    clientX,
    clientY,
    panelWidth,
    panelHeight,
    viewportWidth = window.innerWidth,
    viewportHeight = window.innerHeight) => {
    const gap = 12;
    const edge = 8;
    const left = clientX + gap + panelWidth <= viewportWidth - edge
      ? clientX + gap
      : Math.max(edge, clientX - gap - panelWidth);
    const top = clientY + gap + panelHeight <= viewportHeight - edge
      ? clientY + gap
      : Math.max(edge, clientY - gap - panelHeight);
    return { left, top };
  };

  const calculateOffscreenWaypointCue = (
    targetX,
    targetY,
    bounds,
    insets = { left: 30, top: 58, right: 30, bottom: 76 },
    visibilityMargin = 14) => {
    const left = Number(bounds?.left);
    const top = Number(bounds?.top);
    const right = Number(bounds?.right);
    const bottom = Number(bounds?.bottom);
    if (![targetX, targetY, left, top, right, bottom].every(Number.isFinite)
        || right <= left || bottom <= top) return { visible: false };

    const visibleMargin = Math.max(0, Number(visibilityMargin) || 0);
    if (targetX >= left + visibleMargin && targetX <= right - visibleMargin
        && targetY >= top + visibleMargin && targetY <= bottom - visibleMargin) {
      return { visible: false };
    }

    const safeLeft = Math.min(right, left + Math.max(0, Number(insets?.left) || 0));
    const safeTop = Math.min(bottom, top + Math.max(0, Number(insets?.top) || 0));
    const safeRight = Math.max(safeLeft, right - Math.max(0, Number(insets?.right) || 0));
    const safeBottom = Math.max(safeTop, bottom - Math.max(0, Number(insets?.bottom) || 0));

    const centerX = (safeLeft + safeRight) / 2;
    const centerY = (safeTop + safeBottom) / 2;
    const dx = Number(targetX) - centerX;
    const dy = Number(targetY) - centerY;
    if (Math.abs(dx) < 0.0001 && Math.abs(dy) < 0.0001) return { visible: false };

    const horizontalReach = dx >= 0 ? safeRight - centerX : centerX - safeLeft;
    const verticalReach = dy >= 0 ? safeBottom - centerY : centerY - safeTop;
    const horizontalScale = Math.abs(dx) < 0.0001
      ? Number.POSITIVE_INFINITY
      : horizontalReach / Math.abs(dx);
    const verticalScale = Math.abs(dy) < 0.0001
      ? Number.POSITIVE_INFINITY
      : verticalReach / Math.abs(dy);
    const scale = Math.max(0, Math.min(horizontalScale, verticalScale));
    const side = horizontalScale < verticalScale
      ? (dx >= 0 ? 'right' : 'left')
      : (dy >= 0 ? 'bottom' : 'top');
    return {
      visible: true,
      x: centerX + dx * scale,
      y: centerY + dy * scale,
      side,
      angle: (Math.atan2(dy, dx) * 180 / Math.PI + 90 + 360) % 360
    };
  };

  const buildTacticalPoint = (clientX, clientY) => {
    const raw = clientToMapPoint(clientX, clientY);
    if (!raw || !Number.isFinite(raw.x) || !Number.isFinite(raw.y)
        || raw.x < 0 || raw.x > 1000 || raw.y < 0 || raw.y > 1000) {
      return null;
    }
    const calibration = findReactMapProps()?.calibration;
    return {
      clientX,
      clientY,
      x: raw.x,
      y: raw.y,
      gridReference: mapPointToGridReference(raw.x, raw.y),
      world: mapToWorldPoint(calibration, raw.x, raw.y)
    };
  };

  const closeMapQuickActions = () => {
    quickActionPoint = null;
    if (quickActionMenu) quickActionMenu.style.display = 'none';
  };

  const hideCursorInspector = () => {
    if (cursorInspector) cursorInspector.dataset.visible = 'false';
  };

  const hideWaypointEdgeCue = () => {
    waypointEdgeCueVisible = false;
    waypointEdgeCueSide = '';
    if (waypointEdgeCue) waypointEdgeCue.dataset.visible = 'false';
  };

  const updateWaypointEdgeCue = () => {
    ensureTacticalUi();
    const svg = getMapSvg();
    const screenMatrix = svg?.getScreenCTM?.();
    if (!waypointEdgeCue || !map || !svg || !screenMatrix || !waypoint || streamerMode) {
      hideWaypointEdgeCue();
      return false;
    }

    const point = svg.createSVGPoint();
    point.x = Number(waypoint.x);
    point.y = Number(waypoint.y);
    const screenPoint = point.matrixTransform(screenMatrix);
    const rect = map.getBoundingClientRect();
    const cue = calculateOffscreenWaypointCue(
      screenPoint.x,
      screenPoint.y,
      rect,
      {
        left: Math.min(34, rect.width * 0.09),
        top: Math.min(58, rect.height * 0.14),
        right: Math.min(34, rect.width * 0.09),
        bottom: Math.min(76, rect.height * 0.18)
      });
    if (!cue.visible) {
      hideWaypointEdgeCue();
      return false;
    }

    const arrow = waypointEdgeCue.querySelector('.isle-mapper-waypoint-cue-arrow');
    const label = waypointEdgeCue.querySelector('.isle-mapper-waypoint-cue-label');
    const distance = waypointEdgeCue.querySelector('.isle-mapper-waypoint-cue-distance');
    waypointEdgeCue.style.left = `${cue.x - 13}px`;
    waypointEdgeCue.style.top = `${cue.y - 13}px`;
    waypointEdgeCue.dataset.side = cue.side;
    waypointEdgeCue.dataset.visible = 'true';
    arrow.style.transform = `rotate(${cue.angle.toFixed(1)}deg)`;
    label.textContent = String(waypoint.label || 'Active destination').slice(0, 64);
    if (waypointDistance != null && Number.isFinite(Number(waypointDistance))) {
      distance.textContent = `${Math.max(0, Number(waypointDistance)).toFixed(0)} MU`;
      distance.style.display = '';
    } else {
      distance.textContent = '';
      distance.style.display = 'none';
    }
    waypointEdgeCueVisible = true;
    waypointEdgeCueSide = cue.side;
    return true;
  };

  const finishQuickAction = (button, label) => {
    if (!quickActionMenu) return;
    for (const candidate of quickActionMenu.querySelectorAll('button')) {
      candidate.disabled = true;
    }
    button.dataset.success = 'true';
    button.textContent = label;
    window.setTimeout(() => closeMapQuickActions(), 620);
  };

  const ensureTacticalUi = () => {
    if (tacticalUiRoot?.isConnected) return tacticalUiRoot;
    tacticalUiRoot = document.querySelector('[data-isle-mapper-ui="true"]');
    if (tacticalUiRoot) tacticalUiRoot.remove();

    tacticalUiRoot = document.createElement('div');
    tacticalUiRoot.dataset.isleMapperUi = 'true';
    tacticalUiRoot.setAttribute('aria-label', 'Isley tactical tools');
    if (streamerMode) tacticalUiRoot.setAttribute('aria-hidden', 'true');

    cursorInspector = document.createElement('div');
    cursorInspector.dataset.isleMapperCursor = 'true';
    cursorInspector.dataset.visible = 'false';
    cursorInspector.setAttribute('aria-hidden', 'true');
    cursorInspector.innerHTML = `
      <div class="isle-mapper-cursor-grid" data-isle-mapper-cursor-grid>GRID --</div>
      <div class="isle-mapper-cursor-detail" data-isle-mapper-cursor-map>MAP --</div>
      <div class="isle-mapper-cursor-detail" data-isle-mapper-cursor-world>WORLD --</div>
      <div class="isle-mapper-cursor-hint">RIGHT-CLICK ACTIONS</div>`;

    quickActionMenu = document.createElement('div');
    quickActionMenu.dataset.isleMapperQuickActions = 'true';
    quickActionMenu.setAttribute('role', 'menu');
    quickActionMenu.setAttribute('aria-label', 'Map point actions');
    quickActionMenu.style.display = 'none';
    quickActionMenu.innerHTML = `
      <div class="isle-mapper-quick-title" data-isle-mapper-quick-title>GRID --</div>
      <div class="isle-mapper-quick-detail" data-isle-mapper-quick-detail>MAP --</div>
      <div class="isle-mapper-quick-actions">
        <button type="button" role="menuitem" data-isle-mapper-action="route">Route here</button>
        <button type="button" role="menuitem" data-isle-mapper-action="pin">Save pin here</button>
        <button type="button" role="menuitem" data-isle-mapper-action="copy">Copy location</button>
      </div>`;
    quickActionMenu.addEventListener('click', event => {
      const button = event.target instanceof Element
        ? event.target.closest('button[data-isle-mapper-action]')
        : null;
      if (!button || !quickActionPoint || streamerMode) return;
      const point = { ...quickActionPoint };
      const action = button.dataset.isleMapperAction;
      if (action === 'route') {
        if (setStaticWaypoint(point, `Grid ${point.gridReference} · quick waypoint`)) {
          lastMessage = '';
          notify('quick-route-set');
          finishQuickAction(button, 'ROUTE READY');
        }
      } else if (action === 'pin') {
        const style = pinTypes[pinType] || pinTypes.safe;
        if (addSavedPin(point.x, point.y, pinType, `${style.label} · Grid ${point.gridReference}`)) {
          drawSavedPins();
          lastMessage = '';
          notify('quick-pin-saved');
          finishQuickAction(button, `${style.label.toUpperCase()} PIN SAVED`);
        }
      } else if (action === 'copy') {
        const worldText = point.world
          ? ` | world ${point.world.x.toFixed(2)}, ${point.world.y.toFixed(2)}`
          : '';
        const clipboardText = `Isley location | Grid ${point.gridReference}`
          + ` | map ${point.x.toFixed(2)}, ${point.y.toFixed(2)}${worldText}`;
        window.chrome?.webview?.postMessage({
          type: 'isley-copy-location',
          kind: 'map-location',
          text: clipboardText
        });
        lastMessage = '';
        notify('quick-location-copied');
        finishQuickAction(button, 'LOCATION COPIED');
      }
    });

    waypointEdgeCue = document.createElement('div');
    waypointEdgeCue.dataset.isleMapperWaypointCue = 'true';
    waypointEdgeCue.dataset.visible = 'false';
    waypointEdgeCue.dataset.side = 'top';
    waypointEdgeCue.setAttribute('aria-hidden', 'true');
    waypointEdgeCue.innerHTML = `
      <svg class="isle-mapper-waypoint-cue-arrow" viewBox="0 0 26 26" aria-hidden="true">
        <path d="M13 1.5 24 23 13 18.5 2 23Z"
              fill="#ff6847" stroke="#fff7ed" stroke-width="1.25"
              stroke-linejoin="round" />
      </svg>
      <div class="isle-mapper-waypoint-cue-copy">
        <span class="isle-mapper-waypoint-cue-label">ACTIVE DESTINATION</span>
        <span class="isle-mapper-waypoint-cue-distance"></span>
      </div>`;

    tacticalUiRoot.append(cursorInspector, quickActionMenu, waypointEdgeCue);
    document.body.appendChild(tacticalUiRoot);
    return tacticalUiRoot;
  };

  const renderCursorInspector = point => {
    ensureTacticalUi();
    if (!cursorInspector || !point || streamerMode || quickActionPoint) {
      hideCursorInspector();
      return;
    }
    cursorInspector.querySelector('[data-isle-mapper-cursor-grid]').textContent =
      `GRID ${point.gridReference}`;
    cursorInspector.querySelector('[data-isle-mapper-cursor-map]').textContent =
      `MAP ${point.x.toFixed(1)}, ${point.y.toFixed(1)}`;
    cursorInspector.querySelector('[data-isle-mapper-cursor-world]').textContent = point.world
      ? `WORLD ${point.world.x.toFixed(0)}, ${point.world.y.toFixed(0)}`
      : 'WORLD CALIBRATION WAITING';
    const position = positionFloatingPanel(
      point.clientX,
      point.clientY,
      cursorInspector.offsetWidth || 148,
      cursorInspector.offsetHeight || 68);
    cursorInspector.style.left = `${position.left}px`;
    cursorInspector.style.top = `${position.top}px`;
    cursorInspector.dataset.visible = 'true';
  };

  const scheduleCursorInspector = event => {
    cursorInspectorPosition = { x: event.clientX, y: event.clientY };
    if (cursorInspectorFrame) return;
    cursorInspectorFrame = requestAnimationFrame(() => {
      cursorInspectorFrame = 0;
      const position = cursorInspectorPosition;
      cursorInspectorPosition = null;
      renderCursorInspector(position
        ? buildTacticalPoint(position.x, position.y)
        : null);
    });
  };

  const showMapQuickActions = point => {
    if (!point || streamerMode) return false;
    ensureTacticalUi();
    quickActionPoint = point;
    hideCursorInspector();
    const title = quickActionMenu.querySelector('[data-isle-mapper-quick-title]');
    const detail = quickActionMenu.querySelector('[data-isle-mapper-quick-detail]');
    const routeButton = quickActionMenu.querySelector('[data-isle-mapper-action="route"]');
    const pinButton = quickActionMenu.querySelector('[data-isle-mapper-action="pin"]');
    const copyButton = quickActionMenu.querySelector('[data-isle-mapper-action="copy"]');
    const style = pinTypes[pinType] || pinTypes.safe;
    title.textContent = `GRID ${point.gridReference}`;
    detail.textContent = point.world
      ? `MAP ${point.x.toFixed(1)}, ${point.y.toFixed(1)} · WORLD ${point.world.x.toFixed(0)}, ${point.world.y.toFixed(0)}`
      : `MAP ${point.x.toFixed(1)}, ${point.y.toFixed(1)}`;
    routeButton.textContent = 'Route here';
    pinButton.textContent = `Save ${style.label.toLowerCase()} pin here`;
    copyButton.textContent = 'Copy location';
    for (const button of quickActionMenu.querySelectorAll('button')) {
      button.disabled = false;
      button.dataset.success = 'false';
    }
    quickActionMenu.style.display = 'block';
    const position = positionFloatingPanel(
      point.clientX,
      point.clientY,
      quickActionMenu.offsetWidth || 204,
      quickActionMenu.offsetHeight || 132);
    quickActionMenu.style.left = `${position.left}px`;
    quickActionMenu.style.top = `${position.top}px`;
    return true;
  };

  const setZoomAt = (requestedScale, mapX, mapY, reason) => {
    if (!map || !layer) return false;
    parseTransform();
    const nextScale = Math.min(25, Math.max(1, Number(requestedScale)));
    if (!Number.isFinite(nextScale)) return false;
    if (smartZoomEnabled
        && ['wheel-zoom', 'button-zoom', 'preset-zoom'].includes(reason)) {
      smartZoomSuspended = true;
    }
    const anchorX = Number.isFinite(mapX) ? mapX : map.clientWidth / 2;
    const anchorY = Number.isFinite(mapY) ? mapY : map.clientHeight / 2;
    const localX = (anchorX - view.tx) / view.scale;
    const localY = (anchorY - view.ty) / view.scale;
    view.tx = anchorX - localX * nextScale;
    view.ty = anchorY - localY * nextScale;
    view.scale = nextScale;
    applyTransform();
    if (following) requestAnimationFrame(() => centerOnSelf(reason));
    lastMessage = '';
    notify(reason);
    return true;
  };

  const isClientInsideMap = (clientX, clientY) => {
    if (!map || !Number.isFinite(clientX) || !Number.isFinite(clientY)) return false;
    const bounds = map.getBoundingClientRect();
    return clientX >= bounds.left && clientX <= bounds.right
      && clientY >= bounds.top && clientY <= bounds.bottom;
  };

  const commitPendingMapAction = action => {
    if (!action || streamerMode) return false;
    const point = action.point;
    if (!point) return false;
    if (action.kind === 'no-go') {
      addNoGoTracePoint(point);
      return true;
    }
    if (action.kind === 'route') {
      if (!routePlanArmed || routeStops.length >= 12) return false;
      routeStops.push(point);
      drawRoutePlan();
      lastMessage = '';
      notify('route-stop-added');
      return true;
    }
    if (action.kind === 'measure') {
      if (!measurementArmed) return false;
      if (!measurementStart) {
        measurementStart = point;
        measurement = null;
        drawMeasurement();
        lastMessage = '';
        notify('measurement-start');
      } else {
        measurement = { start: measurementStart, end: point };
        measurementArmed = false;
        drawMeasurement();
        lastMessage = '';
        notify('measurement-set');
      }
      return true;
    }
    if (action.kind === 'pin') {
      if (!pinArmed || !addSavedPin(point.x, point.y, pinType)) return false;
      pinArmed = false;
      enhanceLiveMarkers();
      notify('pin-set');
      return true;
    }
    if (action.kind === 'waypoint') {
      if (!waypointArmed) return false;
      friendRouteName = '';
      packRouteActive = false;
      packOutlierRouteActive = false;
      activePinId = '';
      waypoint = {
        x: Math.min(1000, Math.max(0, point.x)),
        y: Math.min(1000, Math.max(0, point.y)),
        label: 'Map waypoint'
      };
      recentRoutes = recordRecentRoute(recentRoutes, waypoint, waypoint.label);
      waypointArmed = false;
      enhanceLiveMarkers();
      lastMessage = '';
      notify('waypoint-set');
      return true;
    }
    return false;
  };

  const cancelPointerGesture = (requestedPointerId = null) => {
    const capturedPointerId = Number.isFinite(Number(requestedPointerId))
      ? Number(requestedPointerId)
      : Number.isFinite(Number(pendingMapAction?.pointerId))
        ? Number(pendingMapAction.pointerId)
        : Number.isFinite(Number(drag?.pointerId))
          ? Number(drag.pointerId)
          : null;
    pendingMapAction = null;
    drag = null;
    if (map && capturedPointerId !== null) {
      try {
        if (map.hasPointerCapture(capturedPointerId)) {
          map.releasePointerCapture(capturedPointerId);
        }
      } catch { /* The browser may already have released capture. */ }
    }
    closeMapQuickActions();
    hideCursorInspector();
  };
  const onWindowBlur = () => cancelPointerGesture();
  const onPageHide = () => cancelPointerGesture();
  const onVisibilityChange = () => {
    if (document.hidden) cancelPointerGesture();
  };
  const onDocumentPointerOut = event => {
    if (event.relatedTarget === null) cancelPointerGesture(event.pointerId);
  };

  const onPointerDown = event => {
    if (!event.isTrusted || event.button !== 0 || !event.isPrimary || isControl(event.target)
        || !document.hasFocus() || !isClientInsideMap(event.clientX, event.clientY)) return;
    closeMapQuickActions();
    parseTransform();
    const actionKind = !streamerMode
      ? noGoTrace ? 'no-go'
        : routePlanArmed ? 'route'
        : measurementArmed ? 'measure'
        : pinArmed ? 'pin'
        : waypointArmed ? 'waypoint'
        : ''
      : '';
    if (actionKind) {
      const rawPoint = clientToMapPoint(event.clientX, event.clientY);
      const point = ['no-go', 'route', 'measure'].includes(actionKind)
        ? clampMapPoint(rawPoint)
        : rawPoint;
      pendingMapAction = point ? {
        kind: actionKind,
        point,
         pointerId: event.pointerId,
         startedAt: performance.now(),
         mapInteractionRevision,
         x: event.clientX,
         y: event.clientY,
         outside: false
       } : null;
      // Action clicks deliberately do not capture the pointer. A release
      // outside the map must never be retargeted back into a placement.
      event.preventDefault();
      event.stopImmediatePropagation();
      return;
    }
    drag = {
      pointerId: event.pointerId,
      x: event.clientX,
      y: event.clientY,
      tx: view.tx,
      ty: view.ty,
      moved: false
    };
    try { map.setPointerCapture(event.pointerId); } catch { }
    event.preventDefault();
    event.stopImmediatePropagation();
  };

  const onPointerMove = event => {
    if (isClientInsideMap(event.clientX, event.clientY)) scheduleCursorInspector(event);
    else hideCursorInspector();
    if (pendingMapAction && event.pointerId === pendingMapAction.pointerId) {
      pendingMapAction.outside = !isClientInsideMap(event.clientX, event.clientY);
      if (Math.abs(event.clientX - pendingMapAction.x)
          + Math.abs(event.clientY - pendingMapAction.y) > 5) {
        pendingMapAction.outside = true;
      }
      event.preventDefault();
      event.stopImmediatePropagation();
      return;
    }
    if (!drag || event.pointerId !== drag.pointerId) return;
    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    if (!drag.moved && Math.abs(dx) + Math.abs(dy) > 4) {
      drag.moved = true;
      following = false;
      notify('dragged');
    }
    if (drag.moved) {
      const localDelta = screenDeltaToMap(dx, dy);
      view.tx = drag.tx + localDelta.x;
      view.ty = drag.ty + localDelta.y;
      applyTransform();
    }
    event.preventDefault();
    event.stopImmediatePropagation();
  };

  const onPointerLeave = event => {
    if (pendingMapAction && event.pointerId === pendingMapAction.pointerId) {
      cancelPointerGesture(event.pointerId);
      return;
    }
    if (!drag && !quickActionPoint) hideCursorInspector();
  };

  const onLostPointerCapture = event => {
    if ((pendingMapAction && event.pointerId === pendingMapAction.pointerId)
        || (drag && event.pointerId === drag.pointerId)) {
      cancelPointerGesture(event.pointerId);
    }
  };

  const onContextMenu = event => {
    if (!event.isTrusted || streamerMode || isControl(event.target)) return;
    parseTransform();
    const point = buildTacticalPoint(event.clientX, event.clientY);
    if (!point || !showMapQuickActions(point)) return;
    event.preventDefault();
    event.stopImmediatePropagation();
  };

  const evaluateMapActionRelease = (action, event, context = {}) => {
    const movement = Math.abs(Number(event?.clientX) - Number(action?.x))
      + Math.abs(Number(event?.clientY) - Number(action?.y));
    const elapsed = Number(context.now) - Number(action?.startedAt);
    return Boolean(action
      && event?.type === 'pointerup'
      && event.isTrusted === true
      && event.button === 0
      && event.isPrimary === true
      && Number(event.pointerId) === Number(action.pointerId)
      && context.focused === true
      && context.hidden === false
      && context.inside === true
      && action.outside !== true
      && Number(action.mapInteractionRevision) === Number(context.mapInteractionRevision)
      && Number.isFinite(movement)
      && movement <= 5
      && Number.isFinite(elapsed)
      && elapsed >= 0
      && elapsed <= 5000);
  };

  const endPointer = event => {
    if (pendingMapAction && event.pointerId === pendingMapAction.pointerId) {
      const action = pendingMapAction;
      pendingMapAction = null;
      const validRelease = evaluateMapActionRelease(action, event, {
        now: performance.now(),
        focused: document.hasFocus(),
        hidden: document.hidden,
        inside: isClientInsideMap(event.clientX, event.clientY),
        mapInteractionRevision
      });
      if (validRelease) commitPendingMapAction(action);
      event.preventDefault();
      event.stopImmediatePropagation();
      return;
    }
    if (!drag || event.pointerId !== drag.pointerId) return;
    drag = null;
    try { map.releasePointerCapture(event.pointerId); } catch { }
    event.preventDefault();
    event.stopImmediatePropagation();
  };

  const onWheel = event => {
    if (!event.isTrusted || isControl(event.target) || !layer) return;
    closeMapQuickActions();
    hideCursorInspector();
    parseTransform();
    const nextScale = Math.min(25, Math.max(1, view.scale * (event.deltaY < 0 ? 1.15 : 1 / 1.15)));
    const point = clientToMapPoint(event.clientX, event.clientY);
    setZoomAt(nextScale, point?.mapX, point?.mapY, 'wheel-zoom');
    event.preventDefault();
    event.stopImmediatePropagation();
  };

  const unbindMap = () => {
    hideWaypointEdgeCue();
    pendingMapAction = null;
    drag = null;
    if (!map) return;
    map.removeEventListener('pointerdown', onPointerDown, true);
    map.removeEventListener('pointermove', onPointerMove, true);
    map.removeEventListener('pointerup', endPointer, true);
    map.removeEventListener('pointercancel', endPointer, true);
    map.removeEventListener('pointerleave', onPointerLeave, true);
    map.removeEventListener('lostpointercapture', onLostPointerCapture, true);
    map.removeEventListener('contextmenu', onContextMenu, true);
    map.removeEventListener('wheel', onWheel, true);
    closeMapQuickActions();
    hideCursorInspector();
    map.style.visibility = 'visible';
    map.removeAttribute('aria-hidden');
  };

  const applyStreamerPrivacy = () => {
    if (!map) return;
    if (tacticalUiRoot) {
      if (streamerMode) tacticalUiRoot.setAttribute('aria-hidden', 'true');
      else tacticalUiRoot.removeAttribute('aria-hidden');
    }
    if (streamerMode) {
      closeMapQuickActions();
      hideCursorInspector();
      hideWaypointEdgeCue();
    }
    map.style.visibility = streamerMode ? 'hidden' : 'visible';
    if (streamerMode) map.setAttribute('aria-hidden', 'true');
    else map.removeAttribute('aria-hidden');
  };

  const officialToggleMatchers = {
    locations: [/show locations/i],
    sanctuaries: [/show sanctuaries/i],
    migration: [/show migration zones/i],
    patrol: [/show patrol (?:zones|candidates)/i],
    food: [/toggle (?:all food spawns|food and resources)/i],
    heatmap: [/toggle player heatmap/i],
    selfTrail: [/show my path/i],
    friendTrails: [/show friends(?:'|’)? paths/i],
    shareLocation: [/share my location/i]
  };

  const normalizeControlText = value => String(value || '')
    .replace(/\s+/g, ' ')
    .trim();

  const describeOfficialToggle = element => {
    const directText = [
      element.getAttribute('aria-label'),
      element.getAttribute('title'),
      element.getAttribute('name'),
      element.textContent
    ].map(normalizeControlText).filter(Boolean).join(' ');
    const labelText = normalizeControlText(element.closest('label')?.textContent);
    const parentText = normalizeControlText(element.parentElement?.textContent);
    return [directText, labelText, parentText.length <= 120 ? parentText : '']
      .filter(Boolean)
      .join(' ');
  };

  const findOfficialToggle = key => {
    const matchers = officialToggleMatchers[key] || [];
    const candidates = Array.from(document.querySelectorAll(
      '[role="switch"], input[type="checkbox"], button[aria-checked]'));
    return candidates.find(element => {
      const description = describeOfficialToggle(element);
      return matchers.some(matcher => matcher.test(description));
    }) || null;
  };

  const readOfficialToggleState = element => {
    if (!element) return null;
    if (element instanceof HTMLInputElement && element.type === 'checkbox') {
      return Boolean(element.checked);
    }
    const ariaChecked = element.getAttribute('aria-checked');
    if (ariaChecked === 'true' || ariaChecked === 'false') return ariaChecked === 'true';
    const dataState = String(element.getAttribute('data-state') || '').toLowerCase();
    if (['checked', 'on', 'active'].includes(dataState)) return true;
    if (['unchecked', 'off', 'inactive'].includes(dataState)) return false;
    return null;
  };

  const refreshOfficialLayerStates = () => {
    let changed = false;
    for (const key of Object.keys(officialLayers)) {
      const next = readOfficialToggleState(findOfficialToggle(key));
      if (next !== officialLayers[key]) {
        officialLayers[key] = next;
        changed = true;
      }
    }
    if (changed) lastMessage = '';
    return changed;
  };

  const toggleOfficialLayer = (key, desiredState = null) => {
    if (key === 'shareLocation' || !(key in officialLayers)) return false;
    const control = findOfficialToggle(key);
    if (!control) return false;
    const current = readOfficialToggleState(control);
    const desired = typeof desiredState === 'boolean' ? desiredState : !current;
    if (current == null || current !== desired) control.click();
    requestAnimationFrame(() => {
      refreshOfficialLayerStates();
      lastMessage = '';
      notify('official-layer-changed');
    });
    return true;
  };

  const applyOfficialLayerPreset = preset => {
    const presets = {
      clean: {
        locations: false, sanctuaries: false, migration: false, patrol: false,
        food: false, heatmap: false, selfTrail: false, friendTrails: false
      },
      navigation: {
        locations: true, sanctuaries: true, migration: true, patrol: true,
        food: false, heatmap: false, selfTrail: false, friendTrails: false
      },
      survival: {
        locations: true, sanctuaries: true, migration: true, patrol: true,
        food: true, heatmap: true, selfTrail: false, friendTrails: false
      },
      all: {
        locations: true, sanctuaries: true, migration: true, patrol: true,
        food: true, heatmap: true, selfTrail: true, friendTrails: true
      }
    };
    const options = presets[preset];
    if (!options) return false;
    let foundAny = false;
    for (const [key, desired] of Object.entries(options)) {
      foundAny = toggleOfficialLayer(key, desired) || foundAny;
    }
    return foundAny;
  };

  const applyOfficialLayerState = options => {
    if (!options || typeof options !== 'object') return false;
    const allowedKeys = [
      'locations', 'sanctuaries', 'migration', 'patrol',
      'food', 'heatmap', 'selfTrail', 'friendTrails'
    ];
    let requestedAny = false;
    let foundAny = false;
    for (const key of allowedKeys) {
      if (typeof options[key] !== 'boolean') continue;
      requestedAny = true;
      foundAny = toggleOfficialLayer(key, options[key]) || foundAny;
    }
    return requestedAny && foundAny;
  };

  const locateMap = () => {
    const svg = Array.from(document.querySelectorAll('svg[viewBox="0 0 1000 1000"]'))
      .find(candidate => candidate.closest('.touch-none'));
    if (!svg) return { map: null, layer: null };
    let candidate = svg.parentElement;
    const nextLayer = candidate;
    while (candidate && !candidate.classList.contains('touch-none')) {
      candidate = candidate.parentElement;
    }
    return { map: candidate, layer: nextLayer };
  };

  const isolateMap = nextMap => {
    for (const element of document.querySelectorAll('.the-isle-mapper-map-path')) {
      element.classList.remove('the-isle-mapper-map-path');
    }
    for (const element of document.querySelectorAll('.the-isle-mapper-hidden')) {
      element.classList.remove('the-isle-mapper-hidden');
    }

    nextMap.dataset.isleMapperMap = 'true';
    let node = nextMap;
    while (node && node !== document.body) {
      node.classList.add('the-isle-mapper-map-path');
      const parent = node.parentElement;
      if (!parent) break;
      for (const sibling of parent.children) {
        if (sibling !== node && sibling.dataset?.isleMapperUi !== 'true') {
          sibling.classList.add('the-isle-mapper-hidden');
        }
      }
      node = parent;
    }
    document.body.classList.add('the-isle-mapper-map-path');
  };

  const attachMap = (nextMap, nextLayer) => {
    if (map === nextMap && layer === nextLayer) return;
    unbindMap();
    mapInteractionRevision += 1;
    map = nextMap;
    layer = nextLayer;
    drag = null;
    setReactView = null;
    lastDispatchedView = '';
    trailRoot = null;
    playerStyleRoot = null;
    playerStyleRenderSignature = '';
    encounterMemoryRoot = null;
    encounterMemoryRenderSignature = '';
    pinRoot = null;
    noGoAreaRoot = null;
    terrainCommunityHazardRoot = null;
    measurementRoot = null;
    routePlanRoot = null;
    mapGridRoot = null;
    mapGridRenderSignature = '';
    breadcrumbTrailRoot = null;
    breadcrumbTrailRenderSignature = '';
    learnedPassageRoot = null;
    learnedPassageRenderSignature = '';
    soundFinderRoot = null;
    soundFinderRenderSignature = '';
    landmarkLabelLayoutAt = 0;
    visibleLandmarkCount = 0;
    if (!map || !layer) return;
    parseTransform();
    setReactView = findReactViewDispatcher();
    initialZoomPending = view.scale < 2;
    isolateMap(map);
    applyMapOrientation();
    applyStreamerPrivacy();
    map.addEventListener('pointerdown', onPointerDown, { capture: true, passive: false });
    map.addEventListener('pointermove', onPointerMove, { capture: true, passive: false });
    map.addEventListener('pointerup', endPointer, { capture: true, passive: false });
    map.addEventListener('pointercancel', endPointer, { capture: true, passive: false });
    map.addEventListener('pointerleave', onPointerLeave, { capture: true, passive: true });
    map.addEventListener('lostpointercapture', onLostPointerCapture, { capture: true });
    map.addEventListener('contextmenu', onContextMenu, { capture: true, passive: false });
    map.addEventListener('wheel', onWheel, { capture: true, passive: false });
    ensureTacticalUi();

    for (const child of map.children) {
      if (child.querySelector?.('button')
          && /Reset/.test(child.textContent || '')
          && /Fullscreen|Exit/.test(child.textContent || '')) {
        child.style.display = 'none';
      }
    }
  };

  const tick = (reason = 'timer') => {
    const tickAt = Date.now();
    if (liteMode && reason === 'timer' && tickAt - lastControllerWorkAt < 850) {
      return;
    }
    lastControllerWorkAt = tickAt;
    if (Date.now() >= playerSnapshotNextAt) void fetchPlayerSnapshot(false);
    const located = locateMap();
    attachMap(located.map, located.layer);
    if (!map || !layer) {
      setMarkerAvailability(false, 'map-waiting');
      return;
    }
    isolateMap(map);
    refreshOfficialLayerStates();
    applyStreamerPrivacy();
    enhanceLiveMarkers();
    if (following) {
      centerOnSelf(reason === 'marker-response' ? 'marker-update' : 'following');
    }
    else {
      const available = refreshSelfMarkerFreshness();
      setMarkerAvailability(available, 'paused');
    }
    applyLandmarkLabelDensity(reason === 'marker-response');
    notify(following ? reason : 'paused');
  };

  const applyLiteMode = enabled => {
    const nextLiteMode = Boolean(enabled);
    const nextPollIntervalMs = nextLiteMode ? 1000 : 500;
    const nextPlayerSnapshotIntervalMs = nextLiteMode
      ? litePlayerSnapshotIntervalMs
      : fullPlayerSnapshotIntervalMs;
    liteMode = nextLiteMode;
    document.documentElement.dataset.isleyLite = String(liteMode);

    if (pagePollControl) {
      const pollWasBackedOff = Number(pagePollControl.delayMs) >= 5000;
      pagePollControl.targetDelayMs = nextPollIntervalMs;
      if (!pollWasBackedOff) pagePollControl.delayMs = nextPollIntervalMs;
    }
    fastPollIntervalMs = nextPollIntervalMs;
    if (fastPollDelayMs < 5000) fastPollDelayMs = nextPollIntervalMs;
    if (playerSnapshotNextAt - Date.now() > nextPlayerSnapshotIntervalMs) {
      playerSnapshotNextAt = Date.now() + nextPlayerSnapshotIntervalMs;
    }

    playerStyleRenderSignature = '';
    if (timer) {
      clearInterval(timer);
      timer = window.setInterval(tick, liteMode ? 1000 : 250);
    }
  };

  const api = {
    version: 78,
    exportPinShareCode,
    importPinShareCode,
    exportRouteShareCode,
    importRouteShareCode,
    exportNoGoShareCode,
    importNoGoShareCode,
    undoLastClear(kind) {
      return undoLastClear(String(kind || ''));
    },
    recenter() {
      following = true;
      smartZoomSuspended = false;
      initialZoomPending = view.scale < 2;
      const centered = centerOnSelf('recentered');
      notify(centered ? 'recentered' : 'player-waiting');
      return centered;
    },
    refreshSelfFreshness() {
      const available = refreshSelfMarkerFreshness();
      setMarkerAvailability(
        available,
        available ? 'self-freshness' : 'player-waiting');
      notify(available ? 'self-freshness' : 'player-waiting');
      return available;
    },
    setPlayerLabelsVisible(visible) {
      playerLabelsVisible = Boolean(visible);
      enhanceLiveMarkers();
      return playerLabelsVisible;
    },
    configure(options = {}) {
      if ('liteMode' in options) applyLiteMode(Boolean(options.liteMode));
      if ('playerLabelsVisible' in options) playerLabelsVisible = Boolean(options.playerLabelsVisible);
      if ('friendOnly' in options) friendOnly = Boolean(options.friendOnly);
      if ('markerStyle' in options) {
        const requestedMarkerStyle = String(options.markerStyle || '').toLowerCase();
        markerStyle = ['standard', 'contrast', 'shapes'].includes(requestedMarkerStyle)
          ? requestedMarkerStyle
          : 'standard';
        playerStyleRenderSignature = '';
      }
      if ('headingUp' in options) headingUp = Boolean(options.headingUp);
      if ('lookAheadEnabled' in options) {
        lookAheadEnabled = Boolean(options.lookAheadEnabled);
      }
      if ('smartZoomEnabled' in options) {
        const nextSmartZoomEnabled = Boolean(options.smartZoomEnabled);
        if (nextSmartZoomEnabled !== smartZoomEnabled) {
          smartZoomEnabled = nextSmartZoomEnabled;
          smartZoomSuspended = false;
          lastSmartZoomAt = 0;
        }
      }
      if ('terrainRouteStyle' in options) {
        setTerrainRouteStyle(options.terrainRouteStyle);
      }
      if ('terrainGapPolicy' in options) {
        setTerrainGapPolicy(options.terrainGapPolicy);
      }
      if ('terrainRouteEvidenceVisible' in options) {
        terrainRouteEvidenceVisible = Boolean(options.terrainRouteEvidenceVisible);
        drawRoutePlan();
      }
      if ('learnedPassageRoutingEnabled' in options) {
        setLearnedPassageRoutingEnabled(
          Boolean(options.learnedPassageRoutingEnabled));
      }
      if ('learnedPassageVisible' in options) {
        setLearnedPassageVisible(Boolean(options.learnedPassageVisible));
      }
      if ('streamerMode' in options) {
        const wasStreamerMode = streamerMode;
        streamerMode = Boolean(options.streamerMode);
        if (streamerMode) {
          playerSnapshotAbortController?.abort();
          postPlayerSnapshotState('unavailable');
        } else if (wasStreamerMode) {
          playerSnapshotNextAt = 0;
        }
      }
      if (streamerMode) {
        clearRouteAdvanceTimer();
        recentRoutes = [];
        pinArmed = false;
        noGoTrace = null;
        waypointArmed = false;
        cancelMeasurementCapture();
        if (routePlanArmed) resetRoutePlan(false);
        clearEncounterMemoryInternal(false);
        encounterMotionTracks.clear();
        nearestEncounterMotion = '';
        nearestEncounterRelativeSpeed = null;
        nearestEncounterInterceptSeconds = null;
        nearestEncounterMotionSampleCount = 0;
        resetPackSpreadMotion(true);
        soundFinderState = {
          mode: 'sound', target: 'water', first: null, second: null, estimate: null
        };
        soundFinderRenderSignature = '';
        drawSoundFinder();
      }
      drawNoGoAreas();
      drawTerrainCommunityHazards();
      drawLearnedPassages();
      if ('routeAdvanceDistance' in options) {
        routeAdvanceDistance = Math.min(50, Math.max(3, Number(options.routeAdvanceDistance) || 10));
      }
      if ('routeAutoReplan' in options) {
        routeAutoReplanEnabled = Boolean(options.routeAutoReplan);
        if (!routeAutoReplanEnabled && routeAutoReplanTimer) {
          clearTimeout(routeAutoReplanTimer);
          routeAutoReplanTimer = 0;
        }
      }
      if ('rememberLastPosition' in options) {
        rememberLastPositionEnabled = Boolean(options.rememberLastPosition);
        if (!rememberLastPositionEnabled) {
          lastLivePosition = null;
          lastPositionSavedAt = 0;
          try { localStorage.removeItem(lastPositionStorageKey); } catch { }
        }
      }
      if ('rangeRingsVisible' in options) rangeRingsVisible = Boolean(options.rangeRingsVisible);
      if (Array.isArray(options.rangeRingRadii)) {
        const requestedRadii = options.rangeRingRadii.map(Number);
        const requestedSignature = requestedRadii.join(':');
        if (['10:25', '25:50', '50:100'].includes(requestedSignature)) {
          rangeRingRadii = requestedRadii;
        }
      }
      if ('mapGridVisible' in options) {
        mapGridVisible = Boolean(options.mapGridVisible);
        mapGridRenderSignature = '';
      }
      if ('landmarkLabelDensity' in options) {
        const requestedDensity = String(options.landmarkLabelDensity || '').toLowerCase();
        landmarkLabelDensity = ['auto', 'focus', 'all'].includes(requestedDensity)
          ? requestedDensity
          : 'auto';
        landmarkLabelLayoutAt = 0;
      }
      if ('breadcrumbTrailVisible' in options) {
        breadcrumbTrailVisible = Boolean(options.breadcrumbTrailVisible);
        breadcrumbTrailRenderSignature = '';
      }
      if ('explorationEnabled' in options) {
        explorationEnabled = Boolean(options.explorationEnabled);
        explorationRenderSignature = '';
      }
      if ('trailSeconds' in options) {
        const requestedTrail = Number(options.trailSeconds);
        trailSeconds = [0, 15, 30, 60, 120].includes(requestedTrail) ? requestedTrail : 30;
      }
      if ('encounterMemorySeconds' in options) {
        const requestedMemory = Number(options.encounterMemorySeconds);
        encounterMemorySeconds = [0, 120, 300, 600].includes(requestedMemory)
          ? requestedMemory
          : 300;
        encounterMemoryRenderSignature = '';
        if (encounterMemorySeconds <= 0) clearEncounterMemoryInternal(false);
      }
      applyMapOrientation();
      applyStreamerPrivacy();
      enhanceLiveMarkers();
      if (following) centerOnSelf('configured');
      applyLandmarkLabelDensity(true);
      lastMessage = '';
      notify('configured');
      return true;
    },
    refreshPlayerSnapshot() {
      if (streamerMode || playerSnapshotDisposed) return false;
      playerSnapshotNextAt = 0;
      void fetchPlayerSnapshot(true);
      return true;
    },
    zoomBy(factor) {
      return setZoomAt(view.scale * Number(factor), null, null, 'button-zoom');
    },
    setZoom(scale) {
      return setZoomAt(Number(scale), null, null, 'preset-zoom');
    },
    armWaypoint() {
      if (streamerMode) return false;
      resetRoutePlan(true);
      pinArmed = false;
      cancelMeasurementCapture();
      friendRouteName = '';
      packRouteActive = false;
      packOutlierRouteActive = false;
      activePinId = '';
      waypointArmed = true;
      lastMessage = '';
      notify('waypoint-armed');
      return true;
    },
    armRoutePlan() {
      if (streamerMode) return false;
      resetRoutePlan(true);
      waypoint = null;
      waypointArmed = false;
      waypointDistance = null;
      waypointBearing = null;
      waypointCardinal = '';
      friendRouteName = '';
      packRouteActive = false;
      packOutlierRouteActive = false;
      activePinId = '';
      pinArmed = false;
      cancelMeasurementCapture();
      routePlanArmed = true;
      routePlanSource = 'manual';
      drawRoutePlan();
      updateWaypoint(getPlayerMarkers());
      lastMessage = '';
      notify('route-plan-armed');
      return true;
    },
    loadTerrainRoadNetwork(payload) {
      return loadTerrainRoadNetwork(payload);
    },
    setTerrainRouteStyle(style) {
      return setTerrainRouteStyle(style);
    },
    setTerrainGapPolicy(policy) {
      return setTerrainGapPolicy(policy);
    },
    setTerrainRouteEvidenceVisible(visible) {
      terrainRouteEvidenceVisible = Boolean(visible);
      drawRoutePlan();
      lastMessage = '';
      notify(terrainRouteEvidenceVisible
        ? 'terrain-route-evidence-on'
        : 'terrain-route-evidence-off');
      return terrainRouteEvidenceVisible;
    },
    saveCurrentSessionPassage() {
      return saveCurrentSessionPassage();
    },
    setLearnedPassageRoutingEnabled(enabled) {
      return setLearnedPassageRoutingEnabled(Boolean(enabled));
    },
    setLearnedPassageVisible(visible) {
      return setLearnedPassageVisible(Boolean(visible));
    },
    clearLearnedPassages() {
      return clearLearnedPassages();
    },
    cancelMapPointerGesture() {
      cancelPointerGesture();
      return true;
    },
    setTerrainWaterSafety(enabled) {
      return setTerrainWaterSafety(Boolean(enabled));
    },
    setTerrainCommunityHazardsEnabled(enabled) {
      return setTerrainCommunityHazardsEnabled(Boolean(enabled));
    },
    startTerrainCourse() {
      return startTerrainCourse();
    },
    startRoutePlan() {
      if (streamerMode || !routePlanArmed || routeStops.length < 2) return false;
      routePlanArmed = false;
      routePlanActive = true;
      routePlanComplete = false;
      routePlanSource = routePlanSource || 'manual';
      routeCurrentIndex = 0;
      routeAutoReplanAt = Date.now();
      setWaypointFromRouteStop();
      drawRoutePlan();
      updateWaypoint(getPlayerMarkers());
      lastMessage = '';
      notify('route-plan-started');
      return true;
    },
    startBreadcrumbReturn() {
      if (streamerMode) return false;
      const backtrackStops = buildBreadcrumbRouteStops();
      if (backtrackStops.length < 2) return false;
      resetRoutePlan(true);
      waypoint = null;
      waypointArmed = false;
      waypointDistance = null;
      waypointBearing = null;
      waypointCardinal = '';
      friendRouteName = '';
      packRouteActive = false;
      packOutlierRouteActive = false;
      activePinId = '';
      pinArmed = false;
      cancelMeasurementCapture();
      routePlanSource = 'breadcrumb';
      routeStops = backtrackStops;
      routeCurrentIndex = 0;
      routePlanActive = true;
      setWaypointFromRouteStop();
      drawRoutePlan();
      updateWaypoint(getPlayerMarkers());
      lastMessage = '';
      notify('breadcrumb-return-started');
      return true;
    },
    undoRouteStop() {
      if (!routePlanArmed || !routeStops.length) return false;
      routeStops.pop();
      routeCurrentIndex = 0;
      drawRoutePlan();
      lastMessage = '';
      notify('route-stop-undone');
      return true;
    },
    advanceRouteStop() {
      return advanceRouteStopInternal('route-manual-advanced');
    },
    clearRoutePlan() {
      const hadRoute = routePlanArmed || routePlanActive
        || routePlanComplete || routeStops.length > 0;
      if (hadRoute) {
        snapshotMapClear('route', {
          armed: routePlanArmed,
          active: routePlanActive,
          complete: routePlanComplete,
          source: routePlanSource,
          stops: routeStops.map(stop => ({ ...stop })),
          currentIndex: routeCurrentIndex
        });
      }
      resetRoutePlan(true);
      updateWaypoint(getPlayerMarkers());
      lastMessage = '';
      notify('route-plan-cleared');
      return hadRoute;
    },
    armMeasurement() {
      if (streamerMode) return false;
      if (routePlanArmed) resetRoutePlan(false);
      pinArmed = false;
      waypointArmed = false;
      measurement = null;
      measurementStart = null;
      measurementArmed = true;
      drawMeasurement();
      lastMessage = '';
      notify('measurement-armed');
      return true;
    },
    clearMeasurement() {
      const hadMeasurement = measurementArmed || Boolean(measurementStart) || Boolean(measurement);
      if (hadMeasurement) {
        snapshotMapClear('measurement', {
          armed: measurementArmed,
          start: measurementStart ? { ...measurementStart } : null,
          measurement: measurement
            ? {
                start: { ...measurement.start },
                end: { ...measurement.end }
              }
            : null
        });
      }
      measurementArmed = false;
      measurementStart = null;
      measurement = null;
      drawMeasurement();
      lastMessage = '';
      notify('measurement-cleared');
      return hadMeasurement;
    },
    setSoundFinder(state) {
      if (streamerMode) return false;
      soundFinderState = normalizeSoundFinderState(state);
      soundFinderRenderSignature = '';
      drawSoundFinder();
      lastMessage = '';
      notify('sound-finder-set');
      return Boolean(soundFinderState.first);
    },
    clearSoundFinder() {
      const hadSoundFinder = Boolean(
        soundFinderState.first || soundFinderState.second || soundFinderState.estimate);
      soundFinderState = {
        mode: 'sound', target: 'water', first: null, second: null, estimate: null
      };
      soundFinderRenderSignature = '';
      drawSoundFinder();
      lastMessage = '';
      notify('sound-finder-cleared');
      return hadSoundFinder;
    },
    routeSoundFinderEstimate() {
      if (streamerMode || !soundFinderState.estimate) return false;
      const targetLabel = soundFinderState.target.charAt(0).toUpperCase()
        + soundFinderState.target.slice(1);
      const routeLabel = soundFinderState.mode === 'scent'
        ? `${targetLabel} scent estimate - verify in game`
        : 'Sound estimate - verify by sound';
      const routed = setStaticWaypoint(
        soundFinderState.estimate,
        routeLabel,
        '',
        true,
        'estimate');
      if (routed) notify('sound-finder-routed');
      return routed;
    },
    routeMapPoint(payload) {
      if (streamerMode || !payload || typeof payload !== 'object') return false;
      const x = Number(payload.x);
      const y = Number(payload.y);
      if (!Number.isFinite(x) || !Number.isFinite(y)
          || x < 0 || x > 1000 || y < 0 || y > 1000) return false;
      const label = String(payload.label || 'Resource site')
        .replace(/[\u0000-\u001f\u007f]/g, ' ')
        .replace(/\s+/g, ' ')
        .trim()
        .slice(0, 56) || 'Resource site';
      const kind = normalizeWaypointKind(payload.kind) || 'resource';
      const routed = setStaticWaypoint({ x, y }, label, '', true, kind);
      if (routed) notify('resource-site-routed');
      return routed;
    },
    clearWaypoint() {
      resetRoutePlan(false);
      waypoint = null;
      friendRouteName = '';
      packRouteActive = false;
      packOutlierRouteActive = false;
      activePinId = '';
      waypointArmed = false;
      waypointDistance = null;
      waypointBearing = null;
      waypointCardinal = '';
      updateWaypoint(getPlayerMarkers());
      lastMessage = '';
      notify('waypoint-cleared');
      return true;
    },
    setPinType(type) {
      const requestedType = String(type || '').toLowerCase();
      if (!pinTypes[requestedType]) return false;
      pinType = requestedType;
      lastMessage = '';
      notify('pin-type');
      return true;
    },
    armPin(type) {
      if (streamerMode) return false;
      const requestedType = String(type || pinType).toLowerCase();
      if (!pinTypes[requestedType]) return false;
      pinType = requestedType;
      pinArmed = true;
      waypointArmed = false;
      if (routePlanArmed) resetRoutePlan(false);
      cancelMeasurementCapture();
      lastMessage = '';
      notify('pin-armed');
      return true;
    },
    cancelPin() {
      pinArmed = false;
      lastMessage = '';
      notify('pin-cancelled');
      return true;
    },
    dropPinAtSelf(type) {
      if (streamerMode) return false;
      const requestedType = String(type || pinType).toLowerCase();
      if (!pinTypes[requestedType]) return false;
      const players = getPlayerMarkers();
      const selfPlayer = players.find(player => player.isSelf);
      const pose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
      if (!pose || !addSavedPin(pose.x, pose.y, requestedType)) return false;
      pinType = requestedType;
      pinArmed = false;
      drawSavedPins();
      notify('pin-dropped');
      return true;
    },
    dropTimedPinAtSelf(type, minutes, label = '') {
      if (streamerMode) return false;
      const requestedType = String(type || pinType).toLowerCase();
      const requestedMinutes = Number(minutes);
      if (!pinTypes[requestedType] || !pinExpiryMinutes.includes(requestedMinutes)
          || requestedMinutes <= 0) return false;
      const players = getPlayerMarkers();
      const selfPlayer = players.find(player => player.isSelf);
      const pose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
      if (!pose || !addSavedPin(
        pose.x, pose.y, requestedType, String(label || ''), requestedMinutes)) return false;
      pinType = requestedType;
      pinArmed = false;
      drawSavedPins();
      notify('timed-pin-dropped');
      return true;
    },
    dropDeathPin() {
      if (streamerMode) return false;
      const players = getPlayerMarkers();
      const selfPlayer = players.find(player => player.isSelf);
      const livePose = selfPlayer ? readSelfPose(selfPlayer) : readModelSelfPose();
      const point = selectDeathMarkerPoint(
        livePose,
        rememberLastPositionEnabled ? resolveAnchorPoint(lastLivePosition) : null);
      if (!point) return false;

      const previousDeathIds = new Set(savedPins
        .filter(pin => pin.type === 'death')
        .map(pin => pin.id));
      savedPins = savedPins.filter(pin => pin.type !== 'death');
      if (activePinId && previousDeathIds.has(activePinId)) {
        activePinId = '';
        waypoint = null;
        waypointArmed = false;
        waypointDistance = null;
        waypointBearing = null;
        waypointCardinal = '';
        updateWaypoint(players);
      }
      if (!addSavedPin(point.x, point.y, 'death', 'Last death')) return false;
      pinType = 'death';
      pinArmed = false;
      drawSavedPins();
      notify(point.source === 'live'
        ? 'death-pin-dropped-live'
        : 'death-pin-dropped-last');
      return true;
    },
    routeToNewestPin() {
      const pin = savedPins.at(-1);
      return pin ? setPinRoute(pin.id) : false;
    },
    routeToPin(id) {
      return setPinRoute(id);
    },
    routeToNearestPinType(type) {
      return routeToNearestPinType(String(type || ''));
    },
    renamePin(id, label) {
      return renameSavedPin(String(id || ''), String(label || ''));
    },
    togglePinFavorite(id) {
      return toggleSavedPinFavorite(String(id || ''));
    },
    cyclePinExpiry(id) {
      return cycleSavedPinExpiry(String(id || ''));
    },
    cyclePinAlertRadius(id) {
      return cycleSavedPinAlertRadius(String(id || ''));
    },
    beginNoGoTrace(label) {
      return beginNoGoTrace(String(label || ''));
    },
    undoNoGoTracePoint() {
      return undoNoGoTracePoint();
    },
    finishNoGoTrace() {
      return finishNoGoTrace();
    },
    cancelNoGoTrace() {
      return cancelNoGoTrace();
    },
    selectNoGoArea(id) {
      return selectNoGoArea(String(id || ''));
    },
    cycleNoGoArea(direction) {
      return cycleNoGoArea(Number(direction));
    },
    removeNoGoArea(id) {
      return removeNoGoArea(String(id || ''));
    },
    reportBlockedTerrainPassage() {
      return reportBlockedTerrainPassage();
    },
    saveMeasuredSlopeAvoidance(
      worldStartX, worldStartY, worldEndX, worldEndY, label) {
      return saveMeasuredSlopeAvoidance(
        Number(worldStartX),
        Number(worldStartY),
        Number(worldEndX),
        Number(worldEndY),
        String(label || ''));
    },
    exportPinLibrary() {
      if (streamerMode || (!savedPins.length && !noGoAreas.length)) return '';
      return buildPinLibraryBackup(
        savedPins, findReactMapProps()?.calibration, Date.now(), noGoAreas);
    },
    previewPinLibraryImport(backupText) {
      return previewPinLibraryImport(String(backupText || ''));
    },
    importPinLibrary(backupText) {
      return importPinLibrary(String(backupText || ''));
    },
    routeToNamedPlace(query) {
      return routeToNamedPlace(String(query || ''));
    },
    startSharedRouteText(query) {
      return startSharedRoute(String(query || ''));
    },
    searchNamedPlaces(query, limit = 5) {
      return searchDestinations(String(query || ''), Number(limit));
    },
    searchDestinations(query, limit = 5) {
      return searchDestinations(String(query || ''), Number(limit));
    },
    routeToWorldCoordinates(worldX, worldY) {
      return routeToWorldCoordinates(Number(worldX), Number(worldY));
    },
    routeToRecentDestination(id) {
      if (streamerMode) return false;
      const route = recentRoutes.find(candidate => candidate.id === String(id || ''));
      if (!route || !setStaticWaypoint(route, route.label)) return false;
      notify('recent-destination-routed');
      return true;
    },
    routeBack() {
      if (streamerMode || recentRoutes.length < 2) return false;
      const previous = recentRoutes[1];
      if (!setStaticWaypoint(previous, previous.label)) return false;
      notify('previous-destination-routed');
      return true;
    },
    routeToSessionStart() {
      return routeToAnchor(sessionStartPosition, 'Session start');
    },
    routeToLastPosition() {
      if (!rememberLastPositionEnabled) return false;
      return routeToAnchor(lastLivePosition, 'Last live position');
    },
    saveNamedPlacePin(query, type) {
      return saveNamedPlacePin(String(query || ''), String(type || pinType).toLowerCase());
    },
    saveWorldCoordinatePin(worldX, worldY, type) {
      return saveWorldCoordinatePin(
        Number(worldX), Number(worldY), String(type || pinType).toLowerCase());
    },
    routeToFriend(name) {
      return setFriendRoute(String(name || ''));
    },
    routeToPackCenter() {
      return setPackCenterRoute();
    },
    routeToPackOutlier() {
      return setPackOutlierRoute();
    },
    dismissTacticalUi() {
      closeMapQuickActions();
      hideCursorInspector();
      return true;
    },
    routeToNearestFriend() {
      if (streamerMode) return false;
      const players = getPlayerMarkers();
      updateNearestFriend(players);
      if (!nearestFriendName) return false;
      return setFriendRoute(nearestFriendName);
    },
    startEscapeRoute() {
      return startEscapeRoute();
    },
    routeToNearestPlace() {
      if (streamerMode) return false;
      const players = getPlayerMarkers();
      updateNearestPlace(players);
      if (!nearestPlacePoint) return false;
      const routed = setStaticWaypoint(
        nearestPlacePoint,
        `Nearest place · ${nearestPlacePoint.label}`);
      if (routed) notify('nearest-place-routed');
      return routed;
    },
    resetActivityStats() {
      return resetSessionStats();
    },
    clearBreadcrumbTrail() {
      if (!breadcrumbSamples.length) return false;
      breadcrumbSamples = [];
      breadcrumbDistance = 0;
      breadcrumbTrailRenderSignature = '';
      if (routePlanSource === 'breadcrumb') {
        resetRoutePlan(true);
        updateWaypoint(getPlayerMarkers());
      }
      drawBreadcrumbTrail();
      lastMessage = '';
      notify('breadcrumb-trail-cleared');
      return true;
    },
    clearExploration() {
      if (!exploredSectors.size) return false;
      exploredSectors.clear();
      persistExploration();
      explorationRenderSignature = '';
      drawExplorationOverlay();
      lastMessage = '';
      notify('exploration-cleared');
      return true;
    },
    removeNewestPin() {
      const pin = savedPins.at(-1);
      return pin ? removeSavedPin(pin.id) : false;
    },
    removePin(id) {
      return removeSavedPin(id);
    },
    clearPins() {
      if (!savedPins.length) return false;
      snapshotMapClear('pins', {
        pins: savedPins.map(pin => ({ ...pin })),
        activePinId
      });
      savedPins = [];
      pinArmed = false;
      if (activePinId) {
        activePinId = '';
        waypoint = null;
        waypointArmed = false;
        waypointDistance = null;
        waypointBearing = null;
        waypointCardinal = '';
        updateWaypoint(getPlayerMarkers());
      }
      persistSavedPins();
      drawSavedPins();
      lastMessage = '';
      notify('pins-cleared');
      return true;
    },
    clearEncounterMemory() {
      if (streamerMode) return false;
      return clearEncounterMemoryInternal(true);
    },
    toggleOfficialLayer(key) {
      return toggleOfficialLayer(String(key || ''));
    },
    setOfficialLayer(key, desiredState) {
      return toggleOfficialLayer(String(key || ''), Boolean(desiredState));
    },
    applyLayerPreset(preset) {
      return applyOfficialLayerPreset(String(preset || '').toLowerCase());
    },
    applyLayerState(options) {
      return applyOfficialLayerState(options);
    },
    snapshot() {
      return {
        following,
        markerAvailable,
        freshnessAt,
        centerErrorPx,
        otherAnimalCount,
        friendAnimalCount,
        authorizedAnimalCount,
        playerLabelsVisible,
        markerStyle,
        liteMode,
        rangeRingsVisible,
        rangeRingRadii: [...rangeRingRadii],
        mapGridVisible,
        breadcrumbTrailVisible,
        ...buildExplorationState(),
        selfGridReference: !streamerMode && markerAvailable && lastMotionSample
          ? mapPointToGridReference(lastMotionSample.x, lastMotionSample.y)
          : '',
        friendOnly,
        headingUp,
        lookAheadEnabled,
        smartZoomEnabled,
        smartZoomSuspended,
        streamerMode,
        trailSeconds,
        selfHeading,
        waypointArmed,
        waypointActive: Boolean(waypoint),
        waypointDistance,
        waypointBearing,
        waypointCardinal,
        waypointLabel: waypoint?.label || '',
        waypointEdgeCueVisible,
        waypointEdgeCueSide,
        waypointTrend,
        waypointClosingRate,
        waypointProgressPercent,
        friendRouteName,
        packRouteActive,
        packOutlierRouteActive,
        ...buildRoutePlanState(),
        ...buildTripRouteRiskState(),
        ...buildNavigationEtaState(buildRoutePlanState()),
        ...buildMeasurementState(),
        pinArmed,
        pinType,
        pinCount: savedPins.length,
        activePinId,
        pinRoster: buildPinRoster(),
        ...buildNoGoAreaState(),
        ...buildAlertZoneState(),
        recentRoutes: buildRecentRouteRoster(),
        canRouteBack: !streamerMode && recentRoutes.length > 1,
        ...chooseMapScaleBar(
          map?.getBoundingClientRect?.().width || 0,
          view.scale),
        ...buildSessionStatsState(),
        ...buildDangerState(),
        ...buildRecoveryState(),
        nearestFriendName,
        nearestFriendDistance,
        nearestFriendBearing,
        nearestFriendCardinal,
        packFriendCount,
        packSpread,
        packSpreadMotion,
        packSpreadRate,
        packSpreadMotionSampleCount,
        packCourseState,
        packCourseSpeed,
        packCourseBearing,
        packCourseCardinal,
        packCourseSampleCount,
        packRadius,
        packCenterDistance,
        packCenterBearing,
        packCenterCardinal,
        packFarthestFriendName,
        packFarthestFriendDistance,
        packCenterAvailable: Boolean(packCenterPoint),
        encounterPlayerCount,
        nearestEncounterDistance,
        nearestEncounterBearing,
        nearestEncounterCardinal,
        nearestEncounterMotion,
        nearestEncounterRelativeSpeed,
        nearestEncounterInterceptSeconds,
        nearestEncounterMotionSampleCount,
        encounterWithin10,
        encounterWithin25,
        encounterWithin50,
        encounterMemorySeconds,
        encounterMemoryTrackCount,
        rememberedEncounterCount,
        rememberedEncounterNewestAgeMs,
        nearestRememberedEncounterDistance,
        nearestRememberedEncounterBearing,
        nearestRememberedEncounterCardinal,
        nearestPlaceName,
        nearestPlaceDistance,
        nearestPlaceBearing,
        nearestPlaceCardinal,
        officialLandmarkCount: officialLandmarkCatalog.length,
        friendRoster: friendRoster.map(friend => ({ ...friend })),
        markerResponseCount,
        markerResponseStatus,
        markerResponseOk,
        markerResponseSource,
        markerRequestUrl,
        fastPollIntervalMs,
        fastPollDelayMs,
        lastResponseIntervalMs,
        lastFastPollDurationMs,
        fastPollInFlight,
        playerSnapshotIntervalMs: liteMode
          ? litePlayerSnapshotIntervalMs
          : fullPlayerSnapshotIntervalMs,
        playerSnapshotNextAt,
        playerSnapshotInFlight,
        playerSnapshotFailures,
        pollControlPatched: Boolean(pagePollControl?.patched),
        markerNetworkCount,
        pollCallbackCount: Number(pagePollControl?.activeCallbacks) || 0,
        pollCallbackRuns: Number(pagePollControl?.callbackRuns) || 0,
        controllerInstallCount,
        lastMarkerNetworkAt,
        selfPositionAt,
        selfX,
        selfY,
        selfMapX: markerAvailable && lastMotionSample
          ? Number(lastMotionSample.x)
          : null,
        selfMapY: markerAvailable && lastMotionSample
          ? Number(lastMotionSample.y)
          : null,
        soundFinderReadingCount: soundFinderState.second
          ? 2
          : soundFinderState.first ? 1 : 0,
        soundFinderEstimateAvailable: Boolean(soundFinderState.estimate),
        trackFinderMode: soundFinderState.mode,
        trackFinderTarget: soundFinderState.target,
        selfBearing: (selfHeading + 90 + 360) % 360,
        selfSpeed,
        sessionDistance,
        officialLayers: { ...officialLayers },
        selfPoseSource,
        reactSynchronized: Boolean(setReactView),
        scale: view.scale,
        tx: view.tx,
        ty: view.ty
      };
    },
    dispose() {
      playerSnapshotDisposed = true;
      terrainWaterMaskLoadRevision += 1;
      terrainWaterMask = null;
      playerSnapshotAbortController?.abort();
      clearInterval(timer);
      clearTimeout(fastPollTimer);
      clearRouteAdvanceTimer();
      if (terrainCourseReplanTimer) clearTimeout(terrainCourseReplanTimer);
      terrainCourseReplanTimer = 0;
      if (noGoHighlightTimer) clearTimeout(noGoHighlightTimer);
      noGoHighlightTimer = 0;
      resetWaypointApproach();
      resourceObserver?.disconnect();
      if (cursorInspectorFrame) cancelAnimationFrame(cursorInspectorFrame);
      if (wrappedFetch && window.fetch === wrappedFetch && originalFetch) {
        window.fetch = originalFetch;
      }
      window.removeEventListener('blur', onWindowBlur, true);
      window.removeEventListener('pagehide', onPageHide, true);
      document.removeEventListener('visibilitychange', onVisibilityChange, true);
      document.removeEventListener('pointerout', onDocumentPointerOut, true);
      unbindMap();
      trailRoot?.remove();
      playerStyleRoot?.remove();
      encounterMemoryRoot?.remove();
      pinRoot?.remove();
      noGoAreaRoot?.remove();
      terrainCommunityHazardRoot?.remove();
      measurementRoot?.remove();
      routePlanRoot?.remove();
      mapGridRoot?.remove();
      breadcrumbTrailRoot?.remove();
      learnedPassageRoot?.remove();
      soundFinderRoot?.remove();
      explorationRoot?.remove();
      tacticalUiRoot?.remove();
      tacticalUiRoot = null;
      cursorInspector = null;
      quickActionMenu = null;
      quickActionPoint = null;
      waypointEdgeCue = null;
      waypointEdgeCueVisible = false;
      waypointEdgeCueSide = '';
      getMapSvg()?.querySelector(':scope > g[data-isle-mapper-self-navigation="true"]')?.remove();
      getMapSvg()?.querySelector(':scope > g[data-isle-mapper-range-rings="true"]')?.remove();
      getMapSvg()?.querySelector(':scope > g[data-isle-mapper-waypoint="true"]')?.remove();
      getMapSvg()?.querySelector(':scope > line[data-isle-mapper-waypoint-route="true"]')?.remove();
    }
  };

  window.addEventListener('blur', onWindowBlur, true);
  window.addEventListener('pagehide', onPageHide, true);
  document.addEventListener('visibilitychange', onVisibilityChange, true);
  document.addEventListener('pointerout', onDocumentPointerOut, true);
  window.__isley = api;
  // Compatibility alias keeps already-open legacy map sessions usable
  // during the Isley identity migration. Native commands use __isley.
  window.__theIsleMapper = api;
  timer = window.setInterval(tick, liteMode ? 1000 : 250);
  tick();
  notify('installed');
  return 'installed';
})();
