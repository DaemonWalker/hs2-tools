using System.Text;
using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class CharaWindowViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private string MakeGameDir()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative));
        File.WriteAllText(Path.Combine(gameDir, GameProfiles.Hs2.GameExeName), "exe");
        return gameDir;
    }

    private static CharaWindowViewModel MakeVm(
        ConfigService config, SideloadDatabaseService db, DownloadManager? downloads = null) =>
        new(config, new ScannerService(), downloads ?? new DownloadManager(),
            db, new GameLauncherService(config));

    [Fact]
    public void LoadCardPaths_ScansCharaDir()
    {
        var gameDir = MakeGameDir();
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("张三"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c2.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("李四"));
        File.WriteAllText(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative, "note.txt"), "x"); // 非 png 不收录

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        vm.LoadCardPaths();

        Assert.Equal(2, vm.AllPaths.Count);
        Assert.All(vm.AllPaths, p => Assert.EndsWith(".png", p, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConfigChanged_GameSwitch_RescansNewGameDir()
    {
        var hs2Dir = MakeGameDir();
        TestAssets.WritePng(Path.Combine(hs2Dir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("张三"));
        TestAssets.WritePng(Path.Combine(hs2Dir, GameProfiles.Hs2.CharaDirRelative), "c2.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("李四"));

        // kk 目录：角色目录相对路径与 hs2 相同，仅 exe/游戏根不同
        var kkDir = Path.Combine(_dir, "game-kk");
        Directory.CreateDirectory(Path.Combine(kkDir, GameProfiles.Kk.CharaDirRelative));
        File.WriteAllText(Path.Combine(kkDir, GameProfiles.Kk.GameExeName), "exe");
        TestAssets.WritePng(Path.Combine(kkDir, GameProfiles.Kk.CharaDirRelative), "k1.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("王五"));

        using var config = new ConfigService(_dir);
        config.Update(s => { s.CurrentGame = "hs2"; s.Current.GamePath = hs2Dir; });
        config.Update(s => { s.CurrentGame = "kk"; s.Current.GamePath = kkDir; });
        config.Update(s => s.CurrentGame = "hs2");
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        vm.LoadCardPaths();
        Assert.Equal(2, vm.AllPaths.Count);

        // 非游戏切换的 Changed（收藏）不重扫
        var before = vm.AllPaths;
        config.Update(s => s.Current.Favorites.Add(before[0]));
        Assert.Same(before, vm.AllPaths);

        // 切换游戏 → 用新游戏目录重扫，旧选中清空
        vm.SelectedPath = before[0];
        config.Update(s => s.CurrentGame = "kk"); // 测试环境 UiDispatch 同步执行
        Assert.Null(vm.SelectedPath);
        Assert.Single(vm.AllPaths);
        Assert.EndsWith("k1.png", vm.AllPaths[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadDetail_ParsesNamesModsAndRealFileInfo()
    {
        var gameDir = MakeGameDir();
        var card = TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("张三"), TestAssets.ModMarker("g1"), TestAssets.ModMarker("g2"));

        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.GamePath = gameDir;
            s.Current.LocalMods["g1"] = new() { Name = "Owned Mod" };
        });
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string> { ["g2"] = "a/g2.zipmod" }); // g1 本地拥有；g2 有链接
        var vm = MakeVm(config, db);

        vm.SelectedPath = card;
        await vm.Detail.LoadTask!;

        Assert.Equal("张三", vm.Detail.DetailName);
        Assert.Equal(card, vm.Detail.DetailFilePath);
        Assert.NotEqual("-", vm.Detail.DetailModified); // A6：真实文件信息
        Assert.Contains("B", vm.Detail.DetailSize); // formatBytes 单位
        Assert.Equal(2, vm.Detail.ModItems.Count);
        Assert.Equal(1, vm.Detail.LocalCount);
        Assert.Equal(1, vm.Detail.MissingCount);

        var g1 = vm.Detail.ModItems.Single(i => i.Guid == "g1");
        Assert.True(g1.ShowOwned);
        var g2 = vm.Detail.ModItems.Single(i => i.Guid == "g2");
        Assert.True(g2.ShowDownload);
    }

    [Fact]
    public async Task LoadDetail_NoNames_FallsBackToUnknown()
    {
        var gameDir = MakeGameDir();
        var card = TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix());

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        vm.SelectedPath = card;
        await vm.Detail.LoadTask!;

        Assert.Equal("未知", vm.Detail.DetailName);
        Assert.Null(vm.Detail.DetailDescription);
    }

    [Fact]
    public async Task DownloadAllMissing_DownloadsUrlAvailableOnly()
    {
        var gameDir = MakeGameDir();
        var card = TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-a"), TestAssets.ModMarker("g-b"), TestAssets.ModMarker("g-c"));

        using var server = new TestHttpServer();
        var zipBytes = TestAssets.BuildZipBytes(
            ("manifest.xml", Encoding.UTF8.GetBytes(TestAssets.MakeManifest("x")), true));
        server.MapFile("/m/g-a.zipmod", zipBytes);
        server.MapFile("/m/g-b.zipmod", zipBytes);

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string>
        {
            ["g-a"] = "m/g-a.zipmod",
            ["g-b"] = "m/g-b.zipmod",
            // g-c 无链接
        });
        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = MakeVm(config, db, downloads);
        vm.Detail.DownloadBaseUrlOverride = server.BaseUrl; // 下载指向本地测试服务器

        vm.SelectedPath = card;
        await vm.Detail.LoadTask!;
        vm.Detail.DownloadAllMissingCommand.Execute(null);

        // 等两个任务到终态
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (downloads.Tasks.Count(t => t.Status == Models.DownloadTaskStatus.Completed) < 2)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("downloads did not complete");
            await Task.Delay(50);
        }

        var outDir = Path.Combine(gameDir, GameProfiles.Hs2.ModDownloadDirRelative);
        Assert.True(File.Exists(Path.Combine(outDir, "g-a.zipmod")));
        Assert.True(File.Exists(Path.Combine(outDir, "g-b.zipmod")));
        Assert.False(File.Exists(Path.Combine(outDir, "g-c.zipmod")));

        // 任务完成事件刷新列表状态
        Assert.Equal(2, vm.Detail.ModItems.Count(i => i.ShowCompleted));
        Assert.True(vm.Detail.ModItems.Single(i => i.Guid == "g-c").ShowUnavailable);
    }

    [Fact]
    public async Task DownloadMod_StartsTask_AndItemShowsDownloading()
    {
        var gameDir = MakeGameDir();
        var card = TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-a"));

        using var server = new TestHttpServer();
        var zipBytes = TestAssets.BuildZipBytes(
            ("manifest.xml", Encoding.UTF8.GetBytes(TestAssets.MakeManifest("x")), true));
        server.MapSlow("/m/g-a.zipmod", zipBytes, chunkSize: 10, delayMs: 30); // 慢速保证能观察到下载中

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string> { ["g-a"] = "m/g-a.zipmod" });
        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = MakeVm(config, db, downloads);
        vm.Detail.DownloadBaseUrlOverride = server.BaseUrl; // 下载指向本地测试服务器

        vm.SelectedPath = card;
        await vm.Detail.LoadTask!;
        var item = vm.Detail.ModItems.Single();
        vm.Detail.DownloadModCommand.Execute(item);

        Assert.True(item.IsDownloading); // 即时刷新（不等网络事件）

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (item.Status != Models.DownloadTaskStatus.Completed)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("download did not complete");
            await Task.Delay(50);
        }
        Assert.True(item.ShowCompleted);
    }
}
