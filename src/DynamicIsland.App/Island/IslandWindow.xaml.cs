using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.State;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Island;

/// <summary>
/// Adanın görünümü. Sabit boyutlu, şeffaf, katmanlı (layered) bir pencere; yalnızca içteki kapsül
/// boyut değiştirir. Saydam (alfa = 0) pikseller Windows tarafından tıklamaya kapalı sayılır, böylece
/// görünmeyen alanlar fare girişini engellemez. Pencere asla etkinleşmez (WS_EX_NOACTIVATE).
/// Sözler: <c>IslandWindow.Lyrics.cs</c>; ses ve göstergeler: <c>IslandWindow.Media.cs</c>.
/// </summary>
public partial class IslandWindow : Window
{
    public const double BaseWidth = 432;
    public const double BaseHeight = 284;
    public const double MaxIslandWidth = 340;
    public const double MaxIslandHeight = 248;

    private static readonly Size IdleSize = new(150, 30);
    private static readonly Size CompactSize = new(172, 34);
    private static readonly Size CompactTextSize = new(250, 34);
    private static readonly Size PeekSize = new(310, 56);
    private const double ExpandedWidth = 340;

    // Geniş görünüm yerleşimi (DIP): başlık bölümünün altı, söz bloğu, ilerleme satırı, kontroller.
    private const double HeaderBottom = 92;
    private const double LyricsTop = 84;
    private const double LyricsHeight = 74;
    private const double ProgressToControls = 22;
    private const double ControlsToBottom = 60;

    private readonly DispatcherTimer _progressTimer;
    private nint _hwnd;
    private IslandVisual _visual = IslandVisual.Hidden;
    private bool _animations = true;
    private bool _shown;
    private MediaSnapshot _snapshot = MediaSnapshot.Initial();
    private bool _canSeek;
    private bool _seeking;
    private int _hideGeneration;

    private bool _firstShowLogged;
    private bool _compactTextEnabled = true;
    private bool _layoutHasLyrics;
    private bool _layoutHasTimeline;
    private bool _layoutCompactText;

    public IslandWindow()
    {
        InitializeComponent();
        CreateShadowLayers();
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

        _lyricTimer = new DispatcherTimer(DispatcherPriority.Normal);
        _lyricTimer.Tick += (_, _) => UpdateLyrics();

        InitializeMedia();
        ApplyLayerState(IslandVisual.Hidden, animate: false);
        LayoutExpanded();
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

    /// <summary>Teşhis/görüntü dökümü için kök öğe.</summary>
    internal FrameworkElement RenderRoot => ScaleRoot;

    /// <summary>Küçük ada çalan söz satırını / şarkı adı ve sanatçıyı göstersin mi?</summary>
    public bool CompactTextEnabled
    {
        get => _compactTextEnabled;
        set
        {
            if (_compactTextEnabled != value)
            {
                _compactTextEnabled = value;
                OnContentLayoutChanged();
            }
        }
    }

    private bool CompactShowsText => _snapshot.HasTrack && _compactTextEnabled;

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
        _backgroundAlpha = (byte)Math.Round(a * 255);
        ApplyTheme(animate: false);
        ShadowPlate.Opacity = a;

        foreach (var bars in AllBars)
        {
            bars.AnimationsEnabled = animationsEnabled;
        }
    }

    private PlaybackBars[] AllBars => [CompactBars, CompactLyricsBars, PeekBars, ExpandedBars];

    // Not: pencere yüksekliğini adaya göre küçültmek denendi; ölçümde CPU'yu düşürmek yerine artırdı
    // (yeniden boyutlanan katmanlı yüzey her seferinde yeniden ayrılıyor). Pencere sabit boyutta kalır.
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

    /// <summary>
    /// İmleç şu an görünen kapsülün üzerinde mi? WPF'nin MouseLeave olayı katmanlı pencerelerde
    /// hızlı çıkışlarda gecikebildiği için daralma kararı bununla da doğrulanır.
    /// </summary>
    public bool IsCursorOverIsland(double toleranceDip = 1)
    {
        if (!_shown || _hwnd == 0 || !OverlayWindow.TryGetCursorPos(out var x, out var y))
        {
            return false;
        }

        try
        {
            var p = Island.PointFromScreen(new Point(x, y));
            return p.X >= -toleranceDip && p.Y >= -toleranceDip
                && p.X <= Island.ActualWidth + toleranceDip && p.Y <= Island.ActualHeight + toleranceDip;
        }
        catch (InvalidOperationException)
        {
            return false; // Görsel ağaç henüz bir sunum kaynağına bağlı değil.
        }
    }

    public void SetInteractionIndicator(bool visible) =>
        Animate(InteractionRing, OpacityProperty, visible ? 1 : 0, 180, null);

    public void SetIdleText(string text) => IdleText.Text = text;

    public void SetSnapshot(MediaSnapshot snapshot)
    {
        var previousKey = _snapshot.Track?.Key;
        _snapshot = snapshot;
        var track = snapshot.Track;

        if (track is not null)
        {
            PeekTitle.Text = ExpandedTitle.Text = track.Title;
            PeekArtist.Text = ExpandedArtist.Text = ArtistText(track);
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

        foreach (var bars in AllBars)
        {
            bars.IsPlaying = playing;
        }

        DemoBadge.Visibility = snapshot.IsDemo ? Visibility.Visible : Visibility.Collapsed;

        var hasTimeline = snapshot.Timeline is not null;
        ProgressRow.Visibility = hasTimeline ? Visibility.Visible : Visibility.Collapsed;

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

        if (previousKey != track?.Key)
        {
            _compactContentKey = null; // yeni parça: küçük adadaki bilgi yazısı da yenilensin
        }

        if (LayoutNeedsRefresh())
        {
            OnContentLayoutChanged();
        }
        else
        {
            UpdateLyrics();
        }
    }

    private static string ArtistText(TrackInfo track) =>
        string.IsNullOrWhiteSpace(track.Artist) ? "Bilinmeyen sanatçı" : track.Artist;

    public void SetArtwork(ImageSource? image, Color accent, bool animate)
    {
        var anim = animate && _animations && _shown;
        CompactArt.SetImage(image, anim);
        CompactLyricsArt.SetImage(image, anim);
        PeekArt.SetImage(image, anim);
        ExpandedArt.SetImage(image, anim);
        HudArt.SetImage(image, anim);
        _accent = accent;
        _hasArtwork = image is not null;
        ApplyTheme(anim);

        // Işıma: kapak varsa onun rengi, yoksa çok soluk nötr.
        var glow = image is null ? Color.FromRgb(0x9A, 0x9A, 0xA0) : accent;
        var alpha = (byte)(image is null ? 0x22 : 0x4A);
        SetGlowColor(Color.FromArgb(alpha, glow.R, glow.G, glow.B), anim);
    }

    private void SetGlowColor(Color color, bool animate)
    {
        var transparent = Color.FromArgb(0, color.R, color.G, color.B);
        if (!animate)
        {
            GlowStop0.BeginAnimation(GradientStop.ColorProperty, null);
            GlowStop1.BeginAnimation(GradientStop.ColorProperty, null);
            GlowStop0.Color = color;
            GlowStop1.Color = transparent;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(420);
        GlowStop0.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(color, duration), HandoffBehavior.SnapshotAndReplace);
        GlowStop1.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(transparent, duration), HandoffBehavior.SnapshotAndReplace);
    }

    public void SetVisual(IslandVisual visual)
    {
        if (visual == _visual)
        {
            return;
        }

        var previous = _visual;
        _visual = visual;
        OnVisualChangedForMedia();

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
                if (!_firstShowLogged)
                {
                    _firstShowLogged = true;
                    var sinceStart = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
                    DynamicIsland.Core.Diagnostics.Log.Info($"Ada ilk kez gösterildi (işlem başlangıcından {sinceStart.TotalMilliseconds:0} ms sonra)");
                }

                AssertTopmost();
            }

            Animate(Stage, OpacityProperty, 1, 200, null);
        }

        AnimateIslandSize(target);
        ApplyLayerState(visual, _animations);
        UpdateProgressTimer();
        if (visual == IslandVisual.Expanded)
        {
            _expandedIndex = int.MinValue; // satır konumları görünürken yeniden ölçülsün
        }

        UpdateLyrics(animate: false);
    }

    private void HideIsland()
    {
        ApplyLayerState(IslandVisual.Hidden, _animations);
        _progressTimer.Stop();
        _lyricTimer.Stop();
        if (!_shown)
        {
            return;
        }

        var generation = ++_hideGeneration;
        AnimateIslandSize(new Size(Math.Min(Island.ActualWidth, 110), Math.Max(24, Island.ActualHeight * 0.8)));
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
        IslandVisual.Compact => CompactShowsText ? CompactTextSize : CompactSize,
        IslandVisual.Peek => PeekSize,
        IslandVisual.Expanded => new Size(ExpandedWidth, LayoutExpanded()),
        IslandVisual.Hud => HudSize,
        _ => CompactSize,
    };

    private bool LayoutNeedsRefresh() =>
        _layoutHasLyrics != HasLyrics || _layoutHasTimeline != (_snapshot.Timeline is not null) || _layoutCompactText != CompactShowsText;

    /// <summary>Geniş görünümün dikey yerleşimini içeriğe göre kurar ve toplam yüksekliği döndürür.</summary>
    private double LayoutExpanded()
    {
        var hasLyrics = HasLyrics;
        var hasTimeline = _snapshot.Timeline is not null;
        _layoutHasLyrics = hasLyrics;
        _layoutHasTimeline = hasTimeline;

        LyricsBlock.Visibility = hasLyrics ? Visibility.Visible : Visibility.Collapsed;
        LyricsBlock.Margin = new Thickness(18, LyricsTop, 18, 0);
        LyricsBlock.Height = LyricsHeight;
        var y = hasLyrics ? LyricsTop + LyricsHeight + 6 : HeaderBottom;
        ProgressRow.Margin = new Thickness(18, y, 18, 0);
        var controls = hasTimeline ? y + ProgressToControls : y;
        ControlsRow.Margin = new Thickness(0, controls, 0, 0);
        return controls + ControlsToBottom;
    }

    /// <summary>Söz varlığı / zaman çizelgesi / küçük ada yazısı değişti: katmanları ve boyutu yeniden uygula.</summary>
    private void OnContentLayoutChanged()
    {
        if (_visual is IslandVisual.Compact or IslandVisual.Expanded)
        {
            AnimateIslandSize(SizeFor(_visual));
            ApplyLayerState(_visual, _animations && _shown);
        }
        else
        {
            LayoutExpanded();
            _layoutCompactText = CompactShowsText;
        }

        _compactIndex = _expandedIndex = int.MinValue;
        _compactContentKey = null;
        UpdateLyrics(animate: false);
    }

    private void AnimateIslandSize(Size target)
    {
        if (!_animations)
        {
            Island.BeginAnimation(WidthProperty, null);
            Island.BeginAnimation(HeightProperty, null);
            Island.Width = target.Width;
            Island.Height = target.Height;
            return;
        }

        var grow = target.Width * target.Height >= Island.ActualWidth * Island.ActualHeight;

        // SnapshotAndReplace: hızlı art arda geçişlerde animasyonlar birikmez; mevcut değerden yeni hedefe akar.
        // Daralma, büyümeden belirgin şekilde hızlı: fare çekilince ada beklemeden toparlansın.
        var duration = TimeSpan.FromMilliseconds(grow ? 400 : 280);
        var ease = new SpringEase { Damping = grow ? 0.82 : 0.97, Stiffness = grow ? 7.6 : 9.5 };
        var w = new DoubleAnimation(target.Width, duration) { EasingFunction = ease };
        var h = new DoubleAnimation(target.Height, duration) { EasingFunction = ease };
        Island.BeginAnimation(WidthProperty, w, HandoffBehavior.SnapshotAndReplace);
        Island.BeginAnimation(HeightProperty, h, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyLayerState(IslandVisual visual, bool animate)
    {
        _layoutCompactText = CompactShowsText;
        var compactText = visual == IslandVisual.Compact && _layoutCompactText;
        SetLayer(IdleLayer, visual == IslandVisual.Idle, animate);
        SetLayer(CompactLayer, visual == IslandVisual.Compact && !compactText, animate);
        SetLayer(CompactLyricsLayer, compactText, animate);
        SetLayer(PeekLayer, visual == IslandVisual.Peek, animate);
        SetLayer(ExpandedLayer, visual == IslandVisual.Expanded, animate);
        SetLayer(HudVolumeLayer, visual == IslandVisual.Hud && _hudMode == HudMode.Volume, animate);
        SetLayer(HudPlaybackLayer, visual == IslandVisual.Hud && _hudMode == HudMode.Playback, animate);
        CompactBars.LayerActive = visual == IslandVisual.Compact && !compactText;
        CompactLyricsBars.LayerActive = compactText;
        PeekBars.LayerActive = visual == IslandVisual.Peek;
        ExpandedBars.LayerActive = visual == IslandVisual.Expanded;

        var glow = _theme != IslandTheme.PureBlack && visual is IslandVisual.Peek or IslandVisual.Expanded ? 1 : 0;
        if (animate)
        {
            Animate(Glow, OpacityProperty, glow, glow > 0 ? 360 : 160, null);
        }
        else
        {
            Glow.BeginAnimation(OpacityProperty, null);
            Glow.Opacity = glow;
        }
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
            ? new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } }
            : new DoubleAnimation(0, TimeSpan.FromMilliseconds(80)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
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
        UpdateShadowLayers(w, h, r);
        Outline.CornerRadius = radius;
        InteractionRing.CornerRadius = radius;
        var clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        clip.Freeze();
        Island.Clip = clip;
    }

    // ---- İlerleme ----

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
        var width = SeekArea.ActualWidth > 0 ? SeekArea.ActualWidth : MaxIslandWidth - 36 - 76 - 8;
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
        _lyricTimer.Stop();
        if (_hwnd != 0)
        {
            HwndSource.FromHwnd(_hwnd)?.RemoveHook(WndProc);
        }

        base.OnClosed(e);
    }
}
