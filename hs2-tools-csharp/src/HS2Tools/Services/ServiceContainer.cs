using HS2Tools.Windows;

namespace HS2Tools.Services;

/// <summary>
/// 服务容器（迁移方案 §4：状态全部下沉到服务层单例，Window 只是视图）。
/// App 启动时创建全部服务单例，窗口/ViewModel 通过它取用。
/// </summary>
public class ServiceContainer : IDisposable
{
    public ServiceContainer()
    {
        Config = new ConfigService();
        Scanner = new ScannerService();
        Downloads = new DownloadManager(Config.GetProxyString);
        GameLauncher = new GameLauncherService(Config);
        Windows = new WindowManager();
    }

    public ConfigService Config { get; }
    public ScannerService Scanner { get; }
    public DownloadManager Downloads { get; }
    public GameLauncherService GameLauncher { get; }
    public WindowManager Windows { get; }

    /// <summary>
    /// 每次运行爬虫创建新实例（对应原版 RunSideloader 时 NewSideloader(proxy)），
    /// 代理设置即时生效。
    /// </summary>
    public SideloaderService CreateSideloader() => new(Config.GetProxyString());

    public void Dispose() => Config.Dispose();
}
