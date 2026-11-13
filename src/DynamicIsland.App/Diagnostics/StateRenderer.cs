using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.App.Demo;
using DynamicIsland.App.Island;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.State;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Diagnostics;

/// <summary>
/// Geliştirici aracı (<c>--render &lt;klasör&gt;</c>): adanın her durumunu ekrana hiç çizmeden, demo verisiyle PNG'ye döker.
/// Tek kopya kilidine, tepsiye, kısayollara ve Spotify'a dokunmaz; tasarım karşılaştırması için kullanılır.
/// </summary>
internal static class StateRenderer
{
    private const double RenderScale = 2.0;

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var window = new IslandWindow();
        window.EnsureCreated();
        window.ApplyAppearance(1.0, 0.9, animationsEnabled: false);
        window.SetAppVolume(new VolumeState(0.62f, false));

        var demo = new DemoMediaSource();
        await demo.StartAsync();
        var decoder = new ArtworkDecoder();

        async Task Prepare(int trackIndex, bool playing, double progress, bool lyrics)
        {
            demo.Select(trackIndex, playing, progress);
            var snapshot = demo.Current;
            window.SetSnapshot(snapshot);
            var decoded = snapshot.Artwork is { } art ? await decoder.DecodeAsync(art) : null;
            window.SetArtwork(decoded?.Image, decoded?.Accent ?? ArtworkDecoder.FallbackAccent, animate: false);
            window.SetLyrics(lyrics ? DemoMediaSource.DemoLyrics(snapshot.Track!) : null);
        }

        void Shot(string name, IslandVisual visual)
        {
            if (window.Visual == visual)
            {
                window.SetVisual(IslandVisual.Idle); // aynı görünümde yeniden yerleşim için
            }

            window.SetVisual(visual);
            window.RefreshLyricsNow();
            window.UpdateLayout();
            Save(window.RenderRoot, Path.Combine(directory, name + ".png"));
        }

        window.SetIdleText("Spotify kapalı");
        window.SetSnapshot(MediaSnapshot.Initial(isDemo: true) with { Availability = MediaAvailability.SpotifyNotRunning });
        Shot("00_idle", IslandVisual.Idle);

        await Prepare(0, playing: true, progress: 0.18, lyrics: false);
        Shot("01_compact_info_nolyrics", IslandVisual.Compact);
        Shot("02_peek", IslandVisual.Peek);
        Shot("03_expanded_nolyrics", IslandVisual.Expanded);

        await Prepare(0, playing: true, progress: 0.18, lyrics: true);
        Shot("04_compact_lyric", IslandVisual.Compact);
        Shot("05_expanded_lyrics", IslandVisual.Expanded);
        window.SetLyricsToggleState(false);
        window.SetLyrics(null);
        Shot("05b_expanded_lyrics_off", IslandVisual.Expanded);
        window.SetLyricsToggleState(true);

        await Prepare(0, playing: false, progress: 0.18, lyrics: true);
        Shot("06_compact_paused_info", IslandVisual.Compact);

        // Uzun satır: küçük adada parça parça, geniş adada iki satıra bölünmüş
        await Prepare(1, playing: true, progress: 0.0105, lyrics: true);
        Shot("07_long_compact_chunk1", IslandVisual.Compact);
        Shot("08_long_expanded", IslandVisual.Expanded);
        await Prepare(1, playing: true, progress: 0.0145, lyrics: true);
        Shot("09_long_compact_chunk2", IslandVisual.Compact);

        await Prepare(0, playing: true, progress: 0.18, lyrics: true);
        window.ShowVolumeHud(new VolumeState(0.46f, false));
        Shot("10_hud_volume", IslandVisual.Hud);
        window.ShowVolumeHud(new VolumeState(0.46f, true));
        Shot("11_hud_muted", IslandVisual.Hud);
        window.ShowPlaybackHud(false);
        Shot("12_hud_paused", IslandVisual.Hud);
        window.ShowPlaybackHud(true);
        Shot("13_hud_playing", IslandVisual.Hud);

        foreach (var font in Enum.GetValues<LyricsFontChoice>())
        {
            window.SetLyricFont(font);
            await Prepare(0, playing: true, progress: 0.18, lyrics: true);
            Shot($"20_font_{font}_compact", IslandVisual.Compact);
            Shot($"21_font_{font}_expanded", IslandVisual.Expanded);
        }

        window.SetLyricFont(LyricsFontChoice.SegoeVariableBold);
        foreach (var theme in Enum.GetValues<IslandTheme>())
        {
            window.SetTheme(theme);
            await Prepare(0, playing: true, progress: 0.18, lyrics: true);
            Shot($"40_theme_{theme}_compact", IslandVisual.Compact);
            Shot($"41_theme_{theme}_expanded", IslandVisual.Expanded);
            await Prepare(1, playing: true, progress: 0.18, lyrics: true);
            Shot($"42_theme_{theme}_expanded2", IslandVisual.Expanded);
        }

        window.SetTheme(IslandTheme.BlackAccent);
        window.SetAppVolume(null);
        await Prepare(2, playing: true, progress: 0.4, lyrics: false);
        Shot("30_nocover_expanded_novolume", IslandVisual.Expanded);
        Shot("31_nocover_compact_info", IslandVisual.Compact);

        await demo.DisposeAsync();
        window.Close();
    }

    private static void Save(FrameworkElement element, string path)
    {
        var width = element.ActualWidth;
        var height = element.ActualHeight;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * RenderScale), (int)Math.Ceiling(height * RenderScale), 96 * RenderScale, 96 * RenderScale, PixelFormats.Pbgra32);
        bitmap.Render(element);

        // Saydamlığı ve gölgeyi görmek için açık/koyu iki zemin üzerine koy.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = new LinearGradientBrush(Color.FromRgb(0x6C, 0x8C, 0xB8), Color.FromRgb(0x23, 0x2A, 0x38), 0);
            dc.DrawRectangle(bg, null, new Rect(0, 0, width, height));
            dc.DrawImage(bitmap, new Rect(0, 0, width, height));
        }

        var composed = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, 96 * RenderScale, 96 * RenderScale, PixelFormats.Pbgra32);
        composed.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composed));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
