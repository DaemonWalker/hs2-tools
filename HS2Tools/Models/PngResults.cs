namespace HS2Tools.Models;

/// <summary>PNG 解析结果（对应 Go scanner.PngParseResult，用于单卡查看器）</summary>
public class PngParseResult
{
    public string Path { get; set; } = "";
    public List<string> ModIDs { get; set; } = new();
    public List<string> CharaNames { get; set; } = new();
    public int GameDataLen { get; set; }
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
    public int ImageLength { get; set; }
    public long FileLength { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
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
