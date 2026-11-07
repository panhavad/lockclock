$ErrorActionPreference = 'SilentlyContinue'
$dir = Join-Path $env:LOCALAPPDATA 'LockClock'
$exe = Join-Path $dir 'LockClock.exe'
# The app undoes its own setup: stops the Win+L helper, gives Win+L back to Windows, unsets the screensaver, removes shortcuts.
if (Test-Path $exe) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo $exe, '/uninstall'
    $psi.UseShellExecute = $false
    [System.Diagnostics.Process]::Start($psi).WaitForExit()
}
Remove-Item $dir -Recurse -Force
Remove-Item 'HKCU:\Software\LockClock' -Recurse
Write-Host 'LockClock removed.'
Write-Host 'Note: "Skip the lock screen picture" is a machine setting; turn it off in LockClock Settings before uninstalling if you want it back.'
