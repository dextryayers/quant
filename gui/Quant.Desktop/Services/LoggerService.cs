using System;
using System.IO;

namespace Quant.Desktop.Services;

public static class LoggerService
{
    private static readonly object Gate = new();
    private static string _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quant", "logs");

    public static void Init(string? workspaceRoot = null)
    {
        try
        {
            var dir = workspaceRoot != null
                ? Path.Combine(workspaceRoot, ".quant", "logs")
                : _logDir;
            Directory.CreateDirectory(dir);
            _logDir = dir;
            Rotate();
        }
        catch { }
    }

    public static void Info(string scope, string message)
    {
        Write("INFO", scope, message);
    }

    public static void Error(string scope, string message)
    {
        Write("ERROR", scope, message);
    }

    private static void Write(string level, string scope, string message)
    {
        try
        {
            lock (Gate)
            {
                var file = Path.Combine(_logDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss} [{level}] [{scope}] {message}{Environment.NewLine}");
                var info = new FileInfo(file);
                if (info.Length > 5_000_000)
                    File.Move(file, file + ".1", true);
            }
        }
        catch { }
    }

    private static void Rotate()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_logDir, "*.log"))
            {
                if (File.GetCreationTime(f) < DateTime.Now.AddDays(-7))
                    File.Delete(f);
            }
        }
        catch { }
    }
}
