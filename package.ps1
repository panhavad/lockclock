# Builds dist\LockClock.exe (portable) and dist\LockClock.msi (per-user installer).
param([string]$Version = '1.3.0')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')

$wixDir = Join-Path $env:LOCALAPPDATA 'lockclock-tools\wix314'
if (-not (Test-Path "$wixDir\candle.exe")) {
    New-Item -ItemType Directory -Force $wixDir | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest 'https://github.com/wixtoolset/wix3/releases/download/wix3141rtm/wix314-binaries.zip' -OutFile "$wixDir\wix.zip" -UseBasicParsing
    Expand-Archive "$wixDir\wix.zip" $wixDir -Force
}

$dist = Join-Path $PSScriptRoot 'dist'
$obj = Join-Path $PSScriptRoot 'obj'
New-Item -ItemType Directory -Force $dist, $obj | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'bin\LockClock.scr') (Join-Path $dist 'LockClock.exe') -Force

Push-Location $PSScriptRoot
try {
    & "$wixDir\candle.exe" -nologo "-dVersion=$Version" -out "$obj\LockClock.wixobj" LockClock.wxs
    if ($LASTEXITCODE -ne 0) { throw 'candle failed' }
    # ICE91: per-user files in a profile folder are expected for this per-user package.
    & "$wixDir\light.exe" -nologo -spdb -sice:ICE91 -out "$dist\LockClock.msi" "$obj\LockClock.wixobj"
    if ($LASTEXITCODE -ne 0) { throw 'light failed' }
} finally { Pop-Location }

Remove-Item $obj -Recurse -Force
Get-ChildItem $dist | Select-Object Name, @{n='KB'; e={[math]::Round($_.Length / 1KB)}}
