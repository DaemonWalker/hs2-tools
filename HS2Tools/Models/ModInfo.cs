namespace HS2Tools.Models;

/// <summary>Mod 信息（对应 Go scanner.ModInfo）</summary>
public class ModInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Path { get; set; } = "";

    /// <summary>manifest <![CDATA[<MaterialEditor><Shader Name="..."/>]]> 声明的 shader 名（无声明则为空表）</summary>
    public List<string> ShaderNames { get; set; } = new();
}

/// <summary>
/// 「开始分析」扫描的 zipmod 条目（guid + ModInfo，不折叠重复 guid，覆盖 mods 与 unusedmods 两目录）。
/// 持久化在 settings.json，作为 Mods 窗口去重/整理的数据源缓存（避免每次操作重扫磁盘）。
/// </summary>
public class ModScanEntry
{
    public string Guid { get; set; } = "";
    public ModInfo Info { get; set; } = new();
}
