using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Media;

namespace DynamicIsland.App.Island;

public sealed record DecodedArtwork(ImageSource Image, Color Accent);

/// <summary>Kapak baytlarını arka planda çözer (dondurulmuş BitmapSource), vurgu rengini çıkarır, küçük LRU önbellekte tutar.</summary>
public sealed class ArtworkDecoder
{
    public static readonly Color FallbackAccent = Color.FromRgb(0x1E, 0xD7, 0x60);
    private const int Capacity = 12;
    private const int DecodeSize = 200; // en büyük gösterim: 66 DIP × 1.4 ölçek × 2.0 DPI ≈ 185 px

    private readonly LinkedList<(string Key, DecodedArtwork Value)> _lru = new();
    private readonly object _gate = new();

    public async Task<DecodedArtwork?> DecodeAsync(MediaArtwork artwork)
    {
        var key = artwork.TrackKey + "#" + artwork.Hash.ToString("X");
        lock (_gate)
        {
            var node = _lru.First;
            while (node is not null)
            {
                if (node.Value.Key == key)
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    return node.Value.Value;
                }

                node = node.Next;
            }
        }

        var decoded = await Task.Run(() => Decode(artwork.Data)).ConfigureAwait(true);
        if (decoded is null)
        {
            return null;
        }

        lock (_gate)
        {
            _lru.AddFirst((key, decoded));
            while (_lru.Count > Capacity)
            {
                _lru.RemoveLast();
            }
        }

        return decoded;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lru.Clear();
        }
    }

    private static DecodedArtwork? Decode(byte[] data)
    {
        try
        {
            var image = new BitmapImage();
            using (var stream = new MemoryStream(data, writable: false))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.DecodePixelWidth = DecodeSize;
                image.StreamSource = stream;
                image.EndInit();
            }

            image.Freeze();
            return new DecodedArtwork(image, ExtractAccent(data));
        }
        catch (Exception ex)
        {
            Log.Warn("Kapak çözülemedi (yer tutucu gösterilecek)", ex);
            return null;
        }
    }

    /// <summary>Kapağın 12×12 küçültülmüş hâlinden doygunluğa göre ağırlıklı ortalama renk; okunur parlaklığa çekilir.</summary>
    private static Color ExtractAccent(byte[] data)
    {
        try
        {
            var small = new BitmapImage();
            using (var stream = new MemoryStream(data, writable: false))
            {
                small.BeginInit();
                small.CacheOption = BitmapCacheOption.OnLoad;
                small.DecodePixelWidth = 12;
                small.DecodePixelHeight = 12;
                small.StreamSource = stream;
                small.EndInit();
            }

            var converted = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[12 * 12 * 4];
            converted.CopyPixels(pixels, 12 * 4, 0);

            double r = 0, g = 0, b = 0, weight = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                double pb = pixels[i] / 255.0, pg = pixels[i + 1] / 255.0, pr = pixels[i + 2] / 255.0;
                var max = Math.Max(pr, Math.Max(pg, pb));
                var min = Math.Min(pr, Math.Min(pg, pb));
                var sat = max <= 0 ? 0 : (max - min) / max;
                var w = 0.05 + sat * sat * max;
                r += pr * w;
                g += pg * w;
                b += pb * w;
                weight += w;
            }

            if (weight <= 0)
            {
                return FallbackAccent;
            }

            RgbToHsl(r / weight, g / weight, b / weight, out var h, out var s, out var l);
            if (s < 0.12)
            {
                // Neredeyse gri kapak: nötr açık gri.
                return Color.FromRgb(0xD8, 0xD8, 0xD8);
            }

            s = Math.Clamp(s * 1.15, 0.45, 0.85);
            l = Math.Clamp(l, 0.58, 0.72);
            HslToRgb(h, s, l, out var rr, out var gg, out var bb);
            return Color.FromRgb((byte)(rr * 255), (byte)(gg * 255), (byte)(bb * 255));
        }
        catch
        {
            return FallbackAccent;
        }
    }

    internal static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        if (max == min)
        {
            h = s = 0;
            return;
        }

        var d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == r)
        {
            h = (g - b) / d + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = (b - r) / d + 2;
        }
        else
        {
            h = (r - g) / d + 4;
        }

        h /= 6;
    }

    internal static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
    {
        if (s == 0)
        {
            r = g = b = l;
            return;
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        r = Hue(p, q, h + 1.0 / 3);
        g = Hue(p, q, h);
        b = Hue(p, q, h - 1.0 / 3);
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
