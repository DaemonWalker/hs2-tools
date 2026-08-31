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
