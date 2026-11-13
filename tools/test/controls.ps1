# Click the island's own controls (normal mode) and verify Spotify state + foreground.
param([string]$Out)
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$root = Resolve-Path "$PSScriptRoot\..\.."
Import-Module "$PSScriptRoot\Win.psm1" -Force
$probe = "$root\tools\MediaProbe\bin\Debug\net10.0-windows10.0.22621.0\MediaProbe.exe"
$shot = "$PSScriptRoot\shot.ps1"
function Shot($name) { powershell -NoProfile -File $shot -Out "$Out\$name.png" }
function Fg { $f = Get-Fg; "$($f.Title) (pid $($f.Pid))" }
function SpotifyState { (& $probe sessions | Select-String "AUMID='Spotify.exe'").Line.Trim() + " | " + ((& $probe sessions | Select-String "spotify=True" -Context 0,1) | ForEach-Object { $_.Context.PostContext[0].Trim() }) }

$orig = New-Object TW+POINT; [void][TW]::GetCursorPos([ref]$orig)
"FG start: $(Fg)"
"Before: $(SpotifyState)"
[TW]::SetCursorPos(960, 20) | Out-Null; Start-Sleep -Milliseconds 800
Shot "c1_expanded_playing"
[TW]::Click(960, 149); Start-Sleep -Milliseconds 900
"After play/pause click: $(SpotifyState)"
"FG: $(Fg)"
Shot "c2_after_pause_click"
[TW]::Click(960, 149); Start-Sleep -Milliseconds 900
"After 2nd click: $(SpotifyState)"
[TW]::Click(890, 149); Start-Sleep -Milliseconds 1200
"After previous click: $(SpotifyState)"
Shot "c3_after_prev"
"FG end: $(Fg)"
[TW]::SetCursorPos($orig.X, $orig.Y) | Out-Null
