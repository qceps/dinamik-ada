using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Interop;

/// <summary>
/// Ön plan penceresi değişimlerini olay tabanlı izler (SetWinEventHook, süreç dışı).
/// Mesaj döngüsü olan iş parçacığında (WPF UI) oluşturulmalıdır; geri çağrı da o iş parçacığında gelir.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly WinEventProc _callback; // GC'nin toplamaması için alan olarak tutulur.
    private nint _hook;

    public ForegroundWatcher()
    {
        _callback = OnWinEvent;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, _callback, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public event Action<nint>? ForegroundChanged;

    public bool IsActive => _hook != 0;

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND)
        {
            ForegroundChanged?.Invoke(hwnd);
        }
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWinEvent(_hook);
            _hook = 0;
        }
    }
}

/// <summary>Ön plandaki pencerenin tam ekran (kenarlıksız veya özel) olup olmadığını tahmin eder.</summary>
public static class FullscreenDetector
{
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow"];

    public static bool IsForegroundFullscreen(nint ignoreHwnd = 0)
    {
        // D3D özel tam ekran veya "meşgul" (tam ekran sunum/oyun) bildirimi.
        if (SHQueryUserNotificationState(out var state) == 0 && state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_BUSY)
        {
            return true;
        }

        var fg = GetForegroundWindow();
        if (fg == 0 || fg == ignoreHwnd || fg == GetShellWindow() || fg == GetDesktopWindow())
        {
            return false;
        }

        if (!IsWindowVisible(fg) || IsIconic(fg))
        {
            return false;
        }

        var cls = GetClassNameOf(fg);
        if (ShellClasses.Contains(cls, StringComparer.Ordinal))
        {
            return false;
        }

        if (!GetWindowRect(fg, out var r))
        {
            return false;
        }

        var monitor = DisplayMonitors.FromWindow(fg);
        if (monitor is null)
        {
            return false;
        }

        var m = monitor.Bounds;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    public static unsafe string GetClassNameOf(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var len = GetClassName(hwnd, buffer, 256);
        return len > 0 ? new string(buffer, 0, len) : string.Empty;
    }
}

public static class SystemAnimation
{
    /// <summary>Windows "Animasyon efektleri" (azaltılmış hareket) tercihi. Kapalıysa false döner.</summary>
    public static bool ClientAreaAnimationsEnabled =>
        !SystemParametersInfoBool(SPI_GETCLIENTAREAANIMATION, 0, out var enabled, 0) || enabled;
}
