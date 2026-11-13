# 1) TopMost fullscreen fake game: island must stay above it.
# 2) Normal mode (windowed app under island): transparent window areas must pass clicks; island itself must not.
param([string]$Out)
Import-Module "$PSScriptRoot\Win.psm1" -Force
$shot = "$PSScriptRoot\shot.ps1"
function Shot($name) { powershell -NoProfile -File $shot -Out "$Out\$name.png" }
function Clicks { (Get-Content $clickLog | Where-Object { $_ -like 'click*' }).Count }
$clickLog = "$Out\harness_clicks2.txt"
$orig = New-Object TW+POINT; [void][TW]::GetCursorPos([ref]$orig)

$h = Start-Process powershell -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "$PSScriptRoot\harness.ps1", "-Log", $clickLog, "-Fullscreen", "-TopMost", "-Seconds", "20") -PassThru
Start-Sleep -Seconds 4
$island = Get-IslandWindow
"window at island center: " + [TW]::Title([TW]::At(960, 25)) + " (island hwnd $island)"
Shot "m1_topmost_game"
Stop-Process -Id $h.Id -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$h = Start-Process powershell -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "$PSScriptRoot\harness.ps1", "-Log", $clickLog, "-Seconds", "20") -PassThru
Start-Sleep -Seconds 3
$r = New-Object TW+RECT; [void][TW]::GetWindowRect($island, [ref]$r)
"island window rect: $($r.L),$($r.T) - $($r.R),$($r.B) (compact island ~ 874..1046 x 8..42)"
$tests = @(
  @{ n = "transparent area left of island"; x = 800; y = 20 },
  @{ n = "transparent area below island";   x = 960; y = 150 },
  @{ n = "on island (compact)";             x = 960; y = 25 }
)
foreach ($t in $tests) {
  $b = Clicks
  [TW]::Click($t.x, $t.y); Start-Sleep -Milliseconds 400
  [TW]::SetCursorPos(960, 500) | Out-Null; Start-Sleep -Milliseconds 500
  "{0} ({1},{2}): app clicks +{3}" -f $t.n, $t.x, $t.y, ((Clicks) - $b)
}
[TW]::SetCursorPos($orig.X, $orig.Y) | Out-Null
Stop-Process -Id $h.Id -ErrorAction SilentlyContinue
