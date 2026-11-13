using System.Text;

namespace DynamicIsland.Core.Diagnostics;

/// <summary>
/// Küçük, dönen dosya günlüğü. Gizlilik: şarkı adı, sanatçı, albüm gibi medya geçmişi
/// asla yazılmaz; yalnızca durum geçişleri ve hata türleri kaydedilir.
/// </summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static string Directory { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DinamikAda", "logs");

    public static void Configure(string directory)
    {
        lock (Gate)
        {
            Directory = directory;
            _path = null;
        }
    }

    /// <summary>Yalnızca DINAMIKADA_TRACE=1 iken yazılan ayrıntılı tanılama (medya içeriği içermez).</summary>
    public static bool TraceEnabled { get; } = Environment.GetEnvironmentVariable("DINAMIKADA_TRACE") == "1";

    public static void Trace(string message)
    {
        if (TraceEnabled)
        {
            Write("TRACE", message, null);
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder(160);
            sb.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
              .Append(" [").Append(level).Append("] ")
              .Append(message);
            if (ex is not null)
            {
                // Yığın izi kod konumunu gösterir; mesajlarda medya verisi bulunmaz.
                sb.Append(" | ").Append(ex.GetType().Name).Append(" 0x").Append(ex.HResult.ToString("X8"))
                  .Append(": ").Append(ex.Message).AppendLine().Append(ex.StackTrace);
            }

            sb.AppendLine();

            lock (Gate)
            {
                _path ??= Init();
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = _path + ".1";
                    File.Delete(old);
                    File.Move(_path, old);
                }

                File.AppendAllText(_path, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Günlükleme asla uygulamayı düşürmemeli.
        }

        System.Diagnostics.Debug.WriteLine($"[{level}] {message} {ex?.Message}");
    }

    private static string Init()
    {
        System.IO.Directory.CreateDirectory(Directory);
        return Path.Combine(Directory, "app.log");
    }
}
