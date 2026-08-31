using System.Text;
using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;
using Xunit.Abstractions;

namespace HS2Tools.Tests;

public class SideloadWindowViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();
    private readonly ITestOutputHelper _output;

    public SideloadWindowViewModelTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private string MakeGameDir()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative));
        File.WriteAllText(Path.Combine(gameDir, GameProfiles.Hs2.GameExeName), "exe");
        return gameDir;
    }

    private static SideloadWindowViewModel MakeVm(
        ConfigService config, SideloadDatabaseService db, DownloadManager? downloads = null) =>
        new(config, downloads ?? new DownloadManager(), db);

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not met in time");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task RealBundledDatabase_ReloadAndSearch_Performance()
    {
        // 阶段 4 验证点：真实 sideload.zip（12k+ 条目）下建行与搜索过滤的耗时
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config); // 无用户库 → 内嵌全量库
        Assert.True(db.Database.Count > 10_000);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var vm = MakeVm(config, db); // 构造函数内 Reload() 全量建行
        sw.Stop();
        _output.WriteLine($"reload {vm.TotalCount} rows: {sw.ElapsedMilliseconds} ms");
        Assert.True(vm.TotalCount > 10_000);
        Assert.True(sw.ElapsedMilliseconds < 10_000, $"reload too slow: {sw.ElapsedMilliseconds} ms");

        vm.DebounceMs = 1;
        sw.Restart();
        vm.SearchText = "ab";
        await WaitFor(() => vm.Items.Count < vm.TotalCount); // 过滤生效
        sw.Stop();
        _output.WriteLine($"filter 'ab' → {vm.Items.Count} rows: {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < 5_000, $"filter too slow: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Stats_TotalExistingMissing()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.LocalMods["g-owned"] = new ModInfo());
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string>
        {
            ["g-owned"] = "a/g-owned.zipmod",
            ["g-m1"] = "a/g-m1.zipmod",
            ["g-m2"] = "b/g-m2.zipmod",
        });

        var vm = MakeVm(config, db);

        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(1, vm.ExistingCount);
        Assert.Equal(2, vm.MissingCount);
        Assert.Equal(3, vm.Items.Count);
        Assert.Equal("已存在", vm.Items.Single(i => i.Guid == "g-owned").StatusText);
        Assert.Equal("缺失", vm.Items.Single(i => i.Guid == "g-m1").StatusText);
        Assert.False(vm.Items.Single(i => i.Guid == "g-owned").CanDownload);
        Assert.True(vm.Items.Single(i => i.Guid == "g-m1").CanDownload);
    }

    [Fact]
    public void Matches_GuidOrUrl_CaseInsensitiveSubstring()
    {
        Assert.True(SideloadWindowViewModel.Matches("G-ABC", "x/y.zipmod", "g-ab"));
        Assert.True(SideloadWindowViewModel.Matches("g1", "Exclusive HS2/Some Mod.zipmod", "exclusive"));
        Assert.True(SideloadWindowViewModel.Matches("g1", "x/some mod.zipmod", "SOME MOD"));
        Assert.False(SideloadWindowViewModel.Matches("g1", "x/y.zipmod", "zzz"));
    }

    [Fact]
    public void ApplyFilter_FiltersByGuidAndUrl()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string>
        {
            ["g-aaa"] = "pack1/a.zipmod",
            ["g-bbb"] = "pack2/b.zipmod",
            ["g-ccc"] = "pack1/c.zipmod",
        });
        var vm = MakeVm(config, db);

        vm.ApplyFilter("g-aa"); // GUID 子串
        Assert.Equal(["g-aaa"], vm.Items.Select(i => i.Guid).ToArray());

        vm.ApplyFilter("PACK1"); // URL 子串，大小写不敏感
        Assert.Equal(2, vm.Items.Count);

        vm.ApplyFilter(""); // 空搜索词显示全部
        Assert.Equal(3, vm.Items.Count);

        // 统计不受搜索词影响（与原版一致）
        vm.ApplyFilter("g-aa");
        Assert.Equal(3, vm.TotalCount);
    }

    [Fact]
    public async Task SearchText_DebouncesFilter()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string>
        {
            ["g-aaa"] = "a.zipmod",
            ["g-bbb"] = "b.zipmod",
        });
        var vm = MakeVm(config, db);
        vm.DebounceMs = 40; // 缩短防抖间隔，避免拖慢测试

        vm.SearchText = "g-aa";
        Assert.Equal(2, vm.Items.Count); // 防抖期内未过滤

        await WaitFor(() => vm.Items.Count == 1);
        Assert.Equal("g-aaa", vm.Items[0].Guid);
    }

    [Fact]
    public async Task SearchText_RapidChanges_OnlyLastApplies()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string>
        {
            ["g-aaa"] = "a.zipmod",
            ["g-bbb"] = "b.zipmod",
        });
        var vm = MakeVm(config, db);
        vm.DebounceMs = 60;

        vm.SearchText = "g-aa";
        vm.SearchText = "g-bb"; // 前一次防抖应被作废

        await WaitFor(() => vm.Items.Count == 1);
        Assert.Equal("g-bbb", vm.Items[0].Guid);
    }

    [Fact]
    public async Task Download_StartsTask_AndItemTracksCompletion()
    {
        var gameDir = MakeGameDir();
        using var server = new TestHttpServer();
        var zipBytes = TestAssets.BuildZipBytes(
            ("manifest.xml", Encoding.UTF8.GetBytes(TestAssets.MakeManifest("g-a")), true));
        server.MapSlow("/m/g-a.zipmod", zipBytes, chunkSize: 10, delayMs: 30); // 慢速保证能观察到下载中

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string> { ["g-a"] = "m/g-a.zipmod" });
        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = MakeVm(config, db, downloads);
        vm.DownloadBaseUrlOverride = server.BaseUrl; // 下载指向本地测试服务器

        var item = vm.Items.Single();
        vm.DownloadCommand.Execute(item);

        Assert.True(item.IsDownloading); // 即时刷新（不等网络事件）
        Assert.False(item.CanDownload);

        await WaitFor(() => item.TaskStatus == DownloadTaskStatus.Completed);

        var outFile = Path.Combine(gameDir, GameProfiles.Hs2.ModDownloadDirRelative, "g-a.zipmod");
        Assert.True(File.Exists(outFile));
        Assert.Equal("已下载", item.StatusText); // 完成但未重扫入 LocalMods
        Assert.Equal("已完成", item.DownloadText);
        Assert.False(item.CanDownload);
    }

    [Fact]
    public void Download_NoGamePath_DoesNothing()
    {
        using var config = new ConfigService(_dir); // 未设置 GamePath → 无下载目录
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string> { ["g-a"] = "m/g-a.zipmod" });
        var downloads = new DownloadManager();
        var vm = MakeVm(config, db, downloads);

        vm.DownloadCommand.Execute(vm.Items.Single());

        Assert.Empty(downloads.Tasks);
    }

    [Fact]
    public void DbChanged_ReloadsItemsAndStats()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string> { ["g-a"] = "a.zipmod" });
        var vm = MakeVm(config, db);
        Assert.Equal(1, vm.TotalCount);

        // 爬虫更新完成（Changed 在调用线程触发，测试环境 UiDispatch 直跑）
        db.Update(new Dictionary<string, string>
        {
            ["g-a"] = "a.zipmod",
            ["g-b"] = "b.zipmod",
        });

        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(2, vm.MissingCount);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public void Empty_NoDatabase_ShowsHint()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string>()); // 显式空库（不走内嵌库）
        var vm = MakeVm(config, db);

        Assert.True(vm.IsEmpty);
        Assert.Equal(0, vm.TotalCount);
        Assert.Contains("暂无 Sideload 数据", vm.EmptyText);
    }

    [Fact]
    public void Empty_FilterNoMatch_ShowsHint()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string> { ["g-a"] = "a.zipmod" });
        var vm = MakeVm(config, db);

        vm.ApplyFilter("zzz");

        Assert.True(vm.IsEmpty);
        Assert.Equal("没有匹配的记录", vm.EmptyText);
    }
}
