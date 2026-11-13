using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicIsland.Core.Diagnostics;

namespace DynamicIsland.Core.Settings;

/// <summary>Ayarları %APPDATA%\DinamikAda\settings.json içinde atomik olarak saklar.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();

    public SettingsStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DinamikAda");
        FilePath = Path.Combine(Directory, "settings.json");
    }

    public string Directory { get; }

    public string FilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Ayar dosyası okunamadı; varsayılanlar kullanılıyor", ex);
            TryBackupCorrupt();
        }

        var defaults = new AppSettings();
        defaults.Normalize();
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error("Ayarlar kaydedilemedi", ex);
            }
        }
    }

    private void TryBackupCorrupt()
    {
        try
        {
            File.Copy(FilePath, FilePath + ".bozuk", overwrite: true);
        }
        catch
        {
        }
    }
}
