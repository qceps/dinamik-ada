using static DynamicIsland.Interop.NativeMethods;
using DynamicIsland.Core.State;

namespace DynamicIsland.Interop;

/// <summary>Ada penceresinin odak çalmayan, görev çubuğunda görünmeyen, en üstte kalan katman davranışı.</summary>
public static class OverlayWindow
{
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_SETTINGCHANGE = 0x001A;

    /// <summary>WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW (+TOPMOST) uygular, WS_EX_APPWINDOW'u kaldırır.</summary>
    public static void ApplyOverlayStyles(nint hwnd)
    {
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
        ex &= ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)ex);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOOWNERZORDER);
    }

    /// <summary>Tıklamaların alttaki pencereye (ör. oyuna) geçmesi için WS_EX_TRANSPARENT'ı açar/kapatır.</summary>
    public static void SetClickThrough(nint hwnd, bool enabled)
    {
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var updated = enabled ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        if (updated != ex)
        {
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)updated);
        }
    }

    public static bool IsClickThrough(nint hwnd) => ((long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0;

    public static bool HasStyle(nint hwnd, long exStyle) => ((long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) & exStyle) == exStyle;

    /// <summary>Z sırasında en üste taşır; odak/aktivasyon değiştirmez.</summary>
    public static void AssertTopmost(nint hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    /// <summary>Fiziksel piksel cinsinden konum/boyut uygular (aktivasyon yok).</summary>
    public static void SetBounds(nint hwnd, PixelRect rect) =>
        SetWindowPos(hwnd, HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height, SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    public static PixelRect GetBounds(nint hwnd) =>
        GetWindowRect(hwnd, out var r) ? new PixelRect(r.Left, r.Top, r.Right, r.Bottom) : default;

    public static nint Foreground => GetForegroundWindow();

    /// <summary>İmlecin ekran konumu (fiziksel piksel).</summary>
    public static bool TryGetCursorPos(out int x, out int y)
    {
        var ok = GetCursorPos(out var p);
        x = p.X;
        y = p.Y;
        return ok;
    }
}
