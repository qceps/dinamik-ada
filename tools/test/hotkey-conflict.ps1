# Another process grabs Ctrl+Shift+F9 first; the app must report the conflict instead of failing silently.
param([string]$Out, [string]$Exe)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$shot = "$PSScriptRoot\shot.ps1"
Get-Process DinamikAda -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
$holder = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "$PSScriptRoot\hotkey-holder.ps1")
Start-Sleep -Seconds 3
"holder: " + (Get-Content "$env:TEMP\hk_holder.txt")
Get-Process DinamikAda -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
Start-Process $Exe -ArgumentList "--settings"
$win = $null
for ($i = 0; $i -lt 30 -and -not $win; $i++) {
  Start-Sleep -Milliseconds 300
  $win = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like "Dinamik Ada*Ayarlar" } | Select-Object -First 1
}
Start-Sleep -Milliseconds 1000
foreach ($id in "ExpandHotkeyStatus", "VisibilityHotkeyStatus", "InteractionHotkeyStatus") {
  $e = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
  "{0}: {1}" -f $id, $e.Current.Name
}
$b = $win.Current.BoundingRectangle
powershell -NoProfile -File $shot -X ([int]$b.X) -Y ([int]$b.Y) -W ([int]$b.Width) -H ([int]$b.Height) -Out "$Out\st5_settings_top.png"
$sv = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Pane)))
$sv.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 80)
Start-Sleep -Milliseconds 600
powershell -NoProfile -File $shot -X ([int]$b.X) -Y ([int]$b.Y) -W ([int]$b.Width) -H ([int]$b.Height) -Out "$Out\st6_conflict.png"
Stop-Process -Id $holder.Id -ErrorAction SilentlyContinue
"log: " + ((Get-Content "$env:LOCALAPPDATA\DinamikAda\logs\app.log" -Tail 30 | Select-String "Kısayol|K.sayol") -join " / ")
