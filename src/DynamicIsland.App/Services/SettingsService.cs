using System.Windows.Threading;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.App.Services;

/// <summary>Canlı ayarlar + gecikmeli (debounce) kalıcı kayıt.</summary>
public sealed class SettingsService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly DispatcherTimer _saveTimer;

    public SettingsService(SettingsStore store)
    {
        _store = store;
        Current = store.Load();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => Flush();
    }

    public AppSettings Current { get; private set; }

    public string FilePath => _store.FilePath;

    public event Action<AppSettings>? Changed;

    public void Update(Action<AppSettings> mutate)
    {
        var copy = Current.Clone();
        mutate(copy);
        copy.Normalize();
        Current = copy;
        _saveTimer.Stop();
        _saveTimer.Start();
        Changed?.Invoke(copy);
    }

    public void Flush()
    {
        _saveTimer.Stop();
        _store.Save(Current);
    }

    public void Dispose() => Flush();
}
