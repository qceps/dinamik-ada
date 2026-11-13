using DynamicIsland.Core.State;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Interop;

public sealed record MonitorDescriptor(nint Handle, string DeviceName, PixelRect Bounds, PixelRect WorkArea, uint Dpi, bool IsPrimary)
{
    public double DpiScale => Dpi / 96.0;
}

/// <summary>Monitör listesi ve monitör başına DPI (Per-Monitor V2).</summary>
public static class DisplayMonitors
{
    public static IReadOnlyList<MonitorDescriptor> GetAll()
    {
        var list = new List<MonitorDescriptor>();
        EnumDisplayMonitors(0, 0, (nint hMonitor, nint _, ref RECT _, nint _) =>
        {
            var d = Describe(hMonitor);
            if (d is not null)
            {
                list.Add(d);
            }

            return true;
        }, 0);

        // Kararlı sıralama: birincil önce, sonra soldan sağa.
        return list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    /// <summary>Kayıtlı aygıt adına göre monitörü bulur; yoksa (monitör çıkarılmışsa) birincile düşer.</summary>
    public static MonitorDescriptor Resolve(string? deviceName)
    {
        var all = GetAll();
        if (!string.IsNullOrEmpty(deviceName))
        {
            var match = all.FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return all.FirstOrDefault(m => m.IsPrimary)
               ?? all.FirstOrDefault()
               ?? new MonitorDescriptor(0, string.Empty, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 96, true);
    }

    public static MonitorDescriptor? FromWindow(nint hwnd)
    {
        var h = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return h == 0 ? null : Describe(h);
    }

    private static unsafe MonitorDescriptor? Describe(nint hMonitor)
    {
        var info = new MONITORINFOEXW { cbSize = sizeof(MONITORINFOEXW) };
        if (!GetMonitorInfo(hMonitor, ref info))
        {
            return null;
        }

        string device = new string(info.szDevice);
        var nul = device.IndexOf('\0');
        if (nul >= 0)
        {
            device = device[..nul];
        }

        uint dpi = 96;
        if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dx, out _) == 0 && dx > 0)
        {
            dpi = dx;
        }

        return new MonitorDescriptor(
            hMonitor,
            device,
            new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
            new PixelRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
            dpi,
            (info.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }
}
