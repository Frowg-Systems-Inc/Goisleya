# Analyzer baseline — Workstream 5 (branch `swarm/w5-quality`)

Recorded 2026-07-28 against `Isley.sln` Release build (64 projects,
`-p:EnableWindowsTargeting=true -m:1`) with:

```xml
<EnableNETAnalyzers>true</EnableNETAnalyzers>
<AnalysisLevel>8.0-recommended</AnalysisLevel>
<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
```

Analyzers run in **baseline mode**: every finding is a plain warning, nothing
fails the build, and the counts below are the agreed starting point future
waves burn down.

## Headline numbers

- **172 unique analyzer warnings** across the solution after this wave's
  single fix (174 before; the WPF temporary project re-reports each BurntHud
  warning, so the raw MSBuild summary reads higher — all counts here are
  deduplicated per source location).
- **0 errors.** Build is green end to end.
- **1 rule fixed this wave** (CA1050, 2 occurrences). See "Fixes applied".
- ~55 % of all warnings are two mechanical performance rules (CA1859,
  CA1861); they are ideal first backlog batches.

## Top rules (all 15 observed)

| Rule | Count | Disposition |
| --- | ---: | --- |
| CA1859 (use concrete types for performance) | 61 | Backlog — mechanical but touches member signatures; batch per project in a dedicated wave. |
| CA1861 (avoid constant arrays as arguments) | 36 | Backlog — mechanical `static readonly` extraction; safe bulk fix next wave. |
| CA1305 (specify IFormatProvider) | 26 | Backlog — correctness-adjacent (locale); fix product code first, verifiers second. |
| CA1848 (use LoggerMessage delegates) | 9 | Backlog — perf-only; services (Relay/ServerBridge/VoiceServer) logging hot paths. |
| CA1869 (cache JsonSerializerOptions) | 7 | Fix-now next wave — small, safe, mostly static field extraction. |
| CA1865 (use char overloads) | 6 | Fix-now next wave — trivial one-character changes. |
| CA1310 (specify StringComparison) | 6 | Backlog — needs case-by-case review; some call sites may be intentional. |
| CA1822 (mark members static) | 5 | Fix-now next wave — mechanical; check WPF binding call sites first. |
| CA1806 (do not ignore method results) | 5 | Backlog — P/Invoke `ReleaseDC` returns; needs deliberate error-handling decision, not a mechanical fix. |
| CA1826 (use property instead of Enumerable method) | 4 | Handoff — all 4 in `Verification/`, owned by another wave. |
| CA1001 (types owning disposable fields should be disposable) | 3 | Backlog — design change (add `IDisposable`); review ownership first. |
| CA1870 (use SearchValues) | 2 | Backlog — fix attempted this wave and reverted: `string.IndexOfAny(SearchValues<char>)` is not usable in the multi-target compile context here (CS1503); revisit with `MemoryExtensions` on spans or a static `char[]` field (CA1861-style). |
| CA1050 (declare types in namespaces) | 2 | **Fixed this wave** (see below). |
| CA1805 (member initialized to default) | 1 | Backlog — fix attempted and reverted: `_lastServerStatus` is never reassigned anywhere, so removing `= null` raises CS0649; the field is effectively write-dead and needs an owner decision, not a style edit. |
| CA1847 (use string.Contains(char)) | 1 | Handoff — in `Verification/HeadingConfidenceVerifier`, owned by another wave. |

## Fixes applied this wave

Only rules firing ≤ 3 times solution-wide with a trivially safe fix were
eligible. One qualified:

1. **CA1050 — `Isley.Relay/Program.cs`**: the request DTO records
   `DeviceTokenRequest` and `PrivacyUpdateRequest` were declared in the
   global namespace after the top-level statements. Wrapped them in
   `namespace Isley.Relay { }` (the file already has `using Isley.Relay;`,
   so all call sites resolve unchanged; both records are referenced only
   inside `Program.cs`). Clears both occurrences; zero behavior change.

Attempted and reverted (documented so the next wave does not retry blindly):

- **CA1805** (`MainWindow.xaml.cs:461`): removing `= null` surfaced CS0649 —
  the field is read in three partials but never written. Left as-is.
- **CA1870** (`IsleyReleaseLogic.cs:210`): the `SearchValues<char>` overload
  failed to compile (CS1503) in the WPF temp project / verifier compile
  context. Left as-is.

## Per-project distribution (top 10, deduplicated)

| Project | Warnings |
| --- | ---: |
| BurntHud | 54 |
| NestTimerAlertVerifier | 9 |
| SteamFriendVerifier | 9 |
| Isley.Relay | 6 (was 8, −2 CA1050) |
| Isley.ServerBridge | 6 |
| ShorelineCheckVerifier | 5 |
| TripReadinessVerifier | 5 |
| WaterCrossingVerifier | 5 |
| TerrainRoadNetworkVerifier | 5 |
| CommunityServerWatchVerifier | 4 |

The long tail is the 61 `Verification/*` console verifiers; they share a few
recurring patterns (CA1859/CA1861/CA1305) and should be cleaned up in the
wave that owns `Verification/`.

## How to re-record this baseline

```powershell
dotnet build Isley.sln -c Release -p:EnableWindowsTargeting=true -m:1 /t:Rebuild
```

Deduplicate identical warning lines (MSBuild reports each BurntHud warning
once per compiling project: `BurntHud.csproj`, the WPF temp project, and any
verifier that compiles the same logic file).
