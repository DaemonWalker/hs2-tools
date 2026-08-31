using HS2Tools.Models;
using HS2Tools.Services;
using HtmlAgilityPack;
using static HS2Tools.Tests.TestAssets;

namespace HS2Tools.Tests;

public class SideloaderTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private readonly TestHttpServer _server = new();

    public void Dispose()
    {
        _server.Dispose();
        DeleteDir(_dir);
    }

    // ==================== ParseLinks ====================

    private static HtmlDocument LoadHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    private const string IndexHtml = """
        <html><body>
        <table id="indexlist">
        <tr class="head"><th>Name</th><th>Size</th></tr>
        <tr><td><a href="../">../</a></td><td>-</td></tr>
        <tr><td><a href="subdir/">subdir/</a></td><td>-</td></tr>
        <tr><td><a href="mod%20one.zipmod">mod one.zipmod</a></td><td>1</td></tr>
        <tr><td><a href="modtwo.zipmod">modtwo.zipmod</a></td><td>2</td></tr>
        <tr><td><a href="MOD3.ZIPMOD">MOD3.ZIPMOD</a></td><td>3</td></tr>
        <tr><td><a href="readme.txt">readme.txt</a></td><td>4</td></tr>
        </table>
        </body></html>
        """;

    [Fact]
    public void ParseLinks_SkipsFirstTwoRows_AndSplitsDirsMods()
    {
        var (dirs, mods) = SideloaderService.ParseLinks(LoadHtml(IndexHtml), "http://x/");

        // "../" 在第 2 行被跳过；readme.txt 两类都不是
        Assert.Equal(new[] { "http://x/subdir/" }, dirs);
        Assert.Equal(3, mods.Count);
        Assert.Contains("http://x/mod one.zipmod", mods); // %20 已解码
        Assert.Contains("http://x/modtwo.zipmod", mods);
        Assert.Contains("http://x/MOD3.ZIPMOD", mods); // 大小写不敏感
    }

    [Fact]
    public void ParseLinks_SkipsRootAndEmpty()
    {
        var html = """
            <table id="indexlist">
            <tr><td>h</td></tr>
            <tr><td>h</td></tr>
            <tr><td><a href="/">root</a></td></tr>
            <tr><td><a href="">empty</a></td></tr>
            <tr><td><a>noattr</a></td></tr>
            </table>
            """;
        var (dirs, mods) = SideloaderService.ParseLinks(LoadHtml(html), "http://x/");
        Assert.Empty(dirs);
        Assert.Empty(mods);
    }

    [Fact]
    public void ParseLinks_Dedupes()
    {
        var html = """
            <table id="indexlist">
            <tr><td>h</td></tr>
            <tr><td>h</td></tr>
            <tr><td><a href="a.zipmod">1</a></td></tr>
            <tr><td><a href="a.zipmod">2</a></td></tr>
            </table>
            """;
        var (_, mods) = SideloaderService.ParseLinks(LoadHtml(html), "http://x/");
        Assert.Single(mods);
    }

    // ==================== 集成爬取 ====================

    [Fact]
    public async Task Crawl_LocalServer_FindsGuids()
    {
        // 小 zipmod（≤64KB，单次整取）+ 大 zipmod（中央目录 ~186KB，强制多块渐进抓取）
        var small = File.ReadAllBytes(WriteZipmod(_dir, "modA.zipmod",
            MakeManifest("com.test.mod.a"), deflate: false));
        var big = File.ReadAllBytes(WriteBigZipmod(_dir, "modB.zipmod",
            MakeManifest("com.test.mod.b"), dummyEntries: 1500));
        Assert.True(big.Length > 256 * 1024); // 确认走渐进路径

        _server.MapHtml("/", """
            <table id="indexlist">
            <tr><th>Name</th></tr>
            <tr><td><a href="../">../</a></td></tr>
            <tr><td><a href="subdir/">subdir/</a></td></tr>
            <tr><td><a href="modA.zipmod">modA.zipmod</a></td></tr>
            </table>
            """);
        _server.MapHtml("/subdir/", """
            <table id="indexlist">
            <tr><th>Name</th></tr>
            <tr><td><a href="../">../</a></td></tr>
            <tr><td><a href="modB.zipmod">modB.zipmod</a></td></tr>
            </table>
            """);
        _server.MapFile("/modA.zipmod", small);
        _server.MapFile("/subdir/modB.zipmod", big);

        var logs = new List<string>();
        var progress = new List<SideloaderProgress>();
        var svc = new SideloaderService(baseUrl: _server.BaseUrl);
        var result = await svc.RunAsync(logs.Add, new SyncProgress<SideloaderProgress>(progress.Add));

        Assert.Equal(2, result.Count);
        Assert.Equal("modA.zipmod", result["com.test.mod.a"]);
        Assert.Equal("subdir/modB.zipmod", result["com.test.mod.b"]);
        Assert.Contains(logs, l => l.Contains("Crawl completed, found 2 mods"));
        Assert.NotEmpty(progress);

        // 确认确实只发了 Range 小请求而不是整文件下载
        Assert.DoesNotContain(_server.Requests, r => r.Path.EndsWith(".zipmod") && r.Range == null && r.Method == "GET");
    }

    [Fact]
    public async Task Crawl_RootUnfetchable_ReturnsEmpty()
    {
        _server.MapStatus("/", 500);
        var logs = new List<string>();
        var svc = new SideloaderService(baseUrl: _server.BaseUrl);

        var result = await svc.RunAsync(logs.Add);

        Assert.Empty(result);
        Assert.Contains(logs, l => l.Contains("Failed to fetch"));
    }

    [Fact]
    public async Task Crawl_SmallFile_ServerIgnoresRange_StillExtracts()
    {
        // 小文件（≤64KB）走整取分支：服务器返回 200 全量也可用
        var small = File.ReadAllBytes(WriteZipmod(_dir, "modS.zipmod",
            MakeManifest("com.test.small"), deflate: false));

        _server.MapHtml("/", """
            <table id="indexlist">
            <tr><th>Name</th></tr>
            <tr><td><a href="../">../</a></td></tr>
            <tr><td><a href="modS.zipmod">modS.zipmod</a></td></tr>
            </table>
            """);
        _server.MapFile("/modS.zipmod", small, supportRange: false); // 200 全量

        var svc = new SideloaderService(baseUrl: _server.BaseUrl);
        var result = await svc.RunAsync();

        Assert.Equal("modS.zipmod", result["com.test.small"]);
    }

    [Fact]
    public async Task Crawl_BigFile_ServerIgnoresRange_ChunksRejected()
    {
        // 大文件走渐进分支：服务器对 Range 返回 200 全量 → 块被拒（防内存累积），不提取 GUID
        var big = File.ReadAllBytes(WriteBigZipmod(_dir, "modB.zipmod",
            MakeManifest("com.test.big"), dummyEntries: 1500));

        _server.MapHtml("/", """
            <table id="indexlist">
            <tr><th>Name</th></tr>
            <tr><td><a href="../">../</a></td></tr>
            <tr><td><a href="modB.zipmod">modB.zipmod</a></td></tr>
            </table>
            """);
        _server.MapFile("/modB.zipmod", big, supportRange: false); // 200 全量（每次块请求都回整个文件）

        var svc = new SideloaderService(baseUrl: _server.BaseUrl);
        var result = await svc.RunAsync();

        Assert.Empty(result); // 渐进块被拒 → 无法解析中央目录 → 跳过该 mod（而不是吃掉数 GB 内存）
    }

    [Fact]
    public async Task Run_Concurrent_Throws()
    {
        // 用慢响应卡住第一次运行
        _server.MapSlow("/", new byte[10], 1, 300);
        var svc = new SideloaderService(baseUrl: _server.BaseUrl);

        var first = svc.RunAsync();
        await Task.Delay(50);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RunAsync());
        svc.Cancel();
        await first;
    }

    [Fact]
    public void LoadBundledDatabase_ReturnsData()
    {
        var db = SideloaderService.LoadBundledDatabase();
        Assert.NotEmpty(db);
        Assert.All(db, kv =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Key));
            Assert.False(string.IsNullOrWhiteSpace(kv.Value));
        });
    }

    // ==================== 真实站点验证（阶段 1 验收：需 HS2TOOLS_REAL_CRAWL=1） ====================

    [SkippableFact]
    public async Task Crawl_RealSite_CompareWithGoBaseline()
    {
        Skip.If(Environment.GetEnvironmentVariable("HS2TOOLS_REAL_CRAWL") != "1",
            "真实站点爬取为门控验证：设置 HS2TOOLS_REAL_CRAWL=1 后运行（可选 HS2TOOLS_PROXY 指定代理）");

        // 追踪日志：定位崩溃/内存问题（%TEMP%/hs2tools-crawl-trace.log）
        var tracePath = Path.Combine(Path.GetTempPath(), "hs2tools-crawl-trace.log");
        File.WriteAllText(tracePath, "");
        void Trace(string msg) => File.AppendAllText(tracePath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");

        var proxy = Environment.GetEnvironmentVariable("HS2TOOLS_PROXY");
        var logs = new List<string>();
        var svc = new SideloaderService(proxy: string.IsNullOrEmpty(proxy) ? null : proxy);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var foundCount = 0;
        using var timer = new System.Threading.Timer(
            _ =>
            {
                var before = GC.GetTotalMemory(false) / 1048576;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var after = GC.GetTotalMemory(true) / 1048576;
                Trace($"mem={before}MB afterGC={after}MB found={foundCount} elapsed={sw.Elapsed:mm\\:ss}");
            },
            null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        Dictionary<string, string> result;
        try
        {
            result = await svc.RunAsync(logs.Add, new SyncProgress<SideloaderProgress>(p => foundCount = p.Current));
        }
        catch (Exception ex)
        {
            Trace("CRASH: " + ex);
            throw;
        }
        Trace($"done: {result.Count} mods in {sw.Elapsed:mm\\:ss}");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "hs2tools-csharp-crawl.json"),
            System.Text.Json.JsonSerializer.Serialize(result));

        Assert.NotEmpty(result);

        // 与 Go 版爬虫 result.json 对照（若存在）：guid 集合高度重合，共有 guid 的相对路径一致
        var goResultPath = Path.Combine(Path.GetTempPath(), "hs2tools-go-baseline", "result.json");
        if (File.Exists(goResultPath))
        {
            var goResult = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(goResultPath))!;

            var common = result.Keys.Intersect(goResult.Keys).ToArray();
            var overlap = (double)common.Length / Math.Max(result.Count, goResult.Count);
            // 同一 guid 在站点上存在于多个目录（如 Exclusive HS2/AIS、Uncensor Selector/Bleeding Edge），
            // 原版结果 dict 为"后写覆盖"，Go map 随机序与 C# 爬取顺序不同 → 重复 guid 记录的路径
            // 本质是任意的，不作为提取错误。只要求差异占比 ≤1% 并全部记录备查。
            var mismatches = common.Where(g => result[g] != goResult[g])
                .Select(g => $"{g}: cs={result[g]} go={goResult[g]}").ToArray();

            Trace($"go={goResult.Count} cs={result.Count} common={common.Length} overlap={overlap:P2} dupPathMismatch={mismatches.Length}");
            foreach (var m in mismatches)
                Trace("dup: " + m);

            Assert.True(overlap >= 0.95,
                $"guid 重合度 {overlap:P1} 过低（go={goResult.Count}, cs={result.Count}）");
            Assert.True(mismatches.Length <= common.Length * 0.01,
                $"重复 guid 路径差异占比过高 {mismatches.Length}/{common.Length}：{string.Join("; ", mismatches.Take(5))}");
        }
    }
}
