using System.Text;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/app_logging.py - a small rotating runtime log file, next
/// to the exe/config (same CWD-relative, portable-app reasoning as
/// ConfigManager.ConfigFileName). Minimal hand-rolled rotation rather than
/// a logging library dependency, matching the size of what Python's own
/// RotatingFileHandler usage here actually needed.
/// </summary>
public static class AppLogging
{
    public const string LogFileName = "flightops_hub.log";

    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly object Lock = new();

    public static void Info(string path, string message) => Write(path, "INFO", message);

    public static void Warning(string path, string message) => Write(path, "WARNING", message);

    public static void Error(string path, string message) => Write(path, "ERROR", message);

    private static void Write(string path, string level, string message)
    {
        lock (Lock)
        {
            try
            {
                RotateIfNeeded(path);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff} {level} flightops_hub: {message}{Environment.NewLine}";
                File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch
            {
                // Logging must never be the reason the app crashes.
            }
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length < MaxBytes) return;

        var backupPath = path + ".1";
        try
        {
            File.Delete(backupPath);
        }
        catch
        {
            // Nothing to clean up.
        }
        File.Move(path, backupPath);
    }

    /// <summary>
    /// Best-effort tail of the current log file, for bundling into a
    /// diagnostics export - never throws, returns "" if the file can't be
    /// read (e.g. logging was never set up, or this is a fresh install).
    /// </summary>
    public static string ReadRecentLines(string path, int maxLines = 200)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            var start = Math.Max(0, lines.Length - maxLines);
            return string.Join(Environment.NewLine, lines[start..]) + (lines.Length > 0 ? Environment.NewLine : "");
        }
        catch
        {
            return "";
        }
    }
}
