param(
    [string]$BridgeReadyUrl = "http://127.0.0.1:5210/health/ready",
    [string]$RelayReadyUrl = "",
    [int]$IntervalSeconds = 30,
    [int]$FailureThreshold = 3,
    [switch]$Once
)
$ErrorActionPreference = "Stop"
$bf = 0; $rf = 0
function Test-Ready($Url) {
    if ([string]::IsNullOrWhiteSpace($Url)) { return $true }
    try { (Invoke-WebRequest $Url -UseBasicParsing -TimeoutSec 5).StatusCode -lt 300 } catch { $false }
}
do {
    if (-not (Test-Ready $BridgeReadyUrl)) {
        $bf++; Write-Host "$(Get-Date -Format o) Bridge not ready ($bf)"
        if ($bf -ge $FailureThreshold) { Restart-Service IsleyServerBridge -Force -ErrorAction SilentlyContinue; $bf = 0 }
    } else { $bf = 0 }
    if ($RelayReadyUrl -and -not (Test-Ready $RelayReadyUrl)) {
        $rf++; Write-Host "$(Get-Date -Format o) Relay not ready ($rf)"
        if ($rf -ge $FailureThreshold) { Restart-Service IsleyRelay -Force -ErrorAction SilentlyContinue; $rf = 0 }
    } else { $rf = 0 }
    if ($Once) { break }
    Start-Sleep ([Math]::Max(5, $IntervalSeconds))
} while ($true)
