$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $out | Out-Null
if (-not (Test-Path "$PSScriptRoot\LockClock.ico")) { & "$PSScriptRoot\make-icon.ps1" }
& $csc /nologo /optimize+ /target:winexe /platform:anycpu /win32icon:"$PSScriptRoot\LockClock.ico" `
    /out:"$out\LockClock.scr" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll `
    "$PSScriptRoot\LockClock.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $out\LockClock.scr"
