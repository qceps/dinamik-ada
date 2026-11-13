# Fake "game" window: borderless (optionally fullscreen / topmost). Logs every mouse click to a file.
param([string]$Log, [switch]$Fullscreen, [switch]$TopMost, [int]$Seconds = 60)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$form = New-Object System.Windows.Forms.Form
$form.Text = "TestGame"
$form.FormBorderStyle = 'None'
$form.BackColor = [System.Drawing.Color]::FromArgb(255, 70, 90, 120)
$form.StartPosition = 'Manual'
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($Fullscreen) { $form.Bounds = $screen } else { $form.Bounds = New-Object System.Drawing.Rectangle(460, 0, 1000, 600) }
$form.TopMost = [bool]$TopMost
$label = New-Object System.Windows.Forms.Label
$label.Text = "TEST GAME WINDOW"
$label.ForeColor = [System.Drawing.Color]::White
$label.Font = New-Object System.Drawing.Font("Segoe UI", 28)
$label.AutoSize = $true
$label.Location = New-Object System.Drawing.Point(80, 300)
$form.Controls.Add($label)
Set-Content -Path $Log -Value "" -Encoding ascii
$handler = { param($s, $e) Add-Content -Path $Log -Value ("click {0},{1}" -f $e.X, $e.Y) -Encoding ascii }
$form.Add_MouseDown($handler)
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = $Seconds * 1000
$timer.Add_Tick({ $form.Close() })
$timer.Start()
$form.Add_Shown({ $form.Activate() })
[System.Windows.Forms.Application]::Run($form)
