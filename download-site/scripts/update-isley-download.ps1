param(
    [string]$ArchivePath = "",
    [string]$ServerArchivePath = "",
    [string]$SiteRoot = "",
    [string]$ReleaseNotes = "Automatic update notifications and verified one-click installation.",
    [string]$Version = "",
    [ValidateSet("stable", "beta")]
    [string]$Channel = "stable",
    [string]$DeltaArchivePath = ""
)

$ErrorActionPreference = "Stop"

$resolvedSiteRoot = if ([string]::IsNullOrWhiteSpace($SiteRoot)) {
    (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
}
else {
    (Resolve-Path -LiteralPath $SiteRoot).Path
}
$workspace = (Resolve-Path -LiteralPath (Join-Path $resolvedSiteRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $archive = Get-ChildItem -LiteralPath (Join-Path $workspace "artifacts") `
        -Filter "Isley-Windows-x64*.zip" -File -Recurse |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $archive) {
        throw "No packaged Isley Windows archive was found."
    }
    $resolvedArchive = $archive.FullName
}
else {
    $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
}

$resolvedServerArchive = if ([string]::IsNullOrWhiteSpace($ServerArchivePath)) {
    $candidate = Get-ChildItem -LiteralPath (Join-Path $workspace "artifacts") `
        -Filter "Isley-Server-Network*.zip" -File -Recurse |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) { $null } else { $candidate.FullName }
}
else {
    (Resolve-Path -LiteralPath $ServerArchivePath).Path
}

if ([System.IO.Path]::GetExtension($resolvedArchive) -ne ".zip") {
    throw "The selected release is not a ZIP archive."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchive)
try {
    $entries = @($zip.Entries)
    if ($entries.Count -lt 20) {
        throw "The selected release is incomplete."
    }
    $normalizedEntries = @()
    foreach ($entry in $entries) {
        $normalized = $entry.FullName.Replace("\", "/")
        $normalizedEntries += $normalized
        $isUnsafe = $normalized.StartsWith("/") `
            -or $normalized -match "^[A-Za-z]:" `
            -or $normalized.Split("/") -contains ".."
        if ($isUnsafe) {
            throw "The selected release contains an unsafe path."
        }
    }
    foreach ($required in @(
        "Isley.exe",
        "Isley.dll",
        "Map/index.html",
        "Voice/voice.html",
        "Voice/voice.js",
        "Voice/voice-crypto.js",
        "Voice/voice.css",
        "VoiceServer/Isley.VoiceServer.exe",
        "VoiceServer/Isley.VoiceServer.dll",
        "VoiceServer/appsettings.json",
        "Updater/Isley.Updater.exe",
        "README.txt"
    )) {
        if ($normalizedEntries -notcontains $required) {
            throw "The selected release is missing $required."
        }
    }

    $assemblyEntry = $zip.GetEntry("Isley.dll")
    $temporaryAssembly = [System.IO.Path]::GetTempFileName()
    try {
        $input = $assemblyEntry.Open()
        $output = [System.IO.File]::Open(
            $temporaryAssembly,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        try {
            $input.CopyTo($output)
        }
        finally {
            $output.Dispose()
            $input.Dispose()
        }
        $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName(
            $temporaryAssembly).Version
        $releaseVersion = "{0}.{1}.{2}" -f `
            $assemblyVersion.Major, $assemblyVersion.Minor, $assemblyVersion.Build
        if ($assemblyVersion -lt [Version]"1.1.0") {
            throw "The selected release predates the Isley update channel."
        }
    }
    finally {
        Remove-Item -LiteralPath $temporaryAssembly -Force -ErrorAction SilentlyContinue
    }
}
finally {
    $zip.Dispose()
}

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    # The workflow resolves the release version (bump or override) up front and
    # stamps the build with it, so the packaged assembly must already agree.
    # A mismatch here means the pipeline drifted; never publish a manifest whose
    # version outruns the archive (clients refuse staged builds older than the
    # manifest version).
    if ($Version -notmatch '^\d{1,4}\.\d{1,4}\.\d{1,6}$') {
        throw "-Version must be a three-part Isley version (X.Y.Z); got '$Version'."
    }
    if ($Version -ne $releaseVersion) {
        throw ("The requested release version {0} does not match the packaged build {1}; " +
            "package first with scripts\package-isley-1.3.ps1 -Version {0}.") -f `
            $Version, $releaseVersion
    }
}

$serverHash = $null
$serverBytes = $null
if ($null -ne $resolvedServerArchive) {
    $serverZip = [System.IO.Compression.ZipFile]::OpenRead($resolvedServerArchive)
    try {
        $serverEntries = @($serverZip.Entries | ForEach-Object {
            $_.FullName.Replace("\", "/")
        })
        foreach ($required in @(
            "README.md",
            "PLUGIN_TELEMETRY_EXAMPLE.json",
            "Start-IsleyServerBridge.ps1",
            "THE_ISLE_TELEMETRY_INTERFACE_REQUEST.md",
            "Isley.Relay/Isley.Relay.dll",
            "Isley.ServerBridge/Isley.ServerBridge.dll"
        )) {
            if ($serverEntries -notcontains $required) {
                throw "The selected server network kit is missing $required."
            }
        }
    }
    finally {
        $serverZip.Dispose()
    }
    $serverHash = (Get-FileHash -LiteralPath $resolvedServerArchive -Algorithm SHA256).Hash
    $serverBytes = (Get-Item -LiteralPath $resolvedServerArchive).Length
    if ($serverBytes -lt 200KB -or $serverBytes -gt 2MB) {
        throw "The selected server network kit has an unexpected size."
    }
}

$hash = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash
$archiveBytes = (Get-Item -LiteralPath $resolvedArchive).Length
if ($archiveBytes -lt 7MB -or $archiveBytes -gt 15MB) {
    throw "The selected release has an unexpected size."
}
$archiveSize = "{0:0.00} MB" -f ($archiveBytes / 1MB)
$releaseDate = Get-Date -Format "MMMM d, yyyy"

$isBeta = $Channel -eq "beta"
# Stable publishes to the pinned stable paths; beta publishes to the pinned
# beta paths (IsleyReleaseLogic.BetaReleaseEndpoint / BetaDownloadUrl) and
# leaves every stable file untouched.
$publicArchiveName = if ($isBeta) { "Isley-Windows-x64-beta.zip" } else { "Isley-Windows-x64.zip" }
$publicArchive = Join-Path $resolvedSiteRoot "public\$publicArchiveName"
Copy-Item -LiteralPath $resolvedArchive -Destination $publicArchive -Force
if (-not $isBeta -and $null -ne $resolvedServerArchive) {
    Copy-Item -LiteralPath $resolvedServerArchive `
        -Destination (Join-Path $resolvedSiteRoot "public\Isley-Server-Network.zip") `
        -Force
}

# Optional delta offer. The client contract (docs/ISLEY_UPDATER_DELTA.md §3)
# is exactly four fields; an absent block means "no delta offered".
$deltaBlock = $null
$resolvedDelta = $null
$deltaFromVersion = $null
$deltaSha256 = $null
$deltaBytes = $null
if (-not [string]::IsNullOrWhiteSpace($DeltaArchivePath)) {
    $resolvedDelta = (Resolve-Path -LiteralPath $DeltaArchivePath).Path
    $deltaName = [System.IO.Path]::GetFileName($resolvedDelta)
    if ($deltaName -notmatch `
        '^Isley-delta-(?<from>\d{1,4}\.\d{1,4}\.\d{1,6})-(?<to>\d{1,4}\.\d{1,4}\.\d{1,6})\.zip$') {
        throw "The delta archive name '$deltaName' does not match Isley-delta-<from>-<to>.zip."
    }
    $deltaFromVersion = $Matches['from']
    if ($Matches['to'] -ne $releaseVersion) {
        throw "The delta archive targets $($Matches['to']) but this release is $releaseVersion."
    }
    if ([Version]$deltaFromVersion -ge [Version]$releaseVersion) {
        throw "The delta base $deltaFromVersion is not older than $releaseVersion."
    }
    $deltaSha256 = (Get-FileHash -LiteralPath $resolvedDelta -Algorithm SHA256).Hash
    $deltaBytes = (Get-Item -LiteralPath $resolvedDelta).Length
    if ($deltaBytes -lt 256 -or $deltaBytes -gt 100MB) {
        throw "The delta archive size is outside the client's accepted bounds (256 B - 100 MB)."
    }
    Copy-Item -LiteralPath $resolvedDelta `
        -Destination (Join-Path $resolvedSiteRoot "public\$deltaName") `
        -Force
    $deltaBlock = [ordered]@{
        fromVersion = $deltaFromVersion
        url = "https://isley-download.gmith.chatgpt.site/$deltaName"
        sha256 = $deltaSha256
        bytes = $deltaBytes
    }
}

$manifestName = if ($isBeta) { "Isley-release-beta.json" } else { "Isley-release.json" }
$manifestPath = Join-Path $resolvedSiteRoot "public\$manifestName"
$manifestObject = [ordered]@{
    manifestVersion = 1
    channel = $Channel
    version = $releaseVersion
    publishedAt = [DateTimeOffset]::UtcNow.ToString("O")
    downloadUrl = "https://isley-download.gmith.chatgpt.site/$publicArchiveName"
    sha256 = $hash
    bytes = $archiveBytes
    notes = $ReleaseNotes
    required = $false
}
if ($null -ne $deltaBlock) {
    $manifestObject["delta"] = $deltaBlock
}
$manifest = $manifestObject | ConvertTo-Json

if (-not $isBeta) {
    # The public download page and its rendered-html contract describe the
    # stable release only; beta runs leave them untouched.
    $pagePath = Join-Path $resolvedSiteRoot "app\page.tsx"
    $page = [System.IO.File]::ReadAllText($pagePath)
    $page = [regex]::Replace(
        $page,
        'const ARCHIVE_SIZE = "[^"]+";',
        "const ARCHIVE_SIZE = `"$archiveSize`";")
    $page = [regex]::Replace(
        $page,
        'const RELEASE_VERSION = "[^"]+";',
        "const RELEASE_VERSION = `"$releaseVersion`";")
    $page = [regex]::Replace(
        $page,
        'const RELEASE_DATE = "[^"]+";',
        "const RELEASE_DATE = `"$releaseDate`";")
    $page = [regex]::Replace(
        $page,
        'const SHA256 =\s*"[A-F0-9]{64}";',
        "const SHA256 =`r`n  `"$hash`";")

    $testPath = Join-Path $resolvedSiteRoot "tests\rendered-html.test.mjs"
    $test = [System.IO.File]::ReadAllText($testPath)
    $test = [regex]::Replace(
        $test,
        'const EXPECTED_CLIENT_SHA256 =\s*"[A-F0-9]{64}";',
        "const EXPECTED_CLIENT_SHA256 =`r`n  `"$hash`";")
    if ($null -ne $serverHash) {
        $test = [regex]::Replace(
            $test,
            'const EXPECTED_SERVER_SHA256 =\s*"[A-F0-9]{64}";',
            "const EXPECTED_SERVER_SHA256 =`r`n  `"$serverHash`";")
    }
    $test = [regex]::Replace(
        $test,
        'version:\s*"\d+\.\d+\.\d+",',
        "version: `"$releaseVersion`",")

    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($pagePath, $page, $utf8)
    [System.IO.File]::WriteAllText($testPath, $test, $utf8)
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($manifestPath, $manifest, $utf8)

$publishedHash = (Get-FileHash -LiteralPath $publicArchive -Algorithm SHA256).Hash
if ($publishedHash -ne $hash) {
    throw "The staged public archive does not match the verified release."
}

[pscustomobject]@{
    SourceArchive = $resolvedArchive
    StablePublicPath = $publicArchive
    Bytes = $archiveBytes
    DisplaySize = $archiveSize
    Sha256 = $hash
    Version = $releaseVersion
    Channel = $Channel
    ReleaseDate = $releaseDate
    ManifestPath = $manifestPath
    ServerArchive = $resolvedServerArchive
    ServerBytes = $serverBytes
    ServerSha256 = $serverHash
    DeltaArchive = $resolvedDelta
    DeltaFromVersion = $deltaFromVersion
    DeltaBytes = $deltaBytes
    DeltaSha256 = $deltaSha256
}
