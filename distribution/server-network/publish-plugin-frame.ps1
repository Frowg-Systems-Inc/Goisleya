param(
    [string]$BridgeUrl = "http://127.0.0.1:5210/plugin/v1/telemetry",
    [string]$PluginKey = $env:Bridge__PluginKey,
    [string]$ExamplePath = (Join-Path $PSScriptRoot "PLUGIN_TELEMETRY_EXAMPLE.json"),
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($PluginKey) -or $PluginKey.Length -lt 32) {
    throw "Set Bridge__PluginKey (>=32 chars) or pass -PluginKey."
}
if (-not (Test-Path -LiteralPath $ExamplePath)) { throw "Missing $ExamplePath" }
$uri = [Uri]$BridgeUrl
if ($uri.Host -notin @("127.0.0.1", "localhost") -and -not $env:ISLEY_ALLOW_REMOTE_PLUGIN_PUBLISH) {
    throw "Refusing non-loopback publish."
}
$body = Get-Content -LiteralPath $ExamplePath -Raw -Encoding UTF8
if ($DryRun) { Write-Host "Dry run OK · $($body.Length) bytes"; exit 0 }
$response = Invoke-WebRequest -Uri $BridgeUrl -Method Post -Headers @{
    "X-Isley-Plugin-Key" = $PluginKey
    "Content-Type" = "application/json"
} -Body $body -UseBasicParsing
Write-Host "Published plugin frame · HTTP $($response.StatusCode)"
