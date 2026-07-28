param(
    [string]$ArchivePath = "",
    [string]$ServerArchivePath = "",
    [string]$SiteRoot = "",
    [string]$ReleaseNotes = "Automatic update notifications and verified one-click installation."
)

$ErrorActionPreference = "Stop"

$workspace = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$resolvedSiteRoot = if ([string]::IsNullOrWhiteSpace($SiteRoot)) {
    Join-Path $workspace "download-site"
}
else {
    (Resolve-Path -LiteralPath $SiteRoot).Path
}
$siteUpdater = Join-Path $resolvedSiteRoot "scripts\update-isley-download.ps1"
if (-not (Test-Path -LiteralPath $siteUpdater -PathType Leaf)) {
    throw "The Isley download-site updater was not found."
}

& $siteUpdater `
    -ArchivePath $ArchivePath `
    -ServerArchivePath $ServerArchivePath `
    -SiteRoot $resolvedSiteRoot `
    -ReleaseNotes $ReleaseNotes
