# Isley Improvements — K3 Swarm Waves 0–1, July 28, 2026

Scope: repo migration to GitHub (Frowg-Systems-Inc/Goisleya), CI repair,
hygiene wave, and a four-agent parallel improvement wave. Orchestrated swarm:
1 orchestrator + 4 worker agents with exclusive file scopes, PR-per-scope,
CI-gated merges. Baseline before the wave: CI green, catalog 111, 53 verifiers.

## 1. Migration & CI repair (pre-wave)

- Full source uploaded from the Emergent export zip (370 files, ~25 MB).
- First CI run: `verify` (Windows full suite) passed immediately;
  `overlay-scripts` failed at setup — the zip lacked a root `yarn.lock`.
  Generated with Yarn classic and committed; pipeline fully green from run #2.

## 2. Wave 0 — repo hygiene (#1)

- Removed Emergent.sh platform artifacts (`.emergent/`, root `.gitconfig`,
  `memory/PRD.md`); history preserved in git.
- Stopped tracking `test_reports/` output; added to `.gitignore`.
- Added MIT `LICENSE`, `CONTRIBUTING.md` (codifies the contract-ledger rule),
  `SECURITY.md`, bug/feature issue templates, PR template.
- Issue board created (#2–#11, labels: swarm, wave-1..3, area:*, p1/p2).

## 3. Wave 1 — four parallel workstreams (#12–#15)

### A. Map tools P1 (#12, issues #2)
- **Undo for destructive clears** — snapshot before pin/route/measurement/no-go
  destructive actions; one undo level per collection with anti-clobber guards
  (route undo refuses if a new plan exists; no-go respects the 8-area cap;
  pins merge by id under the 20-cap); `map-undo-clear` command.
- **Route/no-go share codes** — `ISLEYROUTE1.` (2–12 stops) and `ISLEYNOGO1.`
  (≤8 areas, 3–12 vertices) codecs mirroring `ISLEYPINS1.` validation
  (prefix, 8192 cap, Object.hasOwn whitelist, finite clamp, dedupe,
  self-intersection refusal); share/import Quick Commands for both.
- **Route auto-replan** — >30 MU deviation from manual/shared routes triggers
  a 450 ms-debounced replan from live position, cadence-bounded to once/8 s,
  toggleable (`map-route-replan`, session-scoped, defaults ON).
- **Contract ledger: 111 → 117.** Agent registered the 6 new commands via a
  temporary supplemental array (the static catalog in MainWindow.xaml.cs was
  outside its scope); the orchestrator folded them into `CommandPaletteActions`
  and bumped all four ledger sites in the same commit, per CONTRIBUTING.md.

### B. Gateway map + download site (#13, issue #3)
- Gateway terrain dataset path (zones/resources) now interpolates: keyed node
  maps reuse DOM nodes across refreshes, transform-based positioning glides
  via the existing 900 ms CSS transitions, reduced-motion honored, duplicate
  labels keep exact node counts (7/12/61 zone + 953 resource contracts green).
- New `download-site` CI job: Node 22.13.0, `npm ci`, `npm run lint`, `npm test`.
- a11y/SEO: skip link, landmark correctness, section aria-labels,
  `:focus-visible` outline, extended reduced-motion; asserted by a new
  rendered-html test (4/4 site tests green).

### C. Backend services (#14, issue #4)
- **Relay `/metrics`** — loopback-gated counters (frames, rejections,
  connections, active bridges/viewers, uptime); aggregate-only payload;
  `Relay:MetricsPubliclyVisible` (default false) widens deliberately.
- **Bridge capability surface** — `PluginCapable`, `lastSuccessfulPublishAt`,
  and a `source` block in `/` + `/status` JSON and `/status/ui`.
- **Frame delta encoding** — viewer stream v2 (opt-in `hello` negotiation),
  delta frames + periodic keyframes (default 240), forced keyframe on
  reconnect/server-change/delta-failure; **v1 remains byte-identical default**
  — deployed viewers see zero change. Kill switch:
  `Relay:ViewerDeltaEncodingEnabled`. BurntHud opt-in is a follow-up.
- **Permanent VoiceServer live test** (`tests/test_voice_server_live.py`) —
  stdlib-only two-peer harness: sealed-envelope forwarding, plaintext refusal,
  malformed-JSON 1008 close, room survival; skips gracefully without dotnet.

### D. Test infrastructure (#15, issue #5)
- **Coverage-gap analysis** (`docs/VERIFIER-COVERAGE.md`): 71 `*Logic.cs`
  files; 66/71 covered → **71/71 (100%)** after 5 new verifiers
  (PortableConfig, WhatsNew, LiveHealth, FocusModeSuggest, PressureCoach),
  all registered in `Isley.sln`. Verifier count: 53 → 58.
- **Mutation harness** (`scripts/mutation-check-contracts.cjs`): 9/9 hard
  mutations caught (fail-open transmit gate, glare flips, sealed-guard weaken,
  identity removal, cap raises). **4 false-pass weaknesses documented** as
  expected-to-pass probes — candidates for contract strengthening:
  KDF domain-prefix rekey, AES-GCM tagLength 128→32, glare-ordering duplicate
  literal, no-go vertex-cap numeric-prefix blind spot.
- Wired into `verify-all.ps1` and (orchestrator integration commit) the
  `overlay-scripts` CI job.

## 4. Process notes

- Org OAuth App restrictions block the GitHub API merge endpoint; merges are
  performed locally (squash) and PRs closed with references. Push/PR/issue
  operations are unaffected.
- Worker environments lack the .NET SDK; all C# compiled and all 58 verifiers
  ran in CI (windows-latest) — every branch was CI-green before merge.
- Final main run: 3/3 jobs green (overlay-scripts incl. mutation check,
  verify, download-site).

## 5. Follow-ups queued

- Persist `_routeAutoReplanEnabled` into the settings schema (schema-versioned).
- BurntHud viewer opt-in to relay stream v2 (`hello` negotiation contract in
  `Isley.Telemetry/TelemetryDelta.cs`).
- Document `/metrics`, `hello`, and new `/status` fields in
  `docs/ISLEY_LIVE_NETWORK.md`.
- Strengthen contracts against the 4 documented false-pass weaknesses.
- Wave 2 (#6–#9): four P2 QoL batches. Wave 3 (#10–#11): Dependabot/CodeQL/
  secret scanning + updater delta/beta/boot-ok.

## 6. Wave 2 — P2 gameplay QoL (#16–#19, four agents, redistributed by file ownership)

All 13 features delivered; catalog 117 → **124**; verifiers 58 → **61**.

- **Planners (#16):** nest timer toasts (preset thresholds, once-per-timer),
  schema-versioned unified planner-state store with legacy migration, capture
  streak stats, server rate presets (Official 1x / Boosted 2x + custom).
- **Voice & friends (#17):** per-peer volume memory (opaque name-hash keys,
  LRU 64), true per-peer WebRTC quality surface (getStats RTT/jitter/loss,
  honest "—" fallback), named friend squads with group presence, right-click
  map watchlist add (validated bridge, cap 32).
- **Survival & vitals (#18):** tactical log export (bounded, honest
  truncation), timer journaling with "expired while away" reconciliation
  (alarms never re-fire), sensor confidence dots, heading confidence decay
  (held last-good never jumps). 3 new verifiers + 7 CoreVitals checks.
- **Chrome & settings (#19):** hotkey conflict detection (blocked + inline
  highlight), Lite Mode auto-suggest (timer-starvation signal, tappable
  toast, never auto-enables), layout profiles (≤8 named, programmatic UI),
  clipboard capture tick, diagnostics bundle export (redacted settings via
  the shared portable allowlist).

Process: three branches hit missing-using/ctor-arity compile errors (no
local dotnet); orchestrator patched all four in minutes (System.IO usings,
JsonElement.EnumerateArray, Thickness arity) and re-ran CI green before
merge. Conflicts at merge were exactly the predicted shared-file set
(catalog, nested settings types, ledger) — resolved as unions + ledger 124,
with dispatch wiring for the planner commands. Final main run: 3/3 jobs
green at catalog 124.

## 7. Follow-ups queued after Wave 2

- Verifiers for NestTimerAlertLogic, PlannerStateStoreLogic,
  ServerRatePresetLogic, VoicePeerVolumeLogic, VoicePeerQualityLogic,
  SteamFriendGroupLogic, EncounterWatchlistLogic, LayoutProfileLogic,
  LiteModeSuggestLogic, DiagnosticsBundleLogic (coverage doc refresh:
  VERIFIER-COVERAGE.md is a w1d snapshot).
- Stop dual-writing legacy LifeRun planner keys after one shipped version.
- Optional palette entry for tactical-log-export (+1 ledger).
- Heading decay on the map compass + position-copy surfaces.
- Wave 3 (#10–#11): Dependabot/CodeQL/secret scanning; updater delta/beta/boot-ok.

## 8. Wave 3 — security & release (#20–#21) — plan complete

- **Supply chain (#20):** Dependabot weekly for NuGet/npm×2/GitHub Actions;
  CodeQL (C# manual build on windows-latest + JavaScript) on push/PR + weekly;
  GitHub secret scanning **and push protection** enabled repo-wide.
- **Updater (#21):** post-update boot-ok marker (result stays pending until a
  healthy boot confirms; honest NOT CONFIRMED surface); real beta channel
  (second pinned manifest/URL pair, channel-equality enforced, stable
  fallback with honest copy); delta downloads v1 (optional manifest block,
  exact-base-match, same hash/size/zip-slip posture, delete list inside the
  verified zip and re-validated client- and updater-side, full-package
  fallback, rollback intact). Root of trust unchanged — no trust widening.
  Design doc: docs/ISLEY_UPDATER_DELTA.md.

Final state: catalog 124 · 61 verifiers · CI 3 jobs + CodeQL · issues #2–#11
all closed · all 21 PRs merged. Remaining follow-ups are listed in section 7
(verifier additions for Wave 2 logic files, legacy key retirement, optional
palette entry, heading decay on compass) — none blocking.

## 9. Plan v2 completion — Waves 4–7 (July 28–29, 2026)

- **Wave 4 (#35):** contract hardening — 4 false-pass weaknesses closed
  (KDF prefix, GCM tagLength, glare dual-site, vertex-cap boundary); mutation
  harness 9/9+4 → **13/13 caught, 0 false-passes**; 10 behavioral verifiers
  for Wave-2 logic; coverage **84/84 (100%)**, 71 verifiers.
- **Wave 5 (#36–#37):** overlay opts into relay **stream v2** (hello
  negotiation, validation-first delta apply, v1 byte-identical fallback, kill
  switch; RelayStreamV2Verifier); quality gates — analyzer baseline (172
  warnings documented, CA1050 fixed), measured .editorconfig (87% already
  clean), diff-scoped informational format-check CI, perf-budget harness.
- **W0 leftovers (#38):** legacy LifeRun planner-key dual-write retired
  (12 keys; RestoreLifeRun migration untouched; contracts re-aimed at store
  tokens in the same PR); live-network doc gained stream-v2 + metrics
  sections. AGENTS.md + branch protection (3 required checks) landed earlier.
- **Wave 6 (#39):** RUNTIME validation. Live VoiceServer test passes
  4/4 consecutive on real Windows (3 harness bugs fixed: Urls precedence,
  Origin allowlist, TCP coalescing — **zero product bugs**). Updater exe
  drill 4/4 (full/orphan sweep, delta delete-list, traversal refusal,
  source==target refusal). Packaged 1.3.6 launches healthy; portable mode
  wrote settings + the Wave-2 planner-state.json live.
- **Wave 7 (#40):** release engineering — version bump inputs (1.3.6→1.4.0
  via `minor`), notes-propagation root cause fixed (npm mangling; env-carried
  now, verbatim), GitHub Releases with SHA256SUMS, delta-chain continuity
  (downloads latest release zip → auto-delta next run), beta channel
  (Isley-release-beta.json + prerelease tags). **Released v1.4.0**:
  tag + client/server zips + checksums; manifest staged to main with
  verbatim notes (delta asset appears from the NEXT release onward, per
  design — no previous GitHub Release existed to diff against).
- Local toolchain: .NET 8 SDK + pytest venv operational on the maintainer
  machine (dotnet-isley.cmd / pytest-isley.cmd wrappers; -m:1 builds).

Plan v2 complete. Remaining optional follow-ups: whitespace burn-down in
MainWindow.Voice.cs/Commands.cs, analyzer backlog (CA1859/CA1861/CA1305),
`_lastServerStatus` dead-state decision, format-check promotion to blocking.
