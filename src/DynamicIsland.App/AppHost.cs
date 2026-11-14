using System.Windows;
using DynamicIsland.App.Demo;
using DynamicIsland.App.Island;
using DynamicIsland.App.Services;
using DynamicIsland.App.Settings;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Lyrics;
using DynamicIsland.Core.Media;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.App;

/// <summary>Bileşim kökü: servisleri oluşturur, birbirine bağlar ve doğru sırada temizler.</summary>
public sealed class AppHost : IDisposable
{
    private readonly bool _demo;
    private SettingsService? _settings;
    private IMediaSource? _media;
    private IslandWindow? _window;
    private IslandController? _controller;
    private GameModeService? _game;
    private LrclibLyricsProvider? _lrclib;
    private HotkeyService? _hotkeys;
    private TrayIconService? _tray;
    private SettingsWindow? _settingsWindow;
    private AppSettings? _lastHotkeySettings;
    private UpdateService? _updates;
    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private Version? _notifiedUpdate;
    private bool _disposed;

    public AppHost(bool demo) => _demo = demo;

    public async void Start(bool openSettings, bool justUpdated = false)
    {
        try
        {
            Log.Info($"Başlatılıyor (sürüm {typeof(AppHost).Assembly.GetName().Version}, demo={_demo})");
            _settings = new SettingsService(new SettingsStore());

            // Başlangıç kaydını yalnızca OKU; kayıt defterine sadece kullanıcı ayarı değiştirince yazılır.
            var registered = StartupRegistration.IsRegistered();
            if (registered != _settings.Current.StartWithWindows)
            {
                _settings.Update(s => s.StartWithWindows = registered);
            }

            _window = new IslandWindow();
            _window.EnsureCreated();
            _game = new GameModeService(() => _window.Handle, _settings.Current.GameMode);
            _media = _demo ? new DemoMediaSource() : new SpotifyMediaSource();
            ILyricsProvider lyricsProvider = _demo ? new DemoLyricsProvider() : (_lrclib = new LrclibLyricsProvider());
            _controller = new IslandController(_window, _media, _settings, _game, new LyricsService(lyricsProvider));
            // Spotify bağlantısı (GSMTC ~0,5–1 sn) kısayol ve tepsi hazırlanırken paralel başlasın.
            var mediaStart = _media.StartAsync();

            _hotkeys = new HotkeyService();
            _hotkeys.Pressed += OnHotkey;
            ApplyHotkeys(notify: true);
            _settings.Changed += OnSettingsChanged;

            _tray = new TrayIconService(_demo);
            _tray.SettingsRequested += OpenSettings;
            _tray.ToggleVisibilityRequested += () => _controller.ToggleVisibility();
            _tray.ToggleInteractionRequested += () => _controller.ToggleInteraction();
            _tray.ExitRequested += Exit;
            _controller.StateChanged += UpdateTray;

            _updates = new UpdateService();
            _updates.Changed += OnUpdateChanged;
            _tray.UpdateRequested += () => _ = InstallUpdateAsync();
            _ = Task.Run(UpdateService.CleanupLeftovers);
            if (justUpdated)
            {
                _tray.ShowInfo("Dinamik Ada güncellendi", $"Sürüm {_updates.CurrentVersion.ToString(3)} çalışıyor.");
            }

            ScheduleUpdateChecks();

            _controller.Start();
            await mediaStart;
            UpdateTray();

            if (openSettings)
            {
                OpenSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Başlatma hatası", ex);
            MessageBox.Show("Dinamik Ada başlatılamadı. Ayrıntılar günlük dosyasında:\n" + Log.Directory, "Dinamik Ada", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    public void OpenSettings()
    {
        if (_settings is null || _hotkeys is null || _game is null)
        {
            return;
        }

        if (_settingsWindow is { IsLoaded: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _hotkeys, () => _game.IsActive, _demo, _updates, () => _ = InstallUpdateAsync());
        _settingsWindow.ExitRequested += Exit;
        _settingsWindow.HotkeyCaptureChanged += capturing =>
        {
            if (capturing)
            {
                _hotkeys.Suspend();
            }
            else
            {
                ApplyHotkeys(notify: false);
            }
        };
        // Ayarlar açıkken ada sürüklenerek taşınabilir; kapanınca kilitlenir.
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _controller?.SetDragEnabled(false);
        };
        _controller?.SetDragEnabled(true);
        _settingsWindow.Show();
        _settingsWindow.Activate(); // Kullanıcı açtığı için öne gelmesi beklenir.
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        var last = _lastHotkeySettings;
        if (last is null
            || last.ToggleExpandHotkey != settings.ToggleExpandHotkey
            || last.ToggleVisibilityHotkey != settings.ToggleVisibilityHotkey
            || last.ToggleInteractionHotkey != settings.ToggleInteractionHotkey)
        {
            ApplyHotkeys(notify: false);
        }

        _settingsWindow?.RefreshStatus();
        UpdateTray();
    }

    private void ApplyHotkeys(bool notify)
    {
        if (_hotkeys is null || _settings is null)
        {
            return;
        }

        _lastHotkeySettings = _settings.Current;
        _hotkeys.Apply(_settings.Current);
        _settingsWindow?.RefreshStatus();

        var conflicts = _hotkeys.Status.Where(p => p.Value is HotkeyStatus.InUseByOther or HotkeyStatus.Duplicate or HotkeyStatus.Invalid).ToList();
        if (notify && conflicts.Count > 0)
        {
            _tray?.ShowNotice("Kısayol çakışması",
                $"{conflicts.Count} kısayol kaydedilemedi (başka bir uygulama kullanıyor olabilir). Ayarlar'dan değiştirebilirsiniz.");
        }
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleExpand:
                _controller?.ToggleExpand();
                break;
            case HotkeyAction.ToggleVisibility:
                _controller?.ToggleVisibility();
                break;
            case HotkeyAction.ToggleInteraction:
                _controller?.ToggleInteraction();
                break;
        }
    }

    private void UpdateTray()
    {
        if (_tray is not null && _controller is not null)
        {
            _tray.UpdateState(_controller.UserHidden, _controller.GameModeActive, _controller.InteractionMode);
        }

        _settingsWindow?.RefreshStatus();
    }

    // ---- Güncellemeler ----

    /// <summary>Açılıştan 20 sn sonra, ardından günde bir (ayar açıksa; demo modunda değil).</summary>
    private void ScheduleUpdateChecks()
    {
        _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(24);
            if (!_demo && _settings?.Current.CheckForUpdates == true && _updates is not null)
            {
                _ = _updates.CheckAsync();
            }
        };
        _updateTimer.Start();
    }

    private void OnUpdateChanged()
    {
        if (_updates is null)
        {
            return;
        }

        var available = _updates.State == UpdateState.Available ? _updates.Available : null;
        _tray?.SetUpdateAvailable(available?.Version.ToString(3));
        _settingsWindow?.RefreshUpdateStatus();

        // Aynı sürüm için yalnızca bir kez bildir.
        if (available is not null && available.Version != _notifiedUpdate)
        {
            _notifiedUpdate = available.Version;
            _tray?.ShowInfo($"Dinamik Ada {available.Version.ToString(3)} hazır",
                "Güncellemek için tıklayın (tepsi menüsünden ya da Ayarlar'dan da yapılabilir).",
                () => _ = InstallUpdateAsync());
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_updates is null)
        {
            return;
        }

        if (await _updates.InstallAsync())
        {
            Log.Info("Yeni sürüm başlatıldı; eski kopya kapanıyor");
            Application.Current.Shutdown();
        }
    }

    private void Exit()
    {
        Log.Info("Kullanıcı çıkış istedi");
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settingsWindow?.Close();
        _controller?.Dispose();
        try
        {
            _media?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            Log.Warn("Medya kaynağı kapatılırken hata", ex);
        }

        _updateTimer?.Stop();
        _updates?.Dispose();
        _hotkeys?.Dispose();
        _lrclib?.Dispose();
        _game?.Dispose();
        _tray?.Dispose();
        _settings?.Dispose();
        _window?.Close();
    }
}
