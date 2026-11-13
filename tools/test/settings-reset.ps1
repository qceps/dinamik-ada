# Open settings, screenshot it, press "reset position", verify island returns to primary.
param([string]$Out, [string]$Exe)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Import-Module "$PSScriptRoot\Win.psm1" -Force
$shot = "$PSScriptRoot\shot.ps1"
function IslandRect { $h = Get-IslandWindow; $r = New-Object TW+RECT; [void][TW]::GetWindowRect($h, [ref]$r); "$($r.L),$($r.T)-$($r.R),$($r.B)" }
function ById($root, $id) { $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id))) }
Start-Process $Exe
$win = $null
for ($i = 0; $i -lt 20 -and -not $win; $i++) {
  Start-Sleep -Milliseconds 300
  $win = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like "Dinamik Ada*Ayarlar" } | Select-Object -First 1
}
Start-Sleep -Milliseconds 800
$b = $win.Current.BoundingRectangle
"settings window: $($b.X),$($b.Y) $($b.Width)x$($b.Height)"
powershell -NoProfile -File $shot -X ([int]$b.X) -Y ([int]$b.Y) -W ([int]$b.Width) -H ([int]$b.Height) -Out "$Out\st3_settings_window.png"
"island before reset: $(IslandRect)"
(ById $win "ResetPositionButton").GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1200
"island after reset: $(IslandRect)"
"saved monitor: '" + ((Get-Content "$env:APPDATA\DinamikAda\settings.json" -Raw | ConvertFrom-Json).MonitorDeviceName) + "'"
# scroll down and capture game mode section
$sv = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Pane)))
try { $sv.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100) } catch { "scroll failed: $_" }
Start-Sleep -Milliseconds 600
powershell -NoProfile -File $shot -X ([int]$b.X) -Y ([int]$b.Y) -W ([int]$b.Width) -H ([int]$b.Height) -Out "$Out\st4_settings_bottom.png"
