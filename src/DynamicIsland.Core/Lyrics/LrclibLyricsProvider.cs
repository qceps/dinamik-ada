using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DynamicIsland.Core.Lyrics;

/// <summary>
/// LRCLIB (https://lrclib.net) üzerinden zamanlanmış sözler. API anahtarı gerektirmez.
/// Gönderilen tek bilgi çalan parçanın adı, sanatçısı, albümü ve süresidir.
/// Sıra: tam eşleşme (<c>/api/get</c>) → sadeleştirilmiş başlıkla arama (<c>/api/search</c>).
/// </summary>
public sealed partial class LrclibLyricsProvider : ILyricsProvider, IDisposable
{
    private static readonly Uri BaseUri = new("https://lrclib.net/");
    private readonly HttpClient _http;
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;

    public LrclibLyricsProvider(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = BaseUri;
        _http.Timeout = TimeSpan.FromSeconds(10);
        var version = typeof(LrclibLyricsProvider).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"DinamikAda/{version} (Windows masaustu Spotify adasi)");
    }

    public async Task<LyricsResult> GetAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _blockedUntil)
        {
            return LyricsResult.Unavailable;
        }

        // 1) Tam eşleşme
        var parameters = new List<(string, string)> { ("track_name", query.Title), ("artist_name", query.Artist) };
        if (!string.IsNullOrWhiteSpace(query.Album))
        {
            parameters.Add(("album_name", query.Album));
        }

        if (query.Duration is { } d && d.TotalSeconds is >= 1 and <= 3600)
        {
            parameters.Add(("duration", ((int)Math.Round(d.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var (status, record) = await SendAsync<LrclibRecord>("api/get", parameters, cancellationToken).ConfigureAwait(false);
        if (status == HttpStatusCode.TooManyRequests || status == 0)
        {
            return LyricsResult.Unavailable;
        }

        if (record is not null && ToResult(record) is { Status: LyricsStatus.Found or LyricsStatus.Instrumental } exact)
        {
            return exact;
        }

        // 2) Arama: "- Remastered 2011", "(feat. …)" gibi ekler atılmış başlık + ilk sanatçı
        var title = CleanTitle(query.Title);
        var artist = PrimaryArtist(query.Artist);
        var search = new List<(string, string)> { ("track_name", title) };
        if (!string.IsNullOrWhiteSpace(artist))
        {
            search.Add(("artist_name", artist));
        }

        var (searchStatus, results) = await SendAsync<LrclibRecord[]>("api/search", search, cancellationToken).ConfigureAwait(false);
        if (searchStatus == HttpStatusCode.TooManyRequests || searchStatus == 0)
        {
            return LyricsResult.Unavailable;
        }

        var best = PickBest(results ?? [], title, query.Duration);
        return best is null ? LyricsResult.NotFound : ToResult(best);
    }

    internal static LrclibRecord? PickBest(IEnumerable<LrclibRecord> records, string title, TimeSpan? duration)
    {
        var candidates = records.Where(r => r.Instrumental || !string.IsNullOrWhiteSpace(r.SyncedLyrics)).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        double Score(LrclibRecord r)
        {
            var score = 0.0;
            if (duration is { } d && r.Duration is { } rd)
            {
                var diff = Math.Abs(rd - d.TotalSeconds);
                if (diff > 8)
                {
                    return double.NegativeInfinity; // başka bir sürüm/kayıt; yanlış zamanlama göstermektense hiç gösterme
                }

                score -= diff;
            }

            if (string.Equals(CleanTitle(r.TrackName ?? string.Empty), title, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }

            if (!string.IsNullOrWhiteSpace(r.SyncedLyrics))
            {
                score += 2;
            }

            return score;
        }

        var best = candidates.Select(r => (r, s: Score(r))).Where(x => !double.IsNegativeInfinity(x.s)).OrderByDescending(x => x.s).FirstOrDefault();
        return best.r;
    }

    internal static LyricsResult ToResult(LrclibRecord record)
    {
        if (record.Instrumental)
        {
            return LyricsResult.Instrumental;
        }

        var parsed = LrcParser.Parse(record.SyncedLyrics);
        return parsed is null ? LyricsResult.NotFound : new LyricsResult(LyricsStatus.Found, parsed);
    }

    [GeneratedRegex(@"\s+-\s+.*$")]
    private static partial Regex DashSuffix();

    [GeneratedRegex(@"\s*[\(\[](?:feat\.?|ft\.?|with|from|remaster(?:ed)?|live|radio edit|sped up|slowed)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Decorations();

    internal static string CleanTitle(string title)
    {
        var cleaned = Decorations().Replace(DashSuffix().Replace(title, string.Empty), string.Empty).Trim();
        return cleaned.Length == 0 ? title.Trim() : cleaned;
    }

    internal static string PrimaryArtist(string artist)
    {
        foreach (var separator in new[] { ", ", " & ", " feat. ", " ft. ", " x ", "; " })
        {
            var index = artist.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                artist = artist[..index];
            }
        }

        return artist.Trim();
    }

    private async Task<(HttpStatusCode Status, T? Value)> SendAsync<T>(string path, List<(string Key, string Value)> parameters, CancellationToken cancellationToken)
        where T : class
    {
        var url = path + "?" + string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
                _blockedUntil = DateTimeOffset.UtcNow + wait + TimeSpan.FromSeconds(Random.Shared.Next(1, 10));
                return (response.StatusCode, null);
            }

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            {
                return (response.StatusCode, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return (response.StatusCode, value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Diagnostics.Log.Info($"Söz servisine ulaşılamadı ({ex.GetType().Name})");
            return (0, null);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public void Dispose() => _http.Dispose();
}

internal sealed class LrclibRecord
{
    [JsonPropertyName("trackName")]
    public string? TrackName { get; set; }

    [JsonPropertyName("artistName")]
    public string? ArtistName { get; set; }

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    [JsonPropertyName("instrumental")]
    public bool Instrumental { get; set; }

    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; set; }
}
