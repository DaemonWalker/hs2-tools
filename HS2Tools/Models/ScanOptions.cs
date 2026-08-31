namespace HS2Tools.Models;

/// <summary>扫描选项（对应 Go scanner.Options）</summary>
public class ScanOptions
{
    /// <summary>排除的目录名（子串匹配）</summary>
    public List<string> ExcludeDir { get; set; } = new();

    /// <summary>目标扩展名（大小写不敏感，含点，如 ".png"）</summary>
    public List<string> TargetExtension { get; set; } = new();
}
