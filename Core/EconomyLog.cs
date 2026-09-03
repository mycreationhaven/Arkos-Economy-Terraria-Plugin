namespace ArkoviaEconomy.Core;

/// <summary>Plugin-owned daily files; never forwards routine output to the server console.</summary>
public static class EconomyLog
{
    private static readonly object Gate = new();
    private static string? _directory;
    private static DateTime _lastFailure;
    private static DateTime _nextCleanup;
    public static void Initialize(string pluginDirectory)
    {
        lock (Gate) { _directory = Path.Combine(pluginDirectory, "logs"); Directory.CreateDirectory(_directory); }
    }
    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);
    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            if (_directory is null) return;
            try
            {
                var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var path = Path.Combine(_directory, $"arkovia-{day}.log");
                if (File.Exists(path) && new FileInfo(path).Length >= 10 * 1024 * 1024)
                    File.Move(path, Path.Combine(_directory, $"arkovia-{day}-{Guid.NewGuid():N}.log"));
                File.AppendAllText(path, $"{DateTime.UtcNow:O} [{level}] {message.Replace("\r", "\\r").Replace("\n", "\\n")}{Environment.NewLine}");
                if(DateTime.UtcNow >= _nextCleanup)
                {
                    _nextCleanup = DateTime.UtcNow.AddHours(1);
                    foreach (var file in Directory.EnumerateFiles(_directory, "arkovia-*.log"))
                        if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14)) File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A broken log destination must be visible, but cannot flood the console.
                if (DateTime.UtcNow - _lastFailure > TimeSpan.FromMinutes(10))
                {
                    _lastFailure = DateTime.UtcNow;
                    TShockAPI.TShock.Log?.ConsoleError("[ArkoviaEconomy] Cannot write plugin log: " + ex.GetType().Name);
                }
            }
        }
    }
}
