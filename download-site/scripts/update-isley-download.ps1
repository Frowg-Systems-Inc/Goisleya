param(
    [string]$ArchivePath = "",
    [string]$ServerArchivePath = "",
    [string]$SiteRoot = "",
    [string]$ReleaseNotes = "Automatic update notifications and verified one-click installation."
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

$publicArchive = Join-Path $resolvedSiteRoot "public\Isley-Windows-x64.zip"
Copy-Item -LiteralPath $resolvedArchive -Destination $publicArchive -Force
if ($null -ne $resolvedServerArchive) {
    Copy-Item -LiteralPath $resolvedServerArchive `
        -Destination (Join-Path $resolvedSiteRoot "public\Isley-Server-Network.zip") `
        -Force
}

$manifestPath = Join-Path $resolvedSiteRoot "public\Isley-release.json"
$manifest = [ordered]@{
    manifestVersion = 1
    channel = "stable"
    version = $releaseVersion
    publishedAt = [DateTimeOffset]::UtcNow.ToString("O")
    downloadUrl = "https://isley-download.gmith.chatgpt.site/Isley-Windows-x64.zip"
    sha256 = $hash
    bytes = $archiveBytes
    notes = $ReleaseNotes
    required = $false
} | ConvertTo-Json

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
    ReleaseDate = $releaseDate
    ManifestPath = $manifestPath
    ServerArchive = $resolvedServerArchive
    ServerBytes = $serverBytes
    ServerSha256 = $serverHash
}
