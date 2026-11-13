# Settings window via UI Automation: move island to the 2nd monitor, exit, restart, verify persistence & placement.
param([string]$Out, [string]$Exe)
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Import-Module "$PSScriptRoot\Win.psm1" -Force
$shot = "$PSScriptRoot\shot.ps1"
$settingsFile = "$env:APPDATA\DinamikAda\settings.json"
function IslandRect { $h = Get-IslandWindow; $r = New-Object TW+RECT; [void][TW]::GetWindowRect($h, [ref]$r); "$($r.L),$($r.T)-$($r.R),$($r.B)" }
function FindWin($title) {
  for ($i = 0; $i -lt 20; $i++) {
    $all = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    $w = $all | Where-Object { $_.Current.Name -like $title } | Select-Object -First 1
    if ($w) { return $w }; Start-Sleep -Milliseconds 300
  }
}
function ById($root, $id) { $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id))) }

"island before: $(IslandRect)"
Start-Process $Exe   # second instance -> opens settings in the running one
$win = FindWin "Dinamik Ada*Ayarlar"
if (-not $win) { "settings window NOT found"; exit 1 }
"settings window opened (second instance signalled the first)"
"running instances: $((Get-Process DinamikAda).Count)"
Start-Sleep -Milliseconds 800
powershell -NoProfile -File $shot -X 0 -Y 0 -W 1920 -H 1080 -Out "$Out\st1_settings.png"

$combo = ById $win "MonitorBox"
$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
$items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
"monitor options: " + (($items | ForEach-Object { $_.Current.Name }) -join " | ")
$target = $items | Where-Object { $_.Current.Name -like "Ekran 2*" } | Select-Object -First 1
$target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
Start-Sleep -Milliseconds 1200
"island after selecting Ekran 2: $(IslandRect)"
powershell -NoProfile -File $shot -X -1920 -Y 0 -W 1920 -H 260 -Out "$Out\st2_monitor2.png"

$exit = ById $win "ExitButton"
$exit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 2
"process after exit: $((Get-Process DinamikAda -ErrorAction SilentlyContinue).Count)"
"saved monitor: " + ((Get-Content $settingsFile -Raw | ConvertFrom-Json).MonitorDeviceName)

Start-Process $Exe; Start-Sleep -Seconds 3
"island after restart: $(IslandRect)"
