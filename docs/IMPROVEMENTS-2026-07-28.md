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
