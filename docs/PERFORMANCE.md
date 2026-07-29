# Performance budgets — Isley

Workstream 5 adds a measurement harness, `scripts/perf-baseline.ps1`, and
the target budgets below. The harness is **informational only**: it is not
wired into CI and nothing fails when a budget is exceeded. Results land in
`test_reports/perf-baseline.json` (gitignored) and a printed table.

```powershell
.\scripts\perf-baseline.ps1            # full measurement (~3-5 min)
.\scripts\perf-baseline.ps1 -SkipBuild # re-measure suite + sizes on existing binaries
```

## What the harness covers today (build/test-side)

| Metric | Target budget | Rationale |
| --- | --- | --- |
| Full Release rebuild of `Isley.sln` (64 projects, `-m:1`) | ≤ 240 s on CI-class hardware | Keeps the inner loop and the Windows verify job tolerable as the solution grows. |
| Verifier suite: 61 `Verification/*` console verifiers + 5 node contract scripts | ≤ 300 s total | The suite is the mandatory pre-push gate; it must stay fast enough that nobody is tempted to skip it. |
| Client assembly (`Isley.dll` in `BurntHud/bin/Release/net8.0-windows10.0.19041.0`) | ≤ 15 MB | Guards against accidental dependency/bloat creep in the shipped overlay. |
| Client zip contents, uncompressed (when a zip exists under `distribution/` or `artifacts/`) | ≤ 250 MB | The updater downloads and expands this; bounds update time and disk pressure. |

These are ratchet budgets: when a measurement comfortably beats a budget,
tighten the budget rather than letting the next wave erode it.

## What the harness does NOT cover (honest note)

App-runtime budgets — overlay startup time, steady-state memory, WebView2
footprint, per-frame cost — are **not measured** here. They require a real
Windows session with a logged-in desktop (and ideally a game session), which
this harness deliberately avoids. Provisional targets to formalize later:

- Cold start to interactive overlay: ≤ 3 s on a mid-range Windows 11 machine.
- Steady-state private bytes (idle, map open): ≤ 350 MB.
- No measurable frame-time impact on the game (overlay renders out-of-band
  via WebView2; game capture is read-only).

When someone with a Windows desktop session picks this up: measure with
`scripts/verify-lock-runtime.ps1`-style published artifacts plus a simple
timer/memory sampler, and record results next to `perf-baseline.json`.

## Re-measuring

Run the harness on the same class of machine before changing a budget.
Build time varies with NuGet cache warmth; the harness forces a full rebuild
(`/t:Rebuild`) so numbers are comparable across runs.
