using HS2Tools.Windows;

namespace HS2Tools.Services;

/// <summary>
/// 服务容器：状态全部下沉到服务层单例，View 只负责展示与绑定。
/// App 启动时创建全部服务单例，页面/ViewModel 通过它取用。
/// </summary>
public class ServiceContainer : IDisposable
{
    public ServiceContainer()
    {
        Config = new ConfigService();
        AnalysisCache = new AnalysisCacheService(Config.ConfigDir);
        Scanner = new ScannerService();
        Downloads = new DownloadManager(Config.GetProxyString);
        GameLauncher = new GameLauncherService(Config);
        SideloadDb = new SideloadDatabaseService(Config);
        Pages = new PageManager();
    }

    public ConfigService Config { get; }
    public AnalysisCacheService AnalysisCache { get; }
    public ScannerService Scanner { get; }
    public DownloadManager Downloads { get; }
    public GameLauncherService GameLauncher { get; }
    public SideloadDatabaseService SideloadDb { get; }
    public PageManager Pages { get; }

    /// <summary>
    /// 每次运行爬虫创建新实例（对应原版 RunSideloader 时 NewSideloader(proxy)），
    /// 代理设置与当前游戏的 sideload base URL 即时生效。
    /// </summary>
    public ISideloaderService CreateSideloader() =>
        new SideloaderService(Config.GetProxyString(), Config.CurrentProfile.SideloadBaseUrl);

    public void Dispose()
    {
        AnalysisCache.Dispose();
        Config.Dispose();
    }
}
