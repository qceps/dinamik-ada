using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DynamicIsland.App.Island;

/// <summary>
/// Dekoratif çalma göstergesi. Gerçek ses verisi analiz EDİLMEZ; yalnızca oynatma durumuna bağlı,
/// periyodik bir animasyondur. Yalnızca çalarken, görünürken ve katmanı etkinken çalışır (24 fps).
/// </summary>
public sealed class PlaybackBars : Border
{
    private static readonly double[] Durations = [0.47, 0.61, 0.41, 0.55];
    private static readonly double[] Minimums = [0.32, 0.24, 0.38, 0.28];
    private static readonly double[] StaticHeights = [0.55, 0.9, 0.7, 0.45];
    private const double PausedHeight = 0.24;
    private const int FrameRate = 24; // dekoratif dalga için yeterli; 15 fps ile ölçülebilir fark çıkmadı

    private readonly ScaleTransform[] _scales = new ScaleTransform[4];
    private readonly Rectangle[] _bars = new Rectangle[4];
    private bool _playing;
    private bool _layerActive = true;
    private bool _animationsEnabled = true;
    private bool _running;

    public PlaybackBars()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 4; i++)
        {
            _scales[i] = new ScaleTransform(1, PausedHeight);
            _bars[i] = new Rectangle
            {
                Width = 3,
                Height = 14,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(i == 0 ? 0 : 2.2, 0, 0, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _scales[i],
                Fill = Brushes.White,
            };
            panel.Children.Add(_bars[i]);
        }

        Child = panel;
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => Update();
    }

    public double BarHeight
    {
        get => _bars[0].Height;
        set
        {
            foreach (var bar in _bars)
            {
                bar.Height = value;
            }
        }
    }

    public double BarWidth
    {
        get => _bars[0].Width;
        set
        {
            foreach (var bar in _bars)
            {
                bar.Width = value;
                bar.RadiusX = bar.RadiusY = value / 2;
            }
        }
    }

    public void SetBarBrush(Brush brush)
    {
        foreach (var bar in _bars)
        {
            bar.Fill = brush;
        }
    }

    public bool IsPlaying
    {
        get => _playing;
        set
        {
            if (_playing != value)
            {
                _playing = value;
                Update();
            }
        }
    }

    /// <summary>Bu göstergeyi içeren katman şu an görünür mü (soluk katmanlarda animasyon çalışmaz).</summary>
    public bool LayerActive
    {
        get => _layerActive;
        set
        {
            if (_layerActive != value)
            {
                _layerActive = value;
                Update();
            }
        }
    }

    public bool AnimationsEnabled
    {
        get => _animationsEnabled;
        set
        {
            if (_animationsEnabled != value)
            {
                _animationsEnabled = value;
                Update(force: true);
            }
        }
    }

    /// <summary>Teşhis/test için: animasyon şu an çalışıyor mu?</summary>
    public bool IsAnimating => _running;

    private void Update(bool force = false)
    {
        var shouldRun = _playing && _layerActive && _animationsEnabled && IsVisible;
        if (shouldRun == _running && !force)
        {
            if (!shouldRun)
            {
                SetStatic();
            }

            return;
        }

        _running = shouldRun;
        if (shouldRun)
        {
            for (var i = 0; i < 4; i++)
            {
                var animation = new DoubleAnimation(Minimums[i], 1.0, TimeSpan.FromSeconds(Durations[i]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(animation, FrameRate);
                _scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            }
        }
        else
        {
            SetStatic();
        }
    }

    private void SetStatic()
    {
        for (var i = 0; i < 4; i++)
        {
            _scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _scales[i].ScaleY = _playing ? StaticHeights[i] : PausedHeight;
        }
    }
}
