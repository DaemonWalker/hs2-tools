namespace HS2Tools.Models;

/// <summary>PNG 解析结果（对应 Go scanner.PngParseResult，用于单卡查看器）</summary>
public class PngParseResult
{
    public List<string> ModIDs { get; set; } = new();
    public List<string> CharaNames { get; set; } = new();
    public int GameDataLen { get; set; }
}

/// <summary>单个 PNG 文件的 Mod 结果</summary>
public class PngModResult
{
    public string Path { get; set; } = "";
    public List<string> ModIDs { get; set; } = new();
}

/// <summary>单个 PNG 文件的角色名结果</summary>
public class PngNamesResult
{
    public string Path { get; set; } = "";
    public List<string> Names { get; set; } = new();
}

/// <summary>单个 PNG 文件的缩略图结果</summary>
public class PngImageResult
{
    public string Path { get; set; } = "";
    public string ImageData { get; set; } = "";
}

/// <summary>单个 PNG 文件的页面数据结果（名称+缩略图，C# 版一次读盘）</summary>
public class PngPageDataResult
{
    public string Path { get; set; } = "";
    public List<string> Names { get; set; } = new();
    public string ImageData { get; set; } = "";
}
