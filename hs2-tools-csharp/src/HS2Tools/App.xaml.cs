using System.Windows;
using HS2Tools.Services;
using HS2Tools.ViewModels;
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

        // 异常兜底：记日志 + 提示
        DispatcherUnhandledException += (_, args) =>
        {
            LogException(args.Exception);
            MessageBox.Show($"发生未处理异常：{args.Exception.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        var mainWindow = Services.Windows.Show<MainWindow>();
        mainWindow.DataContext = new MainWindowViewModel(Services.Config);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.Dispose();
        base.OnExit(e);
    }

    internal static void LogException(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(ConfigService.DefaultConfigDir, "error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // 日志失败不二次抛错
        }
    }
}
