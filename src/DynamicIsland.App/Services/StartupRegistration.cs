using Microsoft.Win32;
using DynamicIsland.Core.Diagnostics;

namespace DynamicIsland.App.Services;

/// <summary>
/// "Windows ile başlat" — yalnızca kullanıcı ayarı değiştirdiğinde HKCU\...\Run anahtarına yazar/siler.
/// Yönetici yetkisi gerekmez; başka hiçbir sistem ayarına dokunulmaz.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DinamikAda";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? throw new InvalidOperationException("İşlem yolu alınamadı");
                key.SetValue(ValueName, $"\"{exe}\" --autostart", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Başlangıç kaydı güncellenemedi", ex);
            return false;
        }
    }
}
