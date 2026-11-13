namespace DynamicIsland.Core.Settings;

public enum IdleBehavior
{
    /// <summary>Spotify kapalıyken / müzik yokken küçük, sade bir bekleme kapsülü göster.</summary>
    ShowIdle,
    /// <summary>Spotify kapalıyken / müzik yokken adayı tamamen gizle.</summary>
    Hide,
}

public enum GameModeSetting
{
    Off,
    On,
    /// <summary>Ön plandaki pencere monitörü tamamen kaplıyorsa otomatik etkinleşir.</summary>
    AutoFullscreen,
}

/// <summary>RegisterHotKey MOD_* değerleriyle aynı bitler.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    public static readonly HotkeyGesture None = new(HotkeyModifiers.None, 0);

    public bool IsEmpty => VirtualKey == 0;
}

public sealed class AppSettings
{
    public const double DefaultTopMargin = 8;

    public int SchemaVersion { get; set; } = 1;

    public bool StartWithWindows { get; set; }

    /// <summary>Monitör aygıt adı (ör. \\.\DISPLAY2). Boşsa birincil monitör.</summary>
    public string? MonitorDeviceName { get; set; }

    /// <summary>Ekran ortasına göre yatay kaydırma (DIP).</summary>
    public double HorizontalOffset { get; set; }

    /// <summary>Üst kenardan uzaklık (DIP).</summary>
    public double TopMargin { get; set; } = DefaultTopMargin;

    /// <summary>Boyut ölçeği (0.8 – 1.4).</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Arka plan opaklığı (0.4 – 1.0).</summary>
    public double BackgroundOpacity { get; set; } = 1.0;

    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>Şarkı değişiminde açık kalma süresi (sn).</summary>
    public double PeekSeconds { get; set; } = 4;

    public IdleBehavior IdleBehavior { get; set; } = IdleBehavior.ShowIdle;

    public bool HoverToExpand { get; set; } = true;

    public GameModeSetting GameMode { get; set; } = GameModeSetting.AutoFullscreen;

    public HotkeyGesture ToggleExpandHotkey { get; set; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x78); // F9

    public HotkeyGesture ToggleVisibilityHotkey { get; set; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x79); // F10

    public HotkeyGesture ToggleInteractionHotkey { get; set; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x7A); // F11

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Elle düzenlenmiş veya bozuk değerleri güvenli aralığa çeker.</summary>
    public void Normalize()
    {
        HorizontalOffset = Finite(HorizontalOffset, 0, -4000, 4000);
        TopMargin = Finite(TopMargin, DefaultTopMargin, 0, 200);
        Scale = Finite(Scale, 1.0, 0.8, 1.4);
        BackgroundOpacity = Finite(BackgroundOpacity, 1.0, 0.4, 1.0);
        PeekSeconds = Finite(PeekSeconds, 4, 1, 15);
        if (!Enum.IsDefined(IdleBehavior))
        {
            IdleBehavior = IdleBehavior.ShowIdle;
        }

        if (!Enum.IsDefined(GameMode))
        {
            GameMode = GameModeSetting.AutoFullscreen;
        }

        ToggleExpandHotkey ??= HotkeyGesture.None;
        ToggleVisibilityHotkey ??= HotkeyGesture.None;
        ToggleInteractionHotkey ??= HotkeyGesture.None;
        if (string.IsNullOrWhiteSpace(MonitorDeviceName))
        {
            MonitorDeviceName = null;
        }
    }

    public void ResetPlacement()
    {
        MonitorDeviceName = null;
        HorizontalOffset = 0;
        TopMargin = DefaultTopMargin;
        Scale = 1.0;
    }

    private static double Finite(double value, double fallback, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
