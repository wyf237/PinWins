using System;
using System.IO;

namespace PinWins.Core;

/// <summary>
/// 轻量文件日志，用于诊断运行时异常。
/// </summary>
internal static class Logger
{
    private static readonly object Sync = new();
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PinWins");
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "PinWins");
    private static readonly string LogPath = Path.Combine(AppDataDir, $"app_{DateTime.Now:yyyyMMdd}.log");
    private static readonly string TempLogPath = Path.Combine(TempDir, $"app_{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null)
    {
        var text = ex != null ? $"{message}: {ex}" : message;
        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";

                // 优先写入 AppData；若失败（如文件被锁定）则回退到临时目录。
                if (!TryWrite(LogPath, line))
                    TryWrite(TempLogPath, line);
            }
        }
        catch
        {
            // 忽略日志写入失败。
        }
    }

    private static bool TryWrite(string path, string line)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // 使用 FileShare.ReadWrite 避免多实例或杀毒软件锁定文件。
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs);
            sw.Write(line);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
