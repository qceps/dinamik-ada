using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Media;

namespace DynamicIsland.Core.Lyrics;

public enum LyricsStatus
{
    Found,
    NotFound,
    Instrumental,
    /// <summary>Ağ hatası / hız sınırı: kısa süre sonra yeniden denenebilir.</summary>
    Unavailable,
}

public sealed record LyricsResult(LyricsStatus Status, TrackLyrics? Lyrics)
{
    public static readonly LyricsResult NotFound = new(LyricsStatus.NotFound, null);
    public static readonly LyricsResult Instrumental = new(LyricsStatus.Instrumental, null);
    public static readonly LyricsResult Unavailable = new(LyricsStatus.Unavailable, null);
}

public sealed record LyricsQuery(string Title, string Artist, string Album, TimeSpan? Duration)
{
    public static LyricsQuery From(TrackInfo track, TimeSpan? duration) => new(track.Title, track.Artist, track.Album, duration);

    /// <summary>Önbellek anahtarı (süre dahil değil: aynı parça farklı süreyle sorulsa da tek kayıt).</summary>
    public string Key => string.Concat(Title, "\u001F", Artist, "\u001F", Album);
}

public interface ILyricsProvider
{
    Task<LyricsResult> GetAsync(LyricsQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Sağlayıcının önüne konan küçük LRU önbellek: aynı parçaya dönüldüğünde ağa yeniden gidilmez.
/// "Bulunamadı" da önbelleğe alınır; geçici hatalar yalnızca kısa süre hatırlanır. Günlüğe parça bilgisi yazılmaz.
/// </summary>
public sealed class LyricsService
{
    private const int Capacity = 48;
    private static readonly TimeSpan UnavailableRetry = TimeSpan.FromSeconds(60);

    private readonly ILyricsProvider _provider;
    private readonly Func<DateTimeOffset> _clock;
    private readonly LinkedList<(string Key, LyricsResult Result, DateTimeOffset At)> _lru = new();
    private readonly object _gate = new();

    public LyricsService(ILyricsProvider provider, Func<DateTimeOffset>? clock = null)
    {
        _provider = provider;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool TryGetCached(LyricsQuery query, out LyricsResult result)
    {
        lock (_gate)
        {
            for (var node = _lru.First; node is not null; node = node.Next)
            {
                if (node.Value.Key != query.Key)
                {
                    continue;
                }

                if (node.Value.Result.Status == LyricsStatus.Unavailable && _clock() - node.Value.At > UnavailableRetry)
                {
                    _lru.Remove(node);
                    break;
                }

                _lru.Remove(node);
                _lru.AddFirst(node);
                result = node.Value.Result;
                return true;
            }
        }

        result = LyricsResult.NotFound;
        return false;
    }

    public async Task<LyricsResult> GetAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return LyricsResult.NotFound;
        }

        if (TryGetCached(query, out var cached))
        {
            return cached;
        }

        LyricsResult result;
        try
        {
            result = await _provider.GetAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn("Şarkı sözü alınamadı", ex);
            result = LyricsResult.Unavailable;
        }

        Store(query.Key, result);
        Log.Trace($"lyrics {result.Status} ({result.Lyrics?.Lines.Count ?? 0} satır)");
        return result;
    }

    private void Store(string key, LyricsResult result)
    {
        lock (_gate)
        {
            for (var node = _lru.First; node is not null; node = node.Next)
            {
                if (node.Value.Key == key)
                {
                    _lru.Remove(node);
                    break;
                }
            }

            _lru.AddFirst((key, result, _clock()));
            while (_lru.Count > Capacity)
            {
                _lru.RemoveLast();
            }
        }
    }
}
