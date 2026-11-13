using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.State;

public enum IslandVisual
{
    Hidden,
    Idle,
    Compact,
    Peek,
    Expanded,
}

public readonly record struct IslandInputs(
    bool UserHidden,
    MediaAvailability Availability,
    bool HasTrack,
    IdleBehavior IdleBehavior,
    bool ExpandRequested,
    bool PeekActive);

/// <summary>Adanın görsel durumunu tek bir yerde, saf fonksiyon olarak belirler (birim test edilir).</summary>
public static class IslandStateResolver
{
    public static IslandVisual Resolve(in IslandInputs inputs)
    {
        if (inputs.UserHidden)
        {
            return IslandVisual.Hidden;
        }

        if (!inputs.HasTrack)
        {
            // İlk okuma tamamlanmadan boş kart gösterme.
            if (inputs.Availability == MediaAvailability.Unknown)
            {
                return IslandVisual.Hidden;
            }

            return inputs.IdleBehavior == IdleBehavior.Hide ? IslandVisual.Hidden : IslandVisual.Idle;
        }

        if (inputs.ExpandRequested)
        {
            return IslandVisual.Expanded;
        }

        return inputs.PeekActive ? IslandVisual.Peek : IslandVisual.Compact;
    }

    /// <summary>Bekleme kapsülünde gösterilecek kısa durum metni.</summary>
    public static string IdleText(MediaAvailability availability, bool hasTrackless) => availability switch
    {
        MediaAvailability.SpotifyNotRunning => "Spotify kapalı",
        MediaAvailability.NoSession => "Spotify hazır",
        MediaAvailability.Ready when hasTrackless => "Müzik yok",
        _ => "Bekleniyor",
    };
}
