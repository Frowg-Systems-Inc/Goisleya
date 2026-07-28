# Isley Code Audit & Refactor — July 27, 2026

Scope: MainWindow structural refactor, cross-platform build verification,
verification-suite repair, and a security/robustness audit focused on
Isley Voice and the auto-updater.

## 1. MainWindow refactor (zero behavior change)

`BurntHud/MainWindow.xaml.cs` had grown to 41,178 lines. It is now split into
feature partial classes with **no code modified, added, or removed** — every
method was moved verbatim; all fields, nested settings types, the constructor,
and window lifecycle stay in `MainWindow.xaml.cs` so field-initializer order is
unchanged.

| File | Lines | Contents |
| --- | --- | --- |
| MainWindow.MapController.cs | 10,446 | Embedded map controller script install + mapper command bridge |
| MainWindow.MapTools.cs | 5,707 | Waypoints, routes, pins, no-go, measurement, terrain, layers, focus modes |
| MainWindow.Survival.cs | 4,056 | Survival assistant, incidents, timers, safe logout, restart watch, tactical brief/log |
| MainWindow.Planners.cs | 3,800 | Life run, growth, nest, mutation, spawn, zone, elder, diet, field guide |
| MainWindow.LiveNetwork.cs | 2,677 | Relay client, community servers, server session, universal coordinates, patch watch |
| MainWindow.WebView.cs | 2,393 | WebView2 lifecycle, snapshot message handling, URI trust checks |
| MainWindow.Voice.cs | 2,238 | Voice engine bridge, PTT, devices, rooms, route offers |
| MainWindow.OverlayChrome.cs | 1,880 | Lock, click-through, docking, resize, Lite Mode, Smart HUD, play focus |
| MainWindow.xaml.cs | 1,674 | Fields, nested types, constructor, lifecycle |
| MainWindow.FriendsEncounters.cs | 1,409 | Steam friends, rosters, encounter awareness, pack tools |
| MainWindow.Commands.cs | 1,311 | Quick Commands palette, onboarding, tools workspace navigation |
| MainWindow.Vitals.cs | 1,105 | Core vitals, visible HUD sensor, vitals trend, wound check |
| MainWindow.Settings.cs | 940 | Load/save/restore settings, storage status |
| MainWindow.Hotkeys.cs | 742 | Hotkey registration, studio, keyboard hooks |
| MainWindow.AimGuide.cs | 570 | Aim guide calibration and presentation |
| MainWindow.Updates.cs | 565 | Update checks, staging, portable config, what's new |
| MainWindow.Hub.cs | 99 | External hub link handlers |

Verified: line-multiset comparison against the original file shows **0 missing
and 0 duplicated lines**; `Isley.dll` compiles cleanly (Linux cross-build with
`-p:EnableWindowsTargeting=true`); all 53 verifier programs and all node
contract scripts pass.

## 2. Pre-existing verification failures found and fixed

These failed on unmodified `main` as well (confirmed via `git stash`):

1. `scripts/verify-controller.cjs` — identity check expected
   `<Product>Isley</Product>` inside `BurntHud.csproj`, but the property lives
   in `Directory.Build.props`. The script now reads both.
2. `scripts/verify-controller.cjs` — Quick Commands catalog drift check
   expected 106 entries; the catalog has 107 (a recent commit added a command
   without updating the contract). Updated to 107.
3. `Verification/SpawnPlanVerifier` — same drift, expected 105. Updated to 107.

Suite adjustments for the partial-class split: `verify-controller.cjs`,
`verify-independent-provider.cjs`, `verify-overlay-shell.cjs`, and the 18
verifier programs that grep MainWindow source now read the concatenation of
every `BurntHud/MainWindow*.cs` file.

## 3. Voice audit

Strong baseline: fail-closed PTT, room-key AES-GCM sealed signaling with AAD,
strict envelope/payload whitelists, glare-free offer ordering, serialized
signaling chain, bounded route offers with replay protection, per-connection
rate limiting, and opaque room/peer IDs. Live-tested on this environment
(two-peer relay, sealed-envelope forwarding, plaintext-signal refusal).

Fixed:

1. **Unhandled `JsonException` in the signaling server** — any peer sending a
   non-JSON text frame threw out of `ReceiveLoopAsync` past the
   `WebSocketException`-only catch. The socket is now closed gracefully with
   policy-violation 1008 and the room survives (live-tested: peer B stayed
   connected and received `peer-left`).
2. **Broadcast head-of-line blocking / sender teardown** — a stalled or broken
   recipient socket could block fan-out for the whole room and a send failure
   to one recipient tore down the *sender's* loop. Sends are now isolated
   per-recipient with a 10-second timeout; a stuck recipient is aborted so its
   own loop cleans up.
3. **No auto-reconnect after an unexpected voice drop** — auto proximity voice
   connected only at startup, on toggle clicks, or on lobby change. If the
   socket dropped (bundled host restart, network blip), voice stayed down
   until a manual reconnect, contradicting the documented always-on proximity
   behavior. The 1-second voice status timer now retries with a bounded
   cadence (first retry ~5 s after the drop, then every 20 s), gated on:
   voice enabled + auto-open + not Streamer Mode + the user did not disconnect
   + a session actually connected earlier. The bundled-host path also restarts
   the local signaling host if it exited.

Noted, not changed: peers self-report proximity positions (documented consent
model); non-browser clients can omit Origin (server is loopback-first and
rooms are unguessable 64-hex secrets derived from the room key).

## 4. Auto-updater audit

Strong baseline: pinned HTTPS manifest + download URLs with redirects disabled,
16 KB manifest bound, SHA-256 fixed-time verification, size/entry/expansion
caps, zip-slip and symlink refusal, staged-package validation, version
downgrade refusal, rollback backup with orphan cleanup, and write-probe before
starting.

Fixed:

1. **PID-reuse race in `Isley.Updater`** — the helper waited on the process id
   passed by the app; if Isley exited quickly and Windows reused the PID, the
   updater could wait up to 120 s on an unrelated process (or fail the update
   when that process outlived the timeout). The helper now verifies the
   process name is `Isley` before waiting and treats a mismatch as already
   closed.
2. **Unsanitized version text in the post-update toast** — `ConsumeUpdaterResult`
   displayed the `version` string from `last-result.json` verbatim. It is now
   validated against the release version pattern before display.

Noted, not changed: the beta-channel toggle is an intentional no-op until a
beta manifest ships (honest UI copy already states this); release integrity
still rests on the manifest host — publishing signed binaries would remove
that single point of trust (backlog).

## 5. Relay / Server Bridge (skim)

No urgent issues. Bearer-scheme auth, device-code Steam OpenID flow, fixed
rate limits, bounded bodies, fixed-time plugin-key comparison, loopback-gated
status UI, HTML-encoded outputs, and strict server-id validation are all in
place.

## 6. Follow-up work — completed July 27, 2026 (second pass)

1. **Map controller extracted to a real asset** — the 10,332-line embedded JS
   raw string moved from `MainWindow.MapController.cs` (now 126 lines) to
   `BurntHud/Map/isley-map-controller.js`, loaded once at runtime from the
   install folder (cached, fail-safe on read errors). Ships automatically via
   the existing `Map\**\*` content glob; added to the updater's staged-package
   required-file validation. Contract suite updated: `verify-controller.cjs`
   validates the packaged file directly (`new Function` syntax check kept),
   and the 7 verifiers referencing controller-internal tokens read it too.
2. **Map shell (`Map/index.html`) deep audit** — strong overall: all injected
   data is bounded and validated, labels use `textContent` with 32-char
   truncation, remote URLs pinned to `https://myislemap.com`, tiles capped
   (12 Lite / 25 normal, 16×16 grid max), zones ≤100 with radius/point caps,
   players ≤128, reduced-motion honored, offline schematic fallback automatic.
   One fix applied: failed high-resolution tile images are now removed on
   error instead of lingering as invisible holes that were never retried.
   Noted, not changed: full `render()` DOM rebuild per position frame is fine
   at the 128-player cap; the initial basemap `load`/`error` listeners attach
   before the href re-set, which is the intended online/offline probe.
3. **Code signing pipeline hardened** (`scripts/package-isley-1.3.ps1`,
   release workflow, `docs/WINDOWS_DEFENDER.md`):
   - **Azure Trusted Signing support** *(added here, later removed at the
     maintainer's request — see section 12)*, with the ACS
     timestamp endpoint applied automatically.
   - **CI-usable certificate signing**: new `ISLEY_CODE_SIGN_PFX_BASE64`
     materializes the PFX from a GitHub secret to a temp file and deletes it
     after signing — the previous path-only `ISLEY_CODE_SIGN_PFX` secret could
     never work on a fresh runner.
   - **Post-sign verification**: every binary is re-checked with
     `signtool verify /pa`; packaging refuses to zip unverifiable binaries.
   - Fixed shadowing of PowerShell's automatic `$args` variable in the
     signing loop; script syntax validated with PowerShell 7.4 parser.
   - Signing still runs before zipping/hashing so `Isley-release.json`
     SHA-256 covers signed binaries. Actual signing requires the maintainer to
     provision a Trusted Signing account or certificate (see updated
     `docs/WINDOWS_DEFENDER.md`).

## 8. Overlay-script CI checks — completed July 27, 2026 (third pass)

1. **ESLint on every push** — repo-root `package.json` + `eslint.config.mjs`
   (ESLint 9 flat config, browser globals) lint the three shipped overlay
   scripts: `Map/isley-map-controller.js`, `Voice/voice.js`,
   `Voice/voice-crypto.js`. `no-control-regex` disabled (sanitizers are
   intentional); empty catch blocks allowed.
2. **Real findings fixed by the first lint run**: write-only `serverUrl`
   state removed from `voice.js`; dead `searchNamedPlaces` and
   `staleSoundEnabled` removed from the controller; useless regex escape and
   redundant `Boolean()` cast fixed.
3. **Finding for maintainer review**: the DOM species-parsing chain
   (`parsePlayerSnapshotDocument`, `snapshotSpeciesCatalog`,
   `normalizeSnapshotSpeciesToken`, helpers) is **orphaned — nothing calls
   it**; the live path sources species from `getVitals()` instead. It is
   contract-covered by verify-controller's Live Species bridge contracts, so
   it was retained with explanatory `eslint-disable` comments rather than
   removed. Either wire it back up or retire it and its contracts.
4. **New `scripts/verify-overlay-scripts.cjs`** — syntax check
   (`new Function`) plus behavior contracts for all three scripts: controller
   identity/bridge tokens; voice fail-closed PTT, teardown mute, glare-free
   offer ordering, and **structural** assertions on both sealed-signaling
   fail-closed branches (capability guard and room-key derivation failure) so
   a regression in one branch cannot hide behind the duplicate literal in the
   other (weakness found by the test agent, fixed, and mutation-tested).
5. **CI wiring** — new fast `overlay-scripts` ubuntu job in
   `.github/workflows/verify.yml` (yarn install, ESLint, overlay contracts,
   controller contracts, voice-crypto contracts) runs on every push/PR
   alongside the Windows full-suite job; `scripts/verify-all.ps1` also runs
   the new script locally. Root `node_modules/` gitignored.

## 10. Species-chain retirement + Trusted Signing activation — July 27, 2026 (fourth pass)
*(The Trusted Signing portion of this pass was later removed at the
maintainer's request — see section 12. The species-chain retirement stands.)*

1. **Orphaned species DOM-parsing chain: RETIRED.** History proof: across all
   41 commits, `parsePlayerSnapshotDocument` and
   `lastKnownPlayerSnapshotIntervalMs` each appear exactly once — their own
   definitions. They never had a caller; live species data flows
   C# → local map `setSnapshot` → `getVitals()` → snapshot post →
   `ReadBoundedIdentifier(root, "speciesId", 32)`. Removed ~134 dead lines
   from the controller and retired the four corresponding JS-token contracts
   in `verify-controller.cjs` (all C#-side/live Species bridge contracts
   remain). Lint, contract scripts, and the full verifier suite stay green.
2. **Azure Trusted Signing activated in CI** (`release-package.yml`):
   OIDC `azure/login` + Trusted Signing client install + metadata generation,
   gated on `AZURE_TRUSTED_SIGNING_ACCOUNT` so unsigned packaging still works
   until secrets exist; `id-token: write` permission added. Provisioning
   runbook at `docs/AZURE_TRUSTED_SIGNING.md` (account, identity validation,
   certificate profile, federated credential, role assignment, six GitHub
   secrets, local-signing recipe, troubleshooting). The Azure-side
   provisioning itself requires the maintainer's subscription and identity
   documents and cannot be performed by tooling.

## 12. Azure Trusted Signing removed — July 27, 2026 (fifth pass)

At the maintainer's request, every Azure dependency was removed without
touching Isley itself (the app never referenced Azure — it was release
tooling only):

- `release-package.yml`: Azure OIDC login and Trusted Signing client steps
  deleted; `id-token: write` permission and the job-env secret mapping
  removed. The workflow is back to checkout → build → package → stage →
  upload.
- `package-isley-1.3.ps1`: the `/dlib`-`/dmdf` signing branch and the ACS
  timestamp switch removed. **Certificate-based signing remains fully
  functional** (PFX path, base64 PFX for CI, or cert-store thumbprint), along
  with all hardening from section 6 (base64 materialization, post-sign
  `signtool verify /pa` gate, `$args` fix).
- `docs/AZURE_TRUSTED_SIGNING.md` deleted; `docs/WINDOWS_DEFENDER.md`
  publisher guidance now covers OV/EV certificates only.
- Regression tests rewritten: they now assert Azure is fully absent from the
  workflow, packaging script, docs, and verify tooling, and that the
  certificate route + species-chain retirement remain intact.

Note: without any code signing, the Defender false-positive mitigations are
the operational ones in `docs/WINDOWS_DEFENDER.md` (submit each release to
Microsoft's false-positive portal; user-side exclusions).

## 11. Backlog / recommendations

- Extract the 10 k-line embedded controller script from
  `MainWindow.MapController.cs` into a packaged `.js` asset loaded at install
  time (kept as-is here to preserve zero behavior change; the contract scripts
  verify it either way).
- Code-sign `Isley.exe`/`Isley.Updater.exe` to reduce Defender ML
  false-positives (see `docs/WINDOWS_DEFENDER.md`) and to harden the update
  channel beyond manifest-host trust.
- `download-site` tooling requires Node ≥ 22.13; the packaged verification
  environment here has Node 20, so the website lint/test leg was skipped.
- Optional: split remaining >5 k-line partials (`MapTools`) further by section
  if they keep growing.
