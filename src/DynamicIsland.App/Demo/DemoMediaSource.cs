using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Core.Lyrics;
using DynamicIsland.Core.Media;

namespace DynamicIsland.App.Demo;

/// <summary>
/// Yalnızca görsel geliştirme için sahte medya kaynağı (<c>--demo</c>). Gerçek Spotify bağlantısı bu modda
/// hiç oluşturulmaz; anlık görüntüler <see cref="MediaSnapshot.IsDemo"/> ile işaretlenir ve adada "DEMO" rozeti görünür.
/// </summary>
public sealed class DemoMediaSource : IMediaSource
{
    private sealed record DemoTrack(TrackInfo Info, TimeSpan Duration, Color? ArtA, Color? ArtB);

    private static readonly DemoTrack[] Tracks =
    [
        new(new TrackInfo("Gece Yürüyüşü", "Örnek Sanatçı", "Demo Albüm"), TimeSpan.FromSeconds(214), Color.FromRgb(0x5B, 0x2E, 0xFF), Color.FromRgb(0xFF, 0x5E, 0x8A)),
        new(new TrackInfo("Bu, yerleşimi bozmaması gereken çok ama çok uzun bir şarkı adı (Genişletilmiş Versiyon)", "Uzun İsimli Bir Sanatçı ve Arkadaşları", "Upuzun Albüm Adı"), TimeSpan.FromSeconds(389), Color.FromRgb(0x0F, 0x9B, 0x8E), Color.FromRgb(0xF2, 0xD0, 0x6B)),
        new(new TrackInfo("Kapaksız Parça", "Bilinmeyen", ""), TimeSpan.FromSeconds(158), null, null),
        new(new TrackInfo("Sabah Kahvesi", "Demo Grup", "Akustik"), TimeSpan.FromSeconds(181), Color.FromRgb(0xF0, 0x8A, 0x24), Color.FromRgb(0x6B, 0x2E, 0x1A)),
    ];

    private readonly Dictionary<int, MediaArtwork?> _art = new();
    private readonly object _gate = new();
    private Timer? _timer;
    private int _index;
    private bool _playing = true;
    private TimeSpan _position;
    private DateTimeOffset _positionAt = DateTimeOffset.UtcNow;
    private MediaSnapshot _current = MediaSnapshot.Initial(isDemo: true);

    public event EventHandler<MediaSnapshot>? SnapshotChanged;

    public MediaSnapshot Current => Volatile.Read(ref _current);

    public bool IsDemo => true;

    /// <summary>UI iş parçacığında çağrılmalı (kapaklar WPF ile çizilir).</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < Tracks.Length; i++)
        {
            var t = Tracks[i];
            _art[i] = t.ArtA is { } a && t.ArtB is { } b ? MediaArtwork.Create(t.Info.Key, RenderArt(a, b, i)) : null;
        }

        Publish();
        // Şarkı değişimi animasyonunu görmek için 14 sn'de bir sonraki parçaya geç.
        _timer = new Timer(_ => Advance(1), null, TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(14));
        return Task.CompletedTask;
    }

    public Task<bool> TogglePlayPauseAsync()
    {
        lock (_gate)
        {
            _position = CurrentPosition();
            _positionAt = DateTimeOffset.UtcNow;
            _playing = !_playing;
        }

        Publish();
        return Task.FromResult(true);
    }

    public Task<bool> NextAsync()
    {
        Advance(1);
        return Task.FromResult(true);
    }

    public Task<bool> PreviousAsync()
    {
        Advance(-1);
        return Task.FromResult(true);
    }

    public Task<bool> SeekAsync(TimeSpan position)
    {
        lock (_gate)
        {
            _position = position;
            _positionAt = DateTimeOffset.UtcNow;
        }

        Publish();
        return Task.FromResult(true);
    }

    public void RequestResync() => Publish();

    /// <summary>Teşhis/görüntü dökümü: belirli parçayı, durumu ve konumu seç.</summary>
    internal void Select(int index, bool playing, double progress)
    {
        lock (_gate)
        {
            _index = ((index % Tracks.Length) + Tracks.Length) % Tracks.Length;
            _playing = playing;
            _position = TimeSpan.FromSeconds(Tracks[_index].Duration.TotalSeconds * Math.Clamp(progress, 0, 1));
            _positionAt = DateTimeOffset.UtcNow;
        }

        Publish();
    }

    /// <summary>Demo parçalar için uydurma, zamanlanmış sözler (ağa hiç gidilmez). Kapaksız parçada söz yok.</summary>
    internal static TrackLyrics? DemoLyrics(TrackInfo track)
    {
        string[] lines = track.Title switch
        {
            "Gece Yürüyüşü" => ["Sokak lambaları tek tek yanarken", "", "Adımlarım şehrin ritmine karışır", "Kimse bilmez nereye gittiğimi", "Ben de bilmem, ama yürürüm yine", "Gece uzun, yol daha da uzun"],
            "Sabah Kahvesi" => ["Pencereden süzülen ilk ışık", "Fincanda dönen küçük bir girdap", "Bugün de güzel olacak belki"],
            "Kapaksız Parça" => [],
            _ => ["Bu satır, küçük adada sığmayacak kadar uzun yazılmış ve üç noktayla kesilmesi gereken bir söz satırıdır", "Kısa satır", "", "Ve bir tane daha, geniş görünümde iki satıra kaydırılması gereken oldukça uzun bir söz dizesi"],
        };

        if (lines.Length == 0)
        {
            return null;
        }

        // Her satır 7 sn; parçanın tamamını dolduracak şekilde döngüyle tekrarla.
        var result = new List<LyricLine>();
        for (var t = 3; t < 400; t += 7)
        {
            result.Add(new LyricLine(TimeSpan.FromSeconds(t), lines[result.Count % lines.Length]));
        }

        return new TrackLyrics(result);
    }

    private void Advance(int delta)
    {
        lock (_gate)
        {
            _index = (_index + delta + Tracks.Length) % Tracks.Length;
            _position = TimeSpan.Zero;
            _positionAt = DateTimeOffset.UtcNow;
        }

        Publish();
    }

    private TimeSpan CurrentPosition() =>
        _playing ? _position + (DateTimeOffset.UtcNow - _positionAt) : _position;

    private void Publish()
    {
        MediaSnapshot snapshot;
        lock (_gate)
        {
            var t = Tracks[_index];
            snapshot = new MediaSnapshot(
                MediaAvailability.Ready,
                t.Info,
                _playing ? PlaybackStatus.Playing : PlaybackStatus.Paused,
                new TimelineInfo(_position, t.Duration, _positionAt, 1.0),
                new MediaCapabilities(true, true, true, true, true, true),
                _art.GetValueOrDefault(_index),
                IsDemo: true);
        }

        Volatile.Write(ref _current, snapshot);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static byte[] RenderArt(Color a, Color b, int seed)
    {
        const int size = 300;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(a, b, 45), null, new Rect(0, 0, size, size));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), null, new Point(size * (0.3 + 0.1 * seed), size * 0.62), 70, 70);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)), null, new Point(size * 0.72, size * 0.3), 46, 46);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
