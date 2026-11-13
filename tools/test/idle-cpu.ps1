# Demo mode: pause, then measure CPU compact-paused vs hidden.
param([string]$Exe)
Import-Module "$PSScriptRoot\Win.psm1" -Force
Get-Process DinamikAda -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1
$orig = New-Object TW+POINT; [void][TW]::GetCursorPos([ref]$orig)
Start-Process $Exe -ArgumentList "--demo"
Start-Sleep -Milliseconds 1500
[TW]::SetCursorPos(960, 20) | Out-Null; Start-Sleep -Milliseconds 900
[TW]::Click(960, 149); Start-Sleep -Milliseconds 300
[TW]::SetCursorPos($orig.X, $orig.Y) | Out-Null
Start-Sleep -Seconds 6
powershell -NoProfile -File "$PSScriptRoot\perf.ps1" -Seconds 12 -Label "paused compact visible"
Send-Hotkey 0x79; Start-Sleep -Seconds 1
powershell -NoProfile -File "$PSScriptRoot\perf.ps1" -Seconds 12 -Label "hidden"
Send-Hotkey 0x79
