using System.Windows.Threading;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Settings;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Services;

/// <summary>
/// Oyun modunun etkin olup olmadığına karar verir. "Otomatik" ayarında ön plan değişimlerini
/// olay tabanlı izler (SetWinEventHook) ve tam ekran tespiti yapar; sürekli sorgulama yapmaz.
/// Oyun belleğine erişmez, hiçbir sürece kod enjekte etmez.
/// </summary>
public sealed class GameModeService : IDisposable
{
    private readonly Func<nint> _islandHandle;
    private readonly ForegroundWatcher _watcher;
    private readonly DispatcherTimer _recheck;
    private int _recheckStep;
    private GameModeSetting _mode;

    public GameModeService(Func<nint> islandHandle, GameModeSetting mode)
    {
        _islandHandle = islandHandle;
        _mode = mode;
        _watcher = new ForegroundWatcher();
        _watcher.ForegroundChanged += OnForegroundChanged;
        _recheck = new DispatcherTimer(DispatcherPriority.Background);
        _recheck.Tick += OnRecheck;
        Evaluate();
    }

    public bool IsActive { get; private set; }

    /// <summary>Oyun modu açıldı/kapandı.</summary>
    public event Action<bool>? ActiveChanged;

    /// <summary>Ön plan penceresi değişti (en üstte kalmayı yeniden doğrulamak için).</summary>
    public event Action? ForegroundChanged;

    public GameModeSetting Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Evaluate();
        }
    }

    public void Evaluate()
    {
        bool active;
        try
        {
            active = _mode switch
            {
                GameModeSetting.On => true,
                GameModeSetting.AutoFullscreen => FullscreenDetector.IsForegroundFullscreen(_islandHandle()),
                _ => false,
            };
        }
        catch (Exception ex)
        {
            Log.Warn("Tam ekran tespiti başarısız", ex);
            active = _mode == GameModeSetting.On;
        }

        if (active != IsActive)
        {
            IsActive = active;
            Log.Info($"Oyun modu: {(active ? "etkin" : "kapalı")} (ayar: {_mode})");
            ActiveChanged?.Invoke(active);
        }
    }

    private void OnForegroundChanged(nint hwnd)
    {
        Evaluate();
        ForegroundChanged?.Invoke();

        // Oyunlar ön plana geldikten kısa süre sonra tam ekrana geçer: iki kez gecikmeli yeniden denetle.
        _recheckStep = 0;
        _recheck.Interval = TimeSpan.FromMilliseconds(900);
        _recheck.Start();
    }

    private void OnRecheck(object? sender, EventArgs e)
    {
        Evaluate();
        ForegroundChanged?.Invoke();
        _recheckStep++;
        if (_recheckStep >= 2)
        {
            _recheck.Stop();
        }
        else
        {
            _recheck.Interval = TimeSpan.FromMilliseconds(2600);
        }
    }

    public void Dispose()
    {
        _recheck.Stop();
        _watcher.ForegroundChanged -= OnForegroundChanged;
        _watcher.Dispose();
    }
}
