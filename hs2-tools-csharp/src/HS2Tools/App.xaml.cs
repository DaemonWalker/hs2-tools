using System.Windows;
using HS2Tools.Services;
using HS2Tools.Views;

namespace HS2Tools;

/// <summary>
/// 应用入口：初始化服务容器、异常兜底、启动主窗口。
/// </summary>
public partial class App : Application
{
    public static ServiceContainer Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        Services = new ServiceContainer();
        RestorePreventSleep(Services.Config, Services.GameLauncher); // 启动时恢复防休眠设置

        // 异常兜底：记日志 + 提示
        DispatcherUnhandledException += (_, args) =>
        {
            LogException(args.Exception);
            MessageBox.Show($"发生未处理异常：{args.Exception.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        Services.Windows.Show<MainWindow>(); // ViewModel 由窗口在 SourceInitialized 时自建
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.Dispose();
        base.OnExit(e);
    }

    /// <summary>启动恢复：配置开启防休眠时立即生效（对应原版应用运行期间阻止休眠）</summary>
    internal static void RestorePreventSleep(ConfigService config, GameLauncherService launcher)
    {
        if (config.Settings.PreventSleep)
            launcher.PreventSleep();
    }

    internal static void LogException(Exception ex) => ErrorLog.Log(ex);
}
