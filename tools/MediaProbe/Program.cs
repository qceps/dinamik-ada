using DynamicIsland.Core.Media;
using Windows.Media.Control;

// Kullanım:
//   MediaProbe sessions            -> tüm GSMTC oturumlarını ve Spotify eşleşmesini listeler
//   MediaProbe watch [saniye]      -> Core katmanının yayınladığı anlık görüntüleri yazar
//   MediaProbe next|prev|playpause -> Spotify oturumuna komut gönderir
// Not: Bu araç konsola parça adı yazar; uygulamanın kendisi günlüğe medya bilgisi YAZMAZ.

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "sessions";
// Uygulamanın günlüğüyle karışmasın.
DynamicIsland.Core.Diagnostics.Log.Configure(Path.Combine(Path.GetTempPath(), "DinamikAda-probe-logs"));
Console.OutputEncoding = System.Text.Encoding.UTF8;

switch (command)
{
    case "sessions":
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var current = manager.GetCurrentSession();
        Console.WriteLine($"Aktif (sistem) oturum: {current?.SourceAppUserModelId ?? "(yok)"}");
        foreach (var s in manager.GetSessions())
        {
            var info = s.GetPlaybackInfo();
            var props = await s.TryGetMediaPropertiesAsync();
            var tl = s.GetTimelineProperties();
            var c = info.Controls;
            Console.WriteLine($"- AUMID='{s.SourceAppUserModelId}' spotify={SpotifyIdentity.IsSpotifyAppId(s.SourceAppUserModelId)} status={info.PlaybackStatus}");
            Console.WriteLine($"    title='{props.Title}' artist='{props.Artist}' album='{props.AlbumTitle}' thumb={(props.Thumbnail is null ? "yok" : "var")}");
            Console.WriteLine($"    timeline start={tl.StartTime} end={tl.EndTime} pos={tl.Position} updated={tl.LastUpdatedTime:O}");
            Console.WriteLine($"    controls play={c.IsPlayEnabled} pause={c.IsPauseEnabled} toggle={c.IsPlayPauseToggleEnabled} next={c.IsNextEnabled} prev={c.IsPreviousEnabled} seek={c.IsPlaybackPositionEnabled}");
        }

        break;
    }

    case "watch":
    {
        var seconds = args.Length > 1 && int.TryParse(args[1], out var s0) ? s0 : 20;
        await using var source = new SpotifyMediaSource(usePolling: args.Contains("--poll"));
        source.SnapshotChanged += (_, s) =>
        {
            var pos = s.Timeline?.PositionAt(DateTimeOffset.UtcNow, s.IsPlaying);
            Console.WriteLine(
                $"{DateTime.Now:HH:mm:ss.fff} avail={s.Availability} status={s.Status} track='{s.Track?.Title}' / '{s.Track?.Artist}' " +
                $"art={(s.Artwork is null ? "yok" : s.Artwork.Data.Length + "B#" + s.Artwork.Hash.ToString("X")[..6])} " +
                $"pos={pos:mm\\:ss}/{s.Timeline?.Duration:mm\\:ss} caps(next={s.Capabilities.CanNext},prev={s.Capabilities.CanPrevious},seek={s.Capabilities.CanSeek})");
        };
        await source.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        break;
    }

    case "next":
    case "prev":
    case "playpause":
    case "seek":
    {
        await using var source = new SpotifyMediaSource();
        await source.StartAsync();
        await Task.Delay(400);
        var ok = command switch
        {
            "next" => await source.NextAsync(),
            "prev" => await source.PreviousAsync(),
            "seek" => await source.SeekAsync(TimeSpan.FromSeconds(args.Length > 1 ? double.Parse(args[1]) : 30)),
            _ => await source.TogglePlayPauseAsync(),
        };
        Console.WriteLine($"{command}: {(ok ? "OK" : "BAŞARISIZ/DESTEKLENMİYOR")}");
        break;
    }

    case "volume":
    {
        // MediaProbe volume [spotify-seviye-0..1] [saniye]: Spotify uygulama sesini ve sistem sesini okur,
        // seviye verilirse Spotify'ı ayarlar; sonra verilen süre boyunca sistem sesi değişimlerini yazar.
        Console.WriteLine("Ses oturumları: " + string.Join(", ", await Task.Run(DynamicIsland.Interop.AppVolume.ListSessionProcesses)));
        Console.WriteLine($"Spotify (önce): {Describe(await Task.Run(() => DynamicIsland.Interop.AppVolume.Get("Spotify")))}");
        if (args.Length > 1 && float.TryParse(args[1], System.Globalization.CultureInfo.InvariantCulture, out var target))
        {
            var ok = await Task.Run(() => DynamicIsland.Interop.AppVolume.Set("Spotify", target, null));
            Console.WriteLine($"Spotify ayarlandı: {ok} -> {Describe(await Task.Run(() => DynamicIsland.Interop.AppVolume.Get("Spotify")))}");
        }

        using var watcher = await DynamicIsland.Interop.SystemVolumeWatcher.StartAsync();
        Console.WriteLine($"Sistem: {Describe(watcher?.Current)}");
        if (watcher is not null)
        {
            watcher.Changed += s => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] sistem değişti: {Describe(s)}");
        }

        await Task.Delay(TimeSpan.FromSeconds(args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0));
        break;

        static string Describe(DynamicIsland.Interop.VolumeState? s) => s is { } v ? $"%{v.Level * 100:0}{(v.Muted ? " (sessiz)" : "")}" : "oturum yok";
    }

    case "events":
    {
        // Ham GSMTC olay sıklığını ölçer (Spotify oturumu).
        var seconds = args.Length > 1 && int.TryParse(args[1], out var s1) ? s1 : 10;
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var spotify = manager.GetSessions().FirstOrDefault(x => SpotifyIdentity.IsSpotifyAppId(x.SourceAppUserModelId));
        int props = 0, playback = 0, timeline = 0, sessions = 0;
        manager.SessionsChanged += (_, _) => Interlocked.Increment(ref sessions);
        if (spotify is not null)
        {
            spotify.MediaPropertiesChanged += (_, _) => Interlocked.Increment(ref props);
            spotify.PlaybackInfoChanged += (_, _) => Interlocked.Increment(ref playback);
            spotify.TimelinePropertiesChanged += (_, _) => Interlocked.Increment(ref timeline);
        }

        await Task.Delay(TimeSpan.FromSeconds(seconds));
        Console.WriteLine($"{seconds} sn: props={props} playback={playback} timeline={timeline} sessions={sessions} (spotify={(spotify is null ? "yok" : spotify.GetPlaybackInfo().PlaybackStatus.ToString())})");
        break;
    }

    default:
        Console.WriteLine("Bilinmeyen komut.");
        break;
}
