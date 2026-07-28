# Isley — Working Notes (Emergent agent)

## Original problem statement
"Tell me what you can fix or improve about Isley" — user chose:
1) refactor the 41k-line `BurntHud/MainWindow.xaml.cs` into feature partials,
2) install .NET 8 and run builds/verifiers here, 3) full audits (security,
robustness, map shell) report-then-fix, 4) priority focus: voice and auto
updater.

## What Isley is
Windows WPF companion overlay for *The Isle* (net8.0-windows, WebView2), plus
cross-platform net8.0 services: Isley.Relay (Steam-auth telemetry relay),
Isley.ServerBridge (RCON/plugin → relay), Isley.VoiceServer (WebRTC signaling),
Isley.Updater (update helper), Isley.Telemetry (shared contracts). Extensive
in-repo verification suite: `Verification/*` (53 console verifiers) +
`scripts/verify-*.cjs` node contract scripts + `scripts/verify-all.ps1`.

## Environment facts (this container)
- .NET 8 SDK installed at /usr/share/dotnet (symlink /usr/local/bin/dotnet).
- WPF app COMPILES on Linux: `dotnet build BurntHud/BurntHud.csproj -c Release
  -p:EnableWindowsTargeting=true`. Expected benign failure: MSB3030 copy of
  `Isley.VoiceServer.exe` (Linux produces no .exe apphost). Isley.dll builds.
- Node 20 present; download-site needs Node >= 22.13 (skip its npm test leg).
- No frontend/backend/supervisor services; this is a desktop-app repo.

## Done — 2026-07-27
1. Split MainWindow.xaml.cs (41,178 lines) into 16 feature partials +
   1,674-line core (fields/nested types/ctor/lifecycle stay put to preserve
   field-initializer order). Verified 0 lost / 0 duplicated lines; clean build.
   Splitter script kept at /tmp/split_mainwindow.py (ephemeral).
2. Repaired verification suite for the split: 3 node scripts + 18 verifier
   Program.cs now read concatenated `BurntHud/MainWindow*.cs`.
3. Fixed pre-existing (on main) verification failures:
   - verify-controller.cjs Product identity check (reads Directory.Build.props now)
   - Quick Commands catalog drift: 106→107 (verify-controller.cjs), 105→107
     (SpawnPlanVerifier).
4. Voice fixes:
   - VoiceServer: malformed JSON now closes socket 1008 instead of unhandled
     JsonException; broadcast sends isolated per-recipient with 10 s timeout +
     Abort of stuck recipients.
   - Overlay: auto proximity voice auto-reconnects after unexpected drop
     (RefreshVoiceStatus 1 s timer, first retry ~5 s then 20 s cadence, gated
     on autoOpen/not streamer/not user-disconnect/was-connected).
     New fields `_voiceSessionConnectedThisSession`,
     `_voiceAutoReconnectNotBefore` in MainWindow.Voice.cs.
5. Updater fixes: PID-reuse guard (process name must be "Isley") in
   Isley.Updater; version string sanitized in ConsumeUpdaterResult toast.
6. Live-tested VoiceServer on Linux (two-peer relay, sealed envelope relay,
   plaintext refusal, malformed-JSON close, room survival): /tmp/test_voice_server.py.
7. Full audit report: docs/CODE-AUDIT-2026-07-27.md.

## Verification status
All 53 Verification/* programs PASS; verify-controller/independent-provider/
overlay-shell/voice-crypto PASS; BurntHud + all 5 service projects build.

## Done — 2026-07-27 (second pass)
8. Extracted embedded map controller: MainWindow.MapController.cs 10,446→126
   lines; JS now at BurntHud/Map/isley-map-controller.js (loaded+cached at
   runtime, in updater required-file list, ships via Map\**\* glob).
   verify-controller.cjs reads the .js directly; 7 verifiers
   (independent-provider + LiteMode/ResponsiveLayout/TerrainRoadNetwork/
   TripReadiness/UniversalCoordinate/WaterCrossing) append it to their source
   surface.
9. Map shell audit (index.html): PASSED review; one fix — failed hi-res tile
   images now removed on error.
10. Signing pipeline: Azure Trusted Signing (ISLEY_CODE_SIGN_DLIB/METADATA),
    ISLEY_CODE_SIGN_PFX_BASE64 for CI, signtool verify /pa gate, $args
    shadowing fix; workflow + WINDOWS_DEFENDER.md updated. pwsh 7.4 (arm64)
    installed at /opt/pwsh/pwsh for script validation.

## Done — 2026-07-27 (third pass)
11. Overlay-script CI: root package.json + eslint.config.mjs (ESLint 9 flat,
    yarn.lock committed); scripts/verify-overlay-scripts.cjs (syntax + voice
    fail-closed contracts with structural dual-branch sealed-signaling
    assertions); new 'overlay-scripts' ubuntu job in verify.yml; added to
    verify-all.ps1 node list; root node_modules gitignored.
12. Lint findings fixed: voice.js write-only serverUrl removed;
    controller dead code (searchNamedPlaces, staleSoundEnabled) removed;
    useless escape + Boolean cast fixed. Species DOM-parsing chain
    (parsePlayerSnapshotDocument etc.) is ORPHANED but contract-covered →
    retained with eslint-disable comments; flagged for maintainer.
13. Tested via testing_agent (iteration_1.json): all pass; its one minor
    finding (duplicate-literal contract false-pass) fixed + mutation-tested.

## Done — 2026-07-27 (fourth pass)
14. Species DOM-parsing chain RETIRED (history-proven never-called across all
    41 commits): ~134 dead lines removed from controller JS; 4 JS-token
    contracts retired from verify-controller.cjs (C#-side Live Species bridge
    contracts kept). Live path (getVitals → speciesId → ReadBoundedIdentifier)
    intact and contract-covered.
15. Azure Trusted Signing activated in release-package.yml: job-env-gated
    OIDC login + client install (pinned 1.0.95) + metadata generation feeding
    ISLEY_CODE_SIGN_DLIB/METADATA via GITHUB_ENV; id-token: write added.
    Runbook: docs/AZURE_TRUSTED_SIGNING.md (6 secrets, role assignment,
    federated credential, USA/Canada individual-validation caveat).
16. Testing agent iteration_2 found 2 CRITICAL workflow bugs (secrets in if:,
    step-env shadowing of GITHUB_ENV) — fixed; iteration_3 retest 100% pass
    (10/10 pytest at tests/test_release_signing_and_species_retirement.py).

## Done — 2026-07-27 (fifth pass)
17. Azure Trusted Signing REMOVED at maintainer's request (was release
    tooling only; Isley app never referenced Azure): workflow steps +
    id-token permission + job env mapping deleted from release-package.yml;
    /dlib branch + ACS timestamp switch removed from package-isley-1.3.ps1;
    docs/AZURE_TRUSTED_SIGNING.md deleted; WINDOWS_DEFENDER.md now cert-only.
    Certificate signing (PFX/base64/thumbprint + verify /pa gate) kept fully
    functional. tests/test_release_signing_and_species_retirement.py
    rewritten: 7/7 pass incl. full Azure-absence sweep; yaml + pwsh parse OK;
    contract scripts green.

## Done — 2026-07-27 (sixth pass: improvements batch + private servers)
18. PRIVATE SERVER smooth-connect package: new Quick Command
    'private-server-connect' (clipboard link → validate → fill → connect via
    new ConnectIsleyRelayAsync refactor in LiveNetwork.cs, with toasts);
    catalog now 108 (contracts bumped in verify-controller.cjs +
    SpawnPlanVerifier); docs/PRIVATE_SERVER_QUICKSTART.md (player 30s +
    operator 10min) linked from README.
19. Improvements implemented: palette fuzzy matching
    (FuzzyCommandPaletteScore: initials + subsequence); voice reconnect
    escalating backoff 5s→60s cap w/ reset; crash reporting in App.xaml.cs
    (3 handlers, IsleyData/Logs or LocalAppData, prune to 10); settings
    SchemaVersion=1; multi-monitor restore fix (virtual-screen clamp instead
    of primary WorkArea — real bug); map tile retry w/ self-scheduled bounded
    backoff (4s*attempts, max 3) after testing agent caught missing
    scheduling.
20. Verified: Isley.dll compiles; 53/53 verifiers; all node contract scripts;
    lint clean; pytest 17/17 (new /app/tests/test_private_server_improvements.py
    by testing agent, iteration_4). Windows-runtime behaviors (clipboard
    connect, reconnect timing, crash writes, monitor restore) reviewed but
    NOT executed — need a Windows smoke test.

## Done — 2026-07-27 (seventh pass: P1 batch)
21. Marker interpolation: index.html render() now keys/reuses player marker
    groups (playerNodes Map) positioned via style.transform with 900ms linear
    CSS transitions (disabled under prefers-reduced-motion); heat circles too.
22. Pin share codes: controller exportPinShareCode/importPinShareCode
    ('ISLEYPINS1.' + base64 JSON; prefix/length/whitelist/finite-clamp/dedupe/
    20-cap validation; Object.hasOwn whitelist after testing agent found a
    __proto__ prototype-chain bypass — fixed + test-verified); Quick Commands
    map-pins-share / map-pins-import (MapTools.cs).
23. Vitals projections: warnings now fire below 35% using boundary label
    ("WATER CRITICAL IN ABOUT 4M"); unchanged wording above 35%.
24. Encounter history: bounded 10-entry session history recorded at both
    alert-activation sites; encounter-history Quick Command copies it.
25. Catalog now 111 (contracts bumped in verify-controller.cjs,
    SpawnPlanVerifier, pytest). docs/WINDOWS_SMOKE_TEST.md created (10-point
    checklist covering both batches — Windows runtime still untested).
26. Verified: build OK; 53/53 verifiers; lint; node contract suites; pytest
    24/24 (new tests/test_p1_marker_pin_vitals_encounters.py from testing
    agent iteration_5, incl. extracted-runtime pin round-trip harness).

## Backlog (P1/P2) — remaining items from the 42-item improvement report
- P1 (remaining): route auto-replan on deviation; undo for destructive
  map-tool clears; route/no-go share codes (pins done); marker interpolation
  for the gateway map page (local shell done).
- P2: Relay /metrics endpoint; bridge capability+last-success surface; frame
  delta encoding; sensor confidence dots; timer journaling; stacked-guidance
  ranking; tactical log export; planner state unification; server multiplier
  presets; nest timer toasts; friend groups; watchlist quick-add from map;
  per-peer volume memory; voice quality surface; layout profiles; Lite Mode
  auto-suggest; hotkey conflict detection; update delta downloads; beta
  channel manifest; post-update boot-ok marker; diagnostics bundle export;
  heading confidence decay; clipboard capture tick sound; capture streak stats.
- P2: Node 22 for download-site; beta update channel no-op; OV/EV signing if
  ever wanted (docs/WINDOWS_DEFENDER.md).
