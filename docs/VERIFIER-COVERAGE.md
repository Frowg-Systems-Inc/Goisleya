# Verifier Coverage Gap Analysis

Date: 2026-07-28 (wave w1d, branch `swarm/w1d-testinfra`; refreshed wave w4,
branch `swarm/w4-contracts`; refreshed wave w9b, branch
`swarm/w9b-backend-verifiers`)
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
- **75** verifier projects exist under `Verification/` (61 before wave w4
  + 10 new in w4 + 4 new in w9b).
- Service assemblies (`Isley.Relay`, `Isley.ServerBridge`, `Isley.Telemetry`)
  are exercised by `TelemetryPlatformVerifier` plus the four wave-w9b backend
  verifiers below. The w9b verifiers compile the service sources directly
  into each verifier assembly (the standard verifier pattern) because
  `InternalsVisibleTo` names only `TelemetryPlatformVerifier` and product
  code was out of scope.

## Risk-ranked gaps

### P1 — uncovered before wave w1d, now closed

| Logic file | Risk area | Why it mattered | New verifier |
|---|---|---|---|
| `PortableConfigLogic.cs` | update / config | Portable config export carries a "no Steam tokens, TURN credentials, or relay secrets" promise plus bounded/sanitized parsing — a regression leaks secrets or accepts hostile configs | `PortableConfigVerifier` |
| `WhatsNewLogic.cs` | update / release | Parses shipped release notes; body cap and fallback honesty were unguarded | `WhatsNewVerifier` |
| `LiveHealthLogic.cs` | network / voice | Composes the live map/relay/voice health strip, including Streamer Mode redaction of voice quality | `LiveHealthVerifier` |
| `FocusModeSuggestLogic.cs` | UX / map | Category→focus mapping and "never changes anything automatically" advisory contract | `FocusModeSuggestVerifier` |
| `PressureCoachLogic.cs` | consent / privacy | Consent-roster coach distinguishes "not a broken connection" from real failures; once-only gating | `PressureCoachVerifier` |

### P2 — service-class gaps closed in wave w9b

**`Isley.Relay` (auth/network):**

- `TelemetryBroker` / `TelemetryRelay` fanout — viewer pseudonymization
  (`Pseudonym(...)`), per-viewer frame scoping. **Was the highest-risk
  remaining gap.** Closed by `RelayViewerPrivacyVerifier` (behavioral — the
  real broker runs against an in-memory fake `WebSocket`, no processes).
- `DeviceAuthorizationStore` + `SteamOpenIdClient` (`SteamAuthentication.cs`) —
  closed by `SteamDeviceAuthVerifier` (behavioral; Steam HTTP edge mocked via
  handler injection). One documented limit: the store's hardcoded 10-minute
  pending-device TTL cannot be exercised without a clock seam (product change),
  so expiry is proven on `AccessTokenService` instead (expired/future/skew
  matrix); the store's one-time exchange replay guard is fully proven.
- `IsleyBearerHandler` (`ViewerAuthentication.cs`) — closed by
  `RelayBearerAuthVerifier` (behavioral: the real handler runs
  `AuthenticateAsync` against crafted headers and tampered/crafted tokens).
- `RelayReadinessHealthCheck` — closed by `RelayViewerPrivacyVerifier`.
- `IsleyJson` — indirectly closed: it backs every serialization path in the
  four new verifiers (tokens, snapshots, privacy state).
- Covered today (via `TelemetryPlatformVerifier`): `BridgeSignatureVerifier`,
  `BridgeReplayGuard`, `AccessTokenService` (now also deep-covered by
  `SteamDeviceAuthVerifier`), `TelemetryFrameStore`, `PrivacyStore`,
  `SteamFriendResolver`, `RelayOptions`.

**`Isley.ServerBridge` (network):**

- `EvrimaRconClient` — closed by `EvrimaRconProtocolVerifier` (behavioral: a
  loopback fake-RCON TCP server captures the exact auth/command wire codec,
  drives auth rejection, EOF silence, oversized and NUL-padded responses, and
  proves transparent reconnect; the `IsPrivateOrLoopback` guard matrix is
  exercised across RFC1918/link-local/public edge addresses).
- `RconPollingWorker` — closed by `EvrimaRconProtocolVerifier` (behavioral:
  disabled/unconfigured guard rails, live RCON → parse → validate → frame
  pipeline, reconnect backoff doubling with recovery).
- `BridgeRuntime` (`BridgeFrameQueue`, `FrameFactory`, `BridgeRuntimeStatus`) —
  partially closed: queue dequeue, status snapshots, and source liveness are
  exercised by `EvrimaRconProtocolVerifier`; `FrameFactory` privacy clamping
  stays covered by `TelemetryPlatformVerifier`.
- **Still uncovered:** `RelayPublisher` / `RelayPublishWorker` (HMAC frame
  signing known-answer, conflict/`sequence_not_newer` handling, newest-frame
  retry coalescing, `LastSuccessfulPublishAt` updates) and `BridgeJson` —
  deferred; wave time-boxed. Both are instantiable (`HttpClient` handler
  injection mirrors `SteamDeviceAuthVerifier`), so no product change is needed
  to close them in a future wave. `BridgeOptions.PluginCapable` derivation
  also remains.
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

## Verifiers added in wave w9b

Four new **backend** projects under `Verification/`, closing the P2
service-class gaps. Because product code was out of scope this wave (and
`InternalsVisibleTo` names only `TelemetryPlatformVerifier`), each verifier
compiles the service sources directly into its own assembly — behavioral
coverage, not source greps. All registered in `Isley.sln` (project entries,
Debug/Release configs, `Verification` folder nesting, sequential GUIDs); the
full Release build passes with 0 errors and every verifier executable passes
locally:

1. `RelayViewerPrivacyVerifier` (`Isley.Relay`: `TelemetryBroker`,
   `TelemetryFrameStore`, `RelayMetrics`, `RelayReadinessHealthCheck`) — the
   real broker runs against an in-memory fake `WebSocket`. Proves per-viewer
   SHA-256 pseudonyms (known-answer derivation, re-derived per viewer), no raw
   Steam/entity IDs in payloads, self-only vitals/species/conditions, stranger
   filtering, friend labels, honest AI labels, same-server fanout, bounded
   one-frame queue coalescing (a stale sequence is provably skipped),
   hello/v2 negotiation (keyframe anchor then delta), control-frame bounds
   (malformed ignored, fragmented closed `MessageTooBig`, bad server id
   `PolicyViolation`), aggregate-only metrics, rate-limit rejection counting
   plus the 429/no-queue limiter wiring grep, and readiness health matrix.
2. `SteamDeviceAuthVerifier` (`DeviceAuthorizationStore`,
   `SteamOpenIdClient`, `AccessTokenService`) — device-code minting (64-hex
   device code, unambiguous `XXXX-XXXX` user code excluding 0/O/1/I), the
   pending→approved→consumed state machine, single-use exchange replay guard,
   user-code normalization, token expiry/skew/tamper/wrong-purpose guards via
   a same-key-ring crafted protector, OpenID login-URI pinning, and callback
   validation with the Steam HTTP edge mocked by handler injection (foreign
   endpoint/return-to/claimed-id rejected without any HTTP call).
3. `RelayBearerAuthVerifier` (`IsleyBearerHandler`) — the real
   `AuthenticationHandler` runs `AuthenticateAsync`: accept matrix (claims,
   case-insensitive scheme, trimming), `NoResult` for non-bearer attempts,
   reject matrix (empty, garbage, tampered, expired, future-issued,
   malformed-id, foreign-purpose), plus contract greps pinning AEAD
   unprotect (no string token compares) and `FixedTimeEquals` at the bridge
   signature edge.
4. `EvrimaRconProtocolVerifier` (`EvrimaRconClient`, `RconPollingWorker`,
   `BridgeFrameQueue`, `BridgeRuntimeStatus`) — a loopback fake-RCON TCP
   server captures the exact auth (`0x01`+password+`0x00`) and command
   (`0x02 0x77 0x00`) wire bytes; proves response round trip, transparent
   reconnect with identical re-authentication, password rejection,
   EOF-silence `IOException`, 1 MiB response cap, NUL trimming, the
   private/loopback address guard matrix (incl. 172.15/172.32 and other
   range-edge lookalikes), worker guard rails, the live RCON → parse →
   validate → frame pipeline with honest capabilities, and reconnect backoff
   (200→400→800 ms doubling, provably not flat-spin) with recovery.

Also fixed the wave-5 analyzer handoff in `Verification/`: CA1826 ×4
(`FieldGuideVerifier`, `MutationPlannerVerifier`: `.First()` → `[0]` on
`IReadOnlyList<T>`) and CA1847 ×1 (`HeadingConfidenceVerifier`:
`Contains(char)`). Build log confirms zero remaining CA1826/CA1847 sites.

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
