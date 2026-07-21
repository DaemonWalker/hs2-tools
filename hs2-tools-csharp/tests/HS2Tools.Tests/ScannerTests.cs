using System.Text;
using HS2Tools.Services;

namespace HS2Tools.Tests;

public class ScannerTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();
    private readonly ScannerService _svc = new();

    public void Dispose() => TestAssets.DeleteDir(_dir);

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

    // ==================== PNG 读取 ====================

    [Fact]
    public void ReadPngNames_ExtractsUtf8Name()
    {
        var path = TestAssets.WritePng(_dir, "card.png", TestAssets.PngPrefix(), TestAssets.NameMarker("测试角色"));
        var names = _svc.ReadPngNames(path);
        Assert.Equal(new[] { "测试角色" }, names);
    }

    [Fact]
    public void ReadPngMods_ExtractsGuids()
    {
        var path = TestAssets.WritePng(_dir, "card.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("com.mod.a"), TestAssets.ModMarker("com.mod.b"));
        var mods = _svc.ReadPngMods(path);
        Assert.Equal(2, mods.Count);
        Assert.Contains("com.mod.a", mods);
        Assert.Contains("com.mod.b", mods);
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

        // 期望：截到最后一个 IEND 的 'D' 之后
        var expectedLen = prefix.Length + "gamedata-".Length + "IEND".Length;
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
        var gameData = TestAssets.NameMarker("角色");
        var path = TestAssets.WritePng(_dir, "card.png", prefix, gameData);

        var result = _svc.ParsePngData(path);

        // Go: gameData = data[iendIndex:]，包含 IEND 后的 4 字节 CRC
        Assert.Equal(4 + gameData.Length, result.GameDataLen);
        Assert.Equal(new[] { "角色" }, result.CharaNames);
    }

    // ==================== 批量 ====================

    [Fact]
    public async Task ReadPngModsBatch_SkipsLockedFiles()
    {
        var good = TestAssets.WritePng(_dir, "good.png", TestAssets.PngPrefix(), TestAssets.ModMarker("com.mod.good"));
        var locked = TestAssets.WritePng(_dir, "locked.png", TestAssets.PngPrefix(), TestAssets.ModMarker("com.mod.locked"));

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
        var p1 = TestAssets.WritePng(_dir, "a.png", TestAssets.PngPrefix(), TestAssets.NameMarker("甲"));
        var p2 = TestAssets.WritePng(_dir, "b.png", TestAssets.PngPrefix(), TestAssets.NameMarker("乙"));

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
