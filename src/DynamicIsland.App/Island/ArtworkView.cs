using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DynamicIsland.App.Island;

/// <summary>Yuvarlatılmış albüm kapağı; kapak yoksa sade bir nota yer tutucusu, değişimde kısa çapraz geçiş.</summary>
public sealed class ArtworkView : Grid
{
    private static readonly Geometry NoteGeometry = Geometry.Parse(
        "M9,1 L19,0 L19,13.2 A3.2,3.2 0 1 1 17,10.3 L17,4.2 L11,4.9 L11,15.2 A3.2,3.2 0 1 1 9,12.3 Z");

    private readonly Border _placeholder;
    private readonly Border _a;
    private readonly Border _b;
    private bool _aIsFront = true;
    private ImageSource? _current;
    private double _radius;

    public ArtworkView()
    {
        _placeholder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = new Path { Data = NoteGeometry, Fill = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)) },
            },
        };
        _a = new Border { Opacity = 0 };
        _b = new Border { Opacity = 0 };
        Children.Add(_placeholder);
        Children.Add(_a);
        Children.Add(_b);
        IsHitTestVisible = false;
        SizeChanged += (_, e) =>
        {
            ((Viewbox)_placeholder.Child).Margin = new Thickness(Math.Max(3, e.NewSize.Width * 0.27));
        };
    }

    public double Radius
    {
        get => _radius;
        set
        {
            _radius = value;
            var r = new CornerRadius(value);
            _placeholder.CornerRadius = r;
            _a.CornerRadius = r;
            _b.CornerRadius = r;
        }
    }

    public void SetImage(ImageSource? source, bool animate)
    {
        if (ReferenceEquals(source, _current))
        {
            return;
        }

        _current = source;
        var front = _aIsFront ? _a : _b;
        var back = _aIsFront ? _b : _a;
        var duration = animate ? TimeSpan.FromMilliseconds(220) : TimeSpan.Zero;

        if (source is null)
        {
            Fade(front, 0, duration, () => front.Background = null);
            return;
        }

        var brush = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        back.Background = brush;
        Panel.SetZIndex(back, 2);
        Panel.SetZIndex(front, 1);
        back.BeginAnimation(OpacityProperty, null);
        back.Opacity = animate ? 0 : 1;
        Fade(back, 1, duration, () =>
        {
            // Eski kapak bitmap'ine referansı bırak (bellek).
            if (!ReferenceEquals(front.Background, back.Background))
            {
                front.BeginAnimation(OpacityProperty, null);
                front.Opacity = 0;
                front.Background = null;
            }
        });
        _aIsFront = !_aIsFront;
    }

    private static void Fade(UIElement element, double to, TimeSpan duration, Action? completed)
    {
        if (duration == TimeSpan.Zero)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(to, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        element.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
