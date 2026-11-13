using System.Windows.Threading;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.State;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Island;

/// <summary>
/// Ses ve medya tuşları: Spotify uygulama sesi (kaydırıcı, sessize alma, tekerlek) ve kısa göstergeler.
/// Klavye DİNLENMEZ: ses tuşlarının sonucu (sistem sesi değişti) Windows ses API'sinden, oynat/duraklat
/// ise Spotify'ın medya oturumundan anlaşılır. Böylece tuşlar Spotify'a/Windows'a aynen ulaşmaya devam eder.
/// </summary>
public sealed partial class IslandController
{
    private const string SpotifyProcess = "Spotify";

    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private readonly DispatcherTimer _playbackHudDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _volumePoll = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1200) };
    private readonly object _volumeGate = new();
    private float? _pendingLevel;
    private bool? _pendingMute;
    private bool _volumeWorkerRunning;
    private bool _volumeRefreshRunning;
    private VolumeState? _appVolume;
    private SystemVolumeWatcher? _systemVolume;
    private long _volumeWatchStartedAt;
    private bool _hudActive;
    private bool _pendingPlaybackHud;
    private string? _pendingPlaybackKey;

    private void InitializeMedia()
    {
        _hudTimer.Tick += (_, _) => EndHud();
        _playbackHudDelay.Tick += (_, _) =>
        {
            _playbackHudDelay.Stop();
            // Kısa süreli "Changing"/geçici duraklamalarda gösterme: durum hâlâ aynıysa göster.
            if (_snapshot.Track?.Key == _pendingPlaybackKey && _snapshot.IsPlaying == _pendingPlaybackHud
                && _window.Visual != IslandVisual.Expanded && _settings.Current.ShowMediaHud)
            {
                _window.ShowPlaybackHud(_pendingPlaybackHud);
                BeginHud();
            }
        };

        _volumePoll.Tick += (_, _) => _ = RefreshAppVolumeAsync();
        _window.ExpandedChanged += expanded =>
        {
            if (expanded)
            {
                _ = RefreshAppVolumeAsync();
                _volumePoll.Start();
            }
            else
            {
                _volumePoll.Stop();
            }
        };

        _window.AppVolumeRequested += level => QueueAppVolume(level, null);
        _window.AppVolumeStepRequested += step =>
        {
            float current;
            lock (_volumeGate)
            {
                current = _pendingLevel ?? _appVolume?.Level ?? 0;
            }

            QueueAppVolume(Math.Clamp(current + step, 0f, 1f), null);
        };
        // Geniş görünümdeki söz düğmesi ayarı değiştirir (kalıcı); ayar penceresi de aynı anda güncellenir.
        _window.LyricsToggleRequested += () =>
        {
            OnPointerActivity();
            _settings.Update(s => s.LyricsEnabled = !s.LyricsEnabled);
        };

        _window.AppMuteToggleRequested += () =>
        {
            if (_appVolume is { } v)
            {
                QueueAppVolume(null, !v.Muted);
            }
        };
    }

    // ---- Spotify uygulama sesi ----

    private void QueueAppVolume(float? level, bool? mute)
    {
        OnPointerActivity();
        if (level > 0 && mute is null && _appVolume?.Muted == true)
        {
            mute = false; // sessizken kaydırıcı yukarı çekilirse sesi aç
        }

        // İyimser güncelleme: kaydırıcı ve tekerlek anında tepki versin.
        if (_appVolume is { } current)
        {
            _appVolume = new VolumeState(level ?? current.Level, mute ?? current.Muted);
            _window.SetAppVolume(_appVolume);
        }

        lock (_volumeGate)
        {
            if (level is not null)
            {
                _pendingLevel = level;
            }

            if (mute is not null)
            {
                _pendingMute = mute;
            }

            if (_volumeWorkerRunning)
            {
                return; // çalışan işçi en son değeri alacak (sürüklerken istekler birikmez)
            }

            _volumeWorkerRunning = true;
        }

        _ = Task.Run(VolumeWorker);
    }

    private void VolumeWorker()
    {
        while (true)
        {
            float? level;
            bool? mute;
            lock (_volumeGate)
            {
                level = _pendingLevel;
                mute = _pendingMute;
                _pendingLevel = null;
                _pendingMute = null;
                if (level is null && mute is null)
                {
                    _volumeWorkerRunning = false;
                    break;
                }
            }

            try
            {
                AppVolume.Set(SpotifyProcess, level, mute);
            }
            catch (Exception ex)
            {
                Log.Warn("Spotify ses seviyesi ayarlanamadı", ex);
            }
        }

        VolumeState? state = null;
        try
        {
            state = AppVolume.Get(SpotifyProcess);
        }
        catch (Exception ex)
        {
            Log.Warn("Spotify ses seviyesi okunamadı", ex);
        }

        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && !IsVolumeWorkerBusy())
            {
                _appVolume = state;
                _window.SetAppVolume(state);
            }
        });
    }

    private bool IsVolumeWorkerBusy()
    {
        lock (_volumeGate)
        {
            return _volumeWorkerRunning;
        }
    }

    private async Task RefreshAppVolumeAsync()
    {
        if (_volumeRefreshRunning || _disposed)
        {
            return;
        }

        _volumeRefreshRunning = true;
        try
        {
            var state = await Task.Run(() => AppVolume.Get(SpotifyProcess));
            if (!_disposed && !IsVolumeWorkerBusy())
            {
                _appVolume = state;
                _window.SetAppVolume(state);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Spotify ses seviyesi okunamadı", ex);
        }
        finally
        {
            _volumeRefreshRunning = false;
        }
    }

    // ---- Göstergeler (medya / ses tuşları) ----

    private async Task StartVolumeWatcherAsync()
    {
        var watcher = await SystemVolumeWatcher.StartAsync();
        if (watcher is null)
        {
            return;
        }

        if (_disposed)
        {
            watcher.Dispose();
            return;
        }

        _systemVolume = watcher;
        _volumeWatchStartedAt = Environment.TickCount64;
        watcher.Changed += state => _dispatcher.BeginInvoke(() => OnSystemVolumeChanged(state));
    }

    private void OnSystemVolumeChanged(VolumeState state)
    {
        // Açılıştaki ilk bildirimleri ve geniş görünümü bölmeyi yok say.
        if (_disposed || !_settings.Current.ShowMediaHud || Environment.TickCount64 - _volumeWatchStartedAt < 1500
            || _window.Visual == IslandVisual.Expanded)
        {
            return;
        }

        _window.ShowVolumeHud(state);
        BeginHud();
    }

    /// <summary>Aynı parçada Çalıyor ↔ Duraklatıldı geçişi: kısa gösterge (Spotify'dan, medya tuşundan veya adadan).</summary>
    private void DetectPlaybackToggle(MediaSnapshot previous, MediaSnapshot next)
    {
        if (!previous.HasTrack || !next.HasTrack || previous.Track!.Key != next.Track!.Key)
        {
            _playbackHudDelay.Stop();
            return;
        }

        bool? playing = (previous.Status, next.Status) switch
        {
            (PlaybackStatus.Playing, PlaybackStatus.Paused) => false,
            (PlaybackStatus.Paused, PlaybackStatus.Playing) => true,
            _ => null,
        };

        if (playing is { } p)
        {
            _pendingPlaybackHud = p;
            _pendingPlaybackKey = next.Track.Key;
            _playbackHudDelay.Stop();
            _playbackHudDelay.Start();
        }
    }

    private void BeginHud()
    {
        _hudActive = true;
        _hudTimer.Stop();
        _hudTimer.Start();
        Recompute();
    }

    private void EndHud()
    {
        _hudTimer.Stop();
        if (!_hudActive)
        {
            return;
        }

        _hudActive = false;
        Recompute();
    }

    private void DisposeMedia()
    {
        _hudTimer.Stop();
        _playbackHudDelay.Stop();
        _volumePoll.Stop();
        _systemVolume?.Dispose();
        _systemVolume = null;
    }
}
