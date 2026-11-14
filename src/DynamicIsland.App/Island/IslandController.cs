using System.Windows.Threading;
using Microsoft.Win32;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Lyrics;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.State;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Island;

/// <summary>
/// Ada davranışının merkezi: medya anlık görüntüleri, şarkı değişiminde kısa açılma, fareyle/kısayolla genişleme,
/// oyun modu (tıklama geçirme) ve etkileşim modu. Tüm durum UI iş parçacığında tutulur.
/// </summary>
public sealed partial class IslandController : IDisposable
{
    private readonly IslandWindow _window;
    private readonly IMediaSource _media;
    private readonly SettingsService _settings;
    private readonly GameModeService _game;
    private readonly ArtworkDecoder _decoder = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _peekTimer;
    private readonly DispatcherTimer _hoverEnterTimer;
    private readonly DispatcherTimer _hoverLeaveTimer;
    private readonly DispatcherTimer _interactionTimeout;
    private readonly DispatcherTimer _placeTimer;
    private readonly DispatcherTimer _hoverWatch = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly LyricsService? _lyricsService;
    private CancellationTokenSource? _lyricsCts;
    private string? _lyricsKey;

    private MediaSnapshot _snapshot;
    private string? _lastTrackKey;
    private bool _peekActive;
    private bool _hover;
    private bool _hotkeyExpanded;
    private bool _interaction;
    private bool _userHidden;
    private string? _artKey;
    private ulong? _artHash;
    private int _artRequest;
    private bool _disposed;

    public IslandController(IslandWindow window, IMediaSource media, SettingsService settings, GameModeService game, LyricsService? lyrics)
    {
        _window = window;
        _media = media;
        _settings = settings;
        _game = game;
        _lyricsService = lyrics;
        _dispatcher = window.Dispatcher;
        _snapshot = media.Current;

        _peekTimer = new DispatcherTimer();
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            _peekActive = false;
            Recompute();
        };

        _hoverEnterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _hoverEnterTimer.Tick += (_, _) =>
        {
            _hoverEnterTimer.Stop();
            if (!_game.IsActive && _settings.Current.HoverToExpand && !_window.IsDragPending && _window.IsCursorOverIsland())
            {
                _hover = true;
                _hoverWatch.Start();
                Recompute();
            }
        };

        // Fare çekildikten sonra kısa bir tolerans (kenardan kısa taşmalarda titremesin), sonra hemen daral.
        _hoverLeaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _hoverLeaveTimer.Tick += (_, _) =>
        {
            _hoverLeaveTimer.Stop();
            if (_hover && _window.IsCursorOverIsland())
            {
                return; // Fare geri döndü (giriş olayı kaçmış olabilir): açık kal.
            }

            _hover = false;
            _hotkeyExpanded = false;
            Recompute();
        };

        // Katmanlı pencerelerde hızlı çıkışta MouseLeave gecikebiliyor/kaçabiliyor: genişken imleci ayrıca denetle.
        _hoverWatch.Tick += (_, _) =>
        {
            if (!_hover)
            {
                _hoverWatch.Stop();
                return;
            }

            var over = _window.IsCursorOverIsland(toleranceDip: 2);
            if (!over && !_hoverLeaveTimer.IsEnabled)
            {
                _hoverLeaveTimer.Start();
            }
            else if (over && _hoverLeaveTimer.IsEnabled)
            {
                _hoverLeaveTimer.Stop();
            }
        };

        _interactionTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _interactionTimeout.Tick += (_, _) =>
        {
            if (_window.IsMouseOver)
            {
                return; // Fare adanın üzerindeyse etkileşim sürsün.
            }

            _interactionTimeout.Stop();
            SetInteraction(false);
        };

        _placeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _placeTimer.Tick += (_, _) =>
        {
            _placeTimer.Stop();
            Place();
        };

        _window.HoverChanged += OnHoverChanged;
        _window.PointerActivity += OnPointerActivity;
        _window.PlayPauseRequested += () => RunCommand(_media.TogglePlayPauseAsync());
        _window.NextRequested += () => RunCommand(_media.NextAsync());
        _window.PreviousRequested += () => RunCommand(_media.PreviousAsync());
        _window.SeekRequested += position => RunCommand(_media.SeekAsync(position));
        _window.PlacementInvalidated += SchedulePlace;
        InitializeMedia();
        InitializeDrag();
        _media.SnapshotChanged += OnSnapshotFromAnyThread;
        _settings.Changed += OnSettingsChanged;
        _game.ActiveChanged += OnGameModeChanged;
        _game.ForegroundChanged += () => _window.AssertTopmost();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Tepsi menüsünü güncellemek için.</summary>
    public event Action? StateChanged;

    public bool UserHidden => _userHidden;

    public bool InteractionMode => _interaction;

    public bool GameModeActive => _game.IsActive;

    public void Start()
    {
        _window.EnsureCreated();
        ApplyAppearance(_settings.Current);
        Place();
        _window.SetLyricFont(_settings.Current.LyricsFont);
        _window.SetLyricsToggleState(_settings.Current.LyricsEnabled);
        _window.SetTheme(_settings.Current.Theme);
        OnSnapshot(_media.Current);
        _ = StartVolumeWatcherAsync();
    }

    public void ToggleExpand()
    {
        if (!_snapshot.HasTrack)
        {
            return;
        }

        var expanded = _hotkeyExpanded || _hover;
        _hotkeyExpanded = !expanded;
        if (expanded)
        {
            _hover = false;
        }

        Recompute();
    }

    public void ToggleVisibility()
    {
        _userHidden = !_userHidden;
        if (_userHidden)
        {
            _interaction = false;
            _interactionTimeout.Stop();
        }

        Recompute();
    }

    public void ToggleInteraction() => SetInteraction(!_interaction);

    private void SetInteraction(bool enabled)
    {
        _interaction = enabled;
        if (enabled)
        {
            _userHidden = false;
            _interactionTimeout.Stop();
            _interactionTimeout.Start();
        }
        else
        {
            _interactionTimeout.Stop();
            _hover = false;
            _hotkeyExpanded = false;
        }

        Recompute();
    }

    /// <summary>Konumu ayarlara göre (monitör, kaydırma, üst boşluk, ölçek, DPI) yeniden hesaplar.</summary>
    public void Place() => Place(_settings.Current.HorizontalOffset, _settings.Current.TopMargin);

    private void Place(double horizontalOffset, double topMargin)
    {
        var s = _settings.Current;
        var monitor = DisplayMonitors.Resolve(s.MonitorDeviceName);
        var rect = PlacementCalculator.Compute(
            monitor.Bounds,
            monitor.DpiScale,
            (IslandWindow.BaseWidth * s.Scale, IslandWindow.BaseHeight * s.Scale),
            IslandWindow.MaxIslandWidth * s.Scale,
            IslandWindow.MaxIslandHeight * s.Scale,
            horizontalOffset,
            topMargin);
        _window.Place(rect);
    }

    private void SchedulePlace()
    {
        if (_disposed)
        {
            return;
        }

        _placeTimer.Stop();
        _placeTimer.Start();
    }

    private void OnSnapshotFromAnyThread(object? sender, MediaSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            // Sıra dışı gelen eski görüntüleri atla: her zaman kaynağın en güncel durumunu uygula.
            if (!_disposed)
            {
                OnSnapshot(_media.Current);
            }
        });
    }

    private void OnSnapshot(MediaSnapshot snapshot)
    {
        DetectPlaybackToggle(_snapshot, snapshot);
        _snapshot = snapshot;
        var key = snapshot.HasTrack ? snapshot.Track!.Key : null;

        _window.SetSnapshot(snapshot);
        _window.SetIdleText(IslandStateResolver.IdleText(snapshot.Availability, hasTrackless: snapshot.Availability == MediaAvailability.Ready));

        if (key is not null && key != _lastTrackKey)
        {
            StartPeek();
        }

        if (key is not null)
        {
            _lastTrackKey = key;
        }
        else if (snapshot.Availability != MediaAvailability.Ready)
        {
            // Spotify kapandı / oturum kayboldu: yeniden açıldığında aynı parça da tanıtılsın.
            _lastTrackKey = null;
            _peekActive = false;
            _peekTimer.Stop();
        }

        _ = UpdateArtworkAsync(snapshot);
        UpdateLyricsRequest();
        Recompute();
    }

    /// <summary>Geçerli parça için sözleri ister (ayar kapalıysa / parça yoksa temizler). Parça değişince eski istek iptal edilir.</summary>
    private void UpdateLyricsRequest()
    {
        var s = _settings.Current;
        _window.CompactTextEnabled = s.LyricsInCompact;

        if (!s.LyricsEnabled || _lyricsService is null || !_snapshot.HasTrack)
        {
            CancelLyrics();
            _lyricsKey = null;
            _window.SetLyrics(null);
            return;
        }

        var track = _snapshot.Track!;
        if (track.Key == _lyricsKey)
        {
            return;
        }

        CancelLyrics();
        _lyricsKey = track.Key;
        _window.SetLyrics(null);

        if (_lyricsService.TryGetCached(LyricsQuery.From(track, null), out var cached))
        {
            _window.SetLyrics(cached.Lyrics);
            return;
        }

        var cts = new CancellationTokenSource();
        _lyricsCts = cts;
        _ = FetchLyricsAsync(track.Key, cts.Token);
    }

    private async Task FetchLyricsAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            // Hızlı art arda geçişlerde ağa gitme; Spotify'ın yeni parçanın süresini bildirmesine de zaman tanı.
            await Task.Delay(450, cancellationToken);
            if (_snapshot.Track is not { } track || track.Key != key || _lyricsService is null)
            {
                return;
            }

            var result = await _lyricsService.GetAsync(LyricsQuery.From(track, _snapshot.Timeline?.Duration), cancellationToken);
            if (cancellationToken.IsCancellationRequested || _disposed || _lyricsKey != key)
            {
                return;
            }

            _window.SetLyrics(result.Lyrics);

            if (result.Status == LyricsStatus.Unavailable)
            {
                // Ağ / hız sınırı: aynı parça hâlâ çalıyorsa bir dakika sonra bir kez daha dene.
                await Task.Delay(TimeSpan.FromSeconds(65), cancellationToken);
                if (!_disposed && _lyricsKey == key)
                {
                    _lyricsKey = null;
                    UpdateLyricsRequest();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warn("Söz isteği başarısız", ex);
        }
    }

    private void CancelLyrics()
    {
        _lyricsCts?.Cancel();
        _lyricsCts?.Dispose();
        _lyricsCts = null;
    }

    private void StartPeek()
    {
        var seconds = _settings.Current.PeekSeconds;
        if (seconds <= 0)
        {
            return;
        }

        // Hızlı art arda geçişlerde zamanlayıcı yeniden başlar; içerik yerinde güncellenir, animasyon birikmez.
        _peekActive = true;
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(seconds);
        _peekTimer.Start();
    }

    private async Task UpdateArtworkAsync(MediaSnapshot snapshot)
    {
        if (!snapshot.HasTrack)
        {
            return;
        }

        var artwork = snapshot.Artwork;
        var key = snapshot.Track!.Key;
        if (artwork is null || artwork.TrackKey != key)
        {
            if (_artKey != key)
            {
                _artKey = key;
                _artHash = null;
                _artRequest++;
                _window.SetArtwork(null, ArtworkDecoder.FallbackAccent, animate: true);
            }

            return;
        }

        if (artwork.TrackKey == _artKey && artwork.Hash == _artHash)
        {
            return;
        }

        _artKey = artwork.TrackKey;
        _artHash = artwork.Hash;
        var request = ++_artRequest;
        DecodedArtwork? decoded;
        try
        {
            decoded = await _decoder.DecodeAsync(artwork);
        }
        catch (Exception ex)
        {
            Log.Warn("Kapak işlenemedi", ex);
            decoded = null;
        }

        if (request != _artRequest || _disposed)
        {
            return; // Bu arada parça değişti.
        }

        _window.SetArtwork(decoded?.Image, decoded?.Accent ?? ArtworkDecoder.FallbackAccent, animate: true);
    }

    private void Recompute()
    {
        if (_disposed)
        {
            return;
        }

        var s = _settings.Current;
        var expand = _hover || _hotkeyExpanded || _interaction;
        var visual = IslandStateResolver.Resolve(new IslandInputs(
            _userHidden, _snapshot.Availability, _snapshot.HasTrack, s.IdleBehavior, expand, _peekActive, _hudActive));

        if (Log.TraceEnabled && visual != _window.Visual)
        {
            Log.Trace($"visual {_window.Visual} -> {visual} (hover={_hover} hotkey={_hotkeyExpanded} interaction={_interaction} peek={_peekActive} game={_game.IsActive})");
        }

        _window.SetVisual(visual);
        _window.SetClickThrough(_game.IsActive && !_interaction && !_dragEnabled);
        _window.SetInteractionIndicator((_interaction || _dragEnabled) && visual != IslandVisual.Hidden);
        StateChanged?.Invoke();
    }

    private void OnHoverChanged(bool inside)
    {
        Log.Trace($"hover {(inside ? "enter" : "leave")}");
        if (inside)
        {
            _hoverLeaveTimer.Stop();
            if (!_game.IsActive && _settings.Current.HoverToExpand && !_hover)
            {
                _hoverEnterTimer.Start();
            }
        }
        else
        {
            _hoverEnterTimer.Stop();
            if (_hover || _hotkeyExpanded)
            {
                _hoverLeaveTimer.Start();
            }
        }

        OnPointerActivity();
    }

    private void OnPointerActivity()
    {
        if (_interaction)
        {
            _interactionTimeout.Stop();
            _interactionTimeout.Start();
        }
    }

    private void RunCommand(Task<bool> command)
    {
        OnPointerActivity();
        _ = command.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Log.Warn("Medya komutu hata verdi", t.Exception);
            }
        }, TaskScheduler.Default);
    }

    private void OnGameModeChanged(bool active)
    {
        if (active)
        {
            // Oyunda fareyle genişleme kapalı; açık kalmış hover durumunu temizle.
            _hover = false;
            _hotkeyExpanded = false;
            _hoverEnterTimer.Stop();
            _hoverLeaveTimer.Stop();
        }

        Recompute();
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        ApplyAppearance(settings);
        _game.Mode = settings.GameMode;
        _window.SetLyricFont(settings.LyricsFont);
        _window.SetLyricsToggleState(settings.LyricsEnabled);
        _window.SetTheme(settings.Theme);
        SchedulePlace();
        UpdateLyricsRequest();
        if (!settings.ShowMediaHud)
        {
            EndHud();
        }

        Recompute();
    }

    private void ApplyAppearance(AppSettings settings)
    {
        var animations = settings.AnimationsEnabled && SystemAnimation.ClientAreaAnimationsEnabled;
        _window.ApplyAppearance(settings.Scale, settings.BackgroundOpacity, animations);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _dispatcher.BeginInvoke(() =>
        {
            SchedulePlace();
            _game.Evaluate();
        });

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        _dispatcher.BeginInvoke(async () =>
        {
            Log.Info("Uykudan dönüldü: konum ve medya yeniden eşitleniyor");
            SchedulePlace();
            _media.RequestResync();
            await Task.Delay(2000);
            SchedulePlace();
        });
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) =>
        _dispatcher.BeginInvoke(() => ApplyAppearance(_settings.Current));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _media.SnapshotChanged -= OnSnapshotFromAnyThread;
        _settings.Changed -= OnSettingsChanged;
        _game.ActiveChanged -= OnGameModeChanged;
        _peekTimer.Stop();
        _hoverEnterTimer.Stop();
        _hoverLeaveTimer.Stop();
        _hoverWatch.Stop();
        _interactionTimeout.Stop();
        _placeTimer.Stop();
        CancelLyrics();
        _decoder.Clear();
        DisposeMedia();
    }
}
