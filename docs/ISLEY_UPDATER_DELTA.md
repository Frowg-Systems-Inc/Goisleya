# Isley Updater — Delta Packages, Beta Channel, and Boot Confirmation

Design doc for the July 28, 2026 Wave-3 updater work (issue #11). All three
features preserve the existing trust model: the pinned manifest host remains
the single root of trust, every artifact is SHA-256 verified with fixed-time
comparison, redirects stay disabled, and every safety cap from the July 27
audit applies unchanged to the new paths.

## 1. Boot-ok marker

**Problem.** `last-result.json` only proves the updater helper finished copying
files. It says nothing about whether the updated build actually runs.

**Design.**

- The app treats a successful update result as *pending* until it reaches a
  healthy steady state: the main window finished loading **and** the
  survival/vitals tick timer is running (it starts immediately after the first
  forced `UpdateCoreVitals` pass in `MainWindow_Loaded`).
- `ConsumeUpdaterResult` keeps `last-result.json` on disk while confirmation
  is pending, so a crash during the first boot leaves the pending state intact
  for the next launch instead of silently dropping it.
- `ConfirmUpdatedBootAsync` waits 4 seconds, then either:
  - writes a bounded boot-ok marker (`last-boot-ok.json`, ≤ 1 KB,
    temp-file + atomic move) containing `{ "version", "confirmedAt" }`, and
    toasts `ISLEY UPDATED TO vX · BOOT CONFIRMED`; or
  - toasts `ISLEY UPDATED TO vX · BOOT NOT CONFIRMED` and sets the update
    status line to `UPDATED TO vX · BOOT NOT CONFIRMED · WATCH FOR ISSUES`.
- Marker location: `IsleyData/last-boot-ok.json` in portable mode, otherwise
  `%LocalAppData%\Isley\last-boot-ok.json`.
- Reads are fully validated (version pattern, parseable timestamp, size cap)
  and never throw; a relaunch after a confirmed boot reports
  `BOOT CONFIRMED` immediately from the marker.

**Threat notes.** The marker is a local diagnostic, not a trust decision; it
cannot gate or roll back updates. Version strings are pattern-validated before
display, as before.

## 2. Beta channel

**Problem.** The beta toggle was an intentional no-op.

**Design.**

- New pinned endpoints in `IsleyReleaseLogic`:
  `Isley-release-beta.json` and `Isley-Windows-x64-beta.zip` on the same
  trusted host (`isley-download.gmith.chatgpt.site`).
- `IsleyUpdateClient.FetchReleaseAsync(preferBeta, …)` fetches the beta
  manifest only when the toggle is on. Every check is identical to stable:
  no redirects, exact pinned response URI, 16 KB manifest bound, channel
  string must equal `"beta"`, version/publishedAt/SHA-256/size validation,
  download URL pinned to the beta zip.
- **Fallback:** any beta failure (404, network, invalid manifest) falls back
  to the stable manifest. Cancellation is never swallowed. The fallback is
  surfaced honestly: `· BETA CHANNEL UNAVAILABLE · SHOWING STABLE` in the
  status line and in the manual-check toast.
- UI copy updated: `BETA CHANNEL ON · BETA RELEASES PREFERRED WHEN PUBLISHED`;
  toggling triggers an immediate recheck so the status line always reflects
  the real channel in use.

**Threat notes.** Beta widens the set of accepted manifests to a second file
on the *same* pinned host — the root of trust is unchanged. An attacker
without host control cannot influence either channel; an attacker with host
control already owned the stable channel.

## 3. Delta packages (file-level)

**Problem.** Every update downloads the full ~9 MB zip even when a handful of
files changed.

**Format.** Not binary diffing — a plain zip of whole files, auditable by
hand:

```
Isley-delta-<from>-<to>.zip
├── <changed or new files, same relative layout as a full package>
├── Updater/Isley.Updater.exe          (always included)
├── Updater/Isley.Updater.dll          (always included)
└── isley-delta-manifest.json
    { "format": 1, "fromVersion": "X", "toVersion": "Y",
      "deletedFiles": ["Voice/old.js", ...] }
```

The delete list lives **inside** the zip so it is covered by the same SHA-256
as the payload. The updater helper is always included so the install-side
delete step runs the verified *new* helper, never a stale installed one that
does not understand `--mode delta`.

**Manifest.** The release manifest gains an optional block:

```json
"delta": {
  "fromVersion": "1.2.0",
  "url": "https://isley-download.gmith.chatgpt.site/Isley-delta-1.2.0-1.3.0.zip",
  "sha256": "…",
  "bytes": 123456
}
```

Validation in `IsleyReleaseLogic.ParseDeltaOffer`: `fromVersion` matches the
release version pattern and is strictly older than the release; the URL is
HTTPS on the pinned trusted host (paths under the host may vary); SHA-256
pattern; size bounded (256 B … 100 MB). An absent block simply means no delta
is offered.

**Client flow** (`IsleyUpdateClient.StageAsync`):

1. Delta is attempted only when `delta.fromVersion` exactly equals the
   installed three-part version (`IsSameVersion`, revision ignored).
2. Download with the same posture: redirects disabled, exact response URI must
   equal the manifest-declared URL, declared-size enforcement during
   streaming, fixed-time SHA-256, entry/expansion caps, zip-slip and symlink
   refusal (minimum entry count relaxed to 1 — deltas can be small).
3. `ValidateDeltaPackage`: the inner `isley-delta-manifest.json` exists
   (≤ 64 KB), parses with `format: 1`, matches the expected from/to versions,
   and every delete-list entry is a validated relative path (≤ 2000 entries,
   ≤ 512 chars each, no rooted paths, no `..`, no control chars, never
   `IsleyData`). The updater helper must be present. If `Isley.dll` is
   present its assembly version must be ≥ the release version.
4. **Any** delta failure — download, hash, extraction, validation — discards
   the staged delta and falls back to the full verified package. Cancellation
   is never swallowed. A broken delta never bricks an update.
5. `LaunchUpdater` passes `--mode delta` for delta stagings.

**Updater flow** (`Isley.Updater --mode delta`):

- Requires `isley-delta-manifest.json` in the staged source, re-parses it
  independently (bounded, `toVersion` must equal the `--version` argument),
  and validates every delete path again: not rooted, no `..` segments, never
  `IsleyData`, resolved path must stay inside the install directory.
- Copies changed/new files with the same rollback-backup logic as full
  updates (skipping the delta manifest file itself), then deletes listed
  files — each backed up first — and **skips the full-package orphan sweep**,
  which would otherwise delete everything the delta did not carry.
- Failure at any point restores the backup exactly as before, writes a
  failure result, and relaunches the previous installation.
- Unknown `--mode` values are rejected. Full mode is byte-for-byte the old
  behavior, including the ≥ 20 file completeness check.

**Packaging** (`scripts/package-isley-1.3.ps1`):

- New `-PreviousClientArchive` parameter; when omitted, the newest other
  `Isley-Windows-x64-*.zip` in `artifacts/` is used as the base. No base →
  no delta, packaging still succeeds.
- Extracts the previous archive to a temp folder, verifies it has no
  `IsleyData`, reads its `Isley.dll` assembly version (must be strictly
  older), and computes SHA-256 maps of both trees.
- Changed/new files + the two updater helper binaries (always forced in) go
  into the delta; missing files form the delete list (validated, sorted,
  ≤ 2000 entries).
- The delta zip lands at `artifacts/Isley-delta-<from>-<to>.zip` and its
  hash/size/version are added to the script's output object
  (`DeltaFromVersion`, `DeltaArchive`, `DeltaBytes`, `DeltaSha256`).
  A delta that ends up ≥ the full archive is deleted instead of published.
- Publishing the delta was initially a manual step: upload the zip next to the
  full one and add the `delta` block (with the emitted hash/size) to
  `Isley-release.json`. Since the Workstream-4 release pipeline (§6), the
  download-site publisher script wires the block automatically; the client
  still treats a missing block as "no delta".

## 4. What did not change

- Pinned HTTPS manifest + download URLs, redirects disabled — extended
  per-channel and to the delta URL (host-pinned).
- 16 KB manifest bound, fixed-time SHA-256, size/entry/expansion caps,
  zip-slip and symlink refusal — all applied to delta archives too.
- Full-package staging validation, rollback backup with orphan cleanup,
  write-probe, PID-reuse guard, version downgrade refusal — untouched.
- Quick Commands catalog: unchanged at 124 (no new palette commands).

## 5. Verification

`Verification/IsleyReleaseUpdateVerifier` now compiles `IsleyUpdateClient.cs`
directly and covers: beta manifest round-trip and cross-channel rejection,
delta offer validation (host, base version, hash, size), delta file-list
validation (format, version match, traversal/rooted/IsleyData refusal,
bounds), `IsSameVersion` semantics, and functional boot-ok marker
round-trip/tamper/oversize cases, plus source-token contracts for the client,
updater, UI, and packaging script. `tests/test_updater_delta_beta_bootok.py`
adds static regression contracts for the same surfaces.

## 6. Release pipeline (version bumps, GitHub Releases, delta chain, beta publish)

Added July 29, 2026 (Workstream 4). The packaging workflow
(`.github/workflows/release-package.yml`, manual dispatch only) owns the whole
publish path; this section supersedes the "manual step" note in §3.

**Version authority.** The live `download-site/public/Isley-release.json`
`version` is the source of truth. `workflow_dispatch` inputs:

- `version_bump`: `patch` (default) / `minor` / `major` / `keep` — applied to
  the manifest version (1.3.6 → 1.3.7 / 1.4.0 / 2.0.0 / 1.3.6).
- `version_override`: explicit X.Y.Z; wins over `version_bump`; must be a
  valid three-part version and never older than the live version.
- `channel`: `stable` (default) / `beta`.

The resolved version is stamped into the build with global MSBuild properties
(`-p:Version` / `-p:AssemblyVersion` / `-p:FileVersion` /
`-p:InformationalVersion`, passed by `scripts/package-isley-1.3.ps1 -Version`),
so the packaged `Isley.dll` assembly version always equals the manifest
version. That equality is required: the client refuses staged builds older
than the manifest declares, and the manifest writer hard-fails on any
mismatch. Product files are never edited by the pipeline.

**Notes propagation.** `release_notes` reaches the manifest writer through an
environment variable (`ISLEY_RELEASE_NOTES`) plus direct PowerShell parameter
binding — never through `npm run --` forwarding, which strips the surrounding
quotes on Windows and mangles non-ASCII text on the cmd hop (the historical
"notes never reached the manifest" bug). `publishedAt` is the run's UTC time.

**Delta chain.** Before packaging, the workflow downloads the latest GitHub
Release's `Isley-Windows-x64-*.zip` asset into `artifacts/` — the folder the
packaging script already auto-scans for a previous archive — so every release
after the first emits `Isley-delta-<prev>-<new>.zip` automatically. The
download no-ops cleanly when no prior release (or asset) exists. The
download-site writer stages the delta into `public/` and emits the four-field
`delta` block (`fromVersion` / `url` / `sha256` / `bytes`, §3 contract) into
the manifest only when a delta was actually produced; otherwise the block is
absent and clients simply take the full package.

**GitHub Releases.** A dedicated job (manual dispatch only; the workflow has
no push trigger) re-verifies every archive against `SHA256SUMS.txt` —
generated by the packaging script in GNU `sha256sum -c` format — and publishes
release `vX.Y.Z` with `softprops/action-gh-release@v2`, attaching the client
zip, the server zip, `SHA256SUMS.txt`, and the delta zip when present. The
body is the release notes plus an artifact hash table.

**Beta publish.** With `channel = beta` the writer stages
`public/Isley-Windows-x64-beta.zip` and `public/Isley-release-beta.json` with
`"channel": "beta"` (the client's pinned beta endpoints, §2) and leaves every
stable file — manifest, zips, download-page constants, rendered-html test —
untouched. The GitHub Release is tagged `vX.Y.Z-beta`, marked prerelease, and
never made `latest`, so the stable release stays the delta base
(`releases/latest` excludes prereleases). Publishing the staged `public/`
files to the live site is the same manual Sites deploy as stable; see
`docs/SITE_PUBLISH.md`.
