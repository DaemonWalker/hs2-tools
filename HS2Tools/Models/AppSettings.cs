using System.Text.Json.Serialization;

namespace HS2Tools.Models;

/// <summary>
/// 统一应用配置，对应数据目录下的 settings.json（绿色版 data/ 优先，见 ConfigService.DefaultConfigDir）。
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

    /// <summary>「开始分析」的完整 zipmod 条目（含重复 guid，覆盖 mods/unusedmods；去重/整理的数据源，不再重扫磁盘）</summary>
    public List<ModScanEntry> ModEntries { get; set; } = new();

    /// <summary>人物卡引用统计（guid -> count；ModUsage 是两卡合并口径，整理需分卡区分"仅场景引用"故单独持久化）</summary>
    public Dictionary<string, int> CharaUsage { get; set; } = new();

    /// <summary>场景卡引用统计（guid -> count）</summary>
    public Dictionary<string, int> SceneUsage { get; set; } = new();

    /// <summary>分析时卡片 KKEx 命中的 shader 名（整理时 shader 包豁免依据）</summary>
    public List<string> UsedShaderNames { get; set; } = new();

    /// <summary>最近一次「开始分析」完成时间（去重/整理确认文案标注缓存时点；null = 从未分析）</summary>
    public DateTime? LastAnalysisTime { get; set; }
}

/// <summary>代理信息（对应 Go app.ProxyInfo）</summary>
public class ProxyInfo
{
    public string Uri { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
