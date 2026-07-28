param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class IsleyLockNativeQa
{
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);
}
"@

function Get-PackedPoint([int]$X, [int]$Y) {
    $packed = (($Y -band 0xffff) -shl 16) -bor ($X -band 0xffff)
    return [IntPtr]::new([int64]$packed)
}

function Get-HitTest([IntPtr]$WindowHandle, [int]$X, [int]$Y) {
    return [IsleyLockNativeQa]::SendMessage(
        $WindowHandle,
        0x0084,
        [IntPtr]::Zero,
        (Get-PackedPoint $X $Y)).ToInt64()
}

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$workingDirectory = Split-Path -Parent $resolvedExecutable
$process = Start-Process -FilePath $resolvedExecutable -WorkingDirectory $workingDirectory -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
        throw "Isley did not expose a main window."
    }

    $window = [System.Windows.Automation.AutomationElement]::FromHandle(
        $process.MainWindowHandle)
    $lockCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "LockButton")
    $lockButton = $window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $lockCondition)
    if ($null -eq $lockButton) {
        throw "The Isley lock button was not available to UI Automation."
    }

    $windowBounds = $window.Current.BoundingRectangle
    $lockBounds = $lockButton.Current.BoundingRectangle
    $panelX = [int][Math]::Round($windowBounds.Left + ($windowBounds.Width * 0.5))
    $panelY = [int][Math]::Round($windowBounds.Top + ($windowBounds.Height * 0.55))
    $unlockX = [int][Math]::Round($lockBounds.Left + ($lockBounds.Width * 0.5))
    $unlockY = [int][Math]::Round($lockBounds.Top + ($lockBounds.Height * 0.5))

    $unlockedPanelHit = Get-HitTest $process.MainWindowHandle $panelX $panelY
    $invoke = [System.Windows.Automation.InvokePattern]$lockButton.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    Start-Sleep -Milliseconds 500

    $lockedPanelHit = Get-HitTest $process.MainWindowHandle $panelX $panelY
    $lockedUnlockHit = Get-HitTest $process.MainWindowHandle $unlockX $unlockY
    $lockedName = $lockButton.Current.Name
    if ($lockedPanelHit -ne -1) {
        throw "Locked panel expected HTTRANSPARENT (-1), received $lockedPanelHit."
    }
    if ($lockedUnlockHit -ne 1) {
        throw "Unlock button expected HTCLIENT (1), received $lockedUnlockHit."
    }
    if ($lockedName -ne "Unlock Isley overlay") {
        throw "Expected the accessible unlock label, received '$lockedName'."
    }

    $invoke.Invoke()
    Start-Sleep -Milliseconds 500
    $restoredPanelHit = Get-HitTest $process.MainWindowHandle $panelX $panelY
    if ($restoredPanelHit -eq -1) {
        throw "The panel remained click-through after unlocking."
    }

    $dockCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "DockButton")
    $dockButton = $window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $dockCondition)
    if ($null -eq $dockButton) {
        throw "The Isley dock button was not available to UI Automation."
    }
    $dockInvoke = [System.Windows.Automation.InvokePattern]$dockButton.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $dockInvoke.Invoke()
    Start-Sleep -Milliseconds 750

    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $process.Id)
    $desktopWindows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        $processCondition)
    $dockWindow = $null
    foreach ($candidate in $desktopWindows) {
        if ($candidate.Current.Name -eq "Isley Dock") {
            $dockWindow = $candidate
            break
        }
    }
    if ($null -eq $dockWindow) {
        throw "Isley did not expose the minimized dock window."
    }

    $dockLockCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "DockLockButton")
    $dockLockButton = $dockWindow.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $dockLockCondition)
    if ($null -eq $dockLockButton) {
        throw "The minimized dock lock button was not available."
    }

    $dockHandle = [IntPtr]::new($dockWindow.Current.NativeWindowHandle)
    $dockBounds = $dockWindow.Current.BoundingRectangle
    $dockLockBounds = $dockLockButton.Current.BoundingRectangle
    $dockPanelX = [int][Math]::Round($dockBounds.Left + ($dockBounds.Width * 0.35))
    $dockPanelY = [int][Math]::Round($dockBounds.Top + ($dockBounds.Height * 0.5))
    $dockUnlockX = [int][Math]::Round($dockLockBounds.Left + ($dockLockBounds.Width * 0.5))
    $dockUnlockY = [int][Math]::Round($dockLockBounds.Top + ($dockLockBounds.Height * 0.5))
    $dockUnlockedPanelHit = Get-HitTest $dockHandle $dockPanelX $dockPanelY
    $dockLockInvoke = [System.Windows.Automation.InvokePattern]$dockLockButton.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $dockLockInvoke.Invoke()
    Start-Sleep -Milliseconds 500
    $dockLockedPanelHit = Get-HitTest $dockHandle $dockPanelX $dockPanelY
    $dockLockedUnlockHit = Get-HitTest $dockHandle $dockUnlockX $dockUnlockY
    if ($dockLockedPanelHit -ne -1) {
        throw "Locked dock expected HTTRANSPARENT (-1), received $dockLockedPanelHit."
    }
    if ($dockLockedUnlockHit -ne 1) {
        throw "Dock unlock button expected HTCLIENT (1), received $dockLockedUnlockHit."
    }
    $dockLockInvoke.Invoke()
    Start-Sleep -Milliseconds 500
    $dockRestoredPanelHit = Get-HitTest $dockHandle $dockPanelX $dockPanelY
    if ($dockRestoredPanelHit -eq -1) {
        throw "The dock remained click-through after unlocking."
    }

    [pscustomobject]@{
        ProcessId = $process.Id
        WindowTitle = $process.MainWindowTitle
        UnlockedPanelHit = $unlockedPanelHit
        LockedPanelHit = $lockedPanelHit
        LockedUnlockHit = $lockedUnlockHit
        RestoredPanelHit = $restoredPanelHit
        AccessibleName = $lockedName
        DockUnlockedPanelHit = $dockUnlockedPanelHit
        DockLockedPanelHit = $dockLockedPanelHit
        DockLockedUnlockHit = $dockLockedUnlockHit
        DockRestoredPanelHit = $dockRestoredPanelHit
    } | Format-List
    Write-Output "Selective lock runtime: PASS"
}
finally {
    if (!$process.HasExited) {
        Stop-Process -Id $process.Id
    }
}
