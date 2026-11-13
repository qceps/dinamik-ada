# Repro: hover-expand then click "next" while still hovering (demo mode, trace on).
param([string]$Exe)
Import-Module "$PSScriptRoot\Win.psm1" -Force
Get-Process DinamikAda -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1
$log = "$env:LOCALAPPDATA\DinamikAda\logs\app.log"
$env:DINAMIKADA_TRACE = "1"
$orig = New-Object TW+POINT; [void][TW]::GetCursorPos([ref]$orig)
[TW]::SetCursorPos(960, 600) | Out-Null
Start-Process $Exe -ArgumentList "--demo"
Start-Sleep -Milliseconds 1500
$mark = (Get-Content $log).Count
"-- hover"; [TW]::SetCursorPos(960, 20) | Out-Null; Start-Sleep -Milliseconds 900
"-- move to next button"; [TW]::SetCursorPos(1030, 149) | Out-Null; Start-Sleep -Milliseconds 300
"-- click"; [TW]::Click(1030, 149); Start-Sleep -Milliseconds 1200
Get-Content $log | Select-Object -Skip $mark | ForEach-Object { $_.Substring(11) }
[TW]::SetCursorPos($orig.X, $orig.Y) | Out-Null
