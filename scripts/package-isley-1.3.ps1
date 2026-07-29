param(
    [string]$Configuration = "Release",
    [string]$PreviousClientArchive = ""
)

$ErrorActionPreference = "Stop"

$workspace = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$artifacts = Join-Path $workspace "artifacts"
$projectText = Get-Content -LiteralPath (Join-Path $workspace "BurntHud\BurntHud.csproj") -Raw
if ($projectText -notmatch '<Version>(?<version>\d+\.\d+\.\d+)</Version>') {
    throw "BurntHud.csproj does not declare a comparable Version."
}
$version = $Matches['version']
$releaseRoot = Join-Path $artifacts "Isley-$version-release"
$clientStage = Join-Path $releaseRoot "client"
$serverStage = Join-Path $releaseRoot "server-network"
$clientArchive = Join-Path $artifacts "Isley-Windows-x64-$version.zip"
$serverArchive = Join-Path $artifacts "Isley-Server-Network-$version.zip"

foreach ($target in @($releaseRoot, $clientArchive, $serverArchive)) {
    $resolvedParent = [System.IO.Path]::GetFullPath(
        $(if ([System.IO.Path]::HasExtension($target)) {
            [System.IO.Path]::GetDirectoryName($target)
        }
        else {
            $target
        }))
    if (-not $resolvedParent.StartsWith(
        [System.IO.Path]::GetFullPath($artifacts),
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Packaging target escaped the workspace artifacts directory."
    }
}

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
foreach ($archive in @($clientArchive, $serverArchive)) {
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
}
New-Item -ItemType Directory -Path $clientStage -Force | Out-Null
New-Item -ItemType Directory -Path $serverStage -Force | Out-Null

& dotnet publish (Join-Path $workspace "BurntHud\BurntHud.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $clientStage
if ($LASTEXITCODE -ne 0) {
    throw "The Isley desktop publish failed."
}

Copy-Item -LiteralPath (Join-Path $workspace "distribution\README.txt") `
    -Destination (Join-Path $clientStage "README.txt") -Force
Copy-Item -LiteralPath (Join-Path $workspace "distribution\Isley.portable") `
    -Destination (Join-Path $clientStage "Isley.portable") -Force
Copy-Item -LiteralPath (Join-Path $workspace "distribution\IsleyLiveData.example.json") `
    -Destination (Join-Path $clientStage "IsleyLiveData.example.json") -Force

foreach ($service in @("Isley.Relay", "Isley.ServerBridge")) {
    $serviceOutput = Join-Path $serverStage $service
    & dotnet publish (Join-Path $workspace "$service\$service.csproj") `
        -c $Configuration `
        --self-contained false `
        -o $serviceOutput
    if ($LASTEXITCODE -ne 0) {
        throw "$service publish failed."
    }
}
Copy-Item -LiteralPath (Join-Path $workspace "docs\ISLEY_LIVE_NETWORK.md") `
    -Destination (Join-Path $serverStage "README.md") -Force
Copy-Item -LiteralPath (Join-Path $workspace "docs\PLUGIN_TELEMETRY_EXAMPLE.json") `
    -Destination (Join-Path $serverStage "PLUGIN_TELEMETRY_EXAMPLE.json") -Force
Copy-Item -LiteralPath (Join-Path $workspace "scripts\Start-IsleyServerBridge.ps1") `
    -Destination (Join-Path $serverStage "Start-IsleyServerBridge.ps1") -Force
Copy-Item -LiteralPath (Join-Path $workspace "docs\THE_ISLE_TELEMETRY_INTERFACE_REQUEST.md") `
    -Destination (Join-Path $serverStage "THE_ISLE_TELEMETRY_INTERFACE_REQUEST.md") -Force
Copy-Item -LiteralPath (Join-Path $workspace "docs\RCON_TO_PLUGIN.md") `
    -Destination (Join-Path $serverStage "RCON_TO_PLUGIN.md") -Force
$operatorScripts = Join-Path $workspace "distribution\server-network"
foreach ($scriptName in @(
    "setup.ps1",
    "install-service.ps1",
    "watchdog.ps1",
    "publish-plugin-frame.ps1",
    "operator-console.html"
)) {
    Copy-Item -LiteralPath (Join-Path $operatorScripts $scriptName) `
        -Destination (Join-Path $serverStage $scriptName) -Force
}

if (Test-Path -LiteralPath (Join-Path $clientStage "IsleyData")) {
    throw "Runtime user data must never be packaged."
}

# Keep portable archives free of debug symbols and local-development settings.
Get-ChildItem -LiteralPath $releaseRoot -Recurse -File |
    Where-Object {
        $_.Extension -eq ".pdb" -or
        $_.Name -like "*.Development.json"
    } |
    ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Force
    }

foreach ($companion in @("VoiceServer", "Updater")) {
    $companionRoot = Join-Path $clientStage $companion
    if (-not (Test-Path -LiteralPath $companionRoot -PathType Container)) {
        throw "The package is missing the $companion companion folder."
    }

    $nestedRidFolders = Get-ChildItem -LiteralPath $companionRoot -Directory -Recurse |
        Where-Object { $_.Name -ieq "win-x64" }
    if ($nestedRidFolders.Count -gt 0) {
        throw ("Companion $companion must be flattened; nested win-x64 folders were packaged: " +
            (($nestedRidFolders | ForEach-Object { $_.FullName }) -join ", "))
    }
}

foreach ($required in @(
    (Join-Path $clientStage "Isley.exe"),
    (Join-Path $clientStage "Isley.dll"),
    (Join-Path $clientStage "Isley.Telemetry.dll"),
    (Join-Path $clientStage "Map\index.html"),
    (Join-Path $clientStage "Voice\voice.html"),
    (Join-Path $clientStage "Voice\voice.js"),
    (Join-Path $clientStage "Voice\voice-crypto.js"),
    (Join-Path $clientStage "Voice\voice.css"),
    (Join-Path $clientStage "VoiceServer\Isley.VoiceServer.exe"),
    (Join-Path $clientStage "VoiceServer\Isley.VoiceServer.dll"),
    (Join-Path $clientStage "VoiceServer\appsettings.json"),
    (Join-Path $clientStage "Updater\Isley.Updater.exe"),
    (Join-Path $serverStage "Isley.Relay\Isley.Relay.dll"),
    (Join-Path $serverStage "Isley.ServerBridge\Isley.ServerBridge.dll"),
    (Join-Path $serverStage "README.md"),
    (Join-Path $serverStage "Start-IsleyServerBridge.ps1"),
    (Join-Path $serverStage "THE_ISLE_TELEMETRY_INTERFACE_REQUEST.md"),
    (Join-Path $serverStage "RCON_TO_PLUGIN.md"),
    (Join-Path $serverStage "operator-console.html"),
    (Join-Path $serverStage "setup.ps1")
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "The package is missing $required."
    }
}

foreach ($forbidden in @(
    (Join-Path $clientStage "VoiceServer\appsettings.Development.json"),
    (Join-Path $clientStage "Updater\appsettings.Development.json")
)) {
    if (Test-Path -LiteralPath $forbidden -PathType Leaf) {
        throw "Development settings must not ship in the portable package: $forbidden"
    }
}

function Protect-IsleyAuthenticode {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $pfx = [Environment]::GetEnvironmentVariable("ISLEY_CODE_SIGN_PFX")
    $pfxBase64 = [Environment]::GetEnvironmentVariable("ISLEY_CODE_SIGN_PFX_BASE64")
    $thumbprint = [Environment]::GetEnvironmentVariable("ISLEY_CODE_SIGN_THUMBPRINT")
    $password = [Environment]::GetEnvironmentVariable("ISLEY_CODE_SIGN_PASSWORD")
    $timestampUrl = [Environment]::GetEnvironmentVariable("ISLEY_CODE_SIGN_TIMESTAMP_URL")
    if ([string]::IsNullOrWhiteSpace($timestampUrl)) {
        $timestampUrl = "http://timestamp.digicert.com"
    }

    if ([string]::IsNullOrWhiteSpace($pfx) -and
        [string]::IsNullOrWhiteSpace($pfxBase64) -and
        [string]::IsNullOrWhiteSpace($thumbprint)) {
        Write-Host ("Authenticode signing skipped (set ISLEY_CODE_SIGN_PFX / ISLEY_CODE_SIGN_PFX_BASE64 / " +
            "ISLEY_CODE_SIGN_THUMBPRINT to sign).")
        return
    }

    $signtoolPath = $null
    $signtoolCommand = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $signtoolCommand) {
        $signtoolPath = $signtoolCommand.Source
    }
    else {
        $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
        if (Test-Path -LiteralPath $kitsRoot) {
            $signtoolPath = Get-ChildItem -LiteralPath $kitsRoot -Filter signtool.exe -Recurse |
                Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
                Sort-Object FullName -Descending |
                Select-Object -ExpandProperty FullName -First 1
        }
    }
    if ([string]::IsNullOrWhiteSpace($signtoolPath)) {
        throw "Authenticode signing was requested but signtool.exe was not found."
    }

    $targets = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Include *.exe, *.dll |
        Where-Object {
            $_.FullName -notmatch '\\runtimes\\' -and
            $_.Name -notlike 'Microsoft.*' -and
            $_.Name -notlike 'WinRT.*' -and
            $_.Name -ne 'WebView2Loader.dll'
        })
    if ($targets.Count -eq 0) {
        throw "No Isley binaries were found to Authenticode-sign under $Root."
    }

    $materializedPfx = $null
    try {
        if ([string]::IsNullOrWhiteSpace($thumbprint) -and
            [string]::IsNullOrWhiteSpace($pfx) -and
            -not [string]::IsNullOrWhiteSpace($pfxBase64)) {
            # CI runners receive the PFX as a base64 secret, not as a file path.
            $materializedPfx = Join-Path ([System.IO.Path]::GetTempPath()) `
                ("isley-sign-" + [Guid]::NewGuid().ToString("N") + ".pfx")
            [System.IO.File]::WriteAllBytes(
                $materializedPfx,
                [Convert]::FromBase64String($pfxBase64.Trim()))
            $pfx = $materializedPfx
        }

        foreach ($target in $targets) {
            $signArguments = @(
                "sign",
                "/fd", "SHA256",
                "/td", "SHA256",
                "/tr", $timestampUrl
            )
            if (-not [string]::IsNullOrWhiteSpace($thumbprint)) {
                $signArguments += @("/sha1", $thumbprint)
            }
            else {
                $signArguments += @("/f", $pfx)
                if (-not [string]::IsNullOrWhiteSpace($password)) {
                    $signArguments += @("/p", $password)
                }
            }
            $signArguments += $target.FullName
            & $signtoolPath @signArguments
            if ($LASTEXITCODE -ne 0) {
                throw "Authenticode signing failed for $($target.FullName)."
            }
        }
    }
    finally {
        if ($null -ne $materializedPfx -and (Test-Path -LiteralPath $materializedPfx)) {
            Remove-Item -LiteralPath $materializedPfx -Force
        }
    }

    foreach ($target in $targets) {
        & $signtoolPath verify /pa /q $target.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "Authenticode verification failed for $($target.FullName); refusing to package an unverifiable release."
        }
    }

    Write-Host "Authenticode-signed and verified $($targets.Count) Isley binaries."
}

Protect-IsleyAuthenticode -Root $releaseRoot

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $clientStage,
    $clientArchive,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $serverStage,
    $serverArchive,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

# --- File-level delta package between the previous and current client build ---
# Emits Isley-delta-<from>-<to>.zip (changed/new files + isley-delta-manifest.json
# with the delete list) so the update client can prefer a smaller verified
# download. Not binary diffing: every entry is a whole file, auditable by hand.
$deltaFromVersion = $null
$deltaArchive = $null
$resolvedPrevious = $null
if (-not [string]::IsNullOrWhiteSpace($PreviousClientArchive)) {
    $resolvedPrevious = (Resolve-Path -LiteralPath $PreviousClientArchive).Path
}
else {
    $previousCandidate = Get-ChildItem -LiteralPath $artifacts `
        -Filter "Isley-Windows-x64-*.zip" -File |
        Where-Object { $_.FullName -ne $clientArchive } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -ne $previousCandidate) {
        $resolvedPrevious = $previousCandidate.FullName
    }
}

if ($null -ne $resolvedPrevious) {
    $previousRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
        ("isley-delta-prev-" + [Guid]::NewGuid().ToString("N"))
    $deltaStage = Join-Path ([System.IO.Path]::GetTempPath()) `
        ("isley-delta-stage-" + [Guid]::NewGuid().ToString("N"))
    try {
        [System.IO.Compression.ZipFile]::ExtractToDirectory($resolvedPrevious, $previousRoot)
        if (Test-Path -LiteralPath (Join-Path $previousRoot "IsleyData")) {
            throw "The previous release archive unexpectedly contains runtime user data."
        }

        $previousAssembly = Join-Path $previousRoot "Isley.dll"
        if (-not (Test-Path -LiteralPath $previousAssembly -PathType Leaf)) {
            throw "The previous release archive is missing Isley.dll."
        }
        $previousAssemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName(
            $previousAssembly).Version
        $deltaFromVersion = "{0}.{1}.{2}" -f `
            $previousAssemblyVersion.Major, `
            $previousAssemblyVersion.Minor, `
            $previousAssemblyVersion.Build
        if ($previousAssemblyVersion -ge [Version]$version) {
            throw "The previous release ($deltaFromVersion) is not older than $version."
        }

        function Get-IsleyTreeHashes {
            param([Parameter(Mandatory = $true)][string]$Root)
            $map = @{}
            Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
                $relative = $_.FullName.Substring($Root.Length).TrimStart('\')
                $map[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
            return $map
        }

        $currentHashes = Get-IsleyTreeHashes -Root $clientStage
        $previousHashes = Get-IsleyTreeHashes -Root $previousRoot
        $changed = @()
        foreach ($relative in $currentHashes.Keys) {
            if (-not $previousHashes.ContainsKey($relative) `
                -or $previousHashes[$relative] -ne $currentHashes[$relative]) {
                $changed += $relative
            }
        }
        $deleted = @()
        foreach ($relative in $previousHashes.Keys) {
            if (-not $currentHashes.ContainsKey($relative)) {
                $deleted += $relative
            }
        }
        # The updater helper always ships inside a delta so the install-side
        # delete-list step runs the verified new helper, never an older one.
        foreach ($helper in @("Updater\Isley.Updater.exe", "Updater\Isley.Updater.dll")) {
            if ($currentHashes.ContainsKey($helper) -and $changed -notcontains $helper) {
                $changed += $helper
            }
        }

        if ($changed.Count -eq 0) {
            throw "The previous and current client builds are identical; no delta is possible."
        }
        if ($deleted.Count -gt 2000) {
            throw "The delta delete list exceeded its safety limit."
        }

        $deletedNormalized = @($deleted |
            ForEach-Object { $_.Replace("\", "/") } |
            Sort-Object)
        foreach ($relative in $deletedNormalized) {
            if ($relative.StartsWith("IsleyData/", [System.StringComparison]::OrdinalIgnoreCase) `
                -or ($relative.Split("/") -contains "..")) {
                throw "The delta delete list contains an unsafe path: $relative"
            }
        }

        New-Item -ItemType Directory -Path $deltaStage -Force | Out-Null
        foreach ($relative in $changed) {
            $destination = Join-Path $deltaStage $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $clientStage $relative) `
                -Destination $destination -Force
        }
        # Unary comma keeps a single-entry delete list a JSON array.
        $deltaManifest = [ordered]@{
            format = 1
            fromVersion = $deltaFromVersion
            toVersion = $version
            deletedFiles = , $deletedNormalized
        } | ConvertTo-Json
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText(
            (Join-Path $deltaStage "isley-delta-manifest.json"),
            $deltaManifest,
            $utf8NoBom)

        $deltaArchive = Join-Path $artifacts `
            ("Isley-delta-{0}-{1}.zip" -f $deltaFromVersion, $version)
        if (Test-Path -LiteralPath $deltaArchive) {
            Remove-Item -LiteralPath $deltaArchive -Force
        }
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $deltaStage,
            $deltaArchive,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)
        if ((Get-Item -LiteralPath $deltaArchive).Length `
                -ge (Get-Item -LiteralPath $clientArchive).Length) {
            # A delta that saves nothing must not be published.
            Remove-Item -LiteralPath $deltaArchive -Force
            $deltaArchive = $null
        }
    }
    finally {
        foreach ($temporary in @($previousRoot, $deltaStage)) {
            if (Test-Path -LiteralPath $temporary) {
                Remove-Item -LiteralPath $temporary -Recurse -Force
            }
        }
    }
}

$deltaBytes = $null
$deltaSha256 = $null
if ($null -ne $deltaArchive) {
    $deltaBytes = (Get-Item -LiteralPath $deltaArchive).Length
    $deltaSha256 = (Get-FileHash -LiteralPath $deltaArchive -Algorithm SHA256).Hash
}

[pscustomobject]@{
    Version = $version
    ClientArchive = $clientArchive
    ClientBytes = (Get-Item -LiteralPath $clientArchive).Length
    ClientSha256 = (Get-FileHash -LiteralPath $clientArchive -Algorithm SHA256).Hash
    ServerArchive = $serverArchive
    ServerBytes = (Get-Item -LiteralPath $serverArchive).Length
    ServerSha256 = (Get-FileHash -LiteralPath $serverArchive -Algorithm SHA256).Hash
    DeltaFromVersion = $deltaFromVersion
    DeltaArchive = $deltaArchive
    DeltaBytes = $deltaBytes
    DeltaSha256 = $deltaSha256
}
