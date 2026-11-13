using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Settings;
using DynamicIsland.Interop;

namespace DynamicIsland.App.Services;

public enum HotkeyAction
{
    ToggleExpand = 1,
    ToggleVisibility = 2,
    ToggleInteraction = 3,
}

public enum HotkeyStatus
{
    Active,
    Disabled,
    /// <summary>Başka bir uygulama veya Windows bu kısayolu kullanıyor.</summary>
    InUseByOther,
    /// <summary>Uygulamanın başka bir kısayoluyla aynı.</summary>
    Duplicate,
    Invalid,
}

/// <summary>Genel kısayolları yalnızca mesaj alan (HWND_MESSAGE) gizli bir pencereye kaydeder.</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<HotkeyAction, HotkeyStatus> _status = new();
    private readonly HashSet<int> _registered = new();

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("DinamikAda.Hotkeys")
        {
            ParentWindow = new nint(-3), // HWND_MESSAGE
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event Action<HotkeyAction>? Pressed;

    public IReadOnlyDictionary<HotkeyAction, HotkeyStatus> Status => _status;

    public void Apply(AppSettings settings)
    {
        UnregisterAll();
        var map = new (HotkeyAction Action, HotkeyGesture Gesture)[]
        {
            (HotkeyAction.ToggleExpand, settings.ToggleExpandHotkey),
            (HotkeyAction.ToggleVisibility, settings.ToggleVisibilityHotkey),
            (HotkeyAction.ToggleInteraction, settings.ToggleInteractionHotkey),
        };

        var seen = new HashSet<HotkeyGesture>();
        foreach (var (action, gesture) in map)
        {
            if (gesture.IsEmpty)
            {
                _status[action] = HotkeyStatus.Disabled;
                continue;
            }

            if (!seen.Add(gesture))
            {
                _status[action] = HotkeyStatus.Duplicate;
                continue;
            }

            var result = GlobalHotkeys.Register(_source.Handle, (int)action, gesture);
            _status[action] = result switch
            {
                HotkeyRegistrationResult.Registered => HotkeyStatus.Active,
                HotkeyRegistrationResult.InUse => HotkeyStatus.InUseByOther,
                HotkeyRegistrationResult.Disabled => HotkeyStatus.Disabled,
                _ => HotkeyStatus.Invalid,
            };

            if (result == HotkeyRegistrationResult.Registered)
            {
                _registered.Add((int)action);
            }
            else
            {
                Log.Warn($"Kısayol kaydedilemedi: {action} ({_status[action]})");
            }
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == OverlayWindow.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (Enum.IsDefined(typeof(HotkeyAction), id))
            {
                handled = true;
                Pressed?.Invoke((HotkeyAction)id);
            }
        }

        return 0;
    }

    /// <summary>Ayar penceresinde kısayol yakalanırken geçici olarak tüm kayıtları bırakır (durum bilgisi korunur).</summary>
    public void Suspend()
    {
        foreach (var id in _registered)
        {
            GlobalHotkeys.Unregister(_source.Handle, id);
        }

        _registered.Clear();
    }

    private void UnregisterAll()
    {
        Suspend();
        _status.Clear();
    }

    public static string Format(HotkeyGesture gesture)
    {
        if (gesture.IsEmpty)
        {
            return "Yok";
        }

        var parts = new List<string>(5);
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey(gesture.VirtualKey);
        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            Key.Space => "Boşluk",
            Key.Oem3 => "\"",
            _ => key.ToString(),
        });
        return string.Join(" + ", parts);
    }

    public static string Describe(HotkeyStatus status) => status switch
    {
        HotkeyStatus.Active => "Etkin",
        HotkeyStatus.Disabled => "Kapalı",
        HotkeyStatus.InUseByOther => "Çakışma: başka bir uygulama bu kısayolu kullanıyor",
        HotkeyStatus.Duplicate => "Çakışma: başka bir Dinamik Ada kısayoluyla aynı",
        _ => "Geçersiz kısayol (bir değiştirici tuş ekleyin)",
    };

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
