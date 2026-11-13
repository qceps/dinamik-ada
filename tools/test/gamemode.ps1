# Game mode test: fullscreen borderless fake game, click-through, hover disabled, interaction mode, hide hotkey.
param([string]$Out, [switch]$TopMost)
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$root = Resolve-Path "$PSScriptRoot\..\.."
Import-Module "$PSScriptRoot\Win.psm1" -Force
$probe = "$root\tools\MediaProbe\bin\Debug\net10.0-windows10.0.22621.0\MediaProbe.exe"
$shot = "$PSScriptRoot\shot.ps1"
function Shot($name) { powershell -NoProfile -File $shot -Out "$Out\$name.png" }
function Fg { $f = Get-Fg; "$($f.Title) (pid $($f.Pid))" }
function Ex { $h = Get-IslandWindow; $v = [TW]::GetWindowLongPtrW($h, -20); "exstyle=0x{0:X} transparent={1} noactivate={2} topmost={3} visible={4}" -f $v, (($v -band 0x20) -ne 0), (($v -band 0x8000000) -ne 0), (($v -band 0x8) -ne 0), [TW]::IsWindowVisible($h) }
function Clicks { (Get-Content $clickLog | Where-Object { $_ -like 'click*' }).Count }
function Status { ((& $probe sessions | Select-String "AUMID='Spotify.exe'").Line -replace '.*status=', '') }

$clickLog = "$Out\harness_clicks.txt"
$orig = New-Object TW+POINT; [void][TW]::GetCursorPos([ref]$orig)
$args2 = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "$PSScriptRoot\harness.ps1", "-Log", $clickLog, "-Fullscreen", "-Seconds", "70")
if ($TopMost) { $args2 += "-TopMost" }
$harness = Start-Process powershell -ArgumentList $args2 -PassThru
Start-Sleep -Seconds 4
"FG: $(Fg)"
"Island: $(Ex)"
Shot "g1_fullscreen_game"

"-- click on island (should pass through to game)"
$before = Clicks
[TW]::Click(960, 25); Start-Sleep -Milliseconds 500
"game clicks +$((Clicks) - $before) ; FG: $(Fg)"

"-- hover on island (must NOT expand in game mode)"
[TW]::SetCursorPos(960, 22) | Out-Null; Start-Sleep -Milliseconds 900
Shot "g2_hover_in_game"
[TW]::SetCursorPos(960, 700) | Out-Null

"-- expand hotkey (view only, still click-through)"
Send-Hotkey 0x78; Start-Sleep -Milliseconds 800
Shot "g3_expand_hotkey_in_game"
"Island: $(Ex)"
Send-Hotkey 0x78; Start-Sleep -Milliseconds 600

"-- interaction mode ON (Ctrl+Shift+F11)"
Send-Hotkey 0x7A; Start-Sleep -Milliseconds 800
"Island: $(Ex)"
Shot "g4_interaction_mode"
$before = Clicks
$s0 = Status
[TW]::Click(960, 149); Start-Sleep -Milliseconds 900
"play/pause click: spotify $s0 -> $(Status) ; game clicks +$((Clicks) - $before) ; FG: $(Fg)"
[TW]::Click(960, 149); Start-Sleep -Milliseconds 900
"play/pause click 2: spotify -> $(Status)"

"-- interaction mode OFF (hotkey)"
[TW]::SetCursorPos(960, 700) | Out-Null
Send-Hotkey 0x7A; Start-Sleep -Milliseconds 800
"Island: $(Ex)"
$before = Clicks
[TW]::Click(960, 25); Start-Sleep -Milliseconds 500
"click on island after interaction off: game clicks +$((Clicks) - $before)"

"-- hide/show hotkey (Ctrl+Shift+F10)"
Send-Hotkey 0x79; Start-Sleep -Milliseconds 700
"after hide: $(Ex)"
Shot "g5_hidden"
Send-Hotkey 0x79; Start-Sleep -Milliseconds 700
"after show: $(Ex)"

"-- interaction auto-timeout (8s, mouse away)"
Send-Hotkey 0x7A; Start-Sleep -Milliseconds 500
"on: $(Ex)"
Start-Sleep -Seconds 9
"after 9s: $(Ex)"
"FG end: $(Fg)"
[TW]::SetCursorPos($orig.X, $orig.Y) | Out-Null
Stop-Process -Id $harness.Id -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
"after game closed: $(Ex)"
