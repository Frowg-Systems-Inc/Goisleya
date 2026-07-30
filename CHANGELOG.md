# Changelog

All notable changes to Isley are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Verified downloads (client and server-network ZIPs) with SHA-256 checksums are
published on the
[GitHub Releases page](https://github.com/Frowg-Systems-Inc/Goisleya/releases).

## [Unreleased]

No unreleased changes yet.

## [1.4.0] - 2026-07-29

The first release cut from the GitHub repository
(`Frowg-Systems-Inc/Goisleya`): thirteen quality-of-life features, relay delta
streaming, a hardened updater with a beta channel and delta downloads, and the
verification culture kept at 100% logic-file coverage.

### Added

- **Thirteen quality-of-life features:**
  - Planners: nest timer alerts (toast at preset thresholds, once per timer);
    a schema-versioned planner-state store with legacy migration and
    capture-streak stats; server growth-rate presets (Official 1x / Boosted 2x,
    plus custom).
  - Voice and friends: per-peer voice volume memory (opaque name-hash keys,
    LRU-bounded) with a real per-peer connection-quality surface from WebRTC
    stats (RTT/jitter/loss, honest `—` when unavailable); named friend squads
    with group presence; a map watchlist (right-click add, cap 32).
  - Survival and vitals: bounded tactical-log export with honest truncation
    (also a Quick Command); timer journaling that reconciles what expired
    while Isley was away without re-firing alarms; sensor confidence dots
    with heading confidence decay so held values never jump.
  - Chrome and settings: hotkey conflict detection (blocked with an inline
    highlight); Lite Mode suggestions driven by a timer-starvation signal
    (tappable, never auto-enable); named layout profiles (up to 8); a
    redacted diagnostics bundle export.
- **Map tools:** undo for destructive pin/route/measurement/no-go clears (one
  level per collection with anti-clobber guards); route and no-go share codes
  (`ISLEYROUTE1.`, `ISLEYNOGO1.`) with the same validation posture as pin
  codes, plus share/import Quick Commands; route auto-replan on >30 MU
  deviation (debounced, cadence-bounded, toggleable, defaults on); stacked
  guidance ranking with deterministic top-3 suggestions and `+N` overflow.
- **Relay viewer stream v2 (delta encoding):** opt-in per connection via
  `hello` negotiation — delta frames with periodic keyframes (default every
  240), forced keyframe on reconnect, server change, or delta failure. The
  overlay negotiates v2 by default with a validation-first apply
  (client kill switch `RelayStreamV2Enabled` in `isley-extras.json`; server
  kill switch `Relay:ViewerDeltaEncodingEnabled`). Stream v1 remains the
  byte-identical default for unnegotiated connections, and mixed v1/v2
  viewer fleets coexist on one relay.
- **Relay `/metrics` endpoint:** loopback-gated aggregate counters (frames,
  rate-limit rejections, viewer connections, active bridges/viewers, uptime);
  `Relay:MetricsPubliclyVisible` widens deliberately. Bridge `/` and `/status`
  gained a `source` capability block and `lastSuccessfulPublishAt`.
- **Updater boot-ok marker:** an update stays pending until a healthy boot
  confirms it, with an honest NOT CONFIRMED surface.
- **Updater beta channel:** a second pinned manifest/URL pair with channel
  equality enforced and a stable-channel fallback with honest copy.
- **Updater delta downloads:** optional manifest delta block, exact base
  match required, the same hash/size/zip-safety posture as full packages, a
  delete list carried inside the verified ZIP and re-validated client- and
  updater-side, automatic full-package fallback, rollback backup intact. The
  first delta asset ships with the *next* release (1.4.0 → 1.4.x) — no
  previous GitHub Release existed to diff against.

### Changed

- Gateway terrain updates interpolate: keyed node maps reuse DOM nodes and
  positions glide via CSS transitions (reduced-motion honored), and
  other-player markers glide between live updates instead of teleporting.
- Verification kept pace with the features: every logic file compiles under a
  focused verifier (84/84, 100% coverage), and the mutation harness proves
  13/13 deliberate contract sabotages are caught with zero false passes — the
  four previously documented false-pass weaknesses are closed.
- Runtime-validated on real Windows before release: VoiceServer live test
  4/4, updater executable drill 4/4, and a healthy packaged portable launch
  with the planner-state store written live.

### Fixed

- Held heading no longer jumps on the map compass and position-copy surfaces
  (heading confidence decay completion).
- Release notes now propagate verbatim into the stable and beta manifests
  (root-caused argument mangling in the release pipeline; notes are carried
  through the environment).

### Removed

- Legacy LifeRun planner-key dual-write retired (12 keys). The one-time
  `RestoreLifeRun` migration into the planner-state store is retained.

### Security

- Supply chain: Dependabot weekly for NuGet, both npm projects, and GitHub
  Actions; CodeQL scanning (C# manual build plus JavaScript) on push/PR and
  weekly; GitHub secret scanning and push protection enabled repo-wide.
- Updater posture holds unchanged across full and delta packages and both
  channels: pinned HTTPS, redirects disabled, 16 KB manifest bound, SHA-256
  fixed-time compare, size/entry/expansion caps, zip-slip/symlink refusal,
  staged validation, downgrade refusal, rollback backup, boot-ok marker.
  Root of trust is unchanged — no trust widening.

## [1.3.6] - 2026-07-26

Reconstructed briefly from the 1.3.6 stable manifest notes. This release
predates the GitHub migration and has no tagged GitHub Release.

### Added

- Paste-coordinates routing and solid zone rendering.

### Changed

- Tools → MORE panel updates; overlay window stays topmost.

### Fixed

- LOCATION DATA freshness (stale self-position data handling).
- Overlay resize behavior.
- Windows Defender false-positive mitigations.

[Unreleased]: https://github.com/Frowg-Systems-Inc/Goisleya/compare/v1.4.0...HEAD
[1.4.0]: https://github.com/Frowg-Systems-Inc/Goisleya/releases/tag/v1.4.0
