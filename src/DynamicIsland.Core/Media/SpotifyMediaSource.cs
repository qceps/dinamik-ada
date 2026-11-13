using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Core.Diagnostics;
using Windows.Media.Control;
using Windows.Storage.Streams;
using GsmtcStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace DynamicIsland.Core.Media;

/// <summary>
/// Windows Global System Media Transport Controls (GSMTC) üzerinden yalnızca Spotify masaüstü
/// uygulamasının oturumunu izler. Olay tabanlıdır; olaylar 60 ms'lik pencerede birleştirilir ve
/// tek bir arka plan işçisi tarafından sırayla işlenir (UI iş parçacığı hiç bloke olmaz).
///
/// Sorgulama yalnızca şu durumlarda yapılır:
///  * Spotify oturumu yokken 4 sn'de bir (Spotify açıldı mı / kapandı mı ayrımı için, çok ucuz),
///  * 30 sn'de bir güvenlik eşitlemesi (olayların sessizce kesildiği durumlara karşı),
///  * <see cref="UsePolling"/> açıkken (olayların çalışmadığı AppContainer/Game Bar ortamı).
/// </summary>
public sealed class SpotifyMediaSource : IMediaSource
{
    [Flags]
    private enum Dirty
    {
        None = 0,
        Sessions = 1,
        Properties = 2,
        Playback = 4,
        Timeline = 8,
        All = Sessions | Properties | Playback | Timeline,
    }

    private const int MaxArtworkBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);

    private readonly CancellationTokenSource _cts = new();
    private readonly ArtworkCache _artworkCache = new(24);
    private readonly Timer _absentTimer;
    private readonly Timer _watchdogTimer;
    private readonly Timer? _pollTimer;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _pendingDirty;
    private int _workerRunning;
    private int _managerFailures;
    private volatile bool _disposed;
    private MediaSnapshot _current = MediaSnapshot.Initial();
    private CancellationTokenSource? _artworkRecheckCts;
    private string? _lastTrackKey;

    public SpotifyMediaSource(bool usePolling = false, TimeSpan? pollInterval = null)
    {
        UsePolling = usePolling;
        _absentTimer = new Timer(_ => RequestRefresh(Dirty.Sessions), null, Timeout.Infinite, Timeout.Infinite);
        _watchdogTimer = new Timer(_ => RequestRefresh(Dirty.All), null, Timeout.Infinite, Timeout.Infinite);
        if (usePolling)
        {
            var interval = pollInterval ?? TimeSpan.FromSeconds(1);
            _pollTimer = new Timer(_ => RequestRefresh(Dirty.Sessions | Dirty.Playback | Dirty.Properties), null, interval, interval);
        }
    }

    public event EventHandler<MediaSnapshot>? SnapshotChanged;

    /// <summary>Olaylar yerine periyodik okuma (Game Bar gibi kısmi güvenli ortamlarda).</summary>
    public bool UsePolling { get; }

    public bool IsDemo => false;

    public MediaSnapshot Current => Volatile.Read(ref _current);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _watchdogTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        RequestRefresh(Dirty.All);
        return Task.CompletedTask;
    }

    public void RequestResync() => RequestRefresh(Dirty.All);

    public Task<bool> TogglePlayPauseAsync() => RunCommandAsync("playpause", static async s =>
    {
        var caps = s.GetPlaybackInfo()?.Controls;
        if (caps is { IsPlayPauseToggleEnabled: true })
        {
            return await s.TryTogglePlayPauseAsync();
        }

        var status = s.GetPlaybackInfo()?.PlaybackStatus;
        return status == GsmtcStatus.Playing ? await s.TryPauseAsync() : await s.TryPlayAsync();
    });

    public Task<bool> NextAsync() => RunCommandAsync("next", static async s => await s.TrySkipNextAsync());

    public Task<bool> PreviousAsync() => RunCommandAsync("previous", static async s => await s.TrySkipPreviousAsync());

    public Task<bool> SeekAsync(TimeSpan position) => RunCommandAsync("seek", async s =>
    {
        var controls = s.GetPlaybackInfo()?.Controls;
        if (controls is not { IsPlaybackPositionEnabled: true })
        {
            return false;
        }

        return await s.TryChangePlaybackPositionAsync(position.Ticks);
    });

    private async Task<bool> RunCommandAsync(string name, Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        var session = Volatile.Read(ref _session);
        if (session is null || _disposed)
        {
            return false;
        }

        try
        {
            var task = action(session);
            var completed = await Task.WhenAny(task, Task.Delay(CommandTimeout)).ConfigureAwait(false);
            if (completed != task)
            {
                Log.Warn($"Medya komutu zaman aşımına uğradı: {name}");
                return false;
            }

            var ok = await task.ConfigureAwait(false);
            RequestRefresh(Dirty.Playback | Dirty.Timeline);
            return ok;
        }
        catch (Exception ex)
        {
            Log.Warn($"Medya komutu başarısız: {name}", ex);
            RequestRefresh(Dirty.Sessions);
            return false;
        }
    }

    private void RequestRefresh(Dirty flags)
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Or(ref _pendingDirty, (int)flags);
        if (Interlocked.CompareExchange(ref _workerRunning, 1, 0) == 0)
        {
            _ = Task.Run(WorkerAsync);
        }
    }

    private async Task WorkerAsync()
    {
        try
        {
            while (!_disposed)
            {
                // Aynı anda gelen olay patlamalarını (parça değişimi ~3-5 olay üretir) birleştir.
                await Task.Delay(60, _cts.Token).ConfigureAwait(false);
                var flags = (Dirty)Interlocked.Exchange(ref _pendingDirty, 0);
                if (flags == Dirty.None)
                {
                    break;
                }

                try
                {
                    await ProcessAsync(flags, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Beklenmeyen medya hataları uygulamayı kapatmamalı: oturumu bırak, yeniden bul.
                    Log.Warn("Medya durumu okunamadı; oturum yeniden aranacak", ex);
                    DetachSession();
                    Interlocked.Or(ref _pendingDirty, (int)Dirty.Sessions);
                    await Task.Delay(500, _cts.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Volatile.Write(ref _workerRunning, 0);
            if (!_disposed && Volatile.Read(ref _pendingDirty) != 0)
            {
                RequestRefresh(Dirty.None);
            }
        }
    }

    private async Task ProcessAsync(Dirty flags, CancellationToken ct)
    {
        if (_manager is null)
        {
            if (!await EnsureManagerAsync(ct).ConfigureAwait(false))
            {
                return;
            }

            flags = Dirty.All;
        }

        if (flags.HasFlag(Dirty.Sessions))
        {
            var found = FindSpotifySession(_manager!);
            if (!ReferenceEquals(found, _session))
            {
                DetachSession();
                if (found is not null)
                {
                    AttachSession(found);
                    Log.Info("Spotify medya oturumu bağlandı");
                }

                flags = Dirty.All;
            }
        }

        var session = _session;
        if (session is null)
        {
            _absentTimer.Change(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
            var availability = IsSpotifyProcessRunning() ? MediaAvailability.NoSession : MediaAvailability.SpotifyNotRunning;
            _lastTrackKey = null;
            Publish(new MediaSnapshot(availability, null, PlaybackStatus.Unknown, null, MediaCapabilities.None, null, false));
            return;
        }

        _absentTimer.Change(Timeout.Infinite, Timeout.Infinite);
        var current = Current;
        var track = current.Track;
        var artwork = current.Artwork;
        var status = current.Status;
        var caps = current.Capabilities;
        var timeline = current.Timeline;

        if (flags.HasFlag(Dirty.Properties) || current.Availability != MediaAvailability.Ready)
        {
            var props = await session.TryGetMediaPropertiesAsync().AsTask(ct).ConfigureAwait(false);
            track = ToTrack(props);
            if (track is null)
            {
                artwork = null;
                _lastTrackKey = null;
            }
            else if (track.Key != _lastTrackKey)
            {
                _lastTrackKey = track.Key;
                artwork = _artworkCache.Get(track.Key);
                if (artwork is null)
                {
                    artwork = await ReadArtworkAsync(track.Key, props?.Thumbnail, ct).ConfigureAwait(false);
                    if (artwork is not null)
                    {
                        _artworkCache.Put(artwork);
                    }
                }

                ScheduleArtworkRecheck(track.Key);
                flags |= Dirty.Timeline | Dirty.Playback;
            }
            else
            {
                // Aynı parça: yeniden denetimin önbelleğe koyduğu daha yeni kapağı al.
                artwork = _artworkCache.Get(track.Key) ?? (artwork?.TrackKey == track.Key ? artwork : null);
            }
        }

        if (flags.HasFlag(Dirty.Playback) || current.Availability != MediaAvailability.Ready)
        {
            var info = session.GetPlaybackInfo();
            status = ToStatus(info?.PlaybackStatus);
            caps = ToCapabilities(info?.Controls);
            flags |= Dirty.Timeline;
        }

        if (flags.HasFlag(Dirty.Timeline))
        {
            var rate = session.GetPlaybackInfo()?.PlaybackRate ?? 1.0;
            timeline = ToTimeline(session.GetTimelineProperties(), rate);
        }

        Publish(new MediaSnapshot(MediaAvailability.Ready, track, status, timeline, caps, track is null ? null : artwork, false));
    }

    private async Task<bool> EnsureManagerAsync(CancellationToken ct)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct).ConfigureAwait(false);
            manager.SessionsChanged += OnSessionsChanged;
            _manager = manager;
            _managerFailures = 0;
            Log.Info("GSMTC oturum yöneticisi hazır");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _managerFailures++;
            Log.Warn($"GSMTC yöneticisi alınamadı (deneme {_managerFailures})", ex);
            var delay = TimeSpan.FromSeconds(Math.Min(30, 2 * _managerFailures));
            _absentTimer.Change(delay, Timeout.InfiniteTimeSpan);
            Publish(new MediaSnapshot(
                IsSpotifyProcessRunning() ? MediaAvailability.NoSession : MediaAvailability.SpotifyNotRunning,
                null, PlaybackStatus.Unknown, null, MediaCapabilities.None, null, false));
            return false;
        }
    }

    private static GlobalSystemMediaTransportControlsSession? FindSpotifySession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        // Sadece "aktif" oturumu değil, tüm oturumları uygulama kimliğine göre tara:
        // tarayıcıda video açılması Spotify'ın yerini almaz.
        foreach (var session in manager.GetSessions())
        {
            string? id;
            try
            {
                id = session.SourceAppUserModelId;
            }
            catch (COMException)
            {
                continue;
            }

            if (SpotifyIdentity.IsSpotifyAppId(id))
            {
                return session;
            }
        }

        return null;
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        Volatile.Write(ref _session, session);
    }

    private void DetachSession()
    {
        var old = Interlocked.Exchange(ref _session, null);
        if (old is null)
        {
            return;
        }

        try
        {
            old.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            old.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            old.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        catch (Exception ex)
        {
            Log.Warn("Eski oturum olaylarından ayrılırken hata (yok sayıldı)", ex);
        }

        _artworkRecheckCts?.Cancel();
        Log.Info("Spotify medya oturumu ayrıldı");
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        => RequestRefresh(Dirty.Sessions);

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => RequestRefresh(Dirty.Properties | Dirty.Playback | Dirty.Timeline);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => RequestRefresh(Dirty.Playback | Dirty.Timeline);

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        => RequestRefresh(Dirty.Timeline);

    /// <summary>
    /// Spotify bazen yeni parçanın meta verisini kapaktan önce yayınlar ya da kısa süre eski kapağı döndürür.
    /// Parça değişiminden sonra iki kez yeniden okuyup farklıysa güncelleriz. Hızlı geçişlerde önceki kontrol iptal edilir.
    /// </summary>
    private void ScheduleArtworkRecheck(string trackKey)
    {
        _artworkRecheckCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _artworkRecheckCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var delay in new[] { 900, 2500 })
                {
                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                    var session = Volatile.Read(ref _session);
                    if (session is null)
                    {
                        return;
                    }

                    var props = await session.TryGetMediaPropertiesAsync().AsTask(cts.Token).ConfigureAwait(false);
                    if (ToTrack(props)?.Key != trackKey)
                    {
                        return;
                    }

                    var fresh = await ReadArtworkAsync(trackKey, props?.Thumbnail, cts.Token).ConfigureAwait(false);
                    if (fresh is null)
                    {
                        continue;
                    }

                    var cached = _artworkCache.Get(trackKey);
                    if (cached?.Hash != fresh.Hash)
                    {
                        _artworkCache.Put(fresh);
                        RequestRefresh(Dirty.Properties);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Warn("Kapak yeniden denetimi başarısız (yok sayıldı)", ex);
            }
        });
    }

    private static async Task<MediaArtwork?> ReadArtworkAsync(string trackKey, IRandomAccessStreamReference? reference, CancellationToken ct)
    {
        if (reference is null)
        {
            return null;
        }

        try
        {
            using var stream = await reference.OpenReadAsync().AsTask(ct).ConfigureAwait(false);
            var size = stream.Size;
            if (size == 0 || size > MaxArtworkBytes)
            {
                return null;
            }

            using var reader = new DataReader(stream);
            var loaded = await reader.LoadAsync((uint)size).AsTask(ct).ConfigureAwait(false);
            var bytes = new byte[loaded];
            reader.ReadBytes(bytes);
            return bytes.Length == 0 ? null : MediaArtwork.Create(trackKey, bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn("Albüm kapağı okunamadı (kapaksız gösterilecek)", ex);
            return null;
        }
    }

    private static TrackInfo? ToTrack(GlobalSystemMediaTransportControlsSessionMediaProperties? props)
    {
        if (props is null)
        {
            return null;
        }

        var title = props.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            return null;
        }

        var artist = props.Artist?.Trim();
        if (string.IsNullOrEmpty(artist))
        {
            artist = props.AlbumArtist?.Trim() ?? string.Empty;
        }

        return new TrackInfo(title, artist, props.AlbumTitle?.Trim() ?? string.Empty);
    }

    private static PlaybackStatus ToStatus(GsmtcStatus? status) => status switch
    {
        GsmtcStatus.Playing => PlaybackStatus.Playing,
        GsmtcStatus.Paused => PlaybackStatus.Paused,
        GsmtcStatus.Stopped or GsmtcStatus.Closed => PlaybackStatus.Stopped,
        GsmtcStatus.Changing or GsmtcStatus.Opened => PlaybackStatus.Changing,
        _ => PlaybackStatus.Unknown,
    };

    private static MediaCapabilities ToCapabilities(GlobalSystemMediaTransportControlsSessionPlaybackControls? c) =>
        c is null
            ? MediaCapabilities.None
            : new MediaCapabilities(
                c.IsPlayEnabled,
                c.IsPauseEnabled,
                c.IsPlayPauseToggleEnabled,
                c.IsNextEnabled,
                c.IsPreviousEnabled,
                c.IsPlaybackPositionEnabled);

    internal static TimelineInfo? ToTimeline(GlobalSystemMediaTransportControlsSessionTimelineProperties? t, double rate)
    {
        if (t is null)
        {
            return null;
        }

        var duration = t.EndTime - t.StartTime;
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24))
        {
            return null;
        }

        var position = t.Position - t.StartTime;
        var now = DateTimeOffset.UtcNow;
        var sampled = t.LastUpdatedTime;
        // Bazı oturumlar geçersiz (1601) ya da gelecekteki zaman damgası bildirir; o durumda okuma anını kullan.
        if (sampled > now || now - sampled > TimeSpan.FromHours(6))
        {
            sampled = now;
        }

        return new TimelineInfo(position, duration, sampled, rate);
    }

    private static bool IsSpotifyProcessRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(SpotifyIdentity.ProcessName);
            var running = processes.Length > 0;
            foreach (var p in processes)
            {
                p.Dispose();
            }

            return running;
        }
        catch
        {
            return false;
        }
    }

    private void Publish(MediaSnapshot snapshot)
    {
        var previous = Current;
        if (previous.IsEquivalentTo(snapshot))
        {
            // Konum örneğini yine de tazele (yerel ilerletme hassas kalsın), ama olay üretme.
            Volatile.Write(ref _current, snapshot);
            return;
        }

        Volatile.Write(ref _current, snapshot);
        if (previous.Availability != snapshot.Availability)
        {
            Log.Info($"Medya durumu: {previous.Availability} -> {snapshot.Availability}");
        }

        try
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            Log.Error("SnapshotChanged işleyicisi hata verdi", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _cts.Cancel();
        _artworkRecheckCts?.Cancel();
        _absentTimer.Dispose();
        _watchdogTimer.Dispose();
        _pollTimer?.Dispose();
        DetachSession();
        if (_manager is { } manager)
        {
            try
            {
                manager.SessionsChanged -= OnSessionsChanged;
            }
            catch
            {
            }

            _manager = null;
        }

        _artworkCache.Clear();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
