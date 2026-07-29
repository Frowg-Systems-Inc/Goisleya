<#
.SYNOPSIS
    Performance budget harness (build/test-side) for Isley.

.DESCRIPTION
    Measures:
      (a) full Release build time of Isley.sln (forced rebuild),
      (b) total verifier-suite run time (all Verification/* console
          verifiers + the node contract scripts),
      (c) BurntHud client assembly size and, when present, the uncompressed
          total size of the shipped client zip contents.

    Writes results to test_reports/perf-baseline.json (gitignored) and prints
    a summary table. Informational only — not wired into CI.
    Budgets and rationale: docs/PERFORMANCE.md.

.EXAMPLE
    .\scripts\perf-baseline.ps1

.EXAMPLE
    # Skip the rebuild and reuse existing binaries (fast re-measure of b/c)
    .\scripts\perf-baseline.ps1 -SkipBuild
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $root "Isley.sln"
$reportDir = Join-Path $root "test_reports"
$reportPath = Join-Path $reportDir "perf-baseline.json"
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null

$results = [ordered]@{
    timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
    machine      = [ordered]@{
        os           = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        logicalCores = [Environment]::ProcessorCount
        dotnetSdk    = (& dotnet --version)
    }
    build        = $null
    verifierSuite = $null
    artifacts    = $null
}

function Measure-Native {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )
    Write-Host "== $Label =="
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $Action | Out-Null
    $exit = $LASTEXITCODE
    $sw.Stop()
    return [ordered]@{ exitCode = $exit; elapsedSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
}

Push-Location $root
try {
    # (a) Full Release build time (forced rebuild for an honest number).
    if ($SkipBuild) {
        Write-Output "== Build skipped (-SkipBuild) =="
        $results.build = [ordered]@{ skipped = $true }
    }
    else {
        $build = Measure-Native "Full Release rebuild of Isley.sln" {
            & dotnet build $solution -c Release -p:EnableWindowsTargeting=true -m:1 /t:Rebuild
        }
        $results.build = [ordered]@{
            skipped        = $false
            configuration  = "Release"
            rebuild        = $true
            exitCode       = $build.exitCode
            elapsedSeconds = $build.elapsedSeconds
        }
        if ($build.exitCode -ne 0) {
            throw "Build failed; perf baseline aborted."
        }
    }

    # (b) Verifier-suite run time: every Verification/*.csproj plus the node
    # contract scripts. Mirrors scripts/verify-all.ps1's suite definition.
    $suiteSw = [System.Diagnostics.Stopwatch]::StartNew()
    $verifierProjects = Get-ChildItem (Join-Path $root "Verification") -Filter "*.csproj" -File -Recurse |
        Sort-Object FullName
    $verifierFailed = @()
    foreach ($project in $verifierProjects) {
        & dotnet run --project $project.FullName -c Release --no-build --no-restore | Out-Null
        if ($LASTEXITCODE -ne 0) { $verifierFailed += $project.Directory.Name }
    }
    $nodeScripts = @(
        "scripts\verify-overlay-scripts.cjs",
        "scripts\verify-independent-provider.cjs",
        "scripts\verify-overlay-shell.cjs",
        "scripts\verify-controller.cjs",
        "scripts\mutation-check-contracts.cjs"
    )
    $nodeFailed = @()
    foreach ($script in $nodeScripts) {
        & node (Join-Path $root $script) | Out-Null
        if ($LASTEXITCODE -ne 0) { $nodeFailed += $script }
    }
    $suiteSw.Stop()
    $results.verifierSuite = [ordered]@{
        verifierProjects     = $verifierProjects.Count
        verifierFailures     = $verifierFailed
        nodeScripts          = $nodeScripts.Count
        nodeFailures         = $nodeFailed
        totalElapsedSeconds  = [math]::Round($suiteSw.Elapsed.TotalSeconds, 1)
    }

    # (c) Artifact sizes.
    $clientBin = Join-Path $root "BurntHud\bin\Release\net8.0-windows10.0.19041.0"
    $assembly = $null
    foreach ($name in @("Isley.dll", "BurntHud.dll")) {
        $candidate = Join-Path $clientBin $name
        if (Test-Path -LiteralPath $candidate) { $assembly = Get-Item -LiteralPath $candidate; break }
    }
    $zipInfo = $null
    $zip = Get-ChildItem -Path (Join-Path $root "distribution"), (Join-Path $root "artifacts") `
        -Filter "*.zip" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -ne $zip) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zip.FullName)
        try {
            $uncompressed = ($archive.Entries | Measure-Object -Property Length -Sum).Sum
            $zipInfo = [ordered]@{
                path              = $zip.FullName.Substring($root.Length + 1)
                compressedBytes   = $zip.Length
                uncompressedBytes = $uncompressed
                entries           = $archive.Entries.Count
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    $results.artifacts = [ordered]@{
        clientAssembly = $(if ($assembly) {
            [ordered]@{ name = $assembly.Name; bytes = $assembly.Length }
        } else { $null })
        clientZip = $zipInfo
        note      = $(if ($null -eq $zipInfo) { "No client zip found under distribution/ or artifacts/; size budget not measured." } else { $null })
    }
}
finally {
    Pop-Location
}

$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8

Write-Output ""
Write-Output "== Isley perf baseline =="
$table = @(
    [pscustomobject]@{
        Metric = "Full Release rebuild"
        Value  = $(if ($results.build.skipped) { "skipped" } else { "{0:N1}s" -f $results.build.elapsedSeconds })
        Budget = "<= 240 s (CI hardware)"
    }
    [pscustomobject]@{
        Metric = "Verifier suite ($($results.verifierSuite.verifierProjects) C# verifiers + $($results.verifierSuite.nodeScripts) node contracts)"
        Value  = "{0:N1}s" -f $results.verifierSuite.totalElapsedSeconds
        Budget = "<= 300 s"
    }
    [pscustomobject]@{
        Metric = "Client assembly ($($results.artifacts.clientAssembly.name))"
        Value  = $(if ($results.artifacts.clientAssembly) { "{0:N1} MB" -f ($results.artifacts.clientAssembly.bytes / 1MB) } else { "not built" })
        Budget = "<= 15 MB"
    }
    [pscustomobject]@{
        Metric = "Client zip contents (uncompressed)"
        Value  = $(if ($results.artifacts.clientZip) { "{0:N1} MB" -f ($results.artifacts.clientZip.uncompressedBytes / 1MB) } else { "no zip present" })
        Budget = "<= 250 MB"
    }
)
$table | Format-Table -AutoSize | Out-String | Write-Output
Write-Output "Wrote $reportPath"

if ($results.verifierSuite.verifierFailures.Count -gt 0 -or $results.verifierSuite.nodeFailures.Count -gt 0) {
    Write-Output "WARNING: suite failures were recorded in the report; timing is still valid."
}
