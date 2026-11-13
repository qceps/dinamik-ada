using System.Windows.Threading;
using Microsoft.Win32;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.State;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Island;

/// <summary>
/// Ada davranışının merkezi: medya anlık görüntüleri, şarkı değişiminde kısa açılma, fareyle/kısayolla genişleme,
/// oyun modu (tıklama geçirme) ve etkileşim modu. Tüm durum UI iş parçacığında tutulur.
/// </summary>
public sealed class IslandController : IDisposable
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

    public IslandController(IslandWindow window, IMediaSource media, SettingsService settings, GameModeService game)
    {
        _window = window;
        _media = media;
        _settings = settings;
        _game = game;
        _dispatcher = window.Dispatcher;
        _snapshot = media.Current;

        _peekTimer = new DispatcherTimer();
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            _peekActive = false;
            Recompute();
        };

        _hoverEnterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _hoverEnterTimer.Tick += (_, _) =>
        {
            _hoverEnterTimer.Stop();
            if (!_game.IsActive && _settings.Current.HoverToExpand)
            {
                _hover = true;
                Recompute();
            }
        };

        _hoverLeaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _hoverLeaveTimer.Tick += (_, _) =>
        {
            _hoverLeaveTimer.Stop();
            _hover = false;
            _hotkeyExpanded = false;
            Recompute();
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
        OnSnapshot(_media.Current);
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
    public void Place()
    {
        var s = _settings.Current;
        var monitor = DisplayMonitors.Resolve(s.MonitorDeviceName);
        var rect = PlacementCalculator.Compute(
            monitor.Bounds,
            monitor.DpiScale,
            (IslandWindow.BaseWidth * s.Scale, IslandWindow.BaseHeight * s.Scale),
            IslandWindow.MaxIslandWidth * s.Scale,
            IslandWindow.MaxIslandHeight * s.Scale,
            s.HorizontalOffset,
            s.TopMargin);
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
        Recompute();
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
            _userHidden, _snapshot.Availability, _snapshot.HasTrack, s.IdleBehavior, expand, _peekActive));

        if (Log.TraceEnabled && visual != _window.Visual)
        {
            Log.Trace($"visual {_window.Visual} -> {visual} (hover={_hover} hotkey={_hotkeyExpanded} interaction={_interaction} peek={_peekActive} game={_game.IsActive})");
        }

        _window.SetVisual(visual);
        _window.SetClickThrough(_game.IsActive && !_interaction);
        _window.SetInteractionIndicator(_interaction && visual != IslandVisual.Hidden);
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
        SchedulePlace();
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
        _interactionTimeout.Stop();
        _placeTimer.Stop();
        _decoder.Clear();
    }
}
