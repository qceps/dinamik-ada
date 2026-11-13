using System.Runtime.InteropServices;
using DynamicIsland.Core.Settings;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Interop;

public enum HotkeyRegistrationResult
{
    Registered,
    Disabled,
    /// <summary>Başka bir uygulama (veya Windows) bu kombinasyonu zaten kullanıyor.</summary>
    InUse,
    Invalid,
}

/// <summary>RegisterHotKey sarmalayıcısı. WM_HOTKEY mesajlarını alacak pencerenin sahibi çağıran taraftır.</summary>
public static class GlobalHotkeys
{
    public static HotkeyRegistrationResult Register(nint hwnd, int id, HotkeyGesture gesture)
    {
        if (gesture.IsEmpty)
        {
            return HotkeyRegistrationResult.Disabled;
        }

        if (gesture.Modifiers == HotkeyModifiers.None && !IsFunctionKey(gesture.VirtualKey))
        {
            // Değiştiricisiz harf/rakam tuşu oyunlarda yazmayı bozar: kabul etme.
            return HotkeyRegistrationResult.Invalid;
        }

        if (RegisterHotKey(hwnd, id, (uint)gesture.Modifiers | MOD_NOREPEAT, (uint)gesture.VirtualKey))
        {
            return HotkeyRegistrationResult.Registered;
        }

        const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
        return Marshal.GetLastPInvokeError() == ERROR_HOTKEY_ALREADY_REGISTERED
            ? HotkeyRegistrationResult.InUse
            : HotkeyRegistrationResult.Invalid;
    }

    public static void Unregister(nint hwnd, int id) => UnregisterHotKey(hwnd, id);

    public static bool IsFunctionKey(int vk) => vk is >= 0x70 and <= 0x87;
}
