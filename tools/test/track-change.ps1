# Track change + rapid skip test (real Spotify). Usage: powershell -File track-change.ps1 -Out <dir>
param([string]$Out)
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$root = Resolve-Path "$PSScriptRoot\..\.."
Import-Module "$PSScriptRoot\Win.psm1" -Force
$probe = "$root\tools\MediaProbe\bin\Debug\net10.0-windows10.0.22621.0\MediaProbe.exe"
$shot = "$PSScriptRoot\shot.ps1"
function Shot($name) { powershell -NoProfile -File $shot -Out "$Out\$name.png" }
function Fg { $f = Get-Fg; "$($f.Title) (pid $($f.Pid))" }

"FG start: $(Fg)"
& $probe next | Out-Host
Start-Sleep -Milliseconds 250; Shot "t1_next_250ms"
Start-Sleep -Milliseconds 700; Shot "t2_next_950ms"
"FG after single skip: $(Fg)"

& $probe next | Out-Null
& $probe next | Out-Null
& $probe next | Out-Null
Start-Sleep -Milliseconds 200; Shot "t3_rapid_200ms"
Start-Sleep -Milliseconds 1300; Shot "t4_rapid_1500ms"
"Spotify now:"
& $probe sessions | Select-String "spotify=True" -Context 0,1 | ForEach-Object { $_.Context.PostContext }
"FG after rapid skips: $(Fg)"
Start-Sleep -Seconds 4; Shot "t5_back_to_compact"
