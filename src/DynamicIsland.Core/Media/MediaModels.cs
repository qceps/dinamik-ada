namespace DynamicIsland.Core.Media;

/// <summary>Spotify bağlantısının genel durumu. Duraklatma bu düzeyde değil, <see cref="PlaybackStatus"/> içinde ifade edilir.</summary>
public enum MediaAvailability
{
    /// <summary>Henüz ilk okuma yapılmadı.</summary>
    Unknown,
    /// <summary>Spotify işlemi çalışmıyor.</summary>
    SpotifyNotRunning,
    /// <summary>Spotify çalışıyor ama Windows medya oturumu yok (ör. hiç çalma başlatılmadı).</summary>
    NoSession,
    /// <summary>Spotify medya oturumu bulundu.</summary>
    Ready,
}

public enum PlaybackStatus
{
    Unknown,
    Stopped,
    Changing,
    Playing,
    Paused,
}

public sealed record TrackInfo(string Title, string Artist, string Album)
{
    /// <summary>Parça kimliği (önbellek ve değişim tespiti için). Günlüğe yazılmaz.</summary>
    public string Key => string.Concat(Title, "\u001F", Artist, "\u001F", Album);
}

public sealed record MediaCapabilities(
    bool CanPlay,
    bool CanPause,
    bool CanTogglePlayPause,
    bool CanNext,
    bool CanPrevious,
    bool CanSeek)
{
    public static readonly MediaCapabilities None = new(false, false, false, false, false, false);

    public bool CanPlayOrPause => CanPlay || CanPause || CanTogglePlayPause;
}

/// <summary>
/// Oturumun bildirdiği zaman çizelgesi. Konum, örneklendiği andan itibaren
/// yerel olarak ilerletilir; böylece sürekli sorgulama gerekmez.
/// </summary>
public sealed record TimelineInfo(TimeSpan Position, TimeSpan Duration, DateTimeOffset SampledAt, double Rate)
{
    public TimeSpan PositionAt(DateTimeOffset now, bool playing)
    {
        var position = Position;
        if (playing)
        {
            var elapsed = now - SampledAt;
            if (elapsed > TimeSpan.Zero)
            {
                position += TimeSpan.FromTicks((long)(elapsed.Ticks * (Rate <= 0 ? 1.0 : Rate)));
            }
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        if (Duration > TimeSpan.Zero && position > Duration)
        {
            position = Duration;
        }

        return position;
    }
}

/// <summary>Kodlanmış (PNG/JPEG) albüm kapağı baytları.</summary>
public sealed class MediaArtwork
{
    private MediaArtwork(string trackKey, byte[] data, ulong hash)
    {
        TrackKey = trackKey;
        Data = data;
        Hash = hash;
    }

    public string TrackKey { get; }
    public byte[] Data { get; }
    public ulong Hash { get; }

    public static MediaArtwork Create(string trackKey, byte[] data) => new(trackKey, data, ComputeHash(data));

    internal static ulong ComputeHash(ReadOnlySpan<byte> data)
    {
        // FNV-1a 64: yalnızca "aynı görsel mi" karşılaştırması için.
        ulong hash = 14695981039346656037UL;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash ^ (ulong)data.Length;
    }
}

public sealed record MediaSnapshot(
    MediaAvailability Availability,
    TrackInfo? Track,
    PlaybackStatus Status,
    TimelineInfo? Timeline,
    MediaCapabilities Capabilities,
    MediaArtwork? Artwork,
    bool IsDemo)
{
    public static MediaSnapshot Initial(bool isDemo = false) =>
        new(MediaAvailability.Unknown, null, PlaybackStatus.Unknown, null, MediaCapabilities.None, null, isDemo);

    public bool HasTrack => Availability == MediaAvailability.Ready && Track is not null;

    public bool IsPlaying => Status == PlaybackStatus.Playing;

    /// <summary>Arayüzü etkileyen farklılık var mı? (Zaman örneği küçük kaymalarda yeni olay üretmez.)</summary>
    public bool IsEquivalentTo(MediaSnapshot other)
    {
        if (Availability != other.Availability || Status != other.Status || IsDemo != other.IsDemo)
        {
            return false;
        }

        if (Track != other.Track || Capabilities != other.Capabilities)
        {
            return false;
        }

        if ((Artwork?.Hash) != (other.Artwork?.Hash) || (Artwork?.TrackKey) != (other.Artwork?.TrackKey))
        {
            return false;
        }

        if (Timeline is null || other.Timeline is null)
        {
            return Timeline is null && other.Timeline is null;
        }

        if (Timeline.Duration != other.Timeline.Duration)
        {
            return false;
        }

        // Aynı anda değerlendirilen konumlar 1 sn'den fazla ayrışıyorsa (seek vb.) yeni olay.
        var now = DateTimeOffset.UtcNow;
        var a = Timeline.PositionAt(now, IsPlaying);
        var b = other.Timeline.PositionAt(now, other.IsPlaying);
        return Math.Abs((a - b).TotalSeconds) < 1.0;
    }
}
