using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.State;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Island;

/// <summary>
/// Adanın görünümü. Sabit boyutlu, şeffaf, katmanlı (layered) bir pencere; yalnızca içteki kapsül
/// boyut değiştirir. Saydam (alfa = 0) pikseller Windows tarafından tıklamaya kapalı sayılır, böylece
/// görünmeyen alanlar fare girişini engellemez. Pencere asla etkinleşmez (WS_EX_NOACTIVATE).
/// </summary>
public partial class IslandWindow : Window
{
    public const double BaseWidth = 432;
    public const double BaseHeight = 208;
    public const double MaxIslandWidth = 376;
    public const double MaxIslandHeight = 176;

    private static readonly Size IdleSize = new(150, 30);
    private static readonly Size CompactSize = new(172, 34);
    private static readonly Size PeekSize = new(330, 54);
    private static readonly Size ExpandedSize = new(376, 176);
    private static readonly Size ExpandedNoTimelineSize = new(376, 154);

    private readonly DispatcherTimer _progressTimer;
    private nint _hwnd;
    private IslandVisual _visual = IslandVisual.Hidden;
    private bool _animations = true;
    private bool _shown;
    private MediaSnapshot _snapshot = MediaSnapshot.Initial();
    private bool _canSeek;
    private bool _seeking;
    private int _hideGeneration;

    public IslandWindow()
    {
        InitializeComponent();
        Island.SizeChanged += (_, _) => UpdateCorners();
        Island.MouseEnter += (_, _) => HoverChanged?.Invoke(true);
        Island.MouseLeave += (_, _) => HoverChanged?.Invoke(false);
        Island.PreviewMouseMove += (_, _) => PointerActivity?.Invoke();
        PlayPauseButton.Click += (_, _) => PlayPauseRequested?.Invoke();
        NextButton.Click += (_, _) => NextRequested?.Invoke();
        PreviousButton.Click += (_, _) => PreviousRequested?.Invoke();

        SeekArea.MouseLeftButtonDown += OnSeekDown;
        SeekArea.MouseMove += OnSeekMove;
        SeekArea.MouseLeftButtonUp += OnSeekUp;
        SeekArea.LostMouseCapture += (_, _) => _seeking = false;
        SeekArea.MouseEnter += (_, _) => { if (_canSeek) SeekThumb.Opacity = 1; };
        SeekArea.MouseLeave += (_, _) => { if (!_seeking) SeekThumb.Opacity = 0; };

        _progressTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _progressTimer.Tick += (_, _) => UpdateProgress();

        ApplyLayerState(IslandVisual.Hidden, animate: false);
        UpdateCorners();
    }

    public event Action<bool>? HoverChanged;
    public event Action? PointerActivity;
    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action<TimeSpan>? SeekRequested;
    /// <summary>DPI veya ekran düzeni değişti; yeniden konumlandırma gerekir.</summary>
    public event Action? PlacementInvalidated;

    public nint Handle => _hwnd;

    public IslandVisual Visual => _visual;

    /// <summary>Pencere tutamacını gösterilmeden oluşturur ve katman stillerini uygular.</summary>
    public void EnsureCreated()
    {
        if (_hwnd != 0)
        {
            return;
        }

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        OverlayWindow.ApplyOverlayStyles(_hwnd);
        // Not: donanım render, yazılım render'a göre ölçümde ~2 kat daha az CPU kullandı (bkz. NOTLAR.md).
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case OverlayWindow.WM_MOUSEACTIVATE:
                // Tıklama hiçbir koşulda bu pencereyi etkinleştirmesin (oyundan/uygulamadan odak çalma yok).
                handled = true;
                return OverlayWindow.MA_NOACTIVATE;
            case OverlayWindow.WM_DPICHANGED:
            case OverlayWindow.WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => PlacementInvalidated?.Invoke());
                break;
        }

        return 0;
    }

    public void ApplyAppearance(double scale, double backgroundOpacity, bool animationsEnabled)
    {
        _animations = animationsEnabled;
        RootScale.ScaleX = RootScale.ScaleY = scale;
        Width = BaseWidth * scale;
        Height = BaseHeight * scale;

        // Ada + gölge plakası üst üste biner; birleşik opaklık istenen değere eşit olsun.
        var a = 1 - Math.Sqrt(1 - Math.Clamp(backgroundOpacity, 0, 1));
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255), 0, 0, 0));
        brush.Freeze();
        Island.Background = brush;
        ShadowPlate.Opacity = a;

        CompactBars.AnimationsEnabled = PeekBars.AnimationsEnabled = ExpandedBars.AnimationsEnabled = animationsEnabled;
    }

    public void Place(PixelRect bounds)
    {
        if (_hwnd != 0)
        {
            OverlayWindow.SetBounds(_hwnd, bounds);
        }
    }

    public void AssertTopmost()
    {
        if (_hwnd != 0 && _shown)
        {
            OverlayWindow.AssertTopmost(_hwnd);
        }
    }

    public void SetClickThrough(bool enabled)
    {
        if (_hwnd != 0)
        {
            OverlayWindow.SetClickThrough(_hwnd, enabled);
        }
    }

    public void SetInteractionIndicator(bool visible) =>
        Animate(InteractionRing, OpacityProperty, visible ? 1 : 0, 180, null);

    public void SetIdleText(string text) => IdleText.Text = text;

    public void SetSnapshot(MediaSnapshot snapshot)
    {
        var previous = _snapshot;
        _snapshot = snapshot;
        var track = snapshot.Track;

        if (track is not null)
        {
            PeekTitle.Text = ExpandedTitle.Text = track.Title;
            var artist = string.IsNullOrWhiteSpace(track.Artist) ? "Bilinmeyen sanatçı" : track.Artist;
            PeekArtist.Text = ExpandedArtist.Text = artist;
            ExpandedAlbum.Text = track.Album;
            ExpandedAlbum.Visibility = string.IsNullOrWhiteSpace(track.Album) ? Visibility.Collapsed : Visibility.Visible;
        }

        var playing = snapshot.IsPlaying;
        PlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        PlayPauseButton.ToolTip = playing ? "Duraklat" : "Oynat";

        var caps = snapshot.Capabilities;
        PlayPauseButton.IsEnabled = caps.CanPlayOrPause;
        NextButton.IsEnabled = caps.CanNext;
        PreviousButton.IsEnabled = caps.CanPrevious;

        CompactBars.IsPlaying = PeekBars.IsPlaying = ExpandedBars.IsPlaying = playing;
        DemoBadge.Visibility = snapshot.IsDemo ? Visibility.Visible : Visibility.Collapsed;

        var hasTimeline = snapshot.Timeline is not null;
        ProgressRow.Visibility = hasTimeline ? Visibility.Visible : Visibility.Collapsed;
        ControlsRow.Margin = new Thickness(0, hasTimeline ? 120 : 96, 0, 0);

        // İleri sarma yalnızca oturum destekliyorsa etkileşimli; aksi hâlde salt gösterge.
        _canSeek = caps.CanSeek && hasTimeline;
        SeekArea.IsHitTestVisible = _canSeek;
        SeekArea.Cursor = _canSeek ? Cursors.Hand : null;
        if (!_canSeek)
        {
            SeekThumb.Opacity = 0;
        }

        UpdateProgress();
        UpdateProgressTimer();

        var hadTimeline = previous.Timeline is not null;
        if (_visual == IslandVisual.Expanded && hadTimeline != hasTimeline)
        {
            AnimateIslandSize(SizeFor(IslandVisual.Expanded), grow: hasTimeline);
        }
    }

    public void SetArtwork(ImageSource? image, Color accent, bool animate)
    {
        var anim = animate && _animations && _shown;
        CompactArt.SetImage(image, anim);
        PeekArt.SetImage(image, anim);
        ExpandedArt.SetImage(image, anim);
        var brush = new SolidColorBrush(accent);
        brush.Freeze();
        CompactBars.SetBarBrush(brush);
        PeekBars.SetBarBrush(brush);
        ExpandedBars.SetBarBrush(brush);
    }

    public void SetVisual(IslandVisual visual)
    {
        if (visual == _visual)
        {
            return;
        }

        var previous = _visual;
        _visual = visual;

        if (visual == IslandVisual.Hidden)
        {
            HideIsland();
            return;
        }

        var target = SizeFor(visual);
        var fromHidden = previous == IslandVisual.Hidden || !_shown;
        if (fromHidden)
        {
            _hideGeneration++;
            if (!_shown)
            {
                // Küçük bir kapsülden açılarak belirsin.
                Island.BeginAnimation(WidthProperty, null);
                Island.BeginAnimation(HeightProperty, null);
                Island.Width = Math.Min(target.Width, 110);
                Island.Height = target.Height;
                Stage.Opacity = 0;
                Show(); // ShowActivated=False → SW_SHOWNA: etkinleştirmez.
                _shown = true;
                AssertTopmost();
            }

            Animate(Stage, OpacityProperty, 1, 200, null);
        }

        var grow = target.Width * target.Height >= Island.ActualWidth * Island.ActualHeight;
        AnimateIslandSize(target, grow);
        ApplyLayerState(visual, _animations);
        UpdateProgressTimer();
    }

    private void HideIsland()
    {
        ApplyLayerState(IslandVisual.Hidden, _animations);
        _progressTimer.Stop();
        if (!_shown)
        {
            return;
        }

        var generation = ++_hideGeneration;
        AnimateIslandSize(new Size(Math.Min(Island.ActualWidth, 110), Math.Max(24, Island.ActualHeight * 0.8)), grow: false);
        Animate(Stage, OpacityProperty, 0, 180, () =>
        {
            if (generation == _hideGeneration && _visual == IslandVisual.Hidden)
            {
                Hide();
                _shown = false;
            }
        });
    }

    private Size SizeFor(IslandVisual visual) => visual switch
    {
        IslandVisual.Idle => IdleSize,
        IslandVisual.Compact => CompactSize,
        IslandVisual.Peek => PeekSize,
        IslandVisual.Expanded => _snapshot.Timeline is null ? ExpandedNoTimelineSize : ExpandedSize,
        _ => CompactSize,
    };

    private void AnimateIslandSize(Size target, bool grow)
    {
        if (!_animations)
        {
            Island.BeginAnimation(WidthProperty, null);
            Island.BeginAnimation(HeightProperty, null);
            Island.Width = target.Width;
            Island.Height = target.Height;
            return;
        }

        // SnapshotAndReplace: hızlı art arda geçişlerde animasyonlar birikmez; mevcut değerden yeni hedefe akar.
        var duration = TimeSpan.FromMilliseconds(grow ? 480 : 400);
        var ease = new SpringEase { Damping = grow ? 0.84 : 0.95, Stiffness = grow ? 7.2 : 8.0 };
        var w = new DoubleAnimation(target.Width, duration) { EasingFunction = ease };
        var h = new DoubleAnimation(target.Height, duration) { EasingFunction = ease };
        Island.BeginAnimation(WidthProperty, w, HandoffBehavior.SnapshotAndReplace);
        Island.BeginAnimation(HeightProperty, h, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyLayerState(IslandVisual visual, bool animate)
    {
        SetLayer(IdleLayer, visual == IslandVisual.Idle, animate);
        SetLayer(CompactLayer, visual == IslandVisual.Compact, animate);
        SetLayer(PeekLayer, visual == IslandVisual.Peek, animate);
        SetLayer(ExpandedLayer, visual == IslandVisual.Expanded, animate);
        CompactBars.LayerActive = visual == IslandVisual.Compact;
        PeekBars.LayerActive = visual == IslandVisual.Peek;
        ExpandedBars.LayerActive = visual == IslandVisual.Expanded;
    }

    private static void SetLayer(FrameworkElement layer, bool active, bool animate)
    {
        layer.IsHitTestVisible = active;
        if (!animate)
        {
            layer.BeginAnimation(OpacityProperty, null);
            layer.Opacity = active ? 1 : 0;
            return;
        }

        var animation = active
            ? new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(110), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } }
            : new DoubleAnimation(0, TimeSpan.FromMilliseconds(110)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        layer.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void Animate(UIElement element, DependencyProperty property, double to, int ms, Action? completed)
    {
        if (!_animations)
        {
            element.BeginAnimation(property, null);
            element.SetValue(property, to);
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void UpdateCorners()
    {
        var w = Math.Max(0, Island.ActualWidth);
        var h = Math.Max(0, Island.ActualHeight);
        var r = Math.Min(h / 2, 30);
        var radius = new CornerRadius(r);
        Island.CornerRadius = radius;
        ShadowPlate.CornerRadius = radius;
        InteractionRing.CornerRadius = radius;
        var clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        clip.Freeze();
        Island.Clip = clip;
    }

    private void UpdateProgressTimer()
    {
        var run = _visual == IslandVisual.Expanded && _snapshot.Timeline is not null && _snapshot.IsPlaying && _shown;
        if (run && !_progressTimer.IsEnabled)
        {
            _progressTimer.Start();
        }
        else if (!run && _progressTimer.IsEnabled)
        {
            _progressTimer.Stop();
        }
    }

    private void UpdateProgress()
    {
        var timeline = _snapshot.Timeline;
        if (timeline is null || _seeking)
        {
            return;
        }

        var position = timeline.PositionAt(DateTimeOffset.UtcNow, _snapshot.IsPlaying);
        var fraction = timeline.Duration.TotalSeconds > 0 ? position.TotalSeconds / timeline.Duration.TotalSeconds : 0;
        SetProgressVisual(Math.Clamp(fraction, 0, 1));
        ElapsedText.Text = Format(position);
        DurationText.Text = Format(timeline.Duration);
    }

    private void SetProgressVisual(double fraction)
    {
        var width = SeekArea.ActualWidth > 0 ? SeekArea.ActualWidth : MaxIslandWidth - 36 - 80 - 8;
        TrackFill.Width = Math.Max(0, width * fraction);
        SeekThumbOffset.X = Math.Max(0, width * fraction - SeekThumb.Width / 2);
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private double SeekFraction(MouseEventArgs e) =>
        SeekArea.ActualWidth <= 0 ? 0 : Math.Clamp(e.GetPosition(SeekArea).X / SeekArea.ActualWidth, 0, 1);

    private void OnSeekDown(object sender, MouseButtonEventArgs e)
    {
        if (!_canSeek)
        {
            return;
        }

        _seeking = true;
        SeekArea.CaptureMouse();
        SeekThumb.Opacity = 1;
        SetProgressVisual(SeekFraction(e));
        e.Handled = true;
    }

    private void OnSeekMove(object sender, MouseEventArgs e)
    {
        if (_seeking && _snapshot.Timeline is { } timeline)
        {
            var fraction = SeekFraction(e);
            SetProgressVisual(fraction);
            ElapsedText.Text = Format(TimeSpan.FromSeconds(timeline.Duration.TotalSeconds * fraction));
        }
    }

    private void OnSeekUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking)
        {
            return;
        }

        var fraction = SeekFraction(e);
        _seeking = false;
        SeekArea.ReleaseMouseCapture();
        if (_snapshot.Timeline is { } timeline)
        {
            SeekRequested?.Invoke(TimeSpan.FromSeconds(timeline.Duration.TotalSeconds * fraction));
        }

        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _progressTimer.Stop();
        if (_hwnd != 0)
        {
            HwndSource.FromHwnd(_hwnd)?.RemoveHook(WndProc);
        }

        base.OnClosed(e);
    }
}
