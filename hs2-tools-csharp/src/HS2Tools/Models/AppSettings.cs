namespace HS2Tools.Models;

/// <summary>
/// 统一应用配置，对应 %AppData%/hs2-tools/settings.json。
/// 替代原版的 IndexedDB / localStorage / Go 端 JSON 三处分散存储。
/// </summary>
public class AppSettings
{
    /// <summary>游戏根目录（含 HoneySelect2.exe）</summary>
    public string GamePath { get; set; } = "";

    public ProxyInfo Proxy { get; set; } = new();

    /// <summary>是否阻止 Windows 休眠</summary>
    public bool PreventSleep { get; set; }

    /// <summary>收藏卡片完整路径（角色/场景共用）</summary>
    public List<string> Favorites { get; set; } = new();

    /// <summary>本地 Mod 扫描结果缓存（guid -> ModInfo）</summary>
    public Dictionary<string, ModInfo> LocalMods { get; set; } = new();

    /// <summary>Mod 使用次数（guid -> count）</summary>
    public Dictionary<string, int> ModUsage { get; set; } = new();
}

/// <summary>代理信息（对应 Go app.ProxyInfo）</summary>
public class ProxyInfo
{
    public string Uri { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
