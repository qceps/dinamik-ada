# CPU while Spotify is playing (bars animating) for several render configurations.
param([string]$Exe)
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$configs = @(
  @{ n = "hw fps30 shadow"; fps = "30"; ns = "" },
  @{ n = "hw fps30 noshadow"; fps = "30"; ns = "1" },
  @{ n = "hw fps20 shadow"; fps = "20"; ns = "" },
  @{ n = "hw fps15 shadow"; fps = "15"; ns = "" }
)
foreach ($c in $configs) {
  Get-Process DinamikAda -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep 1
  $env:DINAMIKADA_FPS = $c.fps; $env:DINAMIKADA_NOSHADOW = $c.ns
  Start-Process $Exe
  Start-Sleep 8
  powershell -NoProfile -File "$PSScriptRoot\perf.ps1" -Seconds 15 -Label $c.n
}
