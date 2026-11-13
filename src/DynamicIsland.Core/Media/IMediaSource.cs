namespace DynamicIsland.Core.Media;

/// <summary>Adanın beslendiği medya kaynağı (gerçek Spotify veya ayrı demo kaynağı).</summary>
public interface IMediaSource : IAsyncDisposable
{
    /// <summary>Herhangi bir iş parçacığından tetiklenebilir; tüketici kendi UI iş parçacığına aktarmalıdır.</summary>
    event EventHandler<MediaSnapshot>? SnapshotChanged;

    MediaSnapshot Current { get; }

    bool IsDemo { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task<bool> TogglePlayPauseAsync();

    Task<bool> NextAsync();

    Task<bool> PreviousAsync();

    Task<bool> SeekAsync(TimeSpan position);

    /// <summary>Uyku sonrası gibi durumlarda tam yeniden eşitleme ister.</summary>
    void RequestResync();
}
