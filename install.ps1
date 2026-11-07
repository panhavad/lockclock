param(
    [int]$TimeoutMinutes = 5,
    [switch]$KeepDisplayOn,
    [switch]$NoLockOnResume
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')

# The app installs itself (copies to %LOCALAPPDATA%\LockClock, sets it as screensaver, creates shortcuts).
$installArgs = @('/install', "$TimeoutMinutes")
if ($NoLockOnResume) { $installArgs += '/nolock' }
$psi = New-Object System.Diagnostics.ProcessStartInfo (Join-Path $PSScriptRoot 'bin\LockClock.scr'), ($installArgs -join ' ')
$psi.UseShellExecute = $false  # shell-launching a .scr drops its arguments
$p = [System.Diagnostics.Process]::Start($psi); $p.WaitForExit()
if ($p.ExitCode -ne 0) { throw "Install failed ($($p.ExitCode))" }

if ($KeepDisplayOn) {
    powercfg /change monitor-timeout-ac 0
    Write-Host 'Display auto-off disabled while plugged in.'
}

$policy = 'HKCU:\Software\Policies\Microsoft\Windows\Control Panel\Desktop'
if (Test-Path $policy) {
    $pol = Get-ItemProperty $policy
    if ($pol.'SCRNSAVE.EXE' -or $pol.ScreenSaveActive -eq '0') {
        Write-Warning 'A Group Policy controls the screensaver on this PC and may override LockClock.'
    }
}

Write-Host "Installed to $env:LOCALAPPDATA\LockClock (starts after $TimeoutMinutes min idle)."
Write-Host "Start Menu: 'Lock with Clock' (Ctrl+Alt+L) and 'LockClock Settings'"
