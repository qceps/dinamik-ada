using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Core.Diagnostics;

namespace DynamicIsland.Interop;

/// <summary>Ses seviyesi (0–1) ve sessiz durumu.</summary>
public readonly record struct VolumeState(float Level, bool Muted);

/// <summary>
/// Bir uygulamanın Windows ses karıştırıcısındaki seviyesi (WASAPI oturumları). Sistem sesine dokunmaz.
/// Tüm etkin çıkış aygıtlarındaki, işlem adı eşleşen oturumlara uygulanır. Arka plan iş parçacığından çağrılmalıdır.
/// </summary>
public static class AppVolume
{
    private static readonly object Gate = new();
    private static readonly Dictionary<uint, (string Name, long At)> ProcessNames = new();
    private static Guid _context = Guid.NewGuid();

    /// <summary>Oturum bulunamazsa null (uygulama henüz ses çalmamış olabilir).</summary>
    public static VolumeState? Get(string processName)
    {
        VolumeState? active = null;
        VolumeState? any = null;
        ForEachSession(processName, (volume, isActive) =>
        {
            if (volume.GetMasterVolume(out var level) != 0 || volume.GetMute(out var muted) != 0)
            {
                return;
            }

            var state = new VolumeState(Math.Clamp(level, 0, 1), muted);
            any ??= state;
            if (isActive)
            {
                active ??= state;
            }
        });

        return active ?? any;
    }

    /// <summary>Seviye ve/veya sessiz durumunu ayarlar; en az bir oturum değiştiyse true.</summary>
    public static bool Set(string processName, float? level, bool? mute)
    {
        var changed = false;
        ForEachSession(processName, (volume, _) =>
        {
            var ok = true;
            if (level is { } l)
            {
                ok &= volume.SetMasterVolume(Math.Clamp(l, 0f, 1f), ref _context) == 0;
            }

            if (mute is { } m)
            {
                ok &= volume.SetMute(m, ref _context) == 0;
            }

            changed |= ok;
        });

        return changed;
    }

    /// <summary>Teşhis: ses oturumu olan işlemlerin adları (etkin olanlar * ile).</summary>
    public static IReadOnlyList<string> ListSessionProcesses()
    {
        var names = new List<string>();
        ForEachSessionCore(null, (_, active, name) => names.Add(active ? name + "*" : name));
        return names;
    }

    private static void ForEachSession(string processName, Action<ISimpleAudioVolume, bool> action) =>
        ForEachSessionCore(processName, (volume, active, _) => action(volume, active));

    /// <param name="processName">Yalnızca bu işlem adı (null: tümü).</param>
    private static void ForEachSessionCore(string? processName, Action<ISimpleAudioVolume, bool, string> action)
    {
        lock (Gate)
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDeviceCollection? devices = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                if (enumerator.EnumAudioEndpoints(EDataFlow.Render, AudioConstants.DEVICE_STATE_ACTIVE, out devices) != 0 || devices is null)
                {
                    return;
                }

                devices.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    if (devices.Item(i, out var device) != 0 || device is null)
                    {
                        continue;
                    }

                    try
                    {
                        VisitDevice(device, processName, action);
                    }
                    finally
                    {
                        Release(device);
                    }
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                Log.Warn("Uygulama ses oturumları okunamadı", ex);
            }
            finally
            {
                Release(devices);
                Release(enumerator);
            }
        }
    }

    private static void VisitDevice(IMMDevice device, string? processName, Action<ISimpleAudioVolume, bool, string> action)
    {
        var iid = AudioConstants.IID_IAudioSessionManager2;
        if (device.Activate(ref iid, AudioConstants.CLSCTX_ALL, 0, out var managerObject) != 0)
        {
            return;
        }

        var manager = (IAudioSessionManager2)managerObject;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            if (manager.GetSessionEnumerator(out sessions) != 0 || sessions is null)
            {
                return;
            }

            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var session) != 0 || session is null)
                {
                    continue;
                }

                try
                {
                    if (session.GetProcessId(out var pid) != 0 || pid == 0)
                    {
                        continue;
                    }

                    var name = ProcessName(pid);
                    if (processName is not null && !string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    session.GetState(out var state);
                    // Bazı oturumlar ISimpleAudioVolume vermiyor (E_NOINTERFACE): yalnızca o oturumu atla.
                    if (session is ISimpleAudioVolume volume)
                    {
                        action(volume, state == AudioConstants.AudioSessionStateActive, name);
                    }
                }
                finally
                {
                    Release(session);
                }
            }
        }
        finally
        {
            Release(sessions);
            Release(manager);
        }
    }

    private static string ProcessName(uint pid)
    {
        var now = Environment.TickCount64;
        if (!ProcessNames.TryGetValue(pid, out var entry) || now - entry.At > 30_000)
        {
            string name;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                name = process.ProcessName;
            }
            catch (ArgumentException)
            {
                name = string.Empty; // işlem kapanmış
            }
            catch (InvalidOperationException)
            {
                name = string.Empty;
            }

            entry = (name, now);
            ProcessNames[pid] = entry;
            if (ProcessNames.Count > 256)
            {
                ProcessNames.Clear();
            }
        }

        return entry.Name;
    }

    internal static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }
}

/// <summary>
/// Varsayılan çıkış aygıtının (sistem) ses seviyesindeki değişiklikleri bildirir: klavyenin ses tuşları,
/// Windows ses paneli vb. Klavye dinlenmez; yalnızca sonucun kendisi (seviye değişti) izlenir.
/// Varsayılan aygıt değişirse kendini yeni aygıta yeniden bağlar. Olay arka plan iş parçacığından gelir.
/// </summary>
public sealed class SystemVolumeWatcher : IDisposable
{
    private readonly object _gate = new();
    // _last için ayrı kilit: Unregister… çağrısı süren geri çağrıyı bekler; geri çağrı _gate beklerse kilitlenirdi.
    private readonly object _stateGate = new();
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private readonly EndpointCallback _callback;
    private readonly DeviceCallback _deviceCallback;
    private VolumeState? _last;
    private int _rebindPending;
    private bool _disposed;

    private SystemVolumeWatcher()
    {
        _callback = new EndpointCallback(this);
        _deviceCallback = new DeviceCallback(this);
    }

    public event Action<VolumeState>? Changed;

    public VolumeState? Current
    {
        get
        {
            lock (_stateGate)
            {
                return _last;
            }
        }
    }

    /// <summary>COM nesneleri MTA'da oluşturulsun diye iş parçacığı havuzunda başlatılır.</summary>
    public static Task<SystemVolumeWatcher?> StartAsync() => Task.Run(() =>
    {
        try
        {
            var watcher = new SystemVolumeWatcher();
            watcher._enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            watcher._enumerator.RegisterEndpointNotificationCallback(watcher._deviceCallback);
            watcher.Bind();
            return watcher;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("Sistem ses seviyesi izlenemiyor", ex);
            return null;
        }
    });

    private void Bind()
    {
        lock (_gate)
        {
            if (_disposed || _enumerator is null)
            {
                return;
            }

            Unbind();
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) != 0 || device is null)
            {
                lock (_stateGate)
                {
                    _last = null;
                }

                return;
            }

            try
            {
                var iid = AudioConstants.IID_IAudioEndpointVolume;
                if (device.Activate(ref iid, AudioConstants.CLSCTX_ALL, 0, out var obj) != 0)
                {
                    return;
                }

                _endpoint = (IAudioEndpointVolume)obj;
                _endpoint.GetMasterVolumeLevelScalar(out var level);
                _endpoint.GetMute(out var muted);
                lock (_stateGate)
                {
                    _last = new VolumeState(level, muted);
                }

                _endpoint.RegisterControlChangeNotify(_callback);
            }
            finally
            {
                AppVolume.Release(device);
            }
        }
    }

    private void Unbind()
    {
        if (_endpoint is null)
        {
            return;
        }

        try
        {
            _endpoint.UnregisterControlChangeNotify(_callback);
        }
        catch (COMException)
        {
        }

        AppVolume.Release(_endpoint);
        _endpoint = null;
    }

    private void OnNotify(nint data)
    {
        if (data == 0)
        {
            return;
        }

        // AUDIO_VOLUME_NOTIFICATION_DATA: GUID guidEventContext; BOOL bMuted; float fMasterVolume; ...
        var muted = Marshal.ReadInt32(data, 16) != 0;
        var level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
        var state = new VolumeState(Math.Clamp(level, 0, 1), muted);
        lock (_stateGate)
        {
            if (_last is { } last && last.Muted == state.Muted && Math.Abs(last.Level - state.Level) < 0.002f)
            {
                return; // yalnızca kanal dengesi vb. değişti
            }

            _last = state;
        }

        Changed?.Invoke(state);
    }

    private void OnDefaultDeviceChanged()
    {
        // Geri çağrının içinde MMDevice'a yeniden girme: kısa gecikmeyle havuzda bağla.
        if (Interlocked.Exchange(ref _rebindPending, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false);
            Interlocked.Exchange(ref _rebindPending, 0);
            try
            {
                Bind();
            }
            catch (COMException ex)
            {
                Log.Warn("Yeni ses aygıtına bağlanılamadı", ex);
            }
        });
    }

    public void Dispose()
    {
        Task.Run(() =>
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                try
                {
                    Unbind();
                    _enumerator?.UnregisterEndpointNotificationCallback(_deviceCallback);
                }
                catch (COMException)
                {
                }

                AppVolume.Release(_enumerator);
                _enumerator = null;
            }
        }).Wait(TimeSpan.FromSeconds(2));
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class EndpointCallback(SystemVolumeWatcher owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint notifyData)
        {
            try
            {
                owner.OnNotify(notifyData);
            }
            catch (Exception ex)
            {
                Log.Warn("Ses bildirimi işlenemedi", ex);
            }

            return 0;
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class DeviceCallback(SystemVolumeWatcher owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint newState) => 0;

        public int OnDeviceAdded(string deviceId) => 0;

        public int OnDeviceRemoved(string deviceId) => 0;

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow == EDataFlow.Render && role == ERole.Multimedia)
            {
                owner.OnDefaultDeviceChanged();
            }

            return 0;
        }

        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;
    }
}
