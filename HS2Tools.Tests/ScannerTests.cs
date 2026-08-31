using System.Text;
using HS2Tools.Services;
using Xunit.Abstractions;

namespace HS2Tools.Tests;

public class ScannerTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();
    private readonly ScannerService _svc = new();
    private readonly ITestOutputHelper _output;

    public ScannerTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => TestAssets.DeleteDir(_dir);

    // ==================== 阶段 4：真实环境基准 ====================

    /// <summary>真实 mods 目录（数千 zipmod）全量扫描耗时；设 HS2_REAL_MODS_DIR 时执行</summary>
    [SkippableFact]
    public async Task RealModsDir_ScanBenchmark()
    {
        var dir = Environment.GetEnvironmentVariable("HS2_REAL_MODS_DIR");
        Skip.If(string.IsNullOrWhiteSpace(dir), "未设置 HS2_REAL_MODS_DIR（真实 mods 目录）");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var files = _svc.ScanDirectory(dir, new() { TargetExtension = { ".zipmod" } });
        var results = await _svc.ReadZipModBatchAsync(files);
        sw.Stop();

        _output.WriteLine($"{files.Count} zipmods → {results.Count} guids, {sw.ElapsedMilliseconds} ms");
        Assert.True(files.Count > 0);
        Assert.True(results.Count > 0);
    }

    // ==================== ScanDirectory ====================

    [Fact]
    public void ScanDirectory_FiltersExtensions_CaseInsensitive()
    {
        File.WriteAllText(Path.Combine(_dir, "a.png"), "x");
        File.WriteAllText(Path.Combine(_dir, "b.PNG"), "x");
        File.WriteAllText(Path.Combine(_dir, "c.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "sub", "d.png"), "x");

        var files = _svc.ScanDirectory(_dir, new() { TargetExtension = new() { ".png" } });

        Assert.Equal(3, files.Count);
        Assert.All(files, f => Assert.EndsWith(".png", f, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanDirectory_ExcludesDirs_BySubstring()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "hs_tools_abc"));
        File.WriteAllText(Path.Combine(_dir, "hs_tools_abc", "a.png"), "x");
        Directory.CreateDirectory(Path.Combine(_dir, "keep"));
        File.WriteAllText(Path.Combine(_dir, "keep", "b.png"), "x");
        File.WriteAllText(Path.Combine(_dir, "root.png"), "x");

        var files = _svc.ScanDirectory(_dir, new() { ExcludeDir = new() { "hs_tools" }, TargetExtension = new() { ".png" } });

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.EndsWith("root.png"));
        Assert.Contains(files, f => f.Contains("keep"));
    }

    [Fact]
    public void ScanDirectory_RootExcluded_ReturnsEmpty()
    {
        // Go filepath.Walk 对根目录同样做排除判定
        var root = Path.Combine(_dir, "foo_hs_tools");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.png"), "x");

        var files = _svc.ScanDirectory(root, new() { ExcludeDir = new() { "hs_tools" } });

        Assert.Empty(files);
    }

    [Fact]
    public void ScanDirectory_NoOptions_ReturnsAll()
    {
        File.WriteAllText(Path.Combine(_dir, "a.png"), "x");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "x");

        var files = _svc.ScanDirectory(_dir);

        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void ScanDirectory_MissingDir_ReturnsEmpty()
    {
        var files = _svc.ScanDirectory(Path.Combine(_dir, "nonexistent"));
        Assert.Empty(files);
    }

    [Fact]
    public void ScanDirectory_LexicalOrder_MatchesGoWalk()
    {
        // Go filepath.Walk 每目录按词典序：子目录内容在其之后的兄弟条目之前
        Directory.CreateDirectory(Path.Combine(_dir, "a"));
        File.WriteAllText(Path.Combine(_dir, "a", "z.png"), "x");
        File.WriteAllText(Path.Combine(_dir, "b.png"), "x");

        var files = _svc.ScanDirectory(_dir, new() { TargetExtension = new() { ".png" } });

        Assert.Equal(2, files.Count);
        Assert.EndsWith(Path.Combine("a", "z.png"), files[0]);
        Assert.EndsWith("b.png", files[1]);
    }

    // ==================== searchBuffer ====================

    [Fact]
    public void SearchBuffer_StripsFirstAndLastByte()
    {
        // content = 0xAA "ABC" 0xBB（长度 5 > 2）→ 去首尾 → "ABC"
        var data = "fullname"u8.ToArray()
            .Concat(new byte[] { 0xAA })
            .Concat("ABC"u8.ToArray())
            .Concat(new byte[] { 0xBB })
            .Concat("personality"u8.ToArray())
            .ToArray();
        var result = ScannerService.SearchBuffer(
            "fullname"u8.ToArray(), "personality"u8.ToArray(), data);
        Assert.Equal(new[] { "ABC" }, result);
    }

    [Fact]
    public void SearchBuffer_ContentTooShort_KeepsAsIs()
    {
        // content 长度 2 → 不去首尾
        var data = Encoding.UTF8.GetBytes("fullnameABpersonality");
        var result = ScannerService.SearchBuffer("fullname"u8.ToArray(), "personality"u8.ToArray(), data);
        Assert.Equal(new[] { "AB" }, result);
    }

    [Fact]
    public void SearchBuffer_EmptyContent_Skipped()
    {
        var data = Encoding.UTF8.GetBytes("fullnamepersonality");
        var result = ScannerService.SearchBuffer("fullname"u8.ToArray(), "personality"u8.ToArray(), data);
        Assert.Empty(result);
    }

    [Fact]
    public void SearchBuffer_TrimsWhitespace()
    {
        var data = Encoding.UTF8.GetBytes("fullname\x01  padded  \x02personality");
        var result = ScannerService.SearchBuffer("fullname"u8.ToArray(), "personality"u8.ToArray(), data);
        Assert.Equal(new[] { "padded" }, result);
    }

    [Fact]
    public void SearchBuffer_Dedupes()
    {
        var marker = TestAssets.NameMarker("角色A");
        var data = marker.Concat(marker).ToArray();
        var result = ScannerService.SearchBuffer("fullname"u8.ToArray(), "personality"u8.ToArray(), data);
        Assert.Single(result);
        Assert.Equal("角色A", result[0]);
    }

    [Fact]
    public void SearchBuffer_MultipleHits_AndContinuesAfterEnd()
    {
        var data = TestAssets.NameMarker("甲").Concat(TestAssets.NameMarker("乙")).ToArray();
        var result = ScannerService.SearchBuffer("fullname"u8.ToArray(), "personality"u8.ToArray(), data);
        Assert.Equal(2, result.Count);
        Assert.Contains("甲", result);
        Assert.Contains("乙", result);
    }

    [Fact]
    public void SearchBuffer_NoEndMarker_Stops()
    {
        var data = Encoding.UTF8.GetBytes("fullnameABC");
        var result = ScannerService.SearchBuffer("fullname"u8.ToArray(), "personality"u8.ToArray(), data);
        Assert.Empty(result);
    }

    // ==================== PNG 读取（结构化解析主路径） ====================

    [Fact]
    public void ReadPngNames_ExtractsUtf8Name()
    {
        var path = TestAssets.WritePng(_dir, "card.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(new[] { "测试角色" }, Array.Empty<string>()));
        var names = _svc.ReadPngNames(path);
        Assert.Equal(new[] { "测试角色" }, names);
    }

    [Fact]
    public void ReadPngNames_ParameterAndParameter2_InOrder()
    {
        var path = TestAssets.WritePng(_dir, "card.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(new[] { "第一", "第二" }, Array.Empty<string>()));
        var names = _svc.ReadPngNames(path);
        Assert.Equal(new[] { "第一", "第二" }, names);
    }

    [Fact]
    public void ReadPngMods_ExtractsGuids()
    {
        var path = TestAssets.WritePng(_dir, "card.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(Array.Empty<string>(), new[] { "com.mod.a", "com.mod.b" }));
        var mods = _svc.ReadPngMods(path);
        Assert.Equal(new[] { "com.mod.a", "com.mod.b" }, mods);
    }

    [Fact]
    public void ReadPngMods_KkexNamespaceIsolation()
    {
        // 其他插件数据里含同名 "ModID" 键：不得误报（KKEx 插件 ID 命名空间隔离）
        var path = TestAssets.WritePng(_dir, "card.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(
                Array.Empty<string>(), new[] { "com.mod.real" }, new[] { "com.other.fake" }));
        var mods = _svc.ReadPngMods(path);
        Assert.Equal(new[] { "com.mod.real" }, mods);
    }

    [Fact]
    public void ReadPngMods_ClothesCard_ExtractsFromKkexTrailer()
    {
        // 坐标卡：【AIS_Clothes】头，mod 数据在文件尾 KKEx trailer
        var path = TestAssets.WritePng(_dir, "clothes.png",
            TestAssets.PngPrefix(), TestAssets.BuildClothesDataRegion(new[] { "com.mod.outfit" }));
        var mods = _svc.ReadPngMods(path);
        Assert.Equal(new[] { "com.mod.outfit" }, mods);
    }

    [Fact]
    public void ParsePngData_Scene_TwoCharaBlobsAndTrailer()
    {
        // 场景：两个内嵌 chara blob + KKEx trailer
        var path = TestAssets.WritePng(_dir, "scene.png",
            TestAssets.PngPrefix(), TestAssets.BuildSceneDataRegion("角色甲", "角色乙", new[] { "com.mod.scene" }));
        var result = _svc.ParsePngData(path);
        Assert.Equal(new[] { "角色甲", "角色乙" }, result.CharaNames);
        Assert.Equal(new[] { "com.mod.scene" }, result.ModIDs);
    }

    // ==================== PNG 读取（回退路径） ====================

    [Fact]
    public void ReadPngNames_NoMarker_FallsBackToByteScan()
    {
        // 无【AIS_Chara】标记 → 结构解析失败，回退旧 SearchBuffer（仅数据区）
        var path = TestAssets.WritePng(_dir, "card.png", TestAssets.PngPrefix(), TestAssets.NameMarker("回退角色"));
        var names = _svc.ReadPngNames(path);
        Assert.Equal(new[] { "回退角色" }, names);
    }

    [Fact]
    public void ReadPngNames_MarkerInImageRegion_NotReported()
    {
        // 回退路径不再扫 PNG 图像字节：图像区（IEND 之前）里的标记不命中，数据区里的才命中
        var prefixHead = TestAssets.PngPrefix()[..^8]; // 去掉末尾 IEND + CRC
        var path = TestAssets.WritePng(_dir, "card.png",
            prefixHead, TestAssets.NameMarker("图像区假名"),
            "IEND"u8.ToArray(), new byte[] { 0xAE, 0x42, 0x60, 0x82 },
            TestAssets.NameMarker("数据区真名"));
        var names = _svc.ReadPngNames(path);
        Assert.Equal(new[] { "数据区真名" }, names);
    }

    [Fact]
    public void ReadPngNames_KkCharaCard_StructuralParse()
    {
        // KK 角色卡按卡头标记自动识别：不依赖"当前游戏"状态，结构解析直接出拼接名
        var path = TestAssets.WritePng(_dir, "kk.png",
            TestAssets.PngPrefix(), TestAssets.BuildKkCharaDataRegion("白峰", "一乃", new[] { "com.kk.mod" }));
        Assert.Equal(new[] { "白峰 一乃" }, _svc.ReadPngNames(path));
        Assert.Equal(new[] { "com.kk.mod" }, _svc.ReadPngMods(path));
    }

    [Fact]
    public void ReadPngNames_KkPattern_FallsBackToKkByteScan()
    {
        // 无结构标记的 KK 字节模式：HS2 模式（fullname..personality）无结果，
        // 回退 KK 模式（lastname..firstname / firstname..nickname 两段合并）
        var path = TestAssets.WritePng(_dir, "card.png",
            TestAssets.PngPrefix(), TestAssets.KkNameMarker("白峰", "一乃"));
        var names = _svc.ReadPngNames(path);
        Assert.Equal(new[] { "白峰", "一乃" }, names);
    }

    [Fact]
    public void ReadPngNames_Hs2PatternHits_KkFallbackSkipped()
    {
        // HS2 模式有结果时不再尝试 KK 模式（优先 fullname）
        var path = TestAssets.WritePng(_dir, "card.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("HS2角色"), TestAssets.KkNameMarker("白峰", "一乃"));
        var names = _svc.ReadPngNames(path);
        Assert.Equal(new[] { "HS2角色" }, names);
    }

    [Fact]
    public void ReadPngNames_NoIend_KkPattern_WholeFileFallback()
    {
        // 无 IEND（非卡片文件）：整体视作数据区回退扫描，KK 模式同样生效
        var path = TestAssets.WritePng(_dir, "card.png", TestAssets.KkNameMarker("白峰", "一乃"));
        var names = _svc.ReadPngNames(path);
        Assert.Equal(new[] { "白峰", "一乃" }, names);
    }

    [Fact]
    public void ReadPngMods_CorruptedBlob_NoThrow_FallsBack()
    {
        // 有【AIS_Chara】标记但 blob 损坏（长度前缀对、后续全是垃圾）→ 不抛，回退字节扫描
        var corrupt = new byte[] { 0x0F }
            .Concat("【AIS_Chara】"u8.ToArray())
            .Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })
            .ToArray();
        var path = TestAssets.WritePng(_dir, "card.png",
            TestAssets.PngPrefix(), corrupt, TestAssets.ModMarker("com.mod.fallback"));
        var mods = _svc.ReadPngMods(path);
        Assert.Equal(new[] { "com.mod.fallback" }, mods);
    }

    [Fact]
    public void ReadPngMods_NonPngFile_ReturnsEmpty()
    {
        var path = TestAssets.WritePng(_dir, "card.txt", TestAssets.PngPrefix(), TestAssets.ModMarker("com.mod.a"));
        Assert.Empty(_svc.ReadPngMods(path));
    }

    [Fact]
    public void ReadPngImage_TruncatesAtLastIend()
    {
        // 游戏数据里再放一个 "IEND"：必须取最后一个
        var prefix = TestAssets.PngPrefix();
        var tail = Encoding.UTF8.GetBytes("gamedata-IEND-more");
        var path = TestAssets.WritePng(_dir, "card.png", prefix, tail);

        var base64 = _svc.ReadPngImage(path);
        var bytes = Convert.FromBase64String(base64);

        // 期望：截到最后一个 IEND 的 'D' 之后 + 4 字节 CRC（完整 PNG）
        var expectedLen = prefix.Length + "gamedata-".Length + "IEND".Length + 4;
        Assert.Equal(expectedLen, bytes.Length);
    }

    [Fact]
    public void ReadPngImage_NoIend_ReturnsEmpty()
    {
        var path = Path.Combine(_dir, "bad.png");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("no marker here"));
        Assert.Equal("", _svc.ReadPngImage(path));
    }

    [Fact]
    public void ReadPngImage_MissingFile_ReturnsEmpty()
    {
        Assert.Equal("", _svc.ReadPngImage(Path.Combine(_dir, "missing.png")));
    }

    [Fact]
    public void ParsePngData_ReturnsGameDataLen()
    {
        var prefix = TestAssets.PngPrefix();
        var gameData = TestAssets.BuildCharaDataRegion(new[] { "角色" }, Array.Empty<string>());
        var path = TestAssets.WritePng(_dir, "card.png", prefix, gameData);

        var result = _svc.ParsePngData(path);

        // GameDataLen = IEND 'D' 之后 + 4 字节 CRC 的追加数据长度（不含 CRC 本身）
        Assert.Equal(gameData.Length, result.GameDataLen);
        Assert.Equal(new[] { "角色" }, result.CharaNames);
    }

    // ==================== 批量 ====================

    [Fact]
    public async Task ReadPngModsBatch_SkipsLockedFiles()
    {
        var good = TestAssets.WritePng(_dir, "good.png", TestAssets.PngPrefix(),
            TestAssets.BuildCharaDataRegion(Array.Empty<string>(), new[] { "com.mod.good" }));
        var locked = TestAssets.WritePng(_dir, "locked.png", TestAssets.PngPrefix(),
            TestAssets.BuildCharaDataRegion(Array.Empty<string>(), new[] { "com.mod.locked" }));

        var errors = new List<string>();
        using (var fs = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var results = await _svc.ReadPngModsBatchAsync(new[] { good, locked }, onError: errors.Add);
            Assert.Single(results);
            Assert.Equal(good, results[0].Path);
            Assert.Equal(new[] { "com.mod.good" }, results[0].ModIDs);
        }
        Assert.Single(errors);
    }

    [Fact]
    public async Task ReadPngPageDataBatch_SingleRead()
    {
        var p1 = TestAssets.WritePng(_dir, "a.png", TestAssets.PngPrefix(),
            TestAssets.BuildCharaDataRegion(new[] { "甲" }, Array.Empty<string>()));
        var p2 = TestAssets.WritePng(_dir, "b.png", TestAssets.PngPrefix(),
            TestAssets.BuildCharaDataRegion(new[] { "乙" }, Array.Empty<string>()));

        var results = await _svc.ReadPngPageDataBatchAsync(new[] { p1, p2 });

        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.Single(r.Names);
            Assert.NotEqual("", r.ImageData);
        });
    }

    // ==================== 文件操作 ====================

    [Fact]
    public void FileExists_Cases()
    {
        var png = TestAssets.WritePng(_dir, "a.png", TestAssets.PngPrefix());
        var upper = TestAssets.WritePng(_dir, "b.PNG", TestAssets.PngPrefix());
        var txt = Path.Combine(_dir, "c.txt");
        File.WriteAllText(txt, "x");

        Assert.True(ScannerService.FileExists(png));
        Assert.True(ScannerService.FileExists(upper));
        Assert.False(ScannerService.FileExists(txt));
        Assert.False(ScannerService.FileExists(_dir)); // 目录 → false
        Assert.False(ScannerService.FileExists(Path.Combine(_dir, "missing.png")));
    }

    [Fact]
    public void MoveFile_CreatesTargetDir()
    {
        var src = TestAssets.WritePng(_dir, "a.png", TestAssets.PngPrefix());
        var dst = Path.Combine(_dir, "new", "sub", "b.png");

        _svc.MoveFile(src, dst);

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dst));
    }

    [Fact]
    public void CheckTargetDir_Creates()
    {
        var target = Path.Combine(_dir, "a", "b");
        _svc.CheckTargetDir(target);
        Assert.True(Directory.Exists(target));
        _svc.CheckTargetDir(target); // 幂等
        Assert.True(Directory.Exists(target));
    }
}
