# AGENTS.md — Isley contributor playbook (human and AI agents)

Read this before touching the repo. It exists so every contributor — especially
automated agents — starts with the conventions that keep this codebase safe.

## What this is

**Isley**: Windows WPF companion overlay for *The Isle* (`BurntHud/`,
net8.0-windows, WebView2) plus cross-platform net8.0 services:
`Isley.Relay` (telemetry relay), `Isley.ServerBridge` (RCON/plugin → relay),
`Isley.VoiceServer` (WebRTC signaling), `Isley.Updater` (update helper),
`Isley.Telemetry` (shared contracts), and `download-site/` (Next.js).
MainWindow is split into feature partials (`MainWindow.<Area>.cs`).

## The verification culture (why CI is the referee)

- `Verification/*Verifier/` — 61 console verifiers that compile logic files
  directly and/or grep contracts. Adding a `BurntHud/*Logic.cs`? Add a
  verifier (mirror an existing one; register in `Isley.sln`: project entry +
  Debug/Release configs + Verification folder nesting).
- `scripts/verify-*.cjs` — node contract scripts (controller, overlay scripts,
  voice crypto, overlay shell, independent provider, map runtime, live update).
- `scripts/mutation-check-contracts.cjs` — proves the contracts actually fail
  when their protected behavior breaks. If you weaken a check it will know.
- `scripts/verify-all.ps1` — the full suite; CI runs it on windows-latest.
- `tests/*.py` — pytest suites (ledger, release signing, private server, p1).

### Local commands

```bash
# Full build (single node — parallel MSBuild nodes break in some environments)
dotnet build Isley.sln -c Release -p:EnableWindowsTargeting=true -m:1
# Node contracts (must all pass before every push)
node scripts/verify-controller.cjs
node scripts/verify-overlay-scripts.cjs
node scripts/verify-independent-provider.cjs
node scripts/verify-voice-crypto.cjs
node scripts/mutation-check-contracts.cjs
# Overlay lint
yarn lint:overlay    # ESLint --max-warnings 0 on the 3 shipped JS files
```

### Running the full suite locally

`scripts/verify-all.ps1` is the complete local referee: restore, Release
build, package audits, node contracts, the pytest suites, every
`Verification/*` console verifier, and the download-site lint/build/test.
The pytest suites resolve the repo root from `ISLEY_REPO_ROOT` when set,
otherwise from their own location (`tests/..`), so they run from any
checkout; they need `pytest` + `pyyaml` (`pip install pytest pyyaml`), node
on PATH, and the .NET SDK on PATH for the VoiceServer live test. A repo-local
`.venv-tools` venv is picked up automatically by `verify-all.ps1`; without
python/pytest that leg skips with a warning.

```powershell
.\scripts\verify-all.ps1                 # everything
python -m pytest tests/ -q               # just the pytest contract suites
dotnet run --project Verification\<Name> -c Release  # one focused verifier
```

## THE CONTRACT LEDGER (law)

The Quick Commands catalog lives in `CommandPaletteActions`
(`BurntHud/MainWindow.xaml.cs`). Its count is cross-asserted in **5 sites**:

1. `scripts/verify-controller.cjs` (`commandCatalogCount !== N`)
2. `Verification/SpawnPlanVerifier/Program.cs` (`== N`)
3. `tests/test_p1_marker_pin_vitals_encounters.py` (`== N`)
4. `tests/test_private_server_improvements.py` (`== N`)
5. `tests/test_private_server_improvements.py` (the `!== N` string literal)

Add/remove a command → bump **all 5 in the same commit**, and add the
dispatch `case` in `ExecuteCommandPaletteActionAsync`
(`MainWindow.Commands.cs`). A catalog entry without dispatch is a dead
command; dispatch without an entry is a crash. Neither merges.

## File etiquette for parallel work

- `MainWindow.xaml.cs`: append-only for catalog entries (end of array) and
  new nested settings types (end of file). Nothing else without coordination.
- `MainWindow.Settings.cs`: append new methods at the end only in parallel
  waves; single-owner otherwise.
- One agent per `MainWindow.*.cs` partial per wave; the map JS files
  (`isley-map-controller.js`, `index.html`, `voice.js`) are single-owner.
- New state: prefer session-scoped or bounded sidecar files over widening
  `MapperSettings` (schema-versioned; bump only with migration).

## Security posture (never weaken)

- Updater: pinned HTTPS, redirects disabled, 16 KB manifest bound, SHA-256
  fixed-time compare, size/entry/expansion caps, zip-slip/symlink refusal,
  staged validation, downgrade refusal, rollback backup, boot-ok marker.
- Voice: fail-closed PTT, AES-GCM sealed envelopes, unguessable room keys,
  per-connection rate limits. Peers self-report positions (consent model).
- Relay/bridge: bearer auth, loopback-gated status/metrics, bounded bodies,
  fixed-time key comparison. No secrets in repo — push protection is on.
- Honest UI: no fake states; unknown data shows "—"; fallback says so.

## PR rules

One concern per PR · CI green (verify suite + overlay contracts + download
site + CodeQL) before merge · no behavior change without tests proving
intent · report what you could NOT verify (e.g. Windows runtime behaviors).
See CONTRIBUTING.md for the human-facing version and docs/ for audit history.
