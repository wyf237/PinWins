using System;
using System.IO;
using System.Threading;
using Avalonia;
using PinWins.Interop;

namespace PinWins;

internal static class Program
{
    private const string InstanceMutexName = "PinWins.SingleInstance.6C2E1F";

    [STAThread]
    public static void Main(string[] args)
    {
        // 单实例：多开会导致多个进程重复注册热键、显示多个托盘图标与多个设置窗口。
        var mutex = new Mutex(false, InstanceMutexName);
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.Zero, false);
        }
        catch (AbandonedMutexException)
        {
            // 上一个实例异常退出，忽略并接管。
            acquired = true;
        }

        if (!acquired)
        {
            // 通知已运行的实例显示设置窗口，然后退出。
            var hwnd = NativeMethods.FindWindow("PinWins.MessageWindow.Class", "PinWins");
            if (hwnd != IntPtr.Zero)
                NativeMethods.PostMessage(hwnd, NativeMethods.WM_APP_SHOW_SETTINGS, IntPtr.Zero, IntPtr.Zero);

            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("UnhandledException", e.ExceptionObject);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog("MainThrew", ex);
            throw;
        }
        finally
        {
            GC.KeepAlive(mutex);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WriteCrashLog(string source, object? exception)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinWins");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"crash_{DateTime.Now:yyyyMMdd}.log");
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {exception}{Environment.NewLine}";
            File.AppendAllText(path, text);
        }
        catch
        {
            // 忽略日志写入失败。
        }
    }
}
