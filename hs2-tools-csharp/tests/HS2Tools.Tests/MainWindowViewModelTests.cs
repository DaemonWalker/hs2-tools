using System.Text;
using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;
using Xunit.Abstractions;

namespace HS2Tools.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();
    private readonly ITestOutputHelper _output;

    public MainWindowViewModelTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private string MakeGameDir()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        Directory.CreateDirectory(Path.Combine(gameDir, ConfigService.SceneDirRelative));
        Directory.CreateDirectory(Path.Combine(gameDir, ConfigService.CharaDirRelative));
        File.WriteAllText(Path.Combine(gameDir, ConfigService.GameExeName), "exe");
        return gameDir;
    }

    private sealed class FakeSideloader : ISideloaderService
    {
        public bool IsRunning { get; private set; }
        public bool Cancelled { get; private set; }
        public Dictionary<string, string> Result { get; set; } = new();
        public readonly TaskCompletionSource Started = new();
        public readonly TaskCompletionSource Gate = new();

        public async Task<Dictionary<string, string>> RunAsync(
            Action<string>? onLog = null, IProgress<SideloaderProgress>? onProgress = null)
        {
            IsRunning = true;
            Started.TrySetResult();
            try
            {
                await Gate.Task;
                onLog?.Invoke("Processing: http://example.test/dir/");
                onProgress?.Report(new SideloaderProgress(Result.Count, 0));
                return Result;
            }
            finally
            {
                IsRunning = false;
            }
        }

        public void Cancel()
        {
            Cancelled = true;
            Gate.TrySetResult();
        }
    }

    private static MainWindowViewModel MakeVm(
        ConfigService config, SideloadDatabaseService db,
        DownloadManager? downloads = null, Func<ISideloaderService>? factory = null) =>
        new(config, new ScannerService(), downloads ?? new DownloadManager(),
            new GameLauncherService(config), db, factory ?? (() => new FakeSideloader()));

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not met in time");
            await Task.Delay(20);
        }
    }

    // ==================== 游戏路径 ====================

    [Fact]
    public void Ctor_LoadsGamePathFromConfig()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.GamePath = @"C:\HS2");

        var vm = MakeVm(config, new SideloadDatabaseService(_dir));

        Assert.Equal(@"C:\HS2", vm.GamePath);
        Assert.False(vm.IsGamePathValid); // 路径不存在 → 未通过校验
        Assert.Contains("未找到", vm.PathStatusText);
    }

    [Fact]
    public void Ctor_DoesNotRewriteSettingsFile()
    {
        // 加载已有配置时 GamePath 未变 → 不触发写盘
        using var config = new ConfigService(_dir);
        _ = MakeVm(config, new SideloadDatabaseService(_dir));
        Assert.False(File.Exists(config.SettingsPath));
    }

    [Fact]
    public void SetValidGamePath_ValidatesAndPersists()
    {
        var gameDir = MakeGameDir();
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(_dir));

        vm.GamePath = gameDir;

        Assert.True(vm.IsGamePathValid);
        Assert.Contains("已验证", vm.PathStatusText);
        config.Save();

        using var reloaded = new ConfigService(_dir);
        Assert.Equal(gameDir, reloaded.Settings.GamePath);
    }

    [Fact]
    public void SetInvalidGamePath_DoesNotValidate()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(_dir));

        vm.GamePath = Path.Combine(_dir, "no-such-dir");

        Assert.False(vm.IsGamePathValid);
        Assert.Contains("未找到", vm.PathStatusText);
    }

    [Fact]
    public void ValidateGamePath_Cases()
    {
        Assert.False(MainWindowViewModel.ValidateGamePath(null));
        Assert.False(MainWindowViewModel.ValidateGamePath(""));
        Assert.False(MainWindowViewModel.ValidateGamePath(_dir)); // 目录存在但无 exe
        Assert.True(MainWindowViewModel.ValidateGamePath(MakeGameDir()));
    }

    [Fact]
    public void LaunchCommands_RequireValidPath()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(_dir));

        Assert.False(vm.LaunchGameCommand.CanExecute(null));
        Assert.False(vm.LaunchStudioCommand.CanExecute(null));

        vm.GamePath = MakeGameDir();
        Assert.True(vm.LaunchGameCommand.CanExecute(null));
        Assert.True(vm.LaunchStudioCommand.CanExecute(null));
    }

    // ==================== 数据分析 ====================

    [Fact]
    public async Task Scan_PopulatesModsUsageAndStats()
    {
        var gameDir = MakeGameDir();
        TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "m1.zipmod",
            TestAssets.MakeManifest("g-mod", "Mod One"));
        TestAssets.WritePng(Path.Combine(gameDir, ConfigService.SceneDirRelative), "s1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-scene"));
        TestAssets.WritePng(Path.Combine(gameDir, ConfigService.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-chara"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(_dir));
        vm.GamePath = gameDir;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ScanCompleted);
        Assert.False(vm.IsScanning);
        Assert.Equal("重新分析", vm.ScanButtonText);
        Assert.Equal("1/1", vm.ModScanProgress);
        Assert.Equal("1/1", vm.SceneScanProgress);
        Assert.Equal("1/1", vm.CharaScanProgress);
        Assert.True(vm.ModScanDone && vm.SceneScanDone && vm.CharaScanDone);

        Assert.True(config.Settings.LocalMods.ContainsKey("g-mod"));
        Assert.Equal("Mod One", config.Settings.LocalMods["g-mod"].Name);
        Assert.Equal(1, config.Settings.ModUsage["g-scene"]);
        Assert.Equal(1, config.Settings.ModUsage["g-chara"]);

        vm.RefreshStats();
        Assert.Equal(1, vm.ModCount);
        Assert.Equal(2, vm.UsageCount);
        Assert.Equal(2, vm.TotalRefs);
    }

    /// <summary>阶段 4：真实游戏目录三阶段扫描（设 HS2_REAL_GAME_DIR 时执行，只读不写游戏目录）</summary>
    [SkippableFact]
    public async Task Scan_RealGameDir_Benchmark()
    {
        var gameDir = Environment.GetEnvironmentVariable("HS2_REAL_GAME_DIR");
        Skip.If(string.IsNullOrWhiteSpace(gameDir), "未设置 HS2_REAL_GAME_DIR（真实游戏目录）");

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(_dir));
        vm.GamePath = gameDir;
        Assert.True(vm.IsGamePathValid);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await vm.ScanCommand.ExecuteAsync(null);
        sw.Stop();

        Assert.True(vm.ScanCompleted);
        vm.RefreshStats();
        _output.WriteLine(
            $"real scan: {sw.ElapsedMilliseconds} ms, mods={vm.ModCount}, usage={vm.UsageCount}, refs={vm.TotalRefs}, missing={vm.MissingModCount}");
        Assert.True(vm.ModCount > 0);
        Assert.True(vm.UsageCount > 0);
    }

    [Fact]
    public async Task Scan_MergeSemantics_CharaOverwritesScene()
    {
        // 原版 { ...scene, ...female }：同 guid 时角色统计覆盖场景统计
        var gameDir = MakeGameDir();
        TestAssets.WritePng(Path.Combine(gameDir, ConfigService.SceneDirRelative), "s1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-both"));
        TestAssets.WritePng(Path.Combine(gameDir, ConfigService.SceneDirRelative), "s2.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-both"));
        TestAssets.WritePng(Path.Combine(gameDir, ConfigService.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-both"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(_dir));
        vm.GamePath = gameDir;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(1, config.Settings.ModUsage["g-both"]); // 场景计 2，被角色计 1 覆盖
    }

    // ==================== Sideloader 更新 ====================

    [Fact]
    public async Task Sideloader_RunToSuccess_UpdatesDatabase()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(_dir);
        var fake = new FakeSideloader { Result = new() { ["g1"] = "dir/g1.zipmod" } };
        var vm = MakeVm(config, db, factory: () => fake);

        vm.ToggleSideloaderCommand.Execute(null);
        await WaitFor(() => fake.IsRunning);
        Assert.Equal(SideloaderUiState.Running, vm.SideloaderState);
        Assert.Equal("点击停止更新", vm.SideloaderButtonText);

        fake.Gate.SetResult();
        await WaitFor(() => vm.SideloaderState == SideloaderUiState.Success);

        Assert.True(fake.Cancelled == false);
        // 结果已落盘（修复原版更新不生效）：新实例从磁盘读到更新后的库
        var reloaded = new SideloadDatabaseService(_dir);
        Assert.Equal("dir/g1.zipmod", reloaded.Database["g1"]);
    }

    [Fact]
    public async Task Sideloader_StopAfterConfirm_DoesNotUpdateDatabase()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(_dir);
        var fake = new FakeSideloader { Result = new() { ["g1"] = "dir/g1.zipmod" } };
        var vm = MakeVm(config, db, factory: () => fake);

        var confirmRequested = false;
        vm.StopConfirmationRequested += (_, _) => confirmRequested = true;

        vm.ToggleSideloaderCommand.Execute(null);
        await WaitFor(() => fake.IsRunning);

        vm.ToggleSideloaderCommand.Execute(null); // 运行中点击 → 请求确认
        Assert.True(confirmRequested);

        vm.ConfirmStopSideloader(); // 用户确认 → Cancel（假实现顺带放行 Gate）
        await WaitFor(() => vm.SideloaderState == SideloaderUiState.Stopped);

        Assert.True(fake.Cancelled);
        Assert.False(File.Exists(Path.Combine(_dir, "sideload.json"))); // 部分结果不落盘
    }

    // ==================== 缺失 Mod 批量补全 ====================

    [Fact]
    public async Task Complement_DownloadsMissingMods_Serially()
    {
        var gameDir = MakeGameDir();
        using var server = new TestHttpServer();
        var zipBytes = TestAssets.BuildZipBytes(
            ("manifest.xml", Encoding.UTF8.GetBytes(TestAssets.MakeManifest("g-missing")), true));
        server.MapFile("/mods/g-missing.zipmod", zipBytes);

        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(_dir);
        db.Update(new Dictionary<string, string> { ["g-missing"] = "mods/g-missing.zipmod" });
        config.Update(s => s.ModUsage["g-missing"] = 1);

        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = MakeVm(config, db, downloads);
        vm.GamePath = gameDir;

        Assert.Equal(1, vm.MissingModCount);
        Assert.True(vm.ComplementMissingModsCommand.CanExecute(null));

        await vm.ComplementMissingModsCommand.ExecuteAsync(null);

        var outFile = Path.Combine(gameDir, ConfigService.ModDownloadDirRelative, "g-missing.zipmod");
        Assert.True(File.Exists(outFile));
        Assert.Equal("1/1 补全完成", vm.ComplementProgress); // 结束汇总（阶段 4：成功数要体现）
        Assert.False(vm.IsComplementing);
    }

    [Fact]
    public async Task Complement_Failure_ShowsSummary()
    {
        var gameDir = MakeGameDir();
        using var server = new TestHttpServer();
        server.MapStatus("/mods/g-bad.zipmod", 404);

        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(_dir);
        db.Update(new Dictionary<string, string> { ["g-bad"] = "mods/g-bad.zipmod" });
        config.Update(s => s.ModUsage["g-bad"] = 1);

        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = MakeVm(config, db, downloads);
        vm.GamePath = gameDir;

        await vm.ComplementMissingModsCommand.ExecuteAsync(null);

        // 阶段 4：失败数体现在主页汇总（原来只在下载窗口可见）
        Assert.Equal("完成：成功 0，失败 1", vm.ComplementProgress);
        Assert.False(vm.IsComplementing);
    }

    [Fact]
    public void ShowModsReady_RequiresScanDataAndNoMissing()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(_dir);
        var vm = MakeVm(config, db);

        // 全新安装（无扫描数据）：不显示"所有 Mods 已就绪"（阶段 4 门控）
        Assert.False(vm.HasScanData);
        Assert.False(vm.ShowModsReady);

        config.Update(s => s.LocalMods["g1"] = new ModInfo { Name = "m" });
        vm.RefreshStats();
        Assert.True(vm.HasScanData);
        Assert.True(vm.ShowModsReady); // 有数据且无缺失

        db.Update(new Dictionary<string, string> { ["g-x"] = "a/g-x.zipmod" });
        config.Update(s => s.ModUsage["g-x"] = 1); // 产生缺失
        vm.RefreshStats();
        Assert.False(vm.ShowModsReady);
    }

    [Fact]
    public void MissingModCount_OnlyUsageMissingLocallyAndInDatabase()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(_dir);
        db.Update(new Dictionary<string, string>
        {
            ["g-missing"] = "a/g-missing.zipmod",
            ["g-owned"] = "a/g-owned.zipmod",
            // g-not-in-db 不在库中
        });
        config.Update(s =>
        {
            s.ModUsage["g-missing"] = 1;
            s.ModUsage["g-owned"] = 2;
            s.ModUsage["g-not-in-db"] = 3;
            s.LocalMods["g-owned"] = new ModInfo { Name = "Owned" };
        });

        var vm = MakeVm(config, db);
        Assert.Equal(1, vm.MissingModCount); // 只有 g-missing 计入
    }
}
