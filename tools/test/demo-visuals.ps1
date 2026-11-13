# Demo mode: long title, missing artwork, paused CPU. Does not touch Spotify.
param([string]$Out, [string]$Exe)
Import-Module "$PSScriptRoot\Win.psm1" -Force
$shot = "$PSScriptRoot\shot.ps1"
function Shot($name) { powershell -NoProfile -File $shot -Out "$Out\$name.png" }
Get-Process DinamikAda -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1
$orig = New-Object TW+POINT; [void][TW]::GetCursorPos([ref]$orig)
Start-Process $Exe -ArgumentList "--demo"
Start-Sleep -Milliseconds 1500; Shot "d1_demo_peek"
[TW]::SetCursorPos(960, 20) | Out-Null; Start-Sleep -Milliseconds 900
[TW]::Click(1030, 149); Start-Sleep -Milliseconds 700; Shot "d2_long_title_expanded"
[TW]::SetCursorPos(960, 600) | Out-Null; Start-Sleep -Milliseconds 300; Shot "d2b_collapse_midway"
Start-Sleep -Milliseconds 900; Shot "d3_long_title_peek"
[TW]::SetCursorPos(960, 20) | Out-Null; Start-Sleep -Milliseconds 900
[TW]::Click(1030, 149); Start-Sleep -Milliseconds 700; Shot "d4_no_cover_expanded"
[TW]::Click(960, 149); Start-Sleep -Milliseconds 400   # pause
[TW]::SetCursorPos(960, 600) | Out-Null; Start-Sleep -Seconds 6; Shot "d5_no_cover_compact_paused"
powershell -NoProfile -File "$PSScriptRoot\perf.ps1" -Seconds 10 -Label "demo paused compact"
[TW]::SetCursorPos($orig.X, $orig.Y) | Out-Null
