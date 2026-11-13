# Seek by clicking the progress bar, then close/reopen Spotify and observe island states.
param([string]$Out)
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$root = Resolve-Path "$PSScriptRoot\..\.."
Import-Module "$PSScriptRoot\Win.psm1" -Force
$probe = "$root\tools\MediaProbe\bin\Debug\net10.0-windows10.0.22621.0\MediaProbe.exe"
$shot = "$PSScriptRoot\shot.ps1"
function Shot($name) { powershell -NoProfile -File $shot -Out "$Out\$name.png" }
function Timeline { ((& $probe sessions | Select-String "spotify=True" -Context 0,2) | ForEach-Object { $_.Context.PostContext[1].Trim() }) }

$orig = New-Object TW+POINT; [void][TW]::GetCursorPos([ref]$orig)
[TW]::SetCursorPos(960, 20) | Out-Null; Start-Sleep -Milliseconds 800
"Before seek: $(Timeline)"
[TW]::Click(960, 114); Start-Sleep -Milliseconds 900
"After seek click at 50%: $(Timeline)"
Shot "k1_after_seek"
[TW]::SetCursorPos($orig.X, $orig.Y) | Out-Null
Start-Sleep -Milliseconds 800

"--- closing Spotify (all processes) ---"
Get-Process Spotify -ErrorAction SilentlyContinue | Stop-Process -Force
$sw = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds 6
Shot "k2_spotify_closed"
"--- reopening Spotify ---"
Start-Process "$env:APPDATA\Spotify\Spotify.exe" -ArgumentList "--minimized"
Start-Sleep -Seconds 8
Shot "k3_spotify_reopened"
& $probe sessions | Out-Host
