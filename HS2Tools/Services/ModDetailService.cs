using HS2Tools.ViewModels;

namespace HS2Tools.Services;

/// <summary>从任意页面请求打开当前游戏中的 Mod 详情。</summary>
public class ModDetailService
{
    private readonly ConfigService _config;
    private readonly SideloadDatabaseService _sideloadDb;
    private readonly GameLauncherService _launcher;

    public ModDetailService(ConfigService config, SideloadDatabaseService sideloadDb, GameLauncherService launcher)
    {
        _config = config;
        _sideloadDb = sideloadDb;
        _launcher = launcher;
    }

    public event EventHandler<ModDetailViewModel>? Requested;

    public void Show(string guid)
    {
        _config.Settings.Current.LocalMods.TryGetValue(guid, out var mod);
        _sideloadDb.Database.TryGetValue(guid, out var url);
        var detail = new ModDetailViewModel(guid, mod, url, _launcher, _config.CurrentProfile.SideloadBaseUrl);
        Requested?.Invoke(this, detail);
        _ = detail.LoadAsync();
    }
}
