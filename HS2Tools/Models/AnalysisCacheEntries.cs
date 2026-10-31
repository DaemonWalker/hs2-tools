namespace HS2Tools.Models;

public class CardAnalysisEntry
{
    public string Scope { get; set; } = "";
    public string Path { get; set; } = "";
    public long FileLength { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
    public List<string> Names { get; set; } = new();
    public List<string> ModIDs { get; set; } = new();
    public int GameDataLen { get; set; }

    public PngParseResult ToResult() => new()
    {
        Path = Path,
        CharaNames = new List<string>(Names),
        ModIDs = new List<string>(ModIDs),
        GameDataLen = GameDataLen,
        FileLength = FileLength,
        LastWriteTimeUtcTicks = LastWriteTimeUtcTicks,
    };

    public static CardAnalysisEntry FromResult(string scope, PngParseResult result) => new()
    {
        Scope = scope,
        Path = result.Path,
        FileLength = result.FileLength,
        LastWriteTimeUtcTicks = result.LastWriteTimeUtcTicks,
        Names = result.CharaNames,
        ModIDs = result.ModIDs,
        GameDataLen = result.GameDataLen,
    };
}

public class ZipModAnalysisEntry
{
    public string Scope { get; set; } = "";
    public string Path { get; set; } = "";
    public long FileLength { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
    public string Guid { get; set; } = "";
    public ModInfo Info { get; set; } = new();

    public static ZipModAnalysisEntry Create(string scope, string guid, ModInfo info) => new()
    {
        Scope = scope,
        Path = info.Path,
        Guid = guid,
        Info = info,
        FileLength = info.FileLength > 0 ? info.FileLength : new FileInfo(info.Path).Length,
        LastWriteTimeUtcTicks = info.LastWriteTimeUtcTicks > 0
            ? info.LastWriteTimeUtcTicks
            : File.GetLastWriteTimeUtc(info.Path).Ticks,
    };
}
