[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[a-z0-9][a-z0-9-]{1,63}$")]
    [string]$ServerId,

    [Parameter(Mandatory = $true)]
    [ValidateLength(1, 80)]
    [string]$ServerName,

    [Parameter(Mandatory = $true)]
    [uri]$RelayUrl,

    [ValidateSet("Rcon", "Plugin", "Both")]
    [string]$SourceMode = "Rcon",

    [string]$RconHost = "127.0.0.1",

    [ValidateRange(1, 65535)]
    [int]$RconPort = 8888,

    [ValidateRange(200, 60000)]
    [int]$PollIntervalMilliseconds = 200,

    [ValidateSet("Self", "Friends", "Server")]
    [string]$DefaultShareScope = "Self",

    [switch]$ServerWideAwareness,

    [string]$BridgePath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Read-RequiredSecret {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt,

        [Parameter(Mandatory = $true)]
        [int]$MinimumLength
    )

    $secureValue = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
    try {
        $plainValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
    if ([string]::IsNullOrWhiteSpace($plainValue) -or $plainValue.Length -lt $MinimumLength) {
        throw "$Prompt must contain at least $MinimumLength characters."
    }
    return $plainValue
}

if ($RelayUrl.Scheme -ne "https" -and -not (
    $RelayUrl.Scheme -eq "http" -and
    $RelayUrl.IsLoopback
)) {
    throw "RelayUrl must use HTTPS. Plain HTTP is allowed only for loopback development."
}

if ([string]::IsNullOrWhiteSpace($BridgePath)) {
    $packagedBridge = Join-Path $PSScriptRoot "Isley.ServerBridge\Isley.ServerBridge.dll"
    $developmentBridge = Join-Path $PSScriptRoot `
        "..\Isley.ServerBridge\bin\Release\net8.0\Isley.ServerBridge.dll"
    $BridgePath = if (Test-Path -LiteralPath $packagedBridge -PathType Leaf) {
        $packagedBridge
    }
    elseif (Test-Path -LiteralPath $developmentBridge -PathType Leaf) {
        $developmentBridge
    }
    else {
        throw "Isley.ServerBridge.dll was not found. Build or extract the server-network kit first."
    }
}
$resolvedBridgePath = (Resolve-Path -LiteralPath $BridgePath).Path

$relaySecret = Read-RequiredSecret `
    -Prompt "Relay bridge secret (32+ characters)" `
    -MinimumLength 32
$rconPassword = ""
$pluginKey = ""
try {
    if ($SourceMode -in @("Rcon", "Both")) {
        $rconPassword = Read-RequiredSecret `
            -Prompt "Private Evrima RCON password" `
            -MinimumLength 1
    }
    if ($SourceMode -in @("Plugin", "Both")) {
        $pluginKey = Read-RequiredSecret `
            -Prompt "Local plugin key (32+ characters)" `
            -MinimumLength 32
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new("dotnet")
    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = Split-Path -Parent $resolvedBridgePath
    $startInfo.Arguments = '"' + $resolvedBridgePath.Replace('"', '\"') + '"'
    $startInfo.EnvironmentVariables["Bridge__ServerId"] = $ServerId
    $startInfo.EnvironmentVariables["Bridge__ServerName"] = $ServerName
    $startInfo.EnvironmentVariables["Bridge__RelayUrl"] = $RelayUrl.AbsoluteUri
    $startInfo.EnvironmentVariables["Bridge__RelaySecret"] = $relaySecret
    $startInfo.EnvironmentVariables["Bridge__SourceMode"] = $SourceMode
    $startInfo.EnvironmentVariables["Bridge__PluginEnabled"] = (
        $SourceMode -in @("Plugin", "Both")
    ).ToString().ToLowerInvariant()
    $startInfo.EnvironmentVariables["Bridge__PluginKey"] = $pluginKey
    $startInfo.EnvironmentVariables["Bridge__AllowRemotePlugin"] = "false"
    $startInfo.EnvironmentVariables["Bridge__ServerWideAwareness"] = (
        $ServerWideAwareness.IsPresent
    ).ToString().ToLowerInvariant()
    $startInfo.EnvironmentVariables["Rcon__Host"] = $RconHost
    $startInfo.EnvironmentVariables["Rcon__Port"] = $RconPort.ToString()
    $startInfo.EnvironmentVariables["Rcon__Password"] = $rconPassword
    $startInfo.EnvironmentVariables["Rcon__PollIntervalMilliseconds"] = (
        $PollIntervalMilliseconds.ToString()
    )
    $startInfo.EnvironmentVariables["Rcon__DefaultShareScope"] = $DefaultShareScope

    Write-Host "Starting the authorized Isley Server Bridge for $ServerName."
    Write-Host "Credentials are being passed only to this process and are not written to disk."
    Write-Host "Keep this window open. Press Ctrl+C to stop the bridge."
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "The Isley Server Bridge could not be started."
    }
    $process.WaitForExit()
    exit $process.ExitCode
}
finally {
    $relaySecret = $null
    $rconPassword = $null
    $pluginKey = $null
}
