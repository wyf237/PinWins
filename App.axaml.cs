using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PinWins.Core;

namespace PinWins;

public partial class App : Application
{
    public static AppController? Controller { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 本程序常驻托盘，不依赖任何可见窗口：
            // 必须使用 OnExplicitShutdown，否则关闭最后一个窗口后应用会退出。
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            desktop.Exit += (_, e) => Logger.Info($"应用退出, ExitCode={e.ApplicationExitCode}");

            Controller = new AppController(desktop);
            Controller.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
