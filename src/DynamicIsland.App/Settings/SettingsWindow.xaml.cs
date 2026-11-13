using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.App.Island;
using DynamicIsland.App.Services;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.State;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly HotkeyService _hotkeys;
    private readonly Func<bool> _gameModeActive;
    private readonly bool _isDemo;
    private bool _loading;
    private bool _selfUpdate;

    public SettingsWindow(SettingsService settings, HotkeyService hotkeys, Func<bool> gameModeActive, bool isDemo)
    {
        _settings = settings;
        _hotkeys = hotkeys;
        _gameModeActive = gameModeActive;
        _isDemo = isDemo;
        InitializeComponent();

        LoadFromSettings(settings.Current);
        settings.Changed += OnSettingsChanged;

        // Checked/Unchecked: fare, klavye ve UI Otomasyonu (erişilebilirlik araçları) ile aynı şekilde çalışır.
        RoutedEventHandler startupChanged = (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            var enabled = StartWithWindowsBox.IsChecked == true;
            // Sistem ayarına yalnızca kullanıcı bu seçeneği değiştirdiğinde dokunulur.
            if (StartupRegistration.Set(enabled))
            {
                Update(s => s.StartWithWindows = enabled);
            }
            else
            {
                _loading = true;
                StartWithWindowsBox.IsChecked = !enabled;
                _loading = false;
                MessageBox.Show(this, "Başlangıç ayarı güncellenemedi. Ayrıntılar günlük dosyasında.", "Dinamik Ada", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        StartWithWindowsBox.Checked += startupChanged;
        StartWithWindowsBox.Unchecked += startupChanged;
        HoverBox.Checked += (_, _) => Update(s => s.HoverToExpand = true);
        HoverBox.Unchecked += (_, _) => Update(s => s.HoverToExpand = false);
        IdleBox.SelectionChanged += (_, _) => Update(s => s.IdleBehavior = IdleBox.SelectedIndex == 1 ? IdleBehavior.Hide : IdleBehavior.ShowIdle);
        MonitorBox.SelectionChanged += (_, _) =>
        {
            Update(s => s.MonitorDeviceName = (MonitorBox.SelectedItem as ComboBoxItem)?.Tag as string);
            UpdateOffsetRange();
        };
        OffsetSlider.ValueChanged += (_, e) => Update(s => s.HorizontalOffset = Math.Round(e.NewValue));
        TopSlider.ValueChanged += (_, e) => Update(s => s.TopMargin = Math.Round(e.NewValue));
        ScaleSlider.ValueChanged += (_, e) => Update(s => s.Scale = Math.Round(e.NewValue, 2));
        OpacitySlider.ValueChanged += (_, e) => Update(s => s.BackgroundOpacity = Math.Round(e.NewValue, 2));
        AnimationsBox.Checked += (_, _) => Update(s => s.AnimationsEnabled = true);
        AnimationsBox.Unchecked += (_, _) => Update(s => s.AnimationsEnabled = false);
        PeekSlider.ValueChanged += (_, e) => Update(s => s.PeekSeconds = Math.Round(e.NewValue * 2) / 2);
        GameModeBox.SelectionChanged += (_, _) => Update(s => s.GameMode = GameModeBox.SelectedIndex switch
        {
            0 => GameModeSetting.Off,
            1 => GameModeSetting.On,
            _ => GameModeSetting.AutoFullscreen,
        });

        WireHotkey(ExpandHotkeyBox, (s, g) => s.ToggleExpandHotkey = g);
        WireHotkey(VisibilityHotkeyBox, (s, g) => s.ToggleVisibilityHotkey = g);
        WireHotkey(InteractionHotkeyBox, (s, g) => s.ToggleInteractionHotkey = g);

        ResetPositionButton.Click += (_, _) => _settings.Update(s => s.ResetPlacement());
        OpenLogsButton.Click += (_, _) =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(Log.Directory);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Directory}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn("Günlük klasörü açılamadı", ex);
            }
        };
        CloseButton.Click += (_, _) => Close();
        ExitButton.Click += (_, _) => ExitRequested?.Invoke();
    }

    public event Action? ExitRequested;

    /// <summary>Kısayol yakalanırken genel kısayolları askıya almak için.</summary>
    public event Action<bool>? HotkeyCaptureChanged;

    public void RefreshStatus()
    {
        var game = _gameModeActive();
        GameModeStatus.Text = game ? "Şu an: oyun modu ETKİN (tıklamalar alttaki uygulamaya geçiyor)." : "Şu an: oyun modu etkin değil.";
        ReducedMotionNote.Text = SystemAnimation.ClientAreaAnimationsEnabled
            ? "Windows'ta \"Animasyon efektleri\" kapatılırsa ada da otomatik olarak animasyonsuz çalışır."
            : "Windows'ta \"Animasyon efektleri\" kapalı: ada şu an animasyonsuz çalışıyor.";
        SetHotkeyStatus(ExpandHotkeyStatus, HotkeyAction.ToggleExpand);
        SetHotkeyStatus(VisibilityHotkeyStatus, HotkeyAction.ToggleVisibility);
        SetHotkeyStatus(InteractionHotkeyStatus, HotkeyAction.ToggleInteraction);
    }

    private void SetHotkeyStatus(TextBlock target, HotkeyAction action)
    {
        var status = _hotkeys.Status.TryGetValue(action, out var s) ? s : HotkeyStatus.Disabled;
        target.Text = HotkeyService.Describe(status);
        target.Foreground = status is HotkeyStatus.Active or HotkeyStatus.Disabled
            ? (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush")
            : System.Windows.Media.Brushes.IndianRed;
        target.Opacity = status is HotkeyStatus.Active or HotkeyStatus.Disabled ? 0.7 : 1;
    }

    private void WireHotkey(HotkeyBox box, Action<AppSettings, HotkeyGesture> apply)
    {
        box.GestureCaptured += g => Update(s => apply(s, g));
        box.CaptureStateChanged += capturing => HotkeyCaptureChanged?.Invoke(capturing);
    }

    private void Update(Action<AppSettings> mutate)
    {
        if (_loading)
        {
            return;
        }

        _selfUpdate = true;
        try
        {
            _settings.Update(mutate);
        }
        finally
        {
            _selfUpdate = false;
        }
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        if (_selfUpdate)
        {
            // Değişiklik bu pencereden geldi: denetimleri yeniden yükleme (sürükleme bozulmasın), yalnızca etiketler.
            UpdateLabels(settings);
            RefreshStatus();
            return;
        }

        LoadFromSettings(settings);
    }

    private void UpdateLabels(AppSettings s)
    {
        OffsetValue.Text = s.HorizontalOffset == 0 ? "Ortada" : $"{s.HorizontalOffset:+0;-0} px";
        TopValue.Text = $"{s.TopMargin:0} px";
        ScaleValue.Text = $"%{s.Scale * 100:0}";
        OpacityValue.Text = $"%{s.BackgroundOpacity * 100:0}";
        PeekValue.Text = $"{s.PeekSeconds:0.#} sn";
    }

    private void LoadFromSettings(AppSettings s)
    {
        _loading = true;
        try
        {
            StartWithWindowsBox.IsChecked = s.StartWithWindows;
            HoverBox.IsChecked = s.HoverToExpand;
            IdleBox.SelectedIndex = s.IdleBehavior == IdleBehavior.Hide ? 1 : 0;
            LoadMonitors(s.MonitorDeviceName);
            UpdateOffsetRange();
            OffsetSlider.Value = s.HorizontalOffset;
            TopSlider.Value = s.TopMargin;
            ScaleSlider.Value = s.Scale;
            OpacitySlider.Value = s.BackgroundOpacity;
            AnimationsBox.IsChecked = s.AnimationsEnabled;
            PeekSlider.Value = s.PeekSeconds;
            GameModeBox.SelectedIndex = s.GameMode switch
            {
                GameModeSetting.Off => 0,
                GameModeSetting.On => 1,
                _ => 2,
            };
            ExpandHotkeyBox.Gesture = s.ToggleExpandHotkey;
            VisibilityHotkeyBox.Gesture = s.ToggleVisibilityHotkey;
            InteractionHotkeyBox.Gesture = s.ToggleInteractionHotkey;

            UpdateLabels(s);

            StatusText.Text = _isDemo
                ? "DEMO MODU: sahte parçalar gösteriliyor, Spotify'a bağlanılmıyor."
                : "Spotify masaüstü uygulamasındaki çalan parça Windows medya oturumundan okunur.";
            AboutText.Text = $"Sürüm {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} · Ayarlar: {_settings.FilePath}";
        }
        finally
        {
            _loading = false;
        }

        RefreshStatus();
    }

    private void LoadMonitors(string? selected)
    {
        var monitors = DisplayMonitors.GetAll();
        MonitorBox.Items.Clear();
        MonitorBox.Items.Add(new ComboBoxItem { Content = "Birincil monitör (otomatik)", Tag = null });
        var index = 1;
        var selectedIndex = 0;
        foreach (var m in monitors)
        {
            var label = $"Ekran {index}{(m.IsPrimary ? " (birincil)" : string.Empty)} — {m.Bounds.Width}×{m.Bounds.Height}, %{m.Dpi * 100 / 96}";
            MonitorBox.Items.Add(new ComboBoxItem { Content = label, Tag = m.DeviceName });
            if (string.Equals(m.DeviceName, selected, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = index;
            }

            index++;
        }

        if (!string.IsNullOrEmpty(selected) && selectedIndex == 0)
        {
            // Kayıtlı monitör şu an bağlı değil: seçimi koru, ada birincilde gösterilir.
            MonitorBox.Items.Add(new ComboBoxItem { Content = "Kayıtlı monitör (şu an bağlı değil)", Tag = selected });
            selectedIndex = MonitorBox.Items.Count - 1;
        }

        MonitorBox.SelectedIndex = selectedIndex;
    }

    private void UpdateOffsetRange()
    {
        var device = (MonitorBox.SelectedItem as ComboBoxItem)?.Tag as string;
        var monitor = DisplayMonitors.Resolve(device);
        var max = PlacementCalculator.MaxOffsetDip(monitor.Bounds, monitor.DpiScale, IslandWindow.MaxIslandWidth * _settings.Current.Scale);
        OffsetSlider.Minimum = -Math.Floor(max);
        OffsetSlider.Maximum = Math.Floor(max);
    }

    protected override void OnClosed(EventArgs e)
    {
        _settings.Changed -= OnSettingsChanged;
        base.OnClosed(e);
    }
}
