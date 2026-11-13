namespace DynamicIsland.Core.State;

/// <summary>Fiziksel piksel cinsinden dikdörtgen.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public override string ToString() => $"{Left},{Top} {Width}x{Height}";
}

public static class PlacementCalculator
{
    /// <summary>
    /// Ada penceresinin fiziksel konumunu hesaplar. Ada (en geniş hâli) her zaman monitör içinde kalacak
    /// şekilde yatay kaydırma kırpılır; böylece çözünürlük değişimi / monitör değişiminde ekran dışına taşmaz.
    /// </summary>
    /// <param name="monitor">Monitörün tam sınırları (fiziksel px).</param>
    /// <param name="dpiScale">Monitör DPI / 96.</param>
    /// <param name="windowDip">Pencere boyutu (DIP, ölçek dahil).</param>
    /// <param name="islandMaxWidthDip">Adanın en geniş hâli (DIP, ölçek dahil).</param>
    /// <param name="islandMaxHeightDip">Adanın en yüksek hâli (DIP, ölçek dahil).</param>
    /// <param name="offsetDip">Ortaya göre yatay kaydırma (DIP).</param>
    /// <param name="topMarginDip">Üst boşluk (DIP).</param>
    public static PixelRect Compute(
        PixelRect monitor,
        double dpiScale,
        (double Width, double Height) windowDip,
        double islandMaxWidthDip,
        double islandMaxHeightDip,
        double offsetDip,
        double topMarginDip)
    {
        if (!(dpiScale > 0) || !double.IsFinite(dpiScale))
        {
            dpiScale = 1;
        }

        var width = (int)Math.Round(windowDip.Width * dpiScale);
        var height = (int)Math.Round(windowDip.Height * dpiScale);
        var halfIsland = islandMaxWidthDip * dpiScale / 2.0;

        var middle = monitor.Left + monitor.Width / 2.0;
        var center = middle + (double.IsFinite(offsetDip) ? offsetDip : 0) * dpiScale;
        var minCenter = monitor.Left + halfIsland;
        var maxCenter = monitor.Right - halfIsland;
        center = minCenter > maxCenter ? middle : Math.Clamp(center, minCenter, maxCenter);

        var left = (int)Math.Round(center - width / 2.0);

        var maxTop = monitor.Bottom - (int)Math.Ceiling(islandMaxHeightDip * dpiScale);
        var top = monitor.Top + (int)Math.Round(Math.Max(0, double.IsFinite(topMarginDip) ? topMarginDip : 0) * dpiScale);
        top = Math.Max(monitor.Top, Math.Min(top, maxTop));

        return new PixelRect(left, top, left + width, top + height);
    }

    /// <summary>Monitör genişliğine göre izin verilen en büyük yatay kaydırma (DIP).</summary>
    public static double MaxOffsetDip(PixelRect monitor, double dpiScale, double islandMaxWidthDip)
    {
        if (!(dpiScale > 0))
        {
            dpiScale = 1;
        }

        return Math.Max(0, (monitor.Width / dpiScale - islandMaxWidthDip) / 2.0);
    }
}
