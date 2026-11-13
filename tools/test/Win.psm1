# Test yardımcıları: fare/klavye simülasyonu ve pencere sorguları (yalnızca yerel test için).
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class TW {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t);
  [DllImport("user32.dll")] public static extern long GetWindowLongPtrW(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static string Title(IntPtr h) { var s = new StringBuilder(256); GetWindowTextW(h, s, 256); return s.ToString(); }
  public static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassNameW(h, s, 256); return s.ToString(); }
  public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  public static IntPtr FindByPidTitle(uint pid, string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { if (Pid(h) == pid && Title(h) == title) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  public static IntPtr At(int x, int y) { var p = new POINT { X = x, Y = y }; return GetAncestor(WindowFromPoint(p), 2); }
  public static void Click(int x, int y) { SetCursorPos(x, y); mouse_event(2, 0, 0, 0, IntPtr.Zero); mouse_event(4, 0, 0, 0, IntPtr.Zero); }
  public static void Keys(byte[] mods, byte key) {
    foreach (var m in mods) keybd_event(m, 0, 0, IntPtr.Zero);
    keybd_event(key, 0, 0, IntPtr.Zero); keybd_event(key, 0, 2, IntPtr.Zero);
    for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, 2, IntPtr.Zero);
  }
}
"@

function Get-IslandWindow {
    $p = Get-Process DinamikAda -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $p) { return $null }
    return [TW]::FindByPidTitle([uint32]$p.Id, 'Dinamik Ada')
}

function Get-Fg {
    $h = [TW]::GetForegroundWindow()
    [pscustomobject]@{ Handle = $h; Title = [TW]::Title($h); Class = [TW]::Cls($h); Pid = [TW]::Pid($h) }
}

# Ctrl+Shift+F9/F10/F11
function Send-Hotkey([byte]$vk) { [TW]::Keys([byte[]](0x11, 0x10), $vk) }

Export-ModuleMember -Function * -Variable *
