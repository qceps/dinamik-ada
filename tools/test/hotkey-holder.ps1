# Holds Ctrl+Shift+F9 globally for N seconds (simulates another app owning the hotkey).
param([int]$Seconds = 40)
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class HK { [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint m, uint vk); }
"@
$ok = [HK]::RegisterHotKey([IntPtr]::Zero, 77, 0x6, 0x78)
Set-Content "$env:TEMP\hk_holder.txt" "registered=$ok"
Start-Sleep -Seconds $Seconds
