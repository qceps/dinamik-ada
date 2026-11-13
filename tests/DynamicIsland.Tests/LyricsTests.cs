using System.Net;
using System.Net.Http;
using System.Text;
using DynamicIsland.Core.Lyrics;

namespace DynamicIsland.Tests;

public class LrcParserTests
{
    [Fact]
    public void Parses_timestamps_metadata_and_multiple_stamps()
    {
        var lrc = "[ar:Sanatçı]\n[ti:Başlık]\n[00:12.50]İlk satır\r\n[00:05.1][01:00.123]Tekrar eden\n[00:20.00]\nmetin damgasız";
        var lyrics = LrcParser.Parse(lrc)!;

        Assert.Equal(4, lyrics.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(5.1), lyrics.Lines[0].Time);
        Assert.Equal("Tekrar eden", lyrics.Lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(12.5), lyrics.Lines[1].Time);
        Assert.True(lyrics.Lines[2].IsGap);
        Assert.Equal(TimeSpan.FromSeconds(60.123), lyrics.Lines[3].Time);
    }

    [Fact]
    public void Applies_offset_and_strips_word_tags()
    {
        var lyrics = LrcParser.Parse("[offset:+500]\n[00:10.00]<00:10.00>Ke<00:10.40>lime")!;
        Assert.Equal(TimeSpan.FromSeconds(9.5), lyrics.Lines[0].Time);
        Assert.Equal("Kelime", lyrics.Lines[0].Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("düz metin, damga yok")]
    [InlineData("[00:01.00]\n[00:02.00]  ")]
    public void Returns_null_for_unusable_input(string? lrc) => Assert.Null(LrcParser.Parse(lrc));
}

public class TrackLyricsTests
{
    private static readonly TrackLyrics Lyrics = new([
        new LyricLine(TimeSpan.FromSeconds(10), "c"),
        new LyricLine(TimeSpan.FromSeconds(2), "a"),
        new LyricLine(TimeSpan.FromSeconds(5), "b"),
    ]);

    [Theory]
    [InlineData(0, -1)]
    [InlineData(1.99, -1)]
    [InlineData(2, 0)]
    [InlineData(4.9, 0)]
    [InlineData(5, 1)]
    [InlineData(99, 2)]
    public void Index_at_position(double seconds, int expected) =>
        Assert.Equal(expected, Lyrics.IndexAt(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Next_change_after()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), Lyrics.NextChangeAfter(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(10), Lyrics.NextChangeAfter(TimeSpan.FromSeconds(6)));
        Assert.Null(Lyrics.NextChangeAfter(TimeSpan.FromSeconds(10)));
    }
}

public class LyricSplitterTests
{
    // Basit ölçü: her karakter 1 birim.
    private static double Measure(string s) => s.Length;

    [Fact]
    public void Short_lines_and_gaps_are_untouched()
    {
        var source = new TrackLyrics([new LyricLine(TimeSpan.FromSeconds(1), "kısa"), new LyricLine(TimeSpan.FromSeconds(3), "")]);
        var split = LyricSplitter.Split(source, Measure, 20);
        Assert.Equal(source.Lines, split.Lines);
    }

    [Fact]
    public void Prefers_punctuation_boundaries()
    {
        var chunks = LyricSplitter.Chunk("Benim yanık kafam, deli düşüncelerdeyim deli deli", Measure, 32);
        Assert.Equal(["Benim yanık kafam,", "deli düşüncelerdeyim deli deli"], chunks);
    }

    [Fact]
    public void Balances_words_when_no_punctuation_fits()
    {
        var text = "bir iki üç dört beş altı yedi sekiz dokuz on";
        var chunks = LyricSplitter.Chunk(text, Measure, 25);
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.True(c.Length <= 25));
        Assert.Equal(text, string.Join(' ', chunks));
        Assert.True(Math.Abs(chunks[0].Length - chunks[1].Length) <= 6, "parçalar dengeli olmalı");
    }

    [Fact]
    public void Chunk_times_are_proportional_and_before_next_line()
    {
        var source = new TrackLyrics([
            new LyricLine(TimeSpan.FromSeconds(10), "aaaa bbbb cccc dddd"),
            new LyricLine(TimeSpan.FromSeconds(12), "sonraki"),
        ]);
        var split = LyricSplitter.Split(source, Measure, 10);

        Assert.Equal(3, split.Lines.Count);
        Assert.Equal("aaaa bbbb", split.Lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(10), split.Lines[0].Time);
        Assert.Equal("cccc dddd", split.Lines[1].Text);
        Assert.Equal(11.0, split.Lines[1].Time.TotalSeconds, 2); // 2 sn'lik satırın yarısı
        Assert.Equal("sonraki", split.Lines[2].Text);
    }

    [Fact]
    public void Long_pause_after_line_uses_singing_estimate()
    {
        var source = new TrackLyrics([
            new LyricLine(TimeSpan.FromSeconds(0), "aaaaaaaaaaaaa bbbbbbbbbbbbb"),
            new LyricLine(TimeSpan.FromSeconds(60), "sonraki"),
        ]);
        var split = LyricSplitter.Split(source, Measure, 14);
        Assert.True(split.Lines[1].Time < TimeSpan.FromSeconds(3), "uzun aradan önce parçalar aranın sonuna yayılmamalı");
    }

    [Fact]
    public void Single_huge_word_is_kept()
    {
        Assert.Equal(["çokçokçokuzunkelime"], LyricSplitter.Chunk("çokçokçokuzunkelime", Measure, 5));
    }
}

public class LyricsServiceTests
{
    private sealed class CountingProvider(LyricsResult result) : ILyricsProvider
    {
        public int Calls;

        public Task<LyricsResult> GetAsync(LyricsQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static readonly LyricsQuery Query = new("Başlık", "Sanatçı", "Albüm", TimeSpan.FromSeconds(200));

    [Fact]
    public async Task Caches_found_and_not_found()
    {
        var provider = new CountingProvider(LyricsResult.NotFound);
        var service = new LyricsService(provider);
        await service.GetAsync(Query, CancellationToken.None);
        await service.GetAsync(Query with { Duration = TimeSpan.FromSeconds(201) }, CancellationToken.None);
        Assert.Equal(1, provider.Calls);
        Assert.True(service.TryGetCached(Query, out var cached));
        Assert.Equal(LyricsStatus.NotFound, cached.Status);
    }

    [Fact]
    public async Task Unavailable_is_retried_after_a_minute()
    {
        var now = DateTimeOffset.UtcNow;
        var provider = new CountingProvider(LyricsResult.Unavailable);
        var service = new LyricsService(provider, () => now);
        await service.GetAsync(Query, CancellationToken.None);
        await service.GetAsync(Query, CancellationToken.None);
        Assert.Equal(1, provider.Calls);

        now += TimeSpan.FromSeconds(61);
        await service.GetAsync(Query, CancellationToken.None);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Provider_exception_becomes_unavailable()
    {
        var service = new LyricsService(new ThrowingProvider());
        var result = await service.GetAsync(Query, CancellationToken.None);
        Assert.Equal(LyricsStatus.Unavailable, result.Status);
    }

    private sealed class ThrowingProvider : ILyricsProvider
    {
        public Task<LyricsResult> GetAsync(LyricsQuery query, CancellationToken cancellationToken) => throw new InvalidOperationException("test");
    }
}

public class LrclibProviderTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("Song - Remastered 2011", "Song")]
    [InlineData("Song (feat. Someone)", "Song")]
    [InlineData("Song [Live]", "Song")]
    [InlineData("Song (Acoustic)", "Song (Acoustic)")]
    [InlineData(" - ", "-")]
    public void Clean_title(string input, string expected) => Assert.Equal(expected, LrclibLyricsProvider.CleanTitle(input));

    [Theory]
    [InlineData("A, B", "A")]
    [InlineData("A & B", "A")]
    [InlineData("A feat. B", "A")]
    [InlineData("Tek", "Tek")]
    public void Primary_artist(string input, string expected) => Assert.Equal(expected, LrclibLyricsProvider.PrimaryArtist(input));

    [Fact]
    public async Task Exact_match_returns_synced_lyrics()
    {
        var handler = new FakeHandler(_ => Json("""{"trackName":"Song","duration":200,"instrumental":false,"syncedLyrics":"[00:01.00]Merhaba"}"""));
        using var provider = new LrclibLyricsProvider(handler);
        var result = await provider.GetAsync(new LyricsQuery("Song", "Artist", "Album", TimeSpan.FromSeconds(200.4)), CancellationToken.None);

        Assert.Equal(LyricsStatus.Found, result.Status);
        Assert.Equal("Merhaba", result.Lyrics!.Lines[0].Text);
        Assert.Single(handler.Requests);
        Assert.Contains("/api/get?track_name=Song&artist_name=Artist&album_name=Album&duration=200", handler.Requests[0]);
    }

    [Fact]
    public async Task Falls_back_to_search_and_rejects_wrong_duration()
    {
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath == "/api/get"
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"code":404}""", Encoding.UTF8, "application/json") }
            : Json("""
                [{"trackName":"Song","duration":320,"syncedLyrics":"[00:01.00]Yanlış sürüm"},
                 {"trackName":"Song","duration":201,"syncedLyrics":"[00:01.00]Doğru"},
                 {"trackName":"Song","duration":200,"plainLyrics":"yalnızca düz"}]
                """));
        using var provider = new LrclibLyricsProvider(handler);
        var result = await provider.GetAsync(new LyricsQuery("Song - Remastered", "Artist, Other", "", TimeSpan.FromSeconds(200)), CancellationToken.None);

        Assert.Equal(LyricsStatus.Found, result.Status);
        Assert.Equal("Doğru", result.Lyrics!.Lines[0].Text);
        Assert.Contains("/api/search?track_name=Song&artist_name=Artist", handler.Requests[1]);
    }

    [Fact]
    public async Task Instrumental_and_rate_limit()
    {
        using var instrumental = new LrclibLyricsProvider(new FakeHandler(_ => Json("""{"trackName":"X","instrumental":true}""")));
        Assert.Equal(LyricsStatus.Instrumental, (await instrumental.GetAsync(new LyricsQuery("X", "Y", "", null), CancellationToken.None)).Status);

        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var limited = new LrclibLyricsProvider(handler);
        Assert.Equal(LyricsStatus.Unavailable, (await limited.GetAsync(new LyricsQuery("X", "Y", "", null), CancellationToken.None)).Status);
        // Hız sınırında beklerken yeni istek gönderilmez.
        Assert.Equal(LyricsStatus.Unavailable, (await limited.GetAsync(new LyricsQuery("Z", "Y", "", null), CancellationToken.None)).Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Network_error_is_unavailable()
    {
        using var provider = new LrclibLyricsProvider(new FakeHandler(_ => throw new HttpRequestException("ağ yok")));
        Assert.Equal(LyricsStatus.Unavailable, (await provider.GetAsync(new LyricsQuery("X", "Y", "", null), CancellationToken.None)).Status);
    }
}

public class LyricsFontSettingsTests
{
    [Fact]
    public void Old_default_font_migrates_to_inter_but_explicit_choice_is_kept()
    {
        var old = new DynamicIsland.Core.Settings.AppSettings { SchemaVersion = 1, LyricsFont = DynamicIsland.Core.Settings.LyricsFontChoice.SegoeVariableBold };
        old.Normalize();
        Assert.Equal(DynamicIsland.Core.Settings.LyricsFontChoice.InterBold, old.LyricsFont);
        Assert.Equal(DynamicIsland.Core.Settings.AppSettings.CurrentSchemaVersion, old.SchemaVersion);

        var chosen = new DynamicIsland.Core.Settings.AppSettings { SchemaVersion = 1, LyricsFont = DynamicIsland.Core.Settings.LyricsFontChoice.Bahnschrift };
        chosen.Normalize();
        Assert.Equal(DynamicIsland.Core.Settings.LyricsFontChoice.Bahnschrift, chosen.LyricsFont);

        var current = new DynamicIsland.Core.Settings.AppSettings { LyricsFont = DynamicIsland.Core.Settings.LyricsFontChoice.SegoeVariableBold };
        current.Normalize();
        Assert.Equal(DynamicIsland.Core.Settings.LyricsFontChoice.SegoeVariableBold, current.LyricsFont);
    }
}
