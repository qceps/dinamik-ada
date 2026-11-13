# Toggle "Windows ile baslat" via UI and verify HKCU Run value; also toggle animations off and back on.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
function RunValue { (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -ErrorAction SilentlyContinue).DinamikAda }
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like "Dinamik Ada*Ayarlar" } | Select-Object -First 1
function ById($id) { $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id))) }
"Run value before: '$(RunValue)'"
$cb = ById "StartWithWindowsBox"
$cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 800
"after ON: '$(RunValue)' ; setting=" + (Get-Content "$env:APPDATA\DinamikAda\settings.json" -Raw | ConvertFrom-Json).StartWithWindows
$cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 800
"after OFF: '$(RunValue)' ; setting=" + (Get-Content "$env:APPDATA\DinamikAda\settings.json" -Raw | ConvertFrom-Json).StartWithWindows
