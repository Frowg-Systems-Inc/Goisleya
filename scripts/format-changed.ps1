<#
.SYNOPSIS
    Diff-scoped format gate: verifies that C# files changed relative to a
    base ref satisfy `dotnet format --verify-no-changes`.

.DESCRIPTION
    Local equivalent of the `format-check` job in .github/workflows/verify.yml.
    Only files changed in the current branch/working tree are checked, so the
    pre-existing whitespace baseline (see docs/PERFORMANCE.md and
    docs/ANALYZER-BASELINE.md) never blocks unrelated work.

.EXAMPLE
    # Check files changed vs origin/main (committed + working tree)
    .\scripts\format-changed.ps1

.EXAMPLE
    # Check only committed changes against a specific base
    .\scripts\format-changed.ps1 -BaseRef main -CommittedOnly
#>
[CmdletBinding()]
param(
    # Base ref to diff against (three-dot merge-base semantics).
    [string]$BaseRef = "origin/main",

    # When set, only committed changes ($BaseRef...HEAD) are checked;
    # otherwise staged + unstaged working-tree changes are included too.
    [switch]$CommittedOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $root "Isley.sln"
Push-Location $root
try {
    $changed = [System.Collections.Generic.SortedSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)

    & git diff --name-only --diff-filter=ACMR "$BaseRef...HEAD" -- "*.cs" |
        ForEach-Object { [void]$changed.Add($_) }

    if (!$CommittedOnly) {
        & git diff --name-only --diff-filter=ACMR -- "*.cs" |
            ForEach-Object { [void]$changed.Add($_) }
        & git diff --name-only --diff-filter=ACMR --cached -- "*.cs" |
            ForEach-Object { [void]$changed.Add($_) }
    }

    $include = @()
    foreach ($relative in $changed) {
        $absolute = Join-Path $root ($relative -replace "/", [IO.Path]::DirectorySeparatorChar)
        if (Test-Path -LiteralPath $absolute) {
            $include += $absolute
        }
    }

    if ($include.Count -eq 0) {
        Write-Output "No changed C# files relative to $BaseRef; nothing to format-check."
        exit 0
    }

    Write-Output ("Format-checking {0} changed C# file(s) relative to {1}:" -f $include.Count, $BaseRef)
    $include | ForEach-Object { Write-Output "  $_" }

    & dotnet format $solution --verify-no-changes --severity warn --include @include
    $formatExit = $LASTEXITCODE

    if ($formatExit -eq 0) {
        Write-Output ""
        Write-Output "Format check passed: all changed files comply."
    }
    else {
        Write-Output ""
        Write-Output "Format check reported violations (exit $formatExit)."
        Write-Output "Run: dotnet format Isley.sln --include <files> to apply fixes,"
        Write-Output "or leave them: the CI leg is informational for now."
    }
    exit $formatExit
}
finally {
    Pop-Location
}
