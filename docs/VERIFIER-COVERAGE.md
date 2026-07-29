# Verifier Coverage Gap Analysis

Date: 2026-07-28 (wave w1d, branch `swarm/w1d-testinfra`)
Method: every `BurntHud/*Logic.cs` file and each service's core classes were
mapped to `Verification/*` coverage by (a) `Compile Include` entries in
verifier `.csproj` files (behavioral coverage — the logic is compiled and
executed) and (b) textual references in verifier `Program.cs` files and the
node contract scripts in `scripts/` (contract-level coverage — grepped, not
executed).

## Headline numbers

- **71** `BurntHud/*Logic.cs` files.
- **66** had behavioral verifier coverage before this change (**93%**).
- **5** had *zero* coverage (not compiled, not grepped anywhere). All five
  now have dedicated verifiers (see "Verifiers added this wave"), bringing
  logic-file coverage to **71/71 (100%)**.
- **58** verifier projects exist under `Verification/` (53 before this wave
  + 5 new).
- Service assemblies (`Isley.Relay`, `Isley.ServerBridge`, `Isley.Telemetry`)
  are partially exercised by `TelemetryPlatformVerifier`; several high-risk
  auth/network classes remain uncovered (below).

## Risk-ranked gaps

### P1 — uncovered before this wave, now closed

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

## Verifiers added this wave

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

## Mutation-testing the contract suite

`scripts/mutation-check-contracts.cjs` copies the files each contract
verifier reads into a temp tree, applies **one** targeted in-memory mutation
to a shipped overlay script (`isley-map-controller.js`, `voice.js`,
`voice-crypto.js`), and spawns the unmodified verifiers against the mutated
copy. It never touches shipped sources or the contract scripts.

**Result: 9/9 hard mutations caught, 4 documented false-pass weaknesses.**

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

### False-pass weaknesses found (probes — do NOT "fix" by weakening contracts)

1. **KDF domain-separation rekey is invisible.** Rekeying
   `isley-voice-signal-key-v1:` → `-v9:` in `voice-crypto.js` passes
   `verify-voice-crypto.cjs`, `verify-overlay-scripts.cjs`, and
   `verify-controller.cjs`, because every behavioral check is self-consistent
   within one copy of the code. A compatibility- or rollback-breaking key
   change would ship green. *Possible future hardening: a fixed test vector
   (expected key fingerprint) in `verify-voice-crypto.cjs`.*
2. **AES-GCM tag shortening is invisible.** `tagLength: 128` → `32` (both
   sites) passes the whole suite — round trips still succeed and the
   single-bit tamper check still fails at any tag length. *Possible future
   hardening: assert `tagLength: 128` literally, or attempt a truncated-tag
   forgery statistically.*
3. **Duplicated literal hides single-site regressions.** The glare-ordering
   contract string `localPeerId.localeCompare(remoteId) < 0` appears twice
   in `voice.js`; inverting only one site still passes the `includes` check.
   The suite already solves this pattern for the ROOM ENCRYPTION guards with
   structural regexes; glare ordering has no such structural assertion.
4. **Numeric-prefix substring weakness.** `noGoAreaMaximumVertices = 12` →
   `= 120` passes `verify-controller.cjs` because the asserted literal is a
   prefix of the mutant. *Possible future hardening: assert with a trailing
   delimiter (e.g. `= 12;`) or a numeric-boundary regex.*

The mutation harness treats these probes as expected-to-pass; if a future
contract improvement catches one, the harness fails loudly and the probe
must be reclassified as a hard mutation.

## Wire-in

- `scripts/verify-all.ps1` now runs `scripts\mutation-check-contracts.cjs`
  in its node-script step (after `verify-controller.cjs`).
- **CI note for the orchestrator** (`.github/workflows` is owned by another
  agent this wave): add a step running `node scripts/mutation-check-contracts.cjs`
  in the same job that runs the other node contract scripts, immediately
  after the `verify-controller.cjs` step. Node-only; no dotnet required.

## Environment gaps observed during this wave

- `dotnet` is not installed on this machine: the five new verifier projects
  could not be compiled locally. They mirror existing verifiers exactly
  (same SDK-style net8.0 csproj shape, same `Check(...)` idiom, same
  `AppContext.BaseDirectory` root resolution as `IsleyReleaseUpdateVerifier`),
  but **must be built (`Release`) and run in CI / on a machine with the
  .NET 8 SDK before merge**.
- `pytest` is not installed in the managed Python; `tests/` suites were not
  run (no `tests/` changes were needed this wave).
- `verify-map-runtime.cjs` / `verify-live-update-runtime.cjs` require a
  packaged running overlay (`verify-all.ps1 -IncludeRuntime` on Windows) and
  were not runnable here; all six static node contract scripts pass.
