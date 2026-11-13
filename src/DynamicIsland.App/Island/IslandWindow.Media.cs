using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DynamicIsland.Core.State;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Island;

/// <summary>
/// Spotify ses kaydırıcısı (geniş görünüm) ve kısa göstergeler (sistem sesi / oynat-duraklat).
/// Ses değerleri denetleyiciden gelir; buradan yalnızca istek olayları çıkar.
/// </summary>
public partial class IslandWindow
{
    private enum HudMode
    {
        Volume,
        Playback,
    }

    private static readonly Size HudVolumeSize = new(216, 34);
    private static readonly Size HudPlaybackSize = new(180, 34);
    private const double HudTrackWidth = 216 - 44 - 56;

    // Segoe Fluent Icons
    private const string GlyphMute = "";
    private const string GlyphVolume0 = "";
    private const string GlyphVolume1 = "";
    private const string GlyphVolume2 = "";
    private const string GlyphVolume3 = "";

    private HudMode _hudMode = HudMode.Volume;
    private VolumeState? _appVolume;
    private bool _volumeDragging;

    /// <summary>Spotify uygulama sesi için hedef seviye (0–1).</summary>
    public event Action<float>? AppVolumeRequested;

    /// <summary>Spotify uygulama sesi için göreli adım (ör. +0.05).</summary>
    public event Action<float>? AppVolumeStepRequested;

    public event Action? AppMuteToggleRequested;

    /// <summary>Geniş görünümdeki söz düğmesine basıldı (sözleri aç / kapat).</summary>
    public event Action? LyricsToggleRequested;

    /// <summary>Söz düğmesinin görünümü: açıkken dolu çip, kapalıyken soluk simge.</summary>
    public void SetLyricsToggleState(bool enabled)
    {
        LyricsToggleChip.Background = enabled ? (System.Windows.Media.Brush)FindResource("ChipOnBrush") : System.Windows.Media.Brushes.Transparent;
        LyricsToggleGlyph.Opacity = enabled ? 1 : 0.55;
        LyricsToggleButton.ToolTip = enabled ? "Sözleri gizle" : "Sözleri göster";
    }

    /// <summary>Geniş görünüm açıldı/kapandı (denetleyici ses seviyesini o sırada yeniler).</summary>
    public event Action<bool>? ExpandedChanged;

    private Size HudSize => _hudMode == HudMode.Volume ? HudVolumeSize : HudPlaybackSize;

    private void InitializeMedia()
    {
        MuteButton.Click += (_, _) => AppMuteToggleRequested?.Invoke();
        LyricsToggleButton.Click += (_, _) => LyricsToggleRequested?.Invoke();
        VolumeArea.MouseLeftButtonDown += (_, e) =>
        {
            if (_appVolume is null)
            {
                return;
            }

            _volumeDragging = true;
            VolumeArea.CaptureMouse();
            VolumeThumb.Opacity = 1;
            RequestVolumeAt(e);
            e.Handled = true;
        };
        VolumeArea.MouseMove += (_, e) =>
        {
            if (_volumeDragging)
            {
                RequestVolumeAt(e);
            }
        };
        VolumeArea.MouseLeftButtonUp += (_, e) =>
        {
            if (!_volumeDragging)
            {
                return;
            }

            RequestVolumeAt(e);
            _volumeDragging = false;
            VolumeArea.ReleaseMouseCapture();
            e.Handled = true;
        };
        VolumeArea.LostMouseCapture += (_, _) => _volumeDragging = false;
        VolumeArea.MouseEnter += (_, _) => { if (_appVolume is not null) VolumeThumb.Opacity = 1; };
        VolumeArea.MouseLeave += (_, _) => { if (!_volumeDragging) VolumeThumb.Opacity = 0; };

        // Geniş görünümde fare tekerleği Spotify sesini değiştirir.
        Island.PreviewMouseWheel += (_, e) =>
        {
            if (_visual == IslandVisual.Expanded && _appVolume is not null)
            {
                AppVolumeStepRequested?.Invoke(e.Delta > 0 ? 0.05f : -0.05f);
                e.Handled = true;
            }
        };

        SetAppVolume(null);
    }

    private void RequestVolumeAt(MouseEventArgs e)
    {
        var width = VolumeArea.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var level = (float)Math.Clamp(e.GetPosition(VolumeArea).X / width, 0, 1);
        SetVolumeVisual(level, _appVolume?.Muted == true && level > 0 ? false : _appVolume?.Muted ?? false);
        AppVolumeRequested?.Invoke(level);
    }

    /// <summary>Spotify'ın uygulama ses seviyesi; null: Spotify'ın ses oturumu yok (henüz ses çalmamış).</summary>
    public void SetAppVolume(VolumeState? state)
    {
        _appVolume = state;
        var available = state is not null;
        VolumeGroup.Opacity = available ? 1 : 0.35;
        VolumeGroup.IsHitTestVisible = available;
        VolumeGroup.ToolTip = available
            ? "Spotify ses seviyesi (fare tekerleği de çalışır)"
            : "Spotify ses çalmaya başlayınca ses seviyesi ayarlanabilir";
        if (!_volumeDragging)
        {
            SetVolumeVisual(state?.Level ?? 0, state?.Muted ?? false);
        }
    }

    private void SetVolumeVisual(float level, bool muted)
    {
        var width = VolumeArea.ActualWidth > 0 ? VolumeArea.ActualWidth : VolumeArea.Width;
        var shown = muted ? 0 : level;
        VolumeFill.Width = Math.Max(0, width * shown);
        VolumeThumbOffset.X = Math.Max(0, width * shown - VolumeThumb.Width / 2);
        MuteGlyph.Text = GlyphFor(level, muted);
    }

    private static string GlyphFor(float level, bool muted) => muted ? GlyphMute : level switch
    {
        <= 0.001f => GlyphVolume0,
        < 0.34f => GlyphVolume1,
        < 0.67f => GlyphVolume2,
        _ => GlyphVolume3,
    };

    /// <summary>Ses göstergesinin içeriği (sistem ses seviyesi).</summary>
    public void ShowVolumeHud(VolumeState state)
    {
        var modeChanged = _hudMode != HudMode.Volume;
        _hudMode = HudMode.Volume;
        HudVolumeGlyph.Text = GlyphFor(state.Level, state.Muted);
        HudVolumeText.Text = state.Muted ? "Sessiz" : $"%{Math.Round(state.Level * 100):0}";
        var width = HudTrackWidth * (state.Muted ? 0 : state.Level);
        if (_animations && _shown && _visual == IslandVisual.Hud && !modeChanged)
        {
            HudVolumeFill.BeginAnimation(WidthProperty,
                new DoubleAnimation(width, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } },
                HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            HudVolumeFill.BeginAnimation(WidthProperty, null);
            HudVolumeFill.Width = width;
        }

        RefreshHudLayout(modeChanged);
    }

    /// <summary>Oynat/duraklat göstergesinin içeriği.</summary>
    public void ShowPlaybackHud(bool playing)
    {
        var modeChanged = _hudMode != HudMode.Playback;
        _hudMode = HudMode.Playback;
        HudPlaybackText.Text = playing ? "Çalıyor" : "Duraklatıldı";
        HudPlayIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        HudPauseIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        RefreshHudLayout(modeChanged);
    }

    private void RefreshHudLayout(bool modeChanged)
    {
        if (_visual == IslandVisual.Hud && modeChanged)
        {
            AnimateIslandSize(HudSize);
            ApplyLayerState(IslandVisual.Hud, _animations && _shown);
        }
    }

    private void OnVisualChangedForMedia() => ExpandedChanged?.Invoke(_visual == IslandVisual.Expanded);
}
