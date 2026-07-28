param(
    [ValidateSet("Relay", "Bridge", "Both")][string]$Target = "Both",
    [string]$KitRoot = $PSScriptRoot,
    [string]$EnvScript = (Join-Path $PSScriptRoot "operator-local\run-env.ps1")
)
$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run elevated."
}
if (-not (Test-Path $EnvScript)) { throw "Run setup.ps1 first." }
. $EnvScript
function Install-Svc($Name, $Dll, $EnvMap) {
    $dotnet = (Get-Command dotnet).Source
    $existing = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -eq "Running") { Stop-Service $Name -Force }
        sc.exe delete $Name | Out-Null
        Start-Sleep 2
    }
    New-Service -Name $Name -BinaryPathName "`"$dotnet`" `"$Dll`"" -DisplayName $Name -StartupType Automatic | Out-Null
    $pairs = @($EnvMap.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" })
    if ($pairs.Count) {
        New-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$Name" -Name Environment -PropertyType MultiString -Value $pairs -Force | Out-Null
    }
    Start-Service $Name
    Write-Host "Started $Name"
}
$bridgeEnv = @{}; $relayEnv = @{}
Get-ChildItem Env: | Where-Object { $_.Name -like "Bridge__*" -or $_.Name -like "Rcon__*" } | ForEach-Object { $bridgeEnv[$_.Name] = $_.Value }
Get-ChildItem Env: | Where-Object { $_.Name -like "Relay__*" -or $_.Name -like "Steam__*" } | ForEach-Object { $relayEnv[$_.Name] = $_.Value }
if ($Target -in "Relay","Both") { Install-Svc "IsleyRelay" (Join-Path $KitRoot "Isley.Relay\Isley.Relay.dll") $relayEnv }
if ($Target -in "Bridge","Both") { Install-Svc "IsleyServerBridge" (Join-Path $KitRoot "Isley.ServerBridge\Isley.ServerBridge.dll") $bridgeEnv }
