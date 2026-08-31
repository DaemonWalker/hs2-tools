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

    private string MakeGameDir() => MakeGameDir(GameProfiles.Hs2, "game");

    private string MakeGameDir(GameProfile profile, string name)
    {
        var gameDir = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(gameDir, profile.ModsDirRelative));
        Directory.CreateDirectory(Path.Combine(gameDir, profile.SceneDirRelative));
        Directory.CreateDirectory(Path.Combine(gameDir, profile.CharaDirRelative));
        File.WriteAllText(Path.Combine(gameDir, profile.GameExeName), "exe");
        return gameDir;
    }

    private sealed class FakeSideloader : ISideloaderService
    {
        public bool IsRunning { get; private set; }
        public bool Cancelled { get; private set; }
        public Dictionary<string, string> Result { get; set; } = new();
        public Exception? Throw { get; set; } // 非 null 时 Gate 放行后抛出（异常分支测试）
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
                if (Throw is not null)
                    throw Throw;
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
        config.Update(s => s.Current.GamePath = @"C:\HS2");

        var vm = MakeVm(config, new SideloadDatabaseService(config));

        Assert.Equal(@"C:\HS2", vm.GamePath);
        Assert.False(vm.IsGamePathValid); // 路径不存在 → 未通过校验
        Assert.Contains("未找到", vm.PathStatusText);
    }

    [Fact]
    public void Ctor_DoesNotRewriteSettingsFile()
    {
        // 加载已有配置时 GamePath 未变 → 不触发写盘
        using var config = new ConfigService(_dir);
        _ = MakeVm(config, new SideloadDatabaseService(config));
        Assert.False(File.Exists(config.SettingsPath));
    }

    [Fact]
    public void SetValidGamePath_ValidatesAndPersists()
    {
        var gameDir = MakeGameDir();
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        vm.GamePath = gameDir;

        Assert.True(vm.IsGamePathValid);
        Assert.Contains("已验证", vm.PathStatusText);
        config.Save();

        using var reloaded = new ConfigService(_dir);
        Assert.Equal(gameDir, reloaded.Settings.Current.GamePath);
    }

    [Fact]
    public void SetInvalidGamePath_DoesNotValidate()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        vm.GamePath = Path.Combine(_dir, "no-such-dir");

        Assert.False(vm.IsGamePathValid);
        Assert.Contains("未找到", vm.PathStatusText);
    }

    [Fact]
    public void ValidateGamePath_Cases()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        Assert.False(vm.ValidateGamePath(null));
        Assert.False(vm.ValidateGamePath(""));
        Assert.False(vm.ValidateGamePath(_dir)); // 目录存在但无 exe
        Assert.True(vm.ValidateGamePath(MakeGameDir()));
    }

    [Fact]
    public void LaunchCommands_RequireValidPath()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        Assert.False(vm.LaunchGameCommand.CanExecute(null));
        Assert.False(vm.LaunchStudioCommand.CanExecute(null));

        vm.GamePath = MakeGameDir();
        Assert.True(vm.LaunchGameCommand.CanExecute(null));
        Assert.True(vm.LaunchStudioCommand.CanExecute(null));
    }

    // ==================== 游戏切换 ====================

    [Fact]
    public void SwitchGame_LoadsTargetGamePathAndHighlights()
    {
        var hs2Dir = MakeGameDir();
        var kkDir = MakeGameDir(GameProfiles.Kk, "game-kk");
        using var config = new ConfigService(_dir);
        config.Update(s => { s.CurrentGame = "hs2"; s.Current.GamePath = hs2Dir; });
        config.Update(s => { s.CurrentGame = "kk"; s.Current.GamePath = kkDir; });
        config.Update(s => s.CurrentGame = "hs2");

        var vm = MakeVm(config, new SideloadDatabaseService(config));
        Assert.Equal(hs2Dir, vm.GamePath);
        Assert.True(vm.GameSwitchItems.Single(i => i.Id == "hs2").IsCurrent);

        vm.SwitchGameCommand.Execute("kk");

        Assert.Equal("kk", config.Settings.CurrentGame);
        Assert.Equal(kkDir, vm.GamePath); // 加载目标游戏的已配路径
        Assert.True(vm.IsGamePathValid);
        Assert.True(vm.GameSwitchItems.Single(i => i.Id == "kk").IsCurrent);
        Assert.False(vm.GameSwitchItems.Single(i => i.Id == "hs2").IsCurrent);
    }

    [Fact]
    public void SwitchGame_PathsIsolatedPerGame()
    {
        var hs2Dir = MakeGameDir();
        var kkDir = MakeGameDir(GameProfiles.Kk, "game-kk");
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        vm.GamePath = hs2Dir; // 配 hs2
        vm.SwitchGameCommand.Execute("kk");
        Assert.Equal("", vm.GamePath); // kk 未配过 → 空
        Assert.False(vm.IsGamePathValid);
        Assert.Contains(GameProfiles.Kk.GameExeName, vm.PathStatusText); // 按 kk 的 exe 名提示

        vm.GamePath = kkDir; // 配 kk（写进 kk 的数据，不碰 hs2）
        vm.SwitchGameCommand.Execute("hs2");

        Assert.Equal(hs2Dir, vm.GamePath);
        Assert.True(vm.IsGamePathValid);
        Assert.Equal(hs2Dir, config.Settings.Games["hs2"].GamePath);
        Assert.Equal(kkDir, config.Settings.Games["kk"].GamePath);
    }

    [Fact]
    public void ValidateGamePath_UsesCurrentGameExeName()
    {
        var kkDir = MakeGameDir(GameProfiles.Kk, "game-kk"); // 只有 Koikatu.exe
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        Assert.False(vm.ValidateGamePath(kkDir)); // hs2 视角：无 HoneySelect2.exe

        vm.SwitchGameCommand.Execute("kk");
        Assert.True(vm.ValidateGamePath(kkDir)); // kk 视角：找到 Koikatu.exe
    }

    [Fact]
    public void SwitchGame_SamePathString_RevalidatesWithNewExe()
    {
        // 两游戏配同一路径串：切换时 setter 不触发，也必须按新 exe 重校验
        var hs2Dir = MakeGameDir();
        using var config = new ConfigService(_dir);
        config.Update(s => { s.CurrentGame = "hs2"; s.Current.GamePath = hs2Dir; });
        config.Update(s => { s.CurrentGame = "kk"; s.Current.GamePath = hs2Dir; });
        config.Update(s => s.CurrentGame = "hs2");

        var vm = MakeVm(config, new SideloadDatabaseService(config));
        Assert.True(vm.IsGamePathValid);

        vm.SwitchGameCommand.Execute("kk"); // 路径串相同，但该目录没有 Koikatu.exe
        Assert.Equal(hs2Dir, vm.GamePath);
        Assert.False(vm.IsGamePathValid);
    }

    [Fact]
    public void SwitchGame_RefreshesStatsAndResetsScanDisplay()
    {
        var hs2Dir = MakeGameDir();
        var kkDir = MakeGameDir(GameProfiles.Kk, "game-kk");
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.LocalMods["g-hs2"] = new ModInfo { Name = "m" };
            s.Current.ModUsage["g-hs2"] = 2;
        });

        var vm = MakeVm(config, new SideloadDatabaseService(config));
        Assert.Equal(1, vm.ModCount);
        Assert.Equal(2, vm.TotalRefs);

        vm.SwitchGameCommand.Execute("kk"); // kk 无扫描数据

        Assert.Equal(0, vm.ModCount);
        Assert.Equal(0, vm.UsageCount);
        Assert.Equal(0, vm.TotalRefs);
        Assert.False(vm.ScanCompleted); // 旧游戏扫描展示重置

        vm.SwitchGameCommand.Execute("hs2"); // 切回读回 hs2 的统计
        Assert.Equal(1, vm.ModCount);
    }

    [Fact]
    public void SwitchGame_SameId_IsNoOp()
    {
        var hs2Dir = MakeGameDir();
        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = hs2Dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        vm.SwitchGameCommand.Execute("hs2"); // 当前游戏：不触发任何变化

        Assert.Equal("hs2", config.Settings.CurrentGame);
        Assert.Equal(hs2Dir, vm.GamePath);
        Assert.False(File.Exists(config.SettingsPath)); // 无写盘
    }

    [Fact]
    public void ConfigChanged_ExternalGameSwitch_SyncsGamePath()
    {
        var hs2Dir = MakeGameDir();
        var kkDir = MakeGameDir(GameProfiles.Kk, "game-kk");
        using var config = new ConfigService(_dir);
        config.Update(s => { s.CurrentGame = "hs2"; s.Current.GamePath = hs2Dir; });
        config.Update(s => { s.CurrentGame = "kk"; s.Current.GamePath = kkDir; });
        config.Update(s => s.CurrentGame = "hs2");

        var vm = MakeVm(config, new SideloadDatabaseService(config));
        Assert.Equal(hs2Dir, vm.GamePath);

        config.Update(s => s.CurrentGame = "kk"); // 模拟别的窗口切游戏

        Assert.Equal(kkDir, vm.GamePath); // 测试环境 UiDispatch 同步执行
        Assert.True(vm.IsGamePathValid);
        Assert.True(vm.GameSwitchItems.Single(i => i.Id == "kk").IsCurrent);
    }

    [Fact]
    public void SwitchGame_DisabledWhileScanning()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));

        Assert.True(vm.SwitchGameCommand.CanExecute("kk"));
        vm.IsScanning = true; // 在飞扫描持有旧游戏目录，禁止切换（结果会落错游戏）
        Assert.False(vm.SwitchGameCommand.CanExecute("kk"));
        vm.IsScanning = false;
        Assert.True(vm.SwitchGameCommand.CanExecute("kk"));
    }

    // ==================== 数据分析 ====================

    [Fact]
    public async Task Scan_PopulatesModsUsageAndStats()
    {
        var gameDir = MakeGameDir();
        TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "m1.zipmod",
            TestAssets.MakeManifest("g-mod", "Mod One"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative), "s1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-scene"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-chara"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));
        vm.GamePath = gameDir;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ScanCompleted);
        Assert.False(vm.IsScanning);
        Assert.Equal("重新分析", vm.ScanButtonText);
        Assert.Equal("1/1", vm.ModScanProgress);
        Assert.Equal("1/1", vm.SceneScanProgress);
        Assert.Equal("1/1", vm.CharaScanProgress);
        Assert.True(vm.ModScanDone && vm.SceneScanDone && vm.CharaScanDone);

        Assert.True(config.Settings.Current.LocalMods.ContainsKey("g-mod"));
        Assert.Equal("Mod One", config.Settings.Current.LocalMods["g-mod"].Name);
        Assert.Equal(1, config.Settings.Current.ModUsage["g-scene"]);
        Assert.Equal(1, config.Settings.Current.ModUsage["g-chara"]);

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
        var vm = MakeVm(config, new SideloadDatabaseService(config));
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
    public async Task Scan_PersistsOrganizeCache()
    {
        // 去重/整理的数据源缓存：完整条目（含重复 guid、覆盖 unusedmods）、分卡引用、shader 命中、时点
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, "mods");
        var unusedDir = Path.Combine(gameDir, "unusedmods");
        Directory.CreateDirectory(unusedDir);
        var shaderManifest = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<manifest>\n" +
                             "<guid>g-shader</guid>\n<name>Shader Pack</name>\n<version>1.0</version>\n" +
                             "<MaterialEditor>\n<Shader Name=\"xukmi/SkinPlus\" />\n<Shader Name=\"xukmi/FX\" />\n" +
                             "</MaterialEditor>\n</manifest>";
        TestAssets.WriteZipmod(modsDir, "a_v1.zipmod", TestAssets.MakeManifest("g-a", "A", "1.0"));
        TestAssets.WriteZipmod(modsDir, "a_v2.zipmod", TestAssets.MakeManifest("g-a", "A", "2.0")); // 重复 guid
        TestAssets.WriteZipmod(modsDir, "shader.zipmod", shaderManifest);
        TestAssets.WriteZipmod(unusedDir, "u.zipmod", TestAssets.MakeManifest("g-u", "U"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative), "s1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-a"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.BuildKkCharaDataRegionWithShaders("白峰", "一乃", ["g-u"], ["xukmi/SkinPlus"]));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));
        vm.GamePath = gameDir;
        await vm.ScanCommand.ExecuteAsync(null);

        var data = config.Settings.Current;
        Assert.Equal(4, data.ModEntries.Count); // 完整条目不折叠重复：a_v1/a_v2/shader + unusedmods 的 u
        Assert.Equal(2, data.ModEntries.Count(e => e.Guid == "g-a"));
        Assert.Contains(data.ModEntries, e => e.Guid == "g-u");
        Assert.Equal(1, data.SceneUsage["g-a"]);
        Assert.False(data.CharaUsage.ContainsKey("g-a")); // 分卡口径：g-a 仅场景引用
        Assert.Equal(1, data.CharaUsage["g-u"]);
        Assert.Equal(["xukmi/SkinPlus"], data.UsedShaderNames); // 只收录卡片命中的 shader 名
        Assert.NotNull(data.LastAnalysisTime);
    }

    [Fact]
    public async Task Scan_MergeSemantics_CharaOverwritesScene()
    {
        // 原版 { ...scene, ...female }：同 guid 时角色统计覆盖场景统计
        var gameDir = MakeGameDir();
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative), "s1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-both"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative), "s2.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-both"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-both"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));
        vm.GamePath = gameDir;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(1, config.Settings.Current.ModUsage["g-both"]); // 场景计 2，被角色计 1 覆盖
    }

    [Fact]
    public async Task Scan_UnusedMods_Counted_ReferencedConfirmedMoveBack()
    {
        var gameDir = MakeGameDir();
        var unusedDir = Path.Combine(gameDir, "unusedmods");
        Directory.CreateDirectory(unusedDir);
        // unusedmods：g-moved 被人物卡引用（候选）、g-keep 无人引用、g-dup 与 mods 里同 GUID（重复，不移回）
        TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "dup.zipmod", TestAssets.MakeManifest("g-dup", "Dup"));
        TestAssets.WriteZipmod(unusedDir, "moved.zipmod", TestAssets.MakeManifest("g-moved", "Moved"));
        TestAssets.WriteZipmod(unusedDir, "keep.zipmod", TestAssets.MakeManifest("g-keep", "Keep"));
        TestAssets.WriteZipmod(unusedDir, "dup2.zipmod", TestAssets.MakeManifest("g-dup", "Dup2"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-moved"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));
        vm.GamePath = gameDir;

        string? confirmMsg = null;
        vm.MoveBackConfirmationRequested += (_, msg) => confirmMsg = msg;
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ScanCompleted);
        Assert.Equal(3, vm.UnusedModCount); // unusedmods 全部 3 个 GUID 计入"未使用"
        Assert.NotNull(confirmMsg);
        Assert.Contains("1 个", confirmMsg); // 候选仅 g-moved
        Assert.False(config.Settings.Current.LocalMods.ContainsKey("g-moved")); // 确认前不动

        var (moved, failed) = await vm.ConfirmMoveBackAsync();
        Assert.Equal((1, 0), (moved, failed));
        Assert.True(File.Exists(Path.Combine(gameDir, "mods", "moved.zipmod")));
        Assert.True(config.Settings.Current.LocalMods.ContainsKey("g-moved"));
        Assert.Equal("Moved", config.Settings.Current.LocalMods["g-moved"].Name);
        Assert.Equal(2, vm.UnusedModCount);
        // 未引用与同 GUID 重复的原地不动
        Assert.True(File.Exists(Path.Combine(unusedDir, "keep.zipmod")));
        Assert.True(File.Exists(Path.Combine(unusedDir, "dup2.zipmod")));
    }

    [Fact]
    public async Task Scan_UnusedModsNothingReferenced_NoConfirmation()
    {
        var gameDir = MakeGameDir();
        var unusedDir = Path.Combine(gameDir, "unusedmods");
        Directory.CreateDirectory(unusedDir);
        TestAssets.WriteZipmod(unusedDir, "keep.zipmod", TestAssets.MakeManifest("g-keep", "Keep"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "c1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g-other"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));
        vm.GamePath = gameDir;

        var fired = false;
        vm.MoveBackConfirmationRequested += (_, _) => fired = true;
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.UnusedModCount);
        Assert.False(fired);
        Assert.Equal((0, 0), await vm.ConfirmMoveBackAsync()); // 无待执行清单
        Assert.True(File.Exists(Path.Combine(unusedDir, "keep.zipmod")));
    }

    // ==================== Sideloader 更新 ====================

    [Fact]
    public async Task Sideloader_RunToSuccess_UpdatesDatabase()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
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
        var reloaded = new SideloadDatabaseService(config);
        Assert.Equal("dir/g1.zipmod", reloaded.Database["g1"]);
    }

    [Fact]
    public async Task Sideloader_StopAfterConfirm_DoesNotUpdateDatabase()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
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
        Assert.False(File.Exists(Path.Combine(_dir, "sideload-hs2.json"))); // 部分结果不落盘
    }

    [Fact]
    public async Task Sideloader_Success_WritesMeta()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        var fake = new FakeSideloader { Result = new() { ["g1"] = "dir/g1.zipmod" } };
        var vm = MakeVm(config, db, factory: () => fake);

        vm.ToggleSideloaderCommand.Execute(null);
        await WaitFor(() => fake.IsRunning);
        fake.Gate.SetResult();
        await WaitFor(() => vm.SideloaderState == SideloaderUiState.Success);

        var meta = db.GetMeta();
        Assert.NotNull(meta);
        Assert.Equal(SideloadScanStatus.Success, meta.Status);
        Assert.Equal(1, meta.FoundCount);
        Assert.True((DateTime.Now - meta.LastScanTime).TotalMinutes < 1);
        Assert.True(File.Exists(Path.Combine(_dir, "sideload-hs2.meta.json"))); // 独立 meta 文件落盘
    }

    [Fact]
    public async Task Sideloader_Stopped_WritesMeta()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        var fake = new FakeSideloader { Result = new() { ["g1"] = "dir/g1.zipmod" } };
        var vm = MakeVm(config, db, factory: () => fake);

        vm.ToggleSideloaderCommand.Execute(null);
        await WaitFor(() => fake.IsRunning);
        vm.ConfirmStopSideloader();
        await WaitFor(() => vm.SideloaderState == SideloaderUiState.Stopped);

        var meta = db.GetMeta();
        Assert.NotNull(meta);
        Assert.Equal(SideloadScanStatus.Stopped, meta.Status);
        Assert.Equal(1, meta.FoundCount); // 已发现的部分结果数
    }

    [Fact]
    public async Task Sideloader_Error_WritesMeta()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        var fake = new FakeSideloader { Throw = new InvalidOperationException("boom") };
        var vm = MakeVm(config, db, factory: () => fake);

        vm.ToggleSideloaderCommand.Execute(null);
        await WaitFor(() => fake.IsRunning);
        fake.Gate.SetResult();
        await WaitFor(() => vm.SideloaderState == SideloaderUiState.Error);

        var meta = db.GetMeta();
        Assert.NotNull(meta);
        Assert.Equal(SideloadScanStatus.Error, meta.Status);
        Assert.Equal("boom", meta.Error);
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
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string> { ["g-missing"] = "mods/g-missing.zipmod" });
        config.Update(s => s.Current.ModUsage["g-missing"] = 1);

        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = MakeVm(config, db, downloads);
        vm.DownloadBaseUrlOverride = server.BaseUrl; // 下载指向本地测试服务器
        vm.GamePath = gameDir;

        Assert.Equal(1, vm.MissingModCount);
        Assert.True(vm.ComplementMissingModsCommand.CanExecute(null));

        await vm.ComplementMissingModsCommand.ExecuteAsync(null);

        var outFile = Path.Combine(gameDir, GameProfiles.Hs2.ModDownloadDirRelative, "g-missing.zipmod");
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
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string> { ["g-bad"] = "mods/g-bad.zipmod" });
        config.Update(s => s.Current.ModUsage["g-bad"] = 1);

        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = MakeVm(config, db, downloads);
        vm.DownloadBaseUrlOverride = server.BaseUrl; // 下载指向本地测试服务器
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
        var db = new SideloadDatabaseService(config);
        var vm = MakeVm(config, db);

        // 全新安装（无扫描数据）：不显示"所有 Mods 已就绪"（阶段 4 门控）
        Assert.False(vm.HasScanData);
        Assert.False(vm.ShowModsReady);

        config.Update(s => s.Current.LocalMods["g1"] = new ModInfo { Name = "m" });
        vm.RefreshStats();
        Assert.True(vm.HasScanData);
        Assert.True(vm.ShowModsReady); // 有数据且无缺失

        db.Update(new Dictionary<string, string> { ["g-x"] = "a/g-x.zipmod" });
        config.Update(s => s.Current.ModUsage["g-x"] = 1); // 产生缺失
        vm.RefreshStats();
        Assert.False(vm.ShowModsReady);
    }

    [Fact]
    public void MissingModCount_OnlyUsageMissingLocallyAndInDatabase()
    {
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string>
        {
            ["g-missing"] = "a/g-missing.zipmod",
            ["g-owned"] = "a/g-owned.zipmod",
            // g-not-in-db 不在库中
        });
        config.Update(s =>
        {
            s.Current.ModUsage["g-missing"] = 1;
            s.Current.ModUsage["g-owned"] = 2;
            s.Current.ModUsage["g-not-in-db"] = 3;
            s.Current.LocalMods["g-owned"] = new ModInfo { Name = "Owned" };
        });

        var vm = MakeVm(config, db);
        Assert.Equal(1, vm.MissingModCount); // 只有 g-missing 计入
    }
}
