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
        Assert.Contains(mods, m => m.Url == "http://x/mod one.zipmod"); // %20 已解码
        Assert.Contains(mods, m => m.Url == "http://x/modtwo.zipmod");
        Assert.Contains(mods, m => m.Url == "http://x/MOD3.ZIPMOD"); // 大小写不敏感
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

    [Fact]
    public void ParseLinks_HashDir_StaysEscaped()
    {
        // KKEC 存在 #KK_MaterialEditor 目录（href 为 %23KK_MaterialEditor/）：解码出的 # 若直接
        // 拼回 URL 会被 Uri 当作 fragment 截断 → 请求落到父目录 → 无限递归。拼回前须转义还原。
        var html = """
            <table id="indexlist">
            <tr><td>h</td></tr>
            <tr><td>h</td></tr>
            <tr><td><a href="%23KK_MaterialEditor/">#KK_MaterialEditor/</a></td></tr>
            <tr><td><a href="mod%23x.zipmod">mod#x.zipmod</a></td></tr>
            </table>
            """;
        var (dirs, mods) = SideloaderService.ParseLinks(LoadHtml(html), "http://x/");

        Assert.Equal(new[] { "http://x/%23KK_MaterialEditor/" }, dirs);
        Assert.Equal(new[] { "http://x/mod%23x.zipmod" }, mods.Select(m => m.Url).ToArray());
    }

    [Fact]
    public void ParseLinks_HtmlEntities_Decoded()
    {
        // HAP 保留属性值中的 HTML 实体：&amp; 不解码会让请求 404（真实站点 Nonna &amp; Clara/ 等目录）
        var html = """
            <table id="indexlist">
            <tr><td>h</td></tr>
            <tr><td>h</td></tr>
            <tr><td><a href="Nonna &amp; Clara/">Nonna &amp; Clara/</a></td></tr>
            <tr><td><a href="SoDevs&amp;YeDevs/mod&amp;x.zipmod">m</a></td></tr>
            </table>
            """;
        var (dirs, mods) = SideloaderService.ParseLinks(LoadHtml(html), "http://x/");

        Assert.Equal(new[] { "http://x/Nonna & Clara/" }, dirs);
        Assert.Equal(new[] { "http://x/SoDevs&YeDevs/mod&x.zipmod" }, mods.Select(m => m.Url).ToArray());
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

        Assert.Equal(2, result.Mods.Count);
        Assert.Equal(2, result.DiscoveredMods); // 根目录和子目录本身不计入发现数
        Assert.Equal("modA.zipmod", result.Mods["com.test.mod.a"].Url);
        Assert.Equal("subdir/modB.zipmod", result.Mods["com.test.mod.b"].Url);
        Assert.Contains(logs, l => l.Contains("Crawl completed, found 2 mods"));
        Assert.NotEmpty(progress);
        Assert.All(progress, p => Assert.True(p.Current >= p.Processed));
        Assert.Equal(2, progress[^1].Current);
        Assert.Equal(2, progress[^1].Processed);

        // 确认确实只发了 Range 小请求而不是整文件下载
        Assert.DoesNotContain(_server.Requests, r => r.Path.EndsWith(".zipmod") && r.Range == null && r.Method == "GET");
        // suffix range 首请求已带总大小（Content-Range）：不再发 HEAD
        Assert.DoesNotContain(_server.Requests, r => r.Method == "HEAD");
    }

    [Fact]
    public async Task Crawl_ZeroSizeFile_SkipsWithoutRequest()
    {
        var small = File.ReadAllBytes(WriteZipmod(_dir, "valid.zipmod",
            MakeManifest("com.test.valid"), deflate: false));
        _server.MapHtml("/", """
            <table id="indexlist">
            <tr><th>Name</th><th>Last modified</th><th>Size</th></tr>
            <tr><td><a href="../">../</a></td><td>-</td><td>-</td></tr>
            <tr><td><a href="empty.zipmod">empty.zipmod</a></td><td>2024-01-02 03:04</td><td>0</td></tr>
            <tr><td><a href="valid.zipmod">valid.zipmod</a></td><td>2024-01-02 03:04</td><td>1.0 KB</td></tr>
            </table>
            """);
        _server.MapFile("/valid.zipmod", small);

        var svc = new SideloaderService(baseUrl: _server.BaseUrl);
        var result = await svc.RunAsync();

        Assert.Single(result.Mods);
        Assert.Equal(1, result.DiscoveredMods);
        Assert.Equal(0, result.FailedMods);
        Assert.DoesNotContain(_server.Requests, r => r.Path == "/empty.zipmod");
    }

    [Fact]
    public async Task Crawl_ThreeNestedParents_CompletesWithoutDeadlock()
    {
        // 旧递归调度中三个父目录占满 DirSem 后等待子目录，子目录又等待 DirSem，形成死锁。
        MapDirectory("/", "a/", "b/", "c/");
        foreach (var dir in new[] { "a", "b", "c" })
        {
            var bytes = File.ReadAllBytes(WriteZipmod(_dir, $"{dir}.zipmod",
                MakeManifest($"com.test.nested.{dir}"), deflate: false));
            MapDirectory($"/{dir}/", "child/");
            MapDirectory($"/{dir}/child/", $"{dir}.zipmod");
            _server.MapFile($"/{dir}/child/{dir}.zipmod", bytes);
        }

        var svc = new SideloaderService(baseUrl: _server.BaseUrl);
        var run = svc.RunAsync();
        var finished = await Task.WhenAny(run,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        if (finished != run)
        {
            svc.Cancel();
            await run;
        }

        Assert.Same(run, finished);
        var result = await run;
        Assert.Equal(3, result.Mods.Count);
        Assert.Equal("a/child/a.zipmod", result.Mods["com.test.nested.a"].Url);
        Assert.Equal("b/child/b.zipmod", result.Mods["com.test.nested.b"].Url);
        Assert.Equal("c/child/c.zipmod", result.Mods["com.test.nested.c"].Url);

        void MapDirectory(string path, params string[] entries) => _server.MapHtml(path, $"""
            <table id="indexlist">
            <tr><th>Name</th></tr>
            <tr><td><a href="../">../</a></td></tr>
            {string.Join("\n", entries.Select(entry => $"<tr><td><a href=\"{entry}\">{entry}</a></td></tr>"))}
            </table>
            """);
    }

    [Fact]
    public async Task Crawl_RootUnfetchable_ReturnsEmpty()
    {
        _server.MapStatus("/", 500);
        var logs = new List<string>();
        var svc = new SideloaderService(baseUrl: _server.BaseUrl);

        var result = await svc.RunAsync(logs.Add);

        Assert.Empty(result.Mods);
        Assert.Equal(1, result.FailedPages);
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

        Assert.Equal("modS.zipmod", result.Mods["com.test.small"].Url);
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

        Assert.Empty(result.Mods); // 渐进块被拒 → 无法解析中央目录 → 跳过该 mod（而不是吃掉数 GB 内存）
    }

    [Fact]
    public async Task Crawl_HashDirectory_DoesNotLoop()
    {
        // 目录名含 #（KKEC 的 #KK_MaterialEditor）：修复前请求被 fragment 截断回落到父目录，无限递归
        var small = File.ReadAllBytes(WriteZipmod(_dir, "modH.zipmod",
            MakeManifest("com.test.hash"), deflate: false));

        _server.MapHtml("/", """
            <table id="indexlist">
            <tr><th>Name</th></tr>
            <tr><td><a href="../">../</a></td></tr>
            <tr><td><a href="%23KK_MaterialEditor/">#KK_MaterialEditor/</a></td></tr>
            </table>
            """);
        _server.MapHtml("/%23KK_MaterialEditor/", """
            <table id="indexlist">
            <tr><th>Name</th></tr>
            <tr><td><a href="../">../</a></td></tr>
            <tr><td><a href="modH.zipmod">modH.zipmod</a></td></tr>
            </table>
            """);
        _server.MapFile("/%23KK_MaterialEditor/modH.zipmod", small);

        var svc = new SideloaderService(baseUrl: _server.BaseUrl);
        var result = await svc.RunAsync();

        Assert.Equal("%23KK_MaterialEditor/modH.zipmod", result.Mods["com.test.hash"].Url);
    }

    [Fact]
    public async Task Crawl_Zip64_ExtractsGuid()
    {
        // ZIP64：CD/本地头 32 位大小均为哨兵，修复前 CD 解析 return null / 哨兵被当 4GB 大小误杀
        var bytes = BuildZip64ZipmodBytes("com.test.zip64");

        _server.MapHtml("/", """
            <table id="indexlist">
            <tr><th>Name</th></tr>
            <tr><td><a href="../">../</a></td></tr>
            <tr><td><a href="modZ.zipmod">modZ.zipmod</a></td></tr>
            </table>
            """);
        _server.MapFile("/modZ.zipmod", bytes);

        var svc = new SideloaderService(baseUrl: _server.BaseUrl);
        var result = await svc.RunAsync();

        Assert.Equal("modZ.zipmod", result.Mods["com.test.zip64"].Url);
    }

    [Fact]
    public async Task Crawl_DataDescriptor_FallsBackToCentralDirSize()
    {
        // 本地头 size = 0（data descriptor）：修复前会发起 end < start 的 Range，GUID 提取失败
        var bytes = BuildDataDescriptorZipmodBytes("com.test.dd");

        _server.MapHtml("/", """
            <table id="indexlist">
            <tr><th>Name</th></tr>
            <tr><td><a href="../">../</a></td></tr>
            <tr><td><a href="modD.zipmod">modD.zipmod</a></td></tr>
            </table>
            """);
        _server.MapFile("/modD.zipmod", bytes);

        var svc = new SideloaderService(baseUrl: _server.BaseUrl);
        var result = await svc.RunAsync();

        Assert.Equal("modD.zipmod", result.Mods["com.test.dd"].Url);
    }

    [Fact]
    public async Task Crawl_Incremental_UnchangedMod_MakesNoRequests()
    {
        var small = File.ReadAllBytes(WriteZipmod(_dir, "modA.zipmod",
            MakeManifest("com.test.mod.a"), deflate: false));
        _server.MapHtml("/", """
            <table id="indexlist">
            <tr><th>Name</th><th>Last modified</th><th>Size</th></tr>
            <tr><td><a href="../">../</a></td><td>-</td><td>-</td></tr>
            <tr><td><a href="modA.zipmod">modA.zipmod</a></td><td>2024-01-02 03:04</td><td>1.0 KB</td></tr>
            </table>
            """);
        _server.MapFile("/modA.zipmod", small);

        var svc = new SideloaderService(baseUrl: _server.BaseUrl);
        var first = await svc.RunAsync();
        var entry = first.Mods["com.test.mod.a"];
        Assert.Equal("1.0 KB", entry.SizeText); // 末两列回退解析为 mtime/size 指纹
        Assert.Equal("2024-01-02 03:04", entry.MtimeText);

        // 第二轮：known 指纹一致 → 零 .zipmod 请求复用
        var zipRequestsBefore = _server.Requests.Count(r => r.Path.EndsWith(".zipmod"));
        var second = await svc.RunAsync(known: new Dictionary<string, KnownSideloadMod>
        {
            ["modA.zipmod"] = new("com.test.mod.a", entry.SizeText, entry.MtimeText),
        });
        Assert.Equal(1, second.ReusedMods);
        Assert.Equal("modA.zipmod", second.Mods["com.test.mod.a"].Url);
        Assert.Equal(zipRequestsBefore, _server.Requests.Count(r => r.Path.EndsWith(".zipmod")));

        // 仅大小列变化、Last modified 未变新：仍按日期跳过，不再请求文件
        var third = await svc.RunAsync(known: new Dictionary<string, KnownSideloadMod>
        {
            ["modA.zipmod"] = new("com.test.mod.a", "2.0 KB", entry.MtimeText),
        });
        Assert.Equal(1, third.ReusedMods);
        Assert.Equal(zipRequestsBefore, _server.Requests.Count(r => r.Path.EndsWith(".zipmod")));

        // 本地日期早于本站日期同样属于"不晚于"：跳过，不请求文件
        var fourth = await svc.RunAsync(known: new Dictionary<string, KnownSideloadMod>
        {
            ["modA.zipmod"] = new("com.test.mod.a", entry.SizeText, "2024-01-01 03:04"),
        });
        Assert.Equal(1, fourth.ReusedMods);
        Assert.Equal(zipRequestsBefore, _server.Requests.Count(r => r.Path.EndsWith(".zipmod")));

        // 本地日期晚于本站日期（例如远端回滚）：保守重抓
        zipRequestsBefore = _server.Requests.Count(r => r.Path.EndsWith(".zipmod"));
        var fifth = await svc.RunAsync(known: new Dictionary<string, KnownSideloadMod>
        {
            ["modA.zipmod"] = new("com.test.mod.a", entry.SizeText, "2024-01-03 03:04"),
        });
        Assert.Equal(0, fifth.ReusedMods);
        Assert.True(_server.Requests.Count(r => r.Path.EndsWith(".zipmod")) > zipRequestsBefore);
    }

    [Fact]
    public async Task Run_Concurrent_Throws()
    {
        // 用慢响应卡住第一次运行
        _server.MapSlow("/", new byte[10], 1, 300);
        var svc = new SideloaderService(baseUrl: _server.BaseUrl);

        var first = svc.RunAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);
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

    [Fact]
    public void LoadBundledDatabase_NonHs2Source_ReturnsEmpty()
    {
        // 内嵌库只有 HS2 一份；kkec 等数据源由用户爬虫更新落盘
        Assert.Empty(SideloaderService.LoadBundledDatabase("kkec"));
    }

    // ==================== 真实站点验证（阶段 1 验收：需 HS2TOOLS_REAL_CRAWL=1） ====================

    [Fact]
    public async Task Crawl_RealSite_CompareWithGoBaseline()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HS2TOOLS_REAL_CRAWL") != "1",
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

        SideloadRunResult result;
        try
        {
            result = await svc.RunAsync(logs.Add, new SyncProgress<SideloaderProgress>(p => foundCount = p.Current));
        }
        catch (Exception ex)
        {
            Trace("CRASH: " + ex);
            throw;
        }
        Trace($"done: {result.Mods.Count} mods in {sw.Elapsed:mm\\:ss}");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "hs2tools-csharp-crawl.json"),
            System.Text.Json.JsonSerializer.Serialize(result.Mods.ToDictionary(kv => kv.Key, kv => kv.Value.Url)));

        Assert.NotEmpty(result.Mods);

        // 与 Go 版爬虫 result.json 对照（若存在）：guid 集合高度重合，共有 guid 的相对路径一致
        var goResultPath = Path.Combine(Path.GetTempPath(), "hs2tools-go-baseline", "result.json");
        if (File.Exists(goResultPath))
        {
            var goResult = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(goResultPath))!;

            var common = result.Mods.Keys.Intersect(goResult.Keys).ToArray();
            var overlap = (double)common.Length / Math.Max(result.Mods.Count, goResult.Count);
            // 同一 guid 在站点上存在于多个目录（如 Exclusive HS2/AIS、Uncensor Selector/Bleeding Edge），
            // 原版结果 dict 为"后写覆盖"，Go map 随机序与 C# 爬取顺序不同 → 重复 guid 记录的路径
            // 本质是任意的，不作为提取错误。只要求差异占比 ≤1% 并全部记录备查。
            var mismatches = common.Where(g => result.Mods[g].Url != goResult[g])
                .Select(g => $"{g}: cs={result.Mods[g].Url} go={goResult[g]}").ToArray();

            Trace($"go={goResult.Count} cs={result.Mods.Count} common={common.Length} overlap={overlap:P2} dupPathMismatch={mismatches.Length}");
            foreach (var m in mismatches)
                Trace("dup: " + m);

            Assert.True(overlap >= 0.95,
                $"guid 重合度 {overlap:P1} 过低（go={goResult.Count}, cs={result.Mods.Count}）");
            Assert.True(mismatches.Length <= common.Length * 0.01,
                $"重复 guid 路径差异占比过高 {mismatches.Length}/{common.Length}：{string.Join("; ", mismatches.Take(5))}");
        }
    }
}
