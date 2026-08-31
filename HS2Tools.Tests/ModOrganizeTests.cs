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

    // ==================== BuildPlan 单测 ====================

    private static KeyValuePair<string, ModInfo> Entry(string guid, string version = "1.0.0", string? path = null) =>
        new(guid, new ModInfo { Name = guid, Version = version, Path = path ?? $@"mods\{guid}.zipmod" });

    private static readonly ISet<string> EmptyUsage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void BuildPlan_Unused_WhenNeitherCardReferences()
    {
        var plan = ModOrganizeHelper.BuildPlan([Entry("g-a")], EmptyUsage, EmptyUsage);

        Assert.Single(plan.Unused);
        Assert.Empty(plan.SceneOnly);
        Assert.Empty(plan.Duplicates);
        Assert.Single(plan.Winners);
    }

    [Fact]
    public void BuildPlan_SceneOnly_WhenOnlySceneReferences()
    {
        var sceneUsage = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "g-b" };
        var plan = ModOrganizeHelper.BuildPlan([Entry("g-b")], EmptyUsage, sceneUsage);

        Assert.Single(plan.SceneOnly);
        Assert.Empty(plan.Unused);
    }

    [Fact]
    public void BuildPlan_Stays_WhenCharaReferences()
    {
        var charaUsage = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "g-a", "g-b" };
        var sceneUsage = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "g-b" };
        var plan = ModOrganizeHelper.BuildPlan([Entry("g-a"), Entry("g-b")], charaUsage, sceneUsage);

        Assert.Empty(plan.Unused);
        Assert.Empty(plan.SceneOnly); // 人物卡引用的留原地（即使场景也引用）
    }

    [Fact]
    public void BuildPlan_DedupFirst_ThenClassifyWinner()
    {
        // 同 GUID 两个版本：v2 赢家进 Unused，v1 落选进 Duplicates
        var plan = ModOrganizeHelper.BuildPlan(
            [Entry("g-d", "1.0.0", @"mods\d_v1.zipmod"), Entry("g-d", "2.0.0", @"mods\d_v2.zipmod")],
            EmptyUsage, EmptyUsage);

        Assert.Equal(1, plan.DupGroups);
        var loser = Assert.Single(plan.Duplicates);
        Assert.Equal("1.0.0", loser.Version);
        var unused = Assert.Single(plan.Unused);
        Assert.Equal("2.0.0", unused.Version);
        Assert.Equal("2.0.0", plan.Winners["g-d"].Version);
    }

    [Fact]
    public void BuildPlan_GuidMatching_CaseInsensitive()
    {
        var charaUsage = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "G-A" };
        var plan = ModOrganizeHelper.BuildPlan(
            [Entry("g-a"), Entry("g-b"), Entry("g-b", "1.0.0", @"mods\g-b2.zipmod")],
            charaUsage, EmptyUsage);

        Assert.Empty(plan.Unused.Concat(plan.SceneOnly).Where(m => m.Name == "g-a")); // g-a 被引用（大小写不同也命中）
        Assert.Single(plan.Unused); // g-b 赢家未使用
        Assert.Single(plan.Duplicates); // g-b 重复落选（同组大小写一致，不重复计）
    }

    // ==================== VM 集成测试 ====================

    [Fact]
    public async Task Organize_MovesFilesAndUpdatesLocalMods()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var charaDir = Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative);
        var sceneDir = Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative);

        // A 人物卡用 / B 仅场景用 / C 无人用 / D 同 GUID 两版本（v2 应保留，也被人物卡引用）
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
        var vm = new ModsWindowViewModel(config, new ScannerService());

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        var messages = new List<string>();
        vm.OrganizeMessageRequested += (_, msg) => messages.Add(msg);

        Assert.True(vm.OrganizeCommand.CanExecute(null));
        await vm.OrganizeCommand.ExecuteAsync(null);

        Assert.NotNull(confirmMsg);
        Assert.Contains("duplicatemods", confirmMsg);
        Assert.Contains("unusedmods", confirmMsg);
        Assert.Contains("scenemods", confirmMsg);

        await vm.ConfirmOrganizeAsync();

        // D 落选 v1 → duplicatemods；C → unusedmods；B → mods/scenemods
        Assert.True(File.Exists(Path.Combine(gameDir, "duplicatemods", "d_v1.zipmod")));
        Assert.True(File.Exists(Path.Combine(gameDir, "unusedmods", "c.zipmod")));
        var sceneModsDir = Path.Combine(modsDir, "scenemods");
        Assert.True(File.Exists(Path.Combine(sceneModsDir, "b.zipmod")));
        Assert.False(File.Exists(pathD1));
        Assert.False(File.Exists(pathC));
        Assert.False(File.Exists(pathB));

        // A 与 D 赢家原地不动
        Assert.True(File.Exists(pathA));
        Assert.True(File.Exists(pathD2));

        // LocalMods：未动赢家保留、SceneOnly 更新 Path、Unused 移除
        var localMods = config.Settings.Current.LocalMods;
        Assert.Equal(3, localMods.Count);
        Assert.Equal(pathA, localMods["g-a"].Path);
        Assert.Equal(Path.Combine(sceneModsDir, "b.zipmod"), localMods["g-b"].Path);
        Assert.Equal(pathD2, localMods["g-d"].Path);
        Assert.False(localMods.ContainsKey("g-c"));

        Assert.Contains(messages, m => m.Contains("整理完成") && m.Contains("去重 1") && m.Contains("未使用 1") && m.Contains("场景专用 1"));
        Assert.False(vm.IsOrganizing);
        Assert.Equal("整理 Mods", vm.OrganizeButtonText);
    }

    [Fact]
    public async Task Organize_NothingToDo_ShowsNoNeedMessage()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var charaDir = Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative);
        TestAssets.WriteZipmod(modsDir, "a.zipmod", TestAssets.MakeManifest("g-a", "Mod A"));
        TestAssets.WritePng(charaDir, "chara1.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(["角色甲"], ["g-a"]));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new ModsWindowViewModel(config, new ScannerService());

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        string? message = null;
        vm.OrganizeMessageRequested += (_, msg) => message = msg;

        await vm.OrganizeCommand.ExecuteAsync(null);

        Assert.Null(confirmMsg); // 无需整理不弹确认
        Assert.Equal("Mods 无需整理", message);
    }

    [Fact]
    public async Task Organize_SkipsSceneModsAndDownloadDirs()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        // mods/scenemods 与下载目录内的 zipmod 即使无人引用也不参与整理
        var sceneModsDir = Path.Combine(modsDir, "scenemods");
        var downloadDir = Path.Combine(gameDir, GameProfiles.Hs2.ModDownloadDirRelative);
        Directory.CreateDirectory(sceneModsDir);
        Directory.CreateDirectory(downloadDir);
        var pathS = TestAssets.WriteZipmod(sceneModsDir, "s.zipmod", TestAssets.MakeManifest("g-s", "Scene Mod"));
        var pathDl = TestAssets.WriteZipmod(downloadDir, "dl.zipmod", TestAssets.MakeManifest("g-dl", "Download Mod"));
        // mods 根目录一个真未使用的，保证有计划可执行
        var pathC = TestAssets.WriteZipmod(modsDir, "c.zipmod", TestAssets.MakeManifest("g-c", "Mod C"));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new ModsWindowViewModel(config, new ScannerService());

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        await vm.OrganizeCommand.ExecuteAsync(null);
        Assert.NotNull(confirmMsg);
        Assert.Contains("1 个未使用", confirmMsg); // 只数到 g-c，不含 scenemods/下载目录内的

        await vm.ConfirmOrganizeAsync();

        Assert.True(File.Exists(pathS));  // 不被二次移动
        Assert.True(File.Exists(pathDl));
        Assert.False(File.Exists(pathC));
        Assert.True(File.Exists(Path.Combine(gameDir, "unusedmods", "c.zipmod")));

        var localMods = config.Settings.Current.LocalMods;
        Assert.Empty(localMods); // 计划只含 g-c，已移出
    }

    [Fact]
    public async Task Organize_NoGamePath_ShowsMessage()
    {
        using var config = new ConfigService(_dir); // 未设 GamePath
        var vm = new ModsWindowViewModel(config, new ScannerService());

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        string? message = null;
        vm.OrganizeMessageRequested += (_, msg) => message = msg;

        await vm.OrganizeCommand.ExecuteAsync(null);

        Assert.Null(confirmMsg);
        Assert.Equal("请先设置游戏目录", message);
    }

    [Fact]
    public void Organize_WhileOrganizing_CannotReenter()
    {
        using var config = new ConfigService(_dir);
        var vm = new ModsWindowViewModel(config, new ScannerService());
        vm.IsOrganizing = true; // 模拟整理中

        Assert.False(vm.OrganizeCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null)); // 三命令互斥
        Assert.False(vm.DedupCommand.CanExecute(null));
        Assert.Equal("整理中...", vm.OrganizeButtonText);
    }
}
