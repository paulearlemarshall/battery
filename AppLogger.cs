using System;
using System.IO;

public static class AppLogger
{
    private static readonly object _lock = new object();
    private static readonly bool _enabled = string.Equals(
        Environment.GetEnvironmentVariable("BATTERYTRAY_DEBUG"),
        "1",
        StringComparison.OrdinalIgnoreCase);

    private static readonly string _logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BatteryTray",
        "logs");

    private static readonly string _logPath = Path.Combine(_logDir, "batterytray.log");

    private const long MaxLogSizeBytes = 1024 * 1024; // 1 MB

    public static string LogPath { get { return _logPath; } }

    public static void Info(string message)
    {
        if (!_enabled) return;
        Write("INFO", message);
    }

    public static void Warn(string message)
    {
        if (!_enabled) return;
        Write("WARN", message);
    }

    public static void Error(string message, Exception ex)
    {
        if (!_enabled) return;
        Write("ERROR", message + " | " + ex.GetType().Name + ": " + ex.Message);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                if (!Directory.Exists(_logDir))
                    Directory.CreateDirectory(_logDir);

                RotateIfNeeded();

                File.AppendAllText(
                    _logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + Environment.NewLine);
            }
        }
        catch
        {
            // Never crash app on logging failures.
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logPath))
                return;

            FileInfo fi = new FileInfo(_logPath);
            if (fi.Length < MaxLogSizeBytes)
                return;

            string backupPath = _logPath + ".bak";
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(_logPath, backupPath);
        }
        catch
        {
            // Best-effort rotation.
        }
    }
}
