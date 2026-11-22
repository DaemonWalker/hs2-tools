using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class ModOrganizeTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private string MakeGameDir()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative));
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative));
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative));
        File.WriteAllText(Path.Combine(gameDir, GameProfiles.Hs2.GameExeName), "exe");
        return gameDir;
    }

    // ==================== BuildPlan 单测（合成路径，不触碰真实文件） ====================

    private const string GamePath = "game";
    private const string ModsDir = @"game\mods";
    private const string UnusedDir = @"game\unusedmods";
    private const string MyModsDir = @"game\mods\MyMods";
    private const string DupDir = @"game\duplicatemods";

    private static KeyValuePair<string, ModInfo> Entry(string guid, string version = "1.0.0", string? path = null) =>
        new(guid, new ModInfo
        {
            Name = guid,
            Version = version,
            Path = path ?? $@"{ModsDir}\{guid}.zipmod",
        });

    private static ModOrganizePlan Plan(
        IReadOnlyList<KeyValuePair<string, ModInfo>> entries,
        IReadOnlyDictionary<string, string>? siteIndex = null) =>
        ModOrganizeHelper.BuildPlan(entries,
            siteIndex ?? new Dictionary<string, string>(), GamePath, ModsDir);

    [Fact]
    public void BuildPlan_NotInIndex_MovesToMyMods()
    {
        var plan = Plan([Entry("g-a")]);

        var move = Assert.Single(plan.Unlisted);
        Assert.Equal(MyModsDir, move.TargetDir);
        Assert.Empty(plan.SitePlaced);
        Assert.Empty(plan.Duplicates);
        Assert.Single(plan.Winners);
    }

    [Fact]
    public void BuildPlan_InIndex_MovesToSiteDir()
    {
        var index = new Dictionary<string, string> { ["g-a"] = "Exclusive HS2/a.zipmod" };
        var plan = Plan([Entry("g-a")], index);

        var move = Assert.Single(plan.SitePlaced);
        Assert.Equal(Path.Combine(ModsDir, "Exclusive HS2"), move.TargetDir);
        Assert.Empty(plan.Unlisted);
    }

    [Fact]
    public void BuildPlan_DedupFirst_ThenClassifyWinner()
    {
        var plan = Plan(
            [Entry("g-d", "1.0.0", $@"{ModsDir}\d_v1.zipmod"), Entry("g-d", "2.0.0", $@"{ModsDir}\d_v2.zipmod")]);

        Assert.Equal(1, plan.DupGroups);
        var loser = Assert.Single(plan.Duplicates);
        Assert.Equal("1.0.0", loser.Mod.Version);
        Assert.Equal(DupDir, loser.TargetDir);
        var unlisted = Assert.Single(plan.Unlisted);
        Assert.Equal("2.0.0", unlisted.Mod.Version);
        Assert.Equal("2.0.0", plan.Winners["g-d"].Version);
    }

    [Fact]
    public void BuildPlan_GuidAndIndexMatching_CaseInsensitive()
    {
        var index = new Dictionary<string, string>
        {
            ["G-A"] = "Pack/a.zipmod",
            ["G-B"] = "Pack/b.zipmod",
        };
        var plan = Plan(
            [Entry("g-a"), Entry("g-b"), Entry("g-b", "1.0.0", $@"{ModsDir}\g-b2.zipmod")], index);

        Assert.Equal(2, plan.SitePlaced.Count);
        Assert.Empty(plan.Unlisted);
        Assert.Single(plan.Duplicates);
    }

    [Fact]
    public void BuildPlan_CaseOnlyDuplicateIndexEntries_UsesOrdinalFirstUrl()
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AleX.oopa"] = "Zulu/a.zipmod",
            ["alex.oopa"] = "Alpha/a.zipmod",
        };

        var plan = Plan([Entry("ALEX.OOPA")], index);

        var move = Assert.Single(plan.SitePlaced);
        Assert.Equal(Path.Combine(ModsDir, "Alpha"), move.TargetDir);
        Assert.Empty(plan.Unlisted);
    }

    [Fact]
    public void BuildPlan_UnlistedInMyMods_StaysPut()
    {
        var plan = Plan([Entry("g-u", path: $@"{MyModsDir}\u.zipmod")]);

        Assert.Empty(plan.Unlisted);
        Assert.Empty(plan.SitePlaced);
        Assert.Single(plan.Winners);
    }

    [Fact]
    public void BuildPlan_InUnusedmods_MovesBySideloadMembership()
    {
        var index = new Dictionary<string, string> { ["g-u"] = "Exclusive HS2/u.zipmod" };
        var plan = Plan(
            [Entry("g-u", path: $@"{UnusedDir}\u.zipmod"), Entry("g-w", path: $@"{UnusedDir}\w.zipmod")], index);

        var siteMove = Assert.Single(plan.SitePlaced);
        Assert.Equal("g-u", siteMove.Mod.Name);
        Assert.Equal(Path.Combine(ModsDir, "Exclusive HS2"), siteMove.TargetDir);
        var unlistedMove = Assert.Single(plan.Unlisted);
        Assert.Equal("g-w", unlistedMove.Mod.Name);
        Assert.Equal(MyModsDir, unlistedMove.TargetDir);
    }

    [Fact]
    public void BuildPlan_AlreadyInSiteDir_NotInPlan()
    {
        var index = new Dictionary<string, string> { ["g-a"] = "Exclusive HS2/a.zipmod" };
        var plan = Plan([Entry("g-a", path: $@"{ModsDir}\Exclusive HS2\a.zipmod")], index);

        Assert.Empty(plan.SitePlaced);
        Assert.Empty(plan.Unlisted);
    }

    [Fact]
    public void BuildPlan_IndexPathNoDir_ModsRoot()
    {
        var index = new Dictionary<string, string> { ["g-a"] = "a.zipmod" };
        var plan = Plan([Entry("g-a", path: $@"{ModsDir}\sub\a.zipmod")], index);

        var move = Assert.Single(plan.SitePlaced);
        Assert.Equal(ModsDir, move.TargetDir);
    }

    [Fact]
    public void BuildPlan_MaliciousSitePath_TreatedAsNotInIndex()
    {
        var index = new Dictionary<string, string>
        {
            ["g-a"] = "../evil/a.zipmod",
            ["g-b"] = "C:/evil/b.zipmod",
            ["g-c"] = @"mods\..\..\c.zipmod",
        };
        var plan = Plan([Entry("g-a"), Entry("g-b"), Entry("g-c")], index);

        Assert.Empty(plan.SitePlaced);
        Assert.Equal(3, plan.Unlisted.Count);
        Assert.All(plan.Unlisted, move => Assert.Equal(MyModsDir, move.TargetDir));
    }

    [Fact]
    public void BuildPlan_DedupAcrossDirs_LoserInUnusedmods_ToDuplicates()
    {
        var index = new Dictionary<string, string> { ["g-d"] = "d_new.zipmod" };
        var plan = Plan(
            [
                Entry("g-d", "1.0.0", $@"{UnusedDir}\d_old.zipmod"),
                Entry("g-d", "2.0.0", $@"{ModsDir}\d_new.zipmod"),
            ], index);

        Assert.Equal(1, plan.DupGroups);
        var loser = Assert.Single(plan.Duplicates);
        Assert.Equal($@"{UnusedDir}\d_old.zipmod", loser.Mod.Path);
        Assert.Equal(DupDir, loser.TargetDir);
        Assert.Empty(plan.SitePlaced);
        Assert.Empty(plan.Unlisted);
    }

    // ==================== VM 集成测试 ====================

    private static ModsWindowViewModel MakeVm(ConfigService config, SideloadDatabaseService? db = null) =>
        new(config, new ScannerService(), db ?? new SideloadDatabaseService(config));

    private static SideloadDatabaseService MakeIndexedDb(ConfigService config, Dictionary<string, string> urls)
    {
        var db = new SideloadDatabaseService(config);
        db.Update(urls);
        db.SaveMeta(new SideloadScanMeta
        {
            LastScanTime = DateTime.Now,
            Status = SideloadScanStatus.Success,
            FoundCount = urls.Count,
        });
        return db;
    }

    [Fact]
    public async Task Organize_MovesFilesAndUpdatesLocalMods()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var charaDir = Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative);
        var sceneDir = Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative);

        var pathA = TestAssets.WriteZipmod(modsDir, "a.zipmod", TestAssets.MakeManifest("g-a", "Mod A", "1.0.0"));
        var pathB = TestAssets.WriteZipmod(modsDir, "b.zipmod", TestAssets.MakeManifest("g-b", "Mod B", "1.0.0"));
        var pathC = TestAssets.WriteZipmod(modsDir, "c.zipmod", TestAssets.MakeManifest("g-c", "Mod C", "1.0.0"));
        var pathD1 = TestAssets.WriteZipmod(modsDir, "d_v1.zipmod", TestAssets.MakeManifest("g-d", "Mod D", "1.0.0"));
        var pathD2 = TestAssets.WriteZipmod(modsDir, "d_v2.zipmod", TestAssets.MakeManifest("g-d", "Mod D", "2.0.0"));

        TestAssets.WritePng(charaDir, "chara1.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(["角色甲"], ["g-a", "g-d"]));
        TestAssets.WritePng(sceneDir, "scene1.png",
            TestAssets.PngPrefix(), TestAssets.BuildSceneDataRegion("名1", "名2", ["g-b"]));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var db = MakeIndexedDb(config, new Dictionary<string, string>
        {
            ["g-a"] = "Exclusive HS2/a.zipmod",
            ["g-b"] = "Studio/b.zipmod",
            ["g-d"] = "Exclusive HS2/d.zipmod",
        });
        await TestAssets.RunAnalysisAsync(config, db);
        var vm = MakeVm(config, db);

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        var messages = new List<string>();
        vm.OrganizeMessageRequested += (_, msg) => messages.Add(msg);

        Assert.True(vm.OrganizeCommand.CanExecute(null));
        await vm.OrganizeCommand.ExecuteAsync(null);

        Assert.NotNull(confirmMsg);
        Assert.Contains("duplicatemods", confirmMsg);
        Assert.Contains("3 个 Sideload Mods", confirmMsg);
        Assert.Contains("1 个不在 Sideload 索引中", confirmMsg);
        Assert.Contains("MyMods", confirmMsg);
        Assert.DoesNotContain("unusedmods", confirmMsg);
        Assert.DoesNotContain("scenemods", confirmMsg);

        await vm.ConfirmOrganizeAsync();

        var siteA = Path.Combine(modsDir, "Exclusive HS2", "a.zipmod");
        var siteB = Path.Combine(modsDir, "Studio", "b.zipmod");
        var siteD = Path.Combine(modsDir, "Exclusive HS2", "d_v2.zipmod");
        var unlistedC = Path.Combine(modsDir, ModOrganizeHelper.CustomModsDirectoryName, "c.zipmod");
        Assert.True(File.Exists(Path.Combine(gameDir, "duplicatemods", "d_v1.zipmod")));
        Assert.True(File.Exists(siteA));
        Assert.True(File.Exists(siteB));
        Assert.True(File.Exists(siteD));
        Assert.True(File.Exists(unlistedC));
        Assert.False(File.Exists(pathA));
        Assert.False(File.Exists(pathB));
        Assert.False(File.Exists(pathC));
        Assert.False(File.Exists(pathD1));
        Assert.False(File.Exists(pathD2));

        var localMods = config.Settings.Current.LocalMods;
        Assert.Equal(4, localMods.Count);
        Assert.Equal(siteA, localMods["g-a"].Path);
        Assert.Equal(siteB, localMods["g-b"].Path);
        Assert.Equal(unlistedC, localMods["g-c"].Path);
        Assert.Equal(siteD, localMods["g-d"].Path);
        Assert.Equal(4, config.Settings.Current.ModEntries.Count);

        Assert.Contains(messages, m => m.Contains("整理完成") && m.Contains("去重 1") &&
            m.Contains("非 Sideload 1") && m.Contains("站点归位 3"));
        Assert.False(vm.IsOrganizing);
        Assert.Equal("整理 Mods", vm.OrganizeButtonText);
    }

    [Fact]
    public async Task Organize_Unusedmods_MovesAllWinnersIntoMods()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var charaDir = Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative);
        var unusedDir = Path.Combine(gameDir, "unusedmods");
        Directory.CreateDirectory(unusedDir);

        var pathU = TestAssets.WriteZipmod(unusedDir, "u.zipmod", TestAssets.MakeManifest("g-u", "Mod U"));
        var pathW = TestAssets.WriteZipmod(unusedDir, "w.zipmod", TestAssets.MakeManifest("g-w", "Mod W"));
        TestAssets.WritePng(charaDir, "chara1.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(["角色甲"], ["g-u"]));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var db = MakeIndexedDb(config, new Dictionary<string, string>
        {
            ["g-u"] = "Exclusive HS2/u.zipmod",
        });
        await TestAssets.RunAnalysisAsync(config, db);
        var vm = MakeVm(config, db);

        await vm.OrganizeCommand.ExecuteAsync(null);
        await vm.ConfirmOrganizeAsync();

        var siteU = Path.Combine(modsDir, "Exclusive HS2", "u.zipmod");
        var unlistedW = Path.Combine(modsDir, ModOrganizeHelper.CustomModsDirectoryName, "w.zipmod");
        Assert.True(File.Exists(siteU));
        Assert.True(File.Exists(unlistedW));
        Assert.False(File.Exists(pathU));
        Assert.False(File.Exists(pathW));

        var localMods = config.Settings.Current.LocalMods;
        Assert.Equal(2, localMods.Count);
        Assert.Equal(siteU, localMods["g-u"].Path);
        Assert.Equal(unlistedW, localMods["g-w"].Path);
    }

    [Fact]
    public async Task Organize_IncludesSceneModsAndDownloadDirs()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var sceneModsDir = Path.Combine(modsDir, "scenemods");
        var downloadDir = Path.Combine(gameDir, GameProfiles.Hs2.ModDownloadDirRelative);
        Directory.CreateDirectory(sceneModsDir);
        Directory.CreateDirectory(downloadDir);
        var pathS = TestAssets.WriteZipmod(sceneModsDir, "s.zipmod", TestAssets.MakeManifest("g-s", "Scene Mod"));
        var pathDl = TestAssets.WriteZipmod(downloadDir, "dl.zipmod", TestAssets.MakeManifest("g-dl", "Download Mod"));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var db = MakeIndexedDb(config, new Dictionary<string, string>
        {
            ["g-s"] = "Studio/s.zipmod",
        });
        await TestAssets.RunAnalysisAsync(config, db);
        var vm = MakeVm(config, db);

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        await vm.OrganizeCommand.ExecuteAsync(null);
        Assert.NotNull(confirmMsg);
        Assert.Contains("1 个 Sideload Mods", confirmMsg);
        Assert.Contains("1 个不在 Sideload 索引中", confirmMsg);

        await vm.ConfirmOrganizeAsync();

        Assert.True(File.Exists(Path.Combine(modsDir, "Studio", "s.zipmod")));
        Assert.True(File.Exists(Path.Combine(modsDir, ModOrganizeHelper.CustomModsDirectoryName, "dl.zipmod")));
        Assert.False(File.Exists(pathS));
        Assert.False(File.Exists(pathDl));
    }

    [Fact]
    public async Task Organize_NoMeta_ShowsHintWithoutMovingFiles()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var pathA = TestAssets.WriteZipmod(modsDir, "a.zipmod", TestAssets.MakeManifest("g-a", "Mod A"));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        await TestAssets.RunAnalysisAsync(config);
        var vm = MakeVm(config);

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        string? message = null;
        vm.OrganizeMessageRequested += (_, msg) => message = msg;
        await vm.OrganizeCommand.ExecuteAsync(null);

        Assert.Null(confirmMsg);
        Assert.Equal("请先在主窗口运行「Sideloader 更新」后再整理 Mods", message);
        Assert.True(File.Exists(pathA));
    }

    [Fact]
    public async Task Organize_NothingToDo_ShowsNoNeedMessage()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        TestAssets.WriteZipmod(modsDir, "a.zipmod", TestAssets.MakeManifest("g-a", "Mod A"));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var db = MakeIndexedDb(config, new Dictionary<string, string> { ["g-a"] = "a.zipmod" });
        await TestAssets.RunAnalysisAsync(config, db);
        var vm = MakeVm(config, db);

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        string? message = null;
        vm.OrganizeMessageRequested += (_, msg) => message = msg;

        await vm.OrganizeCommand.ExecuteAsync(null);

        Assert.Null(confirmMsg);
        Assert.Equal("Mods 无需整理", message);
    }

    [Fact]
    public async Task Cleanup_NeverDeletesMyModsDirectory()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var protectedDir = Path.Combine(modsDir, ModOrganizeHelper.CustomModsDirectoryName);
        Directory.CreateDirectory(protectedDir);
        var protectedPath = TestAssets.WriteZipmod(protectedDir, "protected.zipmod", TestAssets.MakeManifest("g-p", "Protected"));
        var normalPath = TestAssets.WriteZipmod(modsDir, "normal.zipmod", TestAssets.MakeManifest("g-n", "Normal"));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var db = MakeIndexedDb(config, new Dictionary<string, string>
        {
            ["g-p"] = "Pack/protected.zipmod",
            ["g-n"] = "Pack/normal.zipmod",
        });
        await TestAssets.RunAnalysisAsync(config, db);
        var vm = MakeVm(config, db);

        string? confirmMsg = null;
        vm.CleanupConfirmationRequested += (_, msg) => confirmMsg = msg;
        await vm.CleanupCommand.ExecuteAsync(null);
        Assert.NotNull(confirmMsg);
        Assert.Contains("发现 1 个", confirmMsg);
        await vm.ConfirmCleanupAsync();

        Assert.True(File.Exists(protectedPath));
        Assert.False(File.Exists(normalPath));
    }

    [Fact]
    public async Task Organize_NoGamePath_ShowsMessage()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        string? message = null;
        vm.OrganizeMessageRequested += (_, msg) => message = msg;

        await vm.OrganizeCommand.ExecuteAsync(null);

        Assert.Null(confirmMsg);
        Assert.Equal("请先设置游戏目录", message);
    }

    [Fact]
    public async Task Organize_NoAnalysis_ShowsHint()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        TestAssets.WriteZipmod(modsDir, "a.zipmod", TestAssets.MakeManifest("g-a", "Mod A"));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = MakeVm(config);

        string? confirmMsg = null, message = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        vm.OrganizeMessageRequested += (_, msg) => message = msg;

        await vm.OrganizeCommand.ExecuteAsync(null);

        Assert.Null(confirmMsg);
        Assert.Equal("请先在首页运行「开始分析」", message);
    }

    [Fact]
    public void Organize_WhileOrganizing_CannotReenter()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.IsOrganizing = true;

        Assert.False(vm.OrganizeCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.DedupCommand.CanExecute(null));
        Assert.Equal("整理中...", vm.OrganizeButtonText);
    }
}
