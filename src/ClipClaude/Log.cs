namespace ClipClaude;

/// <summary>
/// Простой файловый лог. Содержимое буфера обмена сюда не пишется — только метаданные.
/// </summary>
internal static class Log
{
    private static readonly object Sync = new();
    private static string? _dir;

    public static void Init()
    {
        try
        {
            _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipClaude", "logs");
            Directory.CreateDirectory(_dir);
            foreach (var file in Directory.EnumerateFiles(_dir, "clipclaude-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-7))
                    File.Delete(file);
            }
        }
        catch
        {
            _dir = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        if (_dir is null) return;
        try
        {
            lock (Sync)
            {
                var path = Path.Combine(_dir, $"clipclaude-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // лог не должен ронять приложение
        }
    }
}
