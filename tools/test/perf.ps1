# Measure CPU% and memory of DinamikAda over N seconds.
param([int]$Seconds = 10, [string]$Label = "")
$p = Get-Process DinamikAda | Select-Object -First 1
$c0 = $p.TotalProcessorTime; $t0 = Get-Date
Start-Sleep -Seconds $Seconds
$p.Refresh()
$cpu = ($p.TotalProcessorTime - $c0).TotalMilliseconds / ((Get-Date) - $t0).TotalMilliseconds / [Environment]::ProcessorCount * 100
"{0}: CPU {1:N2}% (of all cores) | WorkingSet {2:N0} MB | Private {3:N0} MB | Threads {4} | Handles {5}" -f $Label, $cpu, ($p.WorkingSet64 / 1MB), ($p.PrivateMemorySize64 / 1MB), $p.Threads.Count, $p.HandleCount
