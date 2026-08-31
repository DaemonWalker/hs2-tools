using System.Text.Json.Serialization;

namespace HS2Tools.Models;

/// <summary>
/// 统一应用配置，对应 %AppData%/hs2-tools/settings.json。
/// 替代原版的 IndexedDB / localStorage / Go 端 JSON 三处分散存储。
/// 多游戏：游戏路径/收藏/本地 Mod 缓存/使用统计按游戏隔离在 Games 下，
/// CurrentGame 指向当前生效的游戏档案（GameProfiles 注册表）。
/// 旧版单游戏顶层字段（gamePath/favorites/localMods/modUsage）由 ConfigService 加载时迁移。
/// </summary>
public class AppSettings
{
    /// <summary>当前游戏 ID（GameProfiles.All 中的 Id）</summary>
    public string CurrentGame { get; set; } = GameProfiles.Hs2.Id;

    /// <summary>各游戏的独立数据，键为 GameProfile.Id（"hs2"/"kk"/"kks"）</summary>
    public Dictionary<string, GameData> Games { get; set; } = new();

    public ProxyInfo Proxy { get; set; } = new();

    /// <summary>是否阻止 Windows 休眠</summary>
    public bool PreventSleep { get; set; }

    /// <summary>当前游戏的数据（不存在则就地创建，保证调用方无需判空）</summary>
    [JsonIgnore]
    public GameData Current => Games.TryGetValue(CurrentGame, out var g) ? g : Games[CurrentGame] = new GameData();
}

/// <summary>单个游戏的隔离数据（路径 + 该游戏下的收藏/缓存/统计）</summary>
public class GameData
{
    /// <summary>游戏根目录（含游戏 exe，如 HoneySelect2.exe / Koikatu.exe）</summary>
    public string GamePath { get; set; } = "";

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
