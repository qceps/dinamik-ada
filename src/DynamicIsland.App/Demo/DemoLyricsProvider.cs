using DynamicIsland.Core.Lyrics;
using DynamicIsland.Core.Media;

namespace DynamicIsland.App.Demo;

/// <summary>Demo modunda söz kaynağı: ağa gitmez, uydurma satırları kısa bir gecikmeyle döndürür.</summary>
public sealed class DemoLyricsProvider : ILyricsProvider
{
    public async Task<LyricsResult> GetAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        var lyrics = DemoMediaSource.DemoLyrics(new TrackInfo(query.Title, query.Artist, query.Album));
        return lyrics is null ? LyricsResult.NotFound : new LyricsResult(LyricsStatus.Found, lyrics);
    }
}
