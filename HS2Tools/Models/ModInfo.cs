namespace HS2Tools.Models;

/// <summary>Mod 信息（对应 Go scanner.ModInfo）</summary>
public class ModInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Path { get; set; } = "";
}
