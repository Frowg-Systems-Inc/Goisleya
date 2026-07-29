# Contributing to Isley

Thanks for helping improve Isley — the Windows companion overlay for *The Isle*
plus its relay, server bridge, voice, telemetry, and updater services.

## Before you start

- Windows 10/11 with .NET 8 SDK and Node.js >= 22.13 for the download site.
- Run the full verification suite before opening a PR:

  ```powershell
  .\scripts\verify-all.ps1
  ```

  This builds all projects, runs every `Verification/*` console verifier, the
  node contract scripts, ESLint, and the pytest suite.

## The contract ledger (important)

Several features are protected by cross-checked "contracts". If you change
any of these, update **all** of the following in the same PR:

- Quick Commands catalog count → `scripts/verify-controller.cjs`,
  `Verification/SpawnPlanVerifier/Program.cs`, and the pytest suite.
- Overlay script behavior → `scripts/verify-overlay-scripts.cjs`,
  `scripts/verify-controller.cjs`, `scripts/verify-voice-crypto.cjs`.
- Files the updater requires → the staged-package validation in the updater
  and its verifier.

CI runs both the Windows full suite and the ubuntu overlay lint/contract job
on every PR; both must be green.

## PR rules

- One concern per PR; keep diffs small and focused.
- Zero behavior change unless the PR includes tests/contracts proving the new
  intended behavior.
- Never commit secrets, signing certificates, or `test_reports/` output.
- `MainWindow.*.cs` partials are large — coordinate before editing more than
  one partial in a single PR.

## Reporting bugs

Open an issue with the bug template: expected vs actual behavior, Isley
version (`whats-new.json`), Windows build, and relevant lines from
`IsleyData/Logs` if available.

## Code style and formatting gates

The repo has a root `.editorconfig` matching the code's dominant style
(4-space indent, LF for C#, file-scoped namespaces, Allman braces). Most
files already comply; a known set of legacy deviations is tracked as
baseline debt in `docs/ANALYZER-BASELINE.md`.

Roslyn analyzers (`AnalysisLevel 8.0-recommended`) run on every build as
plain warnings — they never fail the build. Fix warnings in code you touch;
leave unrelated warnings for the backlog.

CI runs a diff-scoped, informational `format-check` job: it runs
`dotnet format --verify-no-changes` only on the C# files changed in your PR
and posts a summary. To check the same thing locally before pushing:

```powershell
.\scripts\format-changed.ps1
```

To auto-fix formatting only in files you changed:

```powershell
dotnet format Isley.sln --include <paths-to-your-changed-files>
```

Please do not run a whole-solution `dotnet format`: the resulting reformat
diff makes review and parallel work harder.
