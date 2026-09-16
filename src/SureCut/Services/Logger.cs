using System.Text;

namespace SureCut.Services;

/// <summary>LIFE-2: rolling text log in %LOCALAPPDATA%\SureCut\log.txt (1 MB, one backup).</summary>
public static class Logger
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(string folder)
    {
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "log.txt");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : message + Environment.NewLine + ex);

    private static void Write(string level, string message)
    {
        if (_path is null) return;
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var backup = Path.ChangeExtension(_path, ".1.txt");
                    File.Move(_path, backup, overwrite: true);
                }
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
