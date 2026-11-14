using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Updates;

namespace DynamicIsland.App.Services;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Failed,
}

/// <summary>
/// GitHub Releases'ten güncelleme denetimi ve yerinde güncelleme.
/// Yalnızca api.github.com / github.com'a istek yapılır; gönderilen tek bilgi uygulama adı ve sürümüdür (User-Agent).
/// Yerinde güncelleme: yeni exe aynı klasöre indirilir, GitHub'ın SHA-256 özetiyle doğrulanır, çalışan exe
/// "*.old" olarak yeniden adlandırılır (Windows çalışan exe'nin adının değişmesine izin verir), yenisi yerine konur
/// ve yeniden başlatılır. Bir adım başarısız olursa eski exe geri konur.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string Owner = "qceps";
    private const string Repo = "dinamik-ada";
    private static readonly Uri LatestUri = new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
    public static readonly Uri ReleasesPage = new($"https://github.com/{Owner}/{Repo}/releases");

    private readonly HttpClient _http;
    private int _busy;

    public UpdateService()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        CurrentVersion = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        Flavor = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateFlavor")?.Value ?? "full";

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DinamikAda", CurrentVersion.ToString(3)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public event Action? Changed;

    public Version CurrentVersion { get; }

    /// <summary>"full" (tek dosya, .NET dahil) veya "lite" (.NET gerektirir); yayın sırasında belirlenir.</summary>
    public string Flavor { get; }

    public UpdateState State { get; private set; } = UpdateState.Idle;

    public ReleaseInfo? Available { get; private set; }

    public int DownloadPercent { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Tek dosya olarak yayınlanmış ve klasörü yazılabilirse yerinde güncellenebilir (geliştirme derlemesi değil).</summary>
    public bool CanInstallInPlace
    {
        get
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path) || !string.IsNullOrEmpty(typeof(UpdateService).Assembly.Location))
            {
                return false; // Assembly.Location yalnızca tek dosya yayında boştur
            }

            return IsDirectoryWritable(Path.GetDirectoryName(path)!);
        }
    }

    public string StatusText => State switch
    {
        UpdateState.Checking => "Güncellemeler denetleniyor…",
        UpdateState.UpToDate => $"Güncel (sürüm {CurrentVersion.ToString(3)}).",
        UpdateState.Available => $"Yeni sürüm hazır: {Available!.Version.ToString(3)} (yüklü: {CurrentVersion.ToString(3)}).",
        UpdateState.Downloading => $"İndiriliyor… %{DownloadPercent}",
        UpdateState.Failed => $"Güncelleme başarısız: {Error}",
        _ => $"Sürüm {CurrentVersion.ToString(3)}.",
    };

    /// <summary>Önceki güncellemeden kalan "*.old" / yarım indirme dosyalarını siler.</summary>
    public static void CleanupLeftovers()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        foreach (var leftover in new[] { path + ".old", Path.Combine(Path.GetDirectoryName(path)!, "DinamikAda.update.tmp") })
        {
            for (var attempt = 0; attempt < 10 && File.Exists(leftover); attempt++)
            {
                try
                {
                    File.Delete(leftover);
                }
                catch (IOException)
                {
                    Thread.Sleep(300); // eski işlem henüz kapanıyor olabilir
                }
                catch (UnauthorizedAccessException)
                {
                    break;
                }
            }
        }
    }

    public async Task CheckAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            SetState(UpdateState.Checking);
            using var response = await _http.GetAsync(LatestUri).ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
            {
                // 404: henüz sürüm yok ya da depo özel.
                Fail(response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "GitHub'da yayınlanmış sürüm bulunamadı."
                    : $"GitHub yanıtı {(int)response.StatusCode}.");
                return;
            }

            var info = ReleaseParser.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(true));
            if (info is null)
            {
                Fail("Sürüm bilgisi okunamadı.");
                return;
            }

            Available = ReleaseParser.IsNewer(info.Version, CurrentVersion) ? info : null;
            Log.Info(Available is null ? "Güncelleme denetimi: güncel" : $"Güncelleme denetimi: {info.Version.ToString(3)} mevcut");
            SetState(Available is null ? UpdateState.UpToDate : UpdateState.Available);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Info($"Güncelleme denetlenemedi ({ex.GetType().Name})");
            Fail("GitHub'a ulaşılamadı.");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>İndirir, doğrular, değiştirir ve yeni sürümü başlatır. true: uygulama kapanmalı.</summary>
    public async Task<bool> InstallAsync()
    {
        var release = Available;
        var asset = release?.AssetFor(Flavor);
        if (release is null || asset is null)
        {
            return false;
        }

        if (!CanInstallInPlace || asset.Sha256 is null)
        {
            // Geliştirme derlemesi, yazılamayan klasör ya da doğrulama özeti olmayan dosya: sayfayı aç.
            OpenReleasePage();
            return false;
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return false;
        }

        var exe = Environment.ProcessPath!;
        var folder = Path.GetDirectoryName(exe)!;
        var temp = Path.Combine(folder, "DinamikAda.update.tmp");
        var old = exe + ".old";
        try
        {
            DownloadPercent = 0;
            SetState(UpdateState.Downloading);
            await DownloadAsync(asset, temp).ConfigureAwait(true);

            var hash = await Task.Run(() => HashFile(temp)).ConfigureAwait(true);
            if (!string.Equals(hash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                Fail("İndirilen dosya doğrulanamadı (SHA-256 uyuşmuyor).");
                return false;
            }

            File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Move(temp, exe);
            }
            catch
            {
                File.Move(old, exe); // geri al
                throw;
            }

            Log.Info($"Güncelleme kuruldu: {CurrentVersion.ToString(3)} -> {release.Version.ToString(3)}");
            Process.Start(new ProcessStartInfo(exe, $"--after-update {Environment.ProcessId}") { UseShellExecute = false });
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Warn("Güncelleme kurulamadı", ex);
            TryDelete(temp);
            Fail("İndirme ya da dosya değiştirme başarısız oldu.");
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void OpenReleasePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo((Available?.PageUrl ?? ReleasesPage).ToString()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Sürüm sayfası açılamadı", ex);
        }
    }

    private async Task DownloadAsync(ReleaseAsset asset, string path)
    {
        using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(true);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer).ConfigureAwait(true)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(true);
            done += read;
            var percent = total > 0 ? (int)(done * 100 / total) : 0;
            if (percent != DownloadPercent)
            {
                DownloadPercent = percent;
                Changed?.Invoke();
            }
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static bool IsDirectoryWritable(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".dinamikada-yazma-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private void Fail(string message)
    {
        Error = message;
        SetState(UpdateState.Failed);
    }

    private void SetState(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    public void Dispose() => _http.Dispose();
}
