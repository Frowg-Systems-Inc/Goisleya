[CmdletBinding()]
param(
    [switch]$SkipRestore,
    [switch]$IncludeRuntime
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $root "Isley.sln"
$website = Join-Path $root "download-site"
$verificationRoot = Join-Path $root "Verification"

function Invoke-NativeStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Output ""
    Write-Output "== $Label =="
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

Push-Location $root
try {
    if (!$SkipRestore) {
        Invoke-NativeStep "Restore .NET projects" {
            dotnet restore $solution
        }
    }

    Invoke-NativeStep "Build all .NET projects" {
        dotnet build $solution -c Release --no-restore
    }

    foreach ($project in @(
        "BurntHud\BurntHud.csproj",
        "Isley.Relay\Isley.Relay.csproj",
        "Isley.ServerBridge\Isley.ServerBridge.csproj"
    )) {
        $absoluteProject = (Resolve-Path (Join-Path $root $project)).Path
        Invoke-NativeStep "Audit $project packages" {
            dotnet list $absoluteProject package --vulnerable --include-transitive
        }
    }

    foreach ($script in @(
        "scripts\verify-overlay-scripts.cjs",
        "scripts\verify-independent-provider.cjs",
        "scripts\verify-overlay-shell.cjs",
        "scripts\verify-controller.cjs",
        "scripts\mutation-check-contracts.cjs"
    )) {
        Invoke-NativeStep "Run $script" {
            node (Join-Path $root $script)
        }
    }

    # pytest contract suites (tests/). Optional tooling: prefer a repo-local
    # .venv-tools venv, then python on PATH; skip with a warning when neither
    # python nor pytest is available. ISLEY_REPO_ROOT pins the suites to this
    # checkout no matter where the interpreter lives.
    $pytestPython = $null
    foreach ($candidate in @(
        (Join-Path $root ".venv-tools\Scripts\python.exe"),
        (Join-Path $root "..\.venv-tools\Scripts\python.exe")
    )) {
        if (Test-Path $candidate) {
            $pytestPython = (Resolve-Path $candidate).Path
            break
        }
    }
    if (!$pytestPython) {
        $pythonOnPath = Get-Command python -ErrorAction SilentlyContinue
        if ($pythonOnPath) {
            $pytestPython = $pythonOnPath.Source
        }
    }
    if ($pytestPython) {
        & $pytestPython -c "import pytest" 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            $pytestPython = $null
        }
    }
    if (!$pytestPython) {
        Write-Warning "python/pytest not found; skipping the tests/ pytest suites (pip install pytest pyyaml to enable)."
    }
    else {
        $previousRepoRoot = $env:ISLEY_REPO_ROOT
        $env:ISLEY_REPO_ROOT = $root
        try {
            Invoke-NativeStep "Run pytest contract suites (tests/)" {
                & $pytestPython -m pytest (Join-Path $root "tests") -q
            }
        }
        finally {
            $env:ISLEY_REPO_ROOT = $previousRepoRoot
        }
    }

    $verifierProjects = Get-ChildItem $verificationRoot -Filter "*.csproj" -File -Recurse |
        Sort-Object FullName
    foreach ($project in $verifierProjects) {
        Invoke-NativeStep "Run $($project.Directory.Name)" {
            dotnet run --project $project.FullName -c Release --no-build --no-restore
        }
    }

    Push-Location $website
    try {
        if (!$SkipRestore) {
            Invoke-NativeStep "Install exact website dependencies" {
                npm ci
            }
        }
        Invoke-NativeStep "Lint website" {
            npm run lint
        }
        Invoke-NativeStep "Build and test website" {
            npm test
        }
        Invoke-NativeStep "Audit website production dependencies" {
            # Lint/build tooling can carry transitive advisories that do not ship.
            # Fail CI only when production dependencies are vulnerable at high+.
            npm audit --audit-level=high --omit=dev
        }
    }
    finally {
        Pop-Location
    }

    if ($IncludeRuntime) {
        if ($env:OS -ne "Windows_NT") {
            throw "Packaged runtime verification requires Windows."
        }

        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $runtimeRoot = Join-Path $root "artifacts\verification-runtime-$stamp"
        New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
        Invoke-NativeStep "Publish packaged Windows runtime" {
            dotnet publish (Join-Path $root "BurntHud\BurntHud.csproj") `
                -c Release -r win-x64 --self-contained false -o $runtimeRoot
        }
        Copy-Item -LiteralPath (Join-Path $root "distribution\Isley.portable") `
            -Destination (Join-Path $runtimeRoot "Isley.portable") -Force

        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $diagnosticPort = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
        $listener.Stop()

        $previousBrowserArguments = $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
        $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS =
            "--remote-debugging-port=$diagnosticPort"
        try {
            Invoke-NativeStep "Verify selective lock and minimized dock runtime" {
                & (Join-Path $root "scripts\verify-lock-runtime.ps1") `
                    -ExecutablePath (Join-Path $runtimeRoot "Isley.exe")
            }

            $process = Start-Process -FilePath (Join-Path $runtimeRoot "Isley.exe") `
                -WorkingDirectory $runtimeRoot -PassThru
            try {
                Invoke-NativeStep "Verify current live-map runtime" {
                    node (Join-Path $root "scripts\verify-map-runtime.cjs") `
                        $diagnosticPort
                }
                Invoke-NativeStep "Verify realtime marker runtime" {
                    node (Join-Path $root "scripts\verify-live-update-runtime.cjs") `
                        $diagnosticPort
                }
            }
            finally {
                if (!$process.HasExited) {
                    Stop-Process -Id $process.Id -Force
                }
            }
        }
        finally {
            $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = $previousBrowserArguments
        }
    }

    Write-Output ""
    Write-Output "All Isley verification passed ($($verifierProjects.Count) focused verifiers)."
}
finally {
    Pop-Location
}
