using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.App.Island;

/// <summary>
/// Renk teması: tam siyah, siyah + kapak rengi (varsayılan) veya arka planı şarkının kapak renginden koyu bir tona boyayan
/// "şarkıya göre". Arka plan opaklığı ayarı her temada geçerlidir.
/// </summary>
public partial class IslandWindow
{
    private IslandTheme _theme = IslandTheme.BlackAccent;
    private Color _accent = ArtworkDecoder.FallbackAccent;
    private bool _hasArtwork;
    private byte _backgroundAlpha = 255;
    private readonly GradientStop _songStop0 = new(Colors.Black, 0);
    private readonly GradientStop _songStop1 = new(Colors.Black, 1);
    private LinearGradientBrush? _songBrush;
    private Color _barColor;

    public void SetTheme(IslandTheme theme)
    {
        if (theme == _theme)
        {
            return;
        }

        _theme = theme;
        ApplyTheme(_animations && _shown);
        ApplyLayerState(_visual, _animations && _shown); // ışıma
    }

    private void ApplyTheme(bool animate)
    {
        // Çalma göstergesi: tam siyahta beyaz, diğerlerinde kapak rengi.
        var barColor = _theme == IslandTheme.PureBlack ? Colors.White : _accent;
        if (barColor != _barColor)
        {
            _barColor = barColor;
            var bars = new SolidColorBrush(barColor);
            bars.Freeze();
            foreach (var b in AllBars)
            {
                b.SetBarBrush(bars);
            }
        }

        if (_theme != IslandTheme.SongColor)
        {
            if (Island.Background is not SolidColorBrush solid || solid.Color.A != _backgroundAlpha || solid.Color.R != 0 || solid.Color.G != 0 || solid.Color.B != 0)
            {
                var black = new SolidColorBrush(Color.FromArgb(_backgroundAlpha, 0, 0, 0));
                black.Freeze();
                Island.Background = black;
            }

            return;
        }

        // Şarkıya göre: kapak renginin koyu tonundan neredeyse siyaha çapraz geçiş.
        var (top, bottom) = SongTints();
        top.A = bottom.A = _backgroundAlpha;
        if (_songBrush is null)
        {
            _songBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            _songBrush.GradientStops.Add(_songStop0);
            _songBrush.GradientStops.Add(_songStop1);
            animate = false;
        }

        if (!ReferenceEquals(Island.Background, _songBrush))
        {
            Island.Background = _songBrush;
            animate = false;
        }

        SetStop(_songStop0, top, animate);
        SetStop(_songStop1, bottom, animate);
    }

    private static void SetStop(GradientStop stop, Color color, bool animate)
    {
        if (!animate)
        {
            stop.BeginAnimation(GradientStop.ColorProperty, null);
            stop.Color = color;
            return;
        }

        stop.BeginAnimation(GradientStop.ColorProperty,
            new ColorAnimation(color, TimeSpan.FromMilliseconds(650)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } },
            HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Kapak renginden okunabilir (beyaz yazıyla yeterli karşıtlık) iki koyu ton.</summary>
    private (Color Top, Color Bottom) SongTints()
    {
        if (!_hasArtwork)
        {
            return (Color.FromRgb(0x1E, 0x1E, 0x22), Color.FromRgb(0x0A, 0x0A, 0x0C));
        }

        ArtworkDecoder.RgbToHsl(_accent.R / 255.0, _accent.G / 255.0, _accent.B / 255.0, out var h, out var s, out _);
        return (FromHsl(h, Math.Min(s, 0.62), 0.24), FromHsl(h, Math.Min(s, 0.55), 0.085));
    }

    // ---- Gölge ----

    /// <summary>(yayılma, saydamlık): içten dışa doğru giderek silikleşen katmanlar ≈ yumuşak gölge.</summary>
    private static readonly (double Spread, byte Alpha)[] ShadowSteps = [(1.5, 0x30), (3.5, 0x1C), (6.5, 0x10), (10, 0x08), (14, 0x04)];
    private const double ShadowOffsetY = 3;
    private Border[] _shadowLayers = [];

    private void CreateShadowLayers()
    {
        _shadowLayers = ShadowSteps.Select(step =>
        {
            var brush = new SolidColorBrush(Color.FromArgb(step.Alpha, 0, 0, 0));
            brush.Freeze();
            var layer = new Border
            {
                Background = brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
            };
            ShadowPlate.Children.Insert(0, layer); // en geniş en altta
            return layer;
        }).ToArray();

        // Çekirdek: adayla aynı boyutta siyah plaka. Ada + plaka birlikte ayarlanan arka plan opaklığını verir
        // (bkz. ApplyAppearance: a = 1 − √(1 − opaklık)).
        _shadowCore = new Border
        {
            Background = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        ShadowPlate.Children.Add(_shadowCore);
    }

    private Border? _shadowCore;

    private void UpdateShadowLayers(double width, double height, double radius)
    {
        for (var i = 0; i < _shadowLayers.Length; i++)
        {
            var spread = ShadowSteps[i].Spread;
            var layer = _shadowLayers[i];
            layer.Width = width + spread * 2;
            layer.Height = height + spread * 2;
            layer.Margin = new Thickness(0, ShadowOffsetY - spread, 0, 0);
            layer.CornerRadius = new CornerRadius(radius + spread);
        }

        if (_shadowCore is not null)
        {
            _shadowCore.Width = width;
            _shadowCore.Height = height;
            _shadowCore.CornerRadius = new CornerRadius(radius);
        }
    }

    private static Color FromHsl(double h, double s, double l)
    {
        ArtworkDecoder.HslToRgb(h, s, l, out var r, out var g, out var b);
        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }
}
