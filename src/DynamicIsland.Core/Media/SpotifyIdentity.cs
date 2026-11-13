namespace DynamicIsland.Core.Media;

/// <summary>GSMTC oturumunun Spotify masaüstü uygulamasına ait olup olmadığını uygulama kimliğinden belirler.</summary>
public static class SpotifyIdentity
{
    /// <summary>Spotify'ın Win32 (spotify.com) ve Microsoft Store sürümlerinin işlem adı.</summary>
    public const string ProcessName = "Spotify";

    public static bool IsSpotifyAppId(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return false;
        }

        var id = appUserModelId.Trim();

        // Win32 kurulum: "Spotify.exe"
        if (id.Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("\\Spotify.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Microsoft Store kurulumu: "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"
        return id.StartsWith("SpotifyAB.SpotifyMusic_", StringComparison.OrdinalIgnoreCase);
    }
}
