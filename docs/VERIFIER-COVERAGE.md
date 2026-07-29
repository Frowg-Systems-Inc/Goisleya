# Verifier Coverage Gap Analysis

Date: 2026-07-28 (wave w1d, branch `swarm/w1d-testinfra`; refreshed wave w4,
branch `swarm/w4-contracts`)
Method: every `BurntHud/*Logic.cs` file and each service's core classes were
mapped to `Verification/*` coverage by (a) `Compile Include` entries in
verifier `.csproj` files (behavioral coverage — the logic is compiled and
executed) and (b) textual references in verifier `Program.cs` files and the
node contract scripts in `scripts/` (contract-level coverage — grepped, not
executed).

## Headline numbers

- **84** `BurntHud/*Logic.cs` files (71 before Wave 2; Wave 2 added 13).
- **84/84 (100%)** have behavioral verifier coverage: every logic file is
  compiled into at least one verifier project. Wave w4 closed the last 10
  uncovered Wave-2 files (see "Verifiers added in wave w4").
- **71** verifier projects exist under `Verification/` (61 before wave w4
  + 10 new).
- Service assemblies (`Isley.Relay`, `Isley.ServerBridge`, `Isley.Telemetry`)
  are partially exercised by `TelemetryPlatformVerifier`; several high-risk
  auth/network classes remain uncovered (below).

## Risk-ranked gaps

### P1 — uncovered before wave w1d, now closed

| Logic file | Risk area | Why it mattered | New verifier |
|---|---|---|---|
| `PortableConfigLogic.cs` | update / config | Portable config export carries a "no Steam tokens, TURN credentials, or relay secrets" promise plus bounded/sanitized parsing — a regression leaks secrets or accepts hostile configs | `PortableConfigVerifier` |
| `WhatsNewLogic.cs` | update / release | Parses shipped release notes; body cap and fallback honesty were unguarded | `WhatsNewVerifier` |
| `LiveHealthLogic.cs` | network / voice | Composes the live map/relay/voice health strip, including Streamer Mode redaction of voice quality | `LiveHealthVerifier` |
| `FocusModeSuggestLogic.cs` | UX / map | Category→focus mapping and "never changes anything automatically" advisory contract | `FocusModeSuggestVerifier` |
| `PressureCoachLogic.cs` | consent / privacy | Consent-roster coach distinguishes "not a broken connection" from real failures; once-only gating | `PressureCoachVerifier` |

### P2 — still uncovered (candidates for the next wave)

**`Isley.Relay` (auth/network):**

- `TelemetryBroker` / `TelemetryRelay` fanout — viewer pseudonymization
  (`Pseudonym(...)`), per-viewer frame scoping. **Highest-risk remaining
  gap**: a regression leaks one viewer's identity to another.
- `DeviceAuthorizationStore` + `SteamOpenIdClient` (`SteamAuthentication.cs`) —
  Steam device-code flow, user-code alphabet/normalization.
- `IsleyBearerHandler` (`ViewerAuthentication.cs`) — bearer token gate.
- `RelayReadinessHealthCheck`, `IsleyJson`.
- Covered today (via `TelemetryPlatformVerifier`): `BridgeSignatureVerifier`,
  `BridgeReplayGuard`, `AccessTokenService`, `TelemetryFrameStore`,
  `PrivacyStore`, `SteamFriendResolver`, `RelayOptions`.

**`Isley.ServerBridge` (network):**

- `EvrimaRconClient` — RCON wire protocol plus private/loopback address
  guarding (`IsPrivateOrLoopback`).
- `RconPollingWorker`, `RelayPublisher`, `BridgeRuntime`, `BridgeJson`.
- Covered today: `BridgeOptions` (via `TelemetryPlatformVerifier`).

**Contract-level only (grepped, never executed):**

- `BurntHud/IsleyUpdateClient.cs` — update download/verify/launch path;
  source-grepped by `IsleyReleaseUpdateVerifier` only. Consider a compile-in
  verifier for its pure helpers (manifest handling, path safety).
- `Isley.VoiceServer/Program.cs` — source-grepped by `verify-controller.cjs`.
- `Isley.Updater/Program.cs` — source-grepped by `IsleyReleaseUpdateVerifier`
  and `verify-independent-provider.cjs`.
- `BurntHud/GatewayResourceLogic.cs` **is** behaviorally covered
  (`GatewayResourceVerifier` compiles it as `GatewayResourceClient`).

## Verifiers added in wave w1d

Five new projects under `Verification/`, following the existing pattern
(`.csproj` linking the logic file + `Program.cs` with `Check(...)` guards),
all registered in `Isley.sln` (project entries, Debug/Release configs, and
`Verification` solution-folder nesting):

1. `PortableConfigVerifier` — schema envelope, secret-exclusion note,
   120 KB bound, control-character rejection, wrong schema/version/settings
   rejection, export round trip, preview summary.
2. `WhatsNewVerifier` — fallback honesty (null/blank/malformed/missing
   fields), trimming, version handling, 4000-char body cap, highlight
   gating, and a parse of the shipped `BurntHud/whats-new.json`.
3. `LiveHealthVerifier` — strip composition, relay-state mapping, voice
   NAT-failure honesty, warn/ok/idle tones, Streamer Mode redaction of the
   quality label.
4. `FocusModeSuggestVerifier` — full category→mode mapping, tone fallback,
   active-mode suppression, advisory copy, resolution against
   `FocusModeLogic.Definitions`.
5. `PressureCoachVerifier` — once-only gating, consent-roster honesty
   matrix, reassurance copy, unique coach IDs.

## Verifiers added in wave w4

Ten new projects under `Verification/`, same pattern (behavioral — each
compiles the logic file plus its transitive logic dependencies directly).
All registered in `Isley.sln` (project entries, Debug/Release configs, and
`Verification` solution-folder nesting); the full Release build passes with
0 errors and every new verifier executable passes locally:

1. `NestTimerAlertVerifier` — preset roster, threshold normalization,
   notify-mask one-shot gating, duration-boundary honesty.
2. `PlannerStateStoreVerifier` — path resolution, normalization clamps,
   foreign/newer/legacy schema gating, byte caps, atomic round trips.
3. `ServerRatePresetVerifier` — built-in preset stability, id/label
   sanitization, roster composition, cycling, bounded custom creation.
4. `VoicePeerVolumeVerifier` — pinned `isley-voice-peer-volume-v1` key
   domain, normalization, duplicate/timestamp handling, LRU pruning.
5. `VoicePeerQualityVerifier` — severity thresholds, placeholder honesty,
   suffix composition, measured-stats copy.
6. `SteamFriendGroupVerifier` — pinned `isley-friend-group-v1` id domain,
   name normalization, bounded roster/memberships, live-count honesty.
7. `EncounterWatchlistVerifier` — shared name normalization, cardinal and
   distance honesty, refresh-on-rewatch, bounded pruning.
8. `LayoutProfileVerifier` — name normalization and suffixing, size/mode
   clamping, roster caps and dedup, summary composition.
9. `LiteModeSuggestVerifier` — starvation sampling, ratio cap, all five
   suggestion gates, honest offer copy.
10. `DiagnosticsBundleVerifier` — newest-first selection, per-file and
    total byte caps, entry-name sanitization, schema stability.

## Mutation-testing the contract suite

`scripts/mutation-check-contracts.cjs` copies the files each contract
verifier reads into a temp tree, applies **one** targeted in-memory mutation
to a shipped overlay script (`isley-map-controller.js`, `voice.js`,
`voice-crypto.js`), and spawns the unmodified verifiers against the mutated
copy. It never touches shipped sources or the contract scripts.

**Result: 13/13 mutations caught, 0 false-pass weaknesses remaining**
(wave w4 closed the four documented below; they are now hard mutations).

Caught (verifier fails as required):

1. voice.js — transmit gate flipped to fail-open (`track.enabled = true`)
2. voice.js — glare-free offer ordering inverted at *both* call sites
3. voice.js — sealed-signaling capability guard no longer fails closed
4. isley-map-controller.js — `window.__isley` identity removed
5. voice-crypto.js — ICE candidate cap raised 4096→8192
6. voice-crypto.js — strict envelope field check relaxed
7. isley-map-controller.js — controller reuse gate inverted
8. isley-map-controller.js — no-go polygon vertex cap lowered 12→8
9. isley-map-controller.js — map interaction token raised 5s→60s
10. voice-crypto.js — room-key KDF domain prefix rekeyed `-v1:`→`-v9:`
    (closed in wave w4, see below)
11. voice-crypto.js — AES-GCM `tagLength` shortened 128→32 at both sites
    (closed in wave w4, see below)
12. voice.js — glare ordering inverted at only *one* of two call sites
    (closed in wave w4, see below)
13. isley-map-controller.js — no-go vertex cap raised 12→120
    (closed in wave w4, see below)

### False-pass weaknesses found in wave w1d — all closed in wave w4

1. **KDF domain-separation rekey was invisible.** `verify-voice-crypto.cjs`
   now pins `isley-voice-signal-key-v1:` literally (exactly one occurrence)
   *and* behaviorally: a key derived independently as
   SHA-256(domain prefix + normalized secret) must interchange with the
   module's key in both directions, so any rekey fails even though the
   mutated copy stays self-consistent.
2. **AES-GCM tag shortening was invisible.** `verify-voice-crypto.cjs` now
   pins `tagLength: 128` literally (exactly two occurrences) *and*
   behaviorally: sealed ciphertext length must equal plaintext + 16 bytes
   (the 128-bit tag), so a shortened tag fails.
3. **Duplicated literal hid single-site regressions.**
   `verify-overlay-scripts.cjs` now pins the glare-ordering guard to
   exactly two occurrences and asserts each call site structurally
   (welcome-roster branch and peer-joined branch), mirroring the existing
   dual-branch sealed-signaling assertions.
4. **Numeric-prefix substring weakness.** `verify-controller.cjs` now
   asserts the no-go vertex cap with a trailing-delimiter literal
   (`noGoAreaMaximumVertices = 12;`) plus a numeric-boundary regex
   (`\bnoGoAreaMaximumVertices = 12(?!\d)`), so `= 120` can no longer
   prefix-match.

The mutation harness still supports `expect: "pass"` probes for future
audits; none are registered today. If a future contract improvement catches
a new probe, reclassify it as a hard mutation in the same commit.

## Wire-in

- `scripts/verify-all.ps1` runs `scripts\mutation-check-contracts.cjs`
  in its node-script step (after `verify-controller.cjs`).
- **CI note for the orchestrator** (`.github/workflows` is owned by another
  agent this wave): add a step running `node scripts/mutation-check-contracts.cjs`
  in the same job that runs the other node contract scripts, immediately
  after the `verify-controller.cjs` step. Node-only; no dotnet required.

## Environment notes

- Wave w4 built the full solution locally (`Release`,
  `-p:EnableWindowsTargeting=true -m:1`, 0 errors) and ran all ten new
  verifier executables — all pass. The earlier wave's "dotnet not
  installed" caveat no longer applies on this machine.
- `pytest` is not installed in the managed Python; `tests/` suites were not
  run (no `tests/` changes were needed this wave).
- `verify-map-runtime.cjs` / `verify-live-update-runtime.cjs` require a
  packaged running overlay (`verify-all.ps1 -IncludeRuntime` on Windows) and
  were not runnable here; all six static node contract scripts pass.
