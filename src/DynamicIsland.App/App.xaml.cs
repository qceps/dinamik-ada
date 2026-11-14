using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core.Diagnostics;

namespace DynamicIsland.App;

public partial class App : Application
{
    private const string MutexName = @"Local\DinamikAda.SingleInstance.v1";
    private const string ShowSettingsSignalName = @"Local\DinamikAda.ShowSettings.v1";

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private RegisteredWaitHandle? _signalWait;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Geliştirici aracı: durumları PNG'ye döküp çık (tek kopya kilidine ve Spotify'a dokunmaz).
        var renderIndex = Array.FindIndex(e.Args, a => a.Equals("--render", StringComparison.OrdinalIgnoreCase));
        if (renderIndex >= 0 && renderIndex + 1 < e.Args.Length)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await Diagnostics.StateRenderer.RunAsync(e.Args[renderIndex + 1]);
                }
                catch (Exception ex)
                {
                    Log.Error("Görüntü dökümü başarısız", ex);
                }

                Shutdown();
            });
            return;
        }

        // Güncellemeden sonra: eski sürüm kapanana kadar bekle (yoksa tek kopya kilidine takılırız).
        var afterUpdate = Array.FindIndex(e.Args, a => a.Equals("--after-update", StringComparison.OrdinalIgnoreCase));
        if (afterUpdate >= 0 && afterUpdate + 1 < e.Args.Length && int.TryParse(e.Args[afterUpdate + 1], out var oldPid))
        {
            try
            {
                using var old = System.Diagnostics.Process.GetProcessById(oldPid);
                old.WaitForExit(15000);
            }
            catch (ArgumentException)
            {
                // zaten kapanmış
            }
        }

        // Tek kopya: ikinci açılış mevcut kopyanın ayar penceresini öne getirir ve kapanır.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ShowSettingsSignalName);
                existing.Set();
            }
            catch
            {
            }

            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignalName);
        _signalWait = ThreadPool.RegisterWaitForSingleObject(_signal,
            (_, _) => Dispatcher.BeginInvoke(() => _host?.OpenSettings()), null, Timeout.Infinite, executeOnlyOnce: false);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("İşlenmeyen hata", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Warn("Gözlemlenmeyen görev hatası", args.Exception);
            args.SetObserved();
        };

        System.Windows.Forms.Application.EnableVisualStyles();

        var demo = e.Args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        var openSettings = e.Args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase));
        _host = new AppHost(demo);
        _host.Start(openSettings, justUpdated: afterUpdate >= 0);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Beklenmeyen bir arayüz hatası uygulamayı kapatmasın; günlüğe yaz ve devam et.
        Log.Error("Arayüz iş parçacığında işlenmeyen hata", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _signalWait?.Unregister(null);
        _host?.Dispose();
        _signal?.Dispose();
        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
            }

            _mutex.Dispose();
        }

        Log.Info("Çıkış");
        base.OnExit(e);
    }
}
