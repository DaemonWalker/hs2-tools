namespace HS2Tools.Models;

/// <summary>
/// 游戏档案：把各游戏（HS2 / 恋活 KK / 恋活 Sunshine KKS）的写死知识集中成静态描述符。
/// exe 名、目录相对路径、卡片标记字节、Parameter 名字段键、sideload 数据源都在此处。
/// KK 与 KKS 角色卡格式完全相同（同为【KoiKatuChara】+ KKEx，KKS 仅多 About 块，
/// 基准：kkloader/KoikatuCharaLoader 对两者共用 KoikatuCharaData），差异仅在 exe/目录/sideload 分区。
/// 注意：KK/KKS 的 exe 名按常见安装版填写（Koikatu.exe / KoikatsuSunshine.exe），
/// Steam 版（KoikatsuParty.exe）等变体以真实环境基准验证后调整。
/// </summary>
public class GameProfile
{
    /// <summary>游戏 ID（配置键）："hs2" / "kk" / "kks"</summary>
    public required string Id { get; init; }

    /// <summary>显示名（UI 切换器/标题文案）</summary>
    public required string DisplayName { get; init; }

    /// <summary>游戏主程序 exe 名（用于路径校验与启动）</summary>
    public required string GameExeName { get; init; }

    /// <summary>工作室 exe 名</summary>
    public required string StudioExeName { get; init; }

    /// <summary>角色卡目录（相对游戏根目录）</summary>
    public required string CharaDirRelative { get; init; }

    /// <summary>场景目录（相对游戏根目录）</summary>
    public required string SceneDirRelative { get; init; }

    /// <summary>Mod 目录（相对游戏根目录）</summary>
    public required string ModsDirRelative { get; init; }

    /// <summary>Mod 下载子目录（相对游戏根目录）</summary>
    public required string ModDownloadDirRelative { get; init; }

    /// <summary>角色卡卡头标记（UTF-8，如【AIS_Chara】/【KoiKatuChara】）</summary>
    public required string CharaMarker { get; init; }

    /// <summary>坐标（服装）卡卡头标记</summary>
    public required string ClothesMarker { get; init; }

    /// <summary>Parameter 块中角色名的字段键（按序拼接，HS2 为 fullname；KK/KKS 为 lastname+firstname）</summary>
    public required string[] NameKeys { get; init; }

    /// <summary>
    /// 角色卡 blob 信封差异：true（KK/KKS）= version 之后是脸部特写 PNG（int32 长度 + 字节），
    /// 随后直接是 BlockHeader；false（HS2）= version 之后是 lang/userID/dataID，无脸部 PNG。
    /// （KK 真实卡实测：productNo→【KoiKatuChara】→version→facePng→BlockHeader，无 lang/userID/dataID）
    /// </summary>
    public required bool CharaBlobHasFacePng { get; init; }

    /// <summary>Sideload 数据源 ID（数据库文件归属）：HS2 独占 "hs2"；KK/KKS 共享 KKEC 库 "kkec"</summary>
    public required string SideloadSourceId { get; init; }

    /// <summary>Sideload 爬取起点 / 下载 URL 拼接 base（爬虫从根递归下钻全部子目录）</summary>
    public required string SideloadBaseUrl { get; init; }
}

/// <summary>游戏档案静态注册表。未知 ID 一律回退 HS2（保持旧行为）。</summary>
public static class GameProfiles
{
    public static readonly GameProfile Hs2 = new()
    {
        Id = "hs2",
        DisplayName = "Honey Select 2",
        GameExeName = "HoneySelect2.exe",
        StudioExeName = "StudioNEOV2.exe",
        CharaDirRelative = @"UserData\chara\female",
        SceneDirRelative = @"UserData\Studio\scene",
        ModsDirRelative = "mods",
        ModDownloadDirRelative = @"mods\hs2-tool-download",
        CharaMarker = "【AIS_Chara】",
        ClothesMarker = "【AIS_Clothes】",
        NameKeys = ["fullname"],
        CharaBlobHasFacePng = false,
        SideloadSourceId = "hs2",
        SideloadBaseUrl = "https://sideload.betterrepack.com/download/AISHS2/",
    };

    public static readonly GameProfile Kk = new()
    {
        Id = "kk",
        DisplayName = "恋活（Koikatsu）",
        GameExeName = "Koikatu.exe",
        StudioExeName = "CharaStudio.exe",
        CharaDirRelative = @"UserData\chara\female",
        SceneDirRelative = @"UserData\Studio\scene",
        ModsDirRelative = "mods",
        ModDownloadDirRelative = @"mods\hs2-tool-download",
        CharaMarker = "【KoiKatuChara】",
        ClothesMarker = "【KoiKatuClothes】",
        NameKeys = ["lastname", "firstname"],
        CharaBlobHasFacePng = true,
        SideloadSourceId = "kkec",
        SideloadBaseUrl = "https://sideload.betterrepack.com/download/KKEC/",
    };

    public static readonly GameProfile Kks = new()
    {
        Id = "kks",
        DisplayName = "恋活 Sunshine",
        GameExeName = "KoikatsuSunshine.exe",
        StudioExeName = "CharaStudio.exe",
        CharaDirRelative = @"UserData\chara\female",
        SceneDirRelative = @"UserData\Studio\scene",
        ModsDirRelative = "mods",
        ModDownloadDirRelative = @"mods\hs2-tool-download",
        CharaMarker = "【KoiKatuChara】",
        ClothesMarker = "【KoiKatuClothes】",
        NameKeys = ["lastname", "firstname"],
        CharaBlobHasFacePng = true,
        SideloadSourceId = "kkec",
        SideloadBaseUrl = "https://sideload.betterrepack.com/download/KKEC/",
    };

    public static readonly IReadOnlyList<GameProfile> All = [Hs2, Kk, Kks];

    /// <summary>按 ID 取档案；未知/空 ID 回退 HS2（旧配置无此字段时的默认行为）</summary>
    public static GameProfile Get(string? id) =>
        All.FirstOrDefault(p => p.Id == id) ?? Hs2;
}
