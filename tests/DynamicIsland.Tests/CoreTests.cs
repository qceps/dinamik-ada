using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.State;

namespace DynamicIsland.Tests;

public class SpotifyIdentityTests
{
    [Theory]
    [InlineData("Spotify.exe", true)]
    [InlineData("spotify.exe", true)]
    [InlineData(@"C:\Users\x\AppData\Roaming\Spotify\Spotify.exe", true)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", true)]
    [InlineData("Brave", false)]
    [InlineData("chrome.exe", false)]
    [InlineData("MSEdge", false)]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Matches_only_spotify_desktop(string? aumid, bool expected) =>
        Assert.Equal(expected, SpotifyIdentity.IsSpotifyAppId(aumid));
}

public class IslandStateResolverTests
{
    private static IslandInputs Inputs(
        bool hidden = false, MediaAvailability availability = MediaAvailability.Ready, bool hasTrack = true,
        IdleBehavior idle = IdleBehavior.ShowIdle, bool expand = false, bool peek = false) =>
        new(hidden, availability, hasTrack, idle, expand, peek);

    [Fact] public void User_hidden_wins() => Assert.Equal(IslandVisual.Hidden, IslandStateResolver.Resolve(Inputs(hidden: true, expand: true)));
    [Fact] public void Unknown_before_first_read_is_hidden() => Assert.Equal(IslandVisual.Hidden, IslandStateResolver.Resolve(Inputs(availability: MediaAvailability.Unknown, hasTrack: false)));
    [Fact] public void Closed_spotify_shows_idle_by_default() => Assert.Equal(IslandVisual.Idle, IslandStateResolver.Resolve(Inputs(availability: MediaAvailability.SpotifyNotRunning, hasTrack: false)));
    [Fact] public void Closed_spotify_hidden_when_setting_says_hide() => Assert.Equal(IslandVisual.Hidden, IslandStateResolver.Resolve(Inputs(availability: MediaAvailability.SpotifyNotRunning, hasTrack: false, idle: IdleBehavior.Hide)));
    [Fact] public void No_track_never_expands() => Assert.Equal(IslandVisual.Idle, IslandStateResolver.Resolve(Inputs(hasTrack: false, expand: true)));
    [Fact] public void Expand_beats_peek() => Assert.Equal(IslandVisual.Expanded, IslandStateResolver.Resolve(Inputs(expand: true, peek: true)));
    [Fact] public void Peek_when_active() => Assert.Equal(IslandVisual.Peek, IslandStateResolver.Resolve(Inputs(peek: true)));
    [Fact] public void Compact_by_default() => Assert.Equal(IslandVisual.Compact, IslandStateResolver.Resolve(Inputs()));

    [Fact]
    public void Idle_texts_distinguish_closed_and_no_session()
    {
        Assert.Equal("Spotify kapalı", IslandStateResolver.IdleText(MediaAvailability.SpotifyNotRunning, false));
        Assert.Equal("Spotify hazır", IslandStateResolver.IdleText(MediaAvailability.NoSession, false));
        Assert.Equal("Müzik yok", IslandStateResolver.IdleText(MediaAvailability.Ready, true));
    }
}

public class PlacementTests
{
    private static readonly PixelRect Primary = new(0, 0, 1920, 1080);
    private static readonly PixelRect Left = new(-1920, 0, 0, 1080);

    [Fact]
    public void Centers_on_primary_at_100_percent()
    {
        var r = PlacementCalculator.Compute(Primary, 1.0, (432, 208), 376, 176, 0, 8);
        Assert.Equal(new PixelRect(744, 8, 1176, 216), r);
    }

    [Fact]
    public void Scales_with_dpi_150_percent()
    {
        var r = PlacementCalculator.Compute(new PixelRect(0, 0, 2880, 1620), 1.5, (432, 208), 376, 176, 0, 8);
        Assert.Equal(648, r.Width);
        Assert.Equal(312, r.Height);
        Assert.Equal(12, r.Top);
        Assert.Equal(1440, r.Left + r.Width / 2);
    }

    [Fact]
    public void Works_on_negative_coordinates_secondary_monitor()
    {
        var r = PlacementCalculator.Compute(Left, 1.0, (432, 208), 376, 176, 0, 0);
        Assert.Equal(-960, r.Left + r.Width / 2);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void Huge_offset_is_clamped_so_island_stays_on_screen()
    {
        var r = PlacementCalculator.Compute(Primary, 1.0, (432, 208), 376, 176, 99999, 8);
        var center = r.Left + r.Width / 2.0;
        Assert.True(center + 376 / 2.0 <= 1920);
        var l = PlacementCalculator.Compute(Primary, 1.0, (432, 208), 376, 176, -99999, 8);
        Assert.True(l.Left + l.Width / 2.0 - 376 / 2.0 >= 0);
    }

    [Fact]
    public void Offset_saved_for_large_monitor_stays_visible_after_resolution_drop()
    {
        // 2560 genişlikte kaydedilen +1000 kaydırma, 1280'e düşünce ekran içinde kalmalı.
        var r = PlacementCalculator.Compute(new PixelRect(0, 0, 1280, 720), 1.0, (432, 208), 376, 176, 1000, 8);
        Assert.True(r.Left + r.Width / 2.0 + 188 <= 1280);
    }

    [Fact]
    public void Top_margin_cannot_push_island_below_screen()
    {
        var r = PlacementCalculator.Compute(new PixelRect(0, 0, 800, 200), 1.0, (432, 208), 376, 176, 0, 500);
        Assert.True(r.Top + 176 <= 200);
        Assert.True(r.Top >= 0);
    }

    [Fact]
    public void Invalid_dpi_falls_back()
    {
        var r = PlacementCalculator.Compute(Primary, double.NaN, (432, 208), 376, 176, double.NaN, double.PositiveInfinity);
        Assert.Equal(432, r.Width);
    }

    [Fact]
    public void Max_offset_depends_on_width()
    {
        Assert.Equal(772, PlacementCalculator.MaxOffsetDip(Primary, 1.0, 376));
        Assert.Equal(0, PlacementCalculator.MaxOffsetDip(new PixelRect(0, 0, 300, 300), 1.0, 376));
    }
}

public class TimelineTests
{
    [Fact]
    public void Extrapolates_only_while_playing_and_clamps()
    {
        var sampled = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t = new TimelineInfo(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), sampled, 1.0);
        Assert.Equal(TimeSpan.FromSeconds(15), t.PositionAt(sampled.AddSeconds(5), playing: true));
        Assert.Equal(TimeSpan.FromSeconds(10), t.PositionAt(sampled.AddSeconds(5), playing: false));
        Assert.Equal(TimeSpan.FromSeconds(60), t.PositionAt(sampled.AddMinutes(10), playing: true));
    }

    [Fact]
    public void Snapshot_equivalence_ignores_small_time_drift_but_not_seek()
    {
        var now = DateTimeOffset.UtcNow;
        var track = new TrackInfo("a", "b", "c");
        MediaSnapshot Snap(double pos, DateTimeOffset at) => new(MediaAvailability.Ready, track, PlaybackStatus.Playing,
            new TimelineInfo(TimeSpan.FromSeconds(pos), TimeSpan.FromSeconds(100), at, 1), MediaCapabilities.None, null, false);

        Assert.True(Snap(10, now).IsEquivalentTo(Snap(10.3, now.AddSeconds(0.3))));
        Assert.False(Snap(10, now).IsEquivalentTo(Snap(50, now)));
    }

    [Fact]
    public void Artwork_hash_differs_for_different_bytes()
    {
        var a = MediaArtwork.Create("k", [1, 2, 3]);
        var b = MediaArtwork.Create("k", [1, 2, 4]);
        Assert.NotEqual(a.Hash, b.Hash);
        Assert.Equal(a.Hash, MediaArtwork.Create("k", [1, 2, 3]).Hash);
    }
}

public class ArtworkCacheTests
{
    [Fact]
    public void Evicts_least_recently_used()
    {
        var cache = new ArtworkCache(2);
        cache.Put(MediaArtwork.Create("a", [1]));
        cache.Put(MediaArtwork.Create("b", [2]));
        Assert.NotNull(cache.Get("a")); // a artık en yeni
        cache.Put(MediaArtwork.Create("c", [3]));
        Assert.Null(cache.Get("b"));
        Assert.NotNull(cache.Get("a"));
        Assert.NotNull(cache.Get("c"));
    }
}

public class SettingsTests
{
    [Fact]
    public void Roundtrip_preserves_values_and_defaults_are_safe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dinamikada-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(dir);
            var defaults = store.Load();
            Assert.False(defaults.StartWithWindows); // varsayılan kapalı
            Assert.Equal(GameModeSetting.AutoFullscreen, defaults.GameMode);

            defaults.HorizontalOffset = -120;
            defaults.MonitorDeviceName = @"\\.\DISPLAY2";
            defaults.IdleBehavior = IdleBehavior.Hide;
            defaults.ToggleVisibilityHotkey = new HotkeyGesture(HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x48);
            store.Save(defaults);

            var loaded = new SettingsStore(dir).Load();
            Assert.Equal(-120, loaded.HorizontalOffset);
            Assert.Equal(@"\\.\DISPLAY2", loaded.MonitorDeviceName);
            Assert.Equal(IdleBehavior.Hide, loaded.IdleBehavior);
            Assert.Equal(new HotkeyGesture(HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x48), loaded.ToggleVisibilityHotkey);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dinamikada-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ bozuk json");
            var loaded = new SettingsStore(dir).Load();
            Assert.Equal(1.0, loaded.Scale);
            Assert.True(File.Exists(Path.Combine(dir, "settings.json.bozuk")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Normalize_clamps_out_of_range_values()
    {
        var s = new AppSettings { Scale = 9, BackgroundOpacity = 0, PeekSeconds = double.NaN, TopMargin = -5, GameMode = (GameModeSetting)42 };
        s.Normalize();
        Assert.Equal(1.4, s.Scale);
        Assert.Equal(0.4, s.BackgroundOpacity);
        Assert.Equal(4, s.PeekSeconds);
        Assert.Equal(0, s.TopMargin);
        Assert.Equal(GameModeSetting.AutoFullscreen, s.GameMode);
    }
}
