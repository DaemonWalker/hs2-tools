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
    private const string SceneModsDir = @"game\mods\scenemods";
    private const string DupDir = @"game\duplicatemods";

    private static KeyValuePair<string, ModInfo> Entry(string guid, string version = "1.0.0", string? path = null) =>
        new(guid, new ModInfo { Name = guid, Version = version, Path = path ?? $@"{ModsDir}\{guid}.zipmod" });

    private static readonly ISet<string> EmptyUsage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> Usage(params string[] guids) =>
        new(guids, StringComparer.OrdinalIgnoreCase);

    private static ModOrganizePlan Plan(
        IReadOnlyList<KeyValuePair<string, ModInfo>> entries,
        ISet<string>? chara = null,
        ISet<string>? scene = null,
        IReadOnlyDictionary<string, string>? siteIndex = null,
        ISet<string>? shader = null) =>
        ModOrganizeHelper.BuildPlan(entries,
            chara ?? EmptyUsage, scene ?? EmptyUsage, shader ?? EmptyUsage, siteIndex, GamePath, ModsDir);

    [Fact]
    public void BuildPlan_Unused_WhenNeitherCardReferences()
    {
        var plan = Plan([Entry("g-a")]);

        var move = Assert.Single(plan.Unused);
        Assert.Equal(UnusedDir, move.TargetDir);
        Assert.Empty(plan.SceneOnly);
        Assert.Empty(plan.SitePlaced);
        Assert.Empty(plan.Duplicates);
        Assert.Single(plan.Winners);
    }

    [Fact]
    public void BuildPlan_SceneOnly_WhenOnlySceneReferences()
    {
        var plan = Plan([Entry("g-b")], scene: Usage("g-b"));

        var move = Assert.Single(plan.SceneOnly);
        Assert.Equal(SceneModsDir, move.TargetDir);
        Assert.Empty(plan.Unused);
        Assert.Empty(plan.SitePlaced);
    }

    [Fact]
    public void BuildPlan_Stays_WhenCharaReferences()
    {
        var plan = Plan([Entry("g-a"), Entry("g-b")], chara: Usage("g-a", "g-b"), scene: Usage("g-b"));

        Assert.Empty(plan.Unused);
        Assert.Empty(plan.SceneOnly);
        Assert.Empty(plan.SitePlaced); // 人物卡引用的留原地（即使场景也引用）
    }

    [Fact]
    public void BuildPlan_DedupFirst_ThenClassifyWinner()
    {
        // 同 GUID 两个版本：v2 赢家进 Unused，v1 落选进 Duplicates
        var plan = Plan(
            [Entry("g-d", "1.0.0", $@"{ModsDir}\d_v1.zipmod"), Entry("g-d", "2.0.0", $@"{ModsDir}\d_v2.zipmod")]);

        Assert.Equal(1, plan.DupGroups);
        var loser = Assert.Single(plan.Duplicates);
        Assert.Equal("1.0.0", loser.Mod.Version);
        Assert.Equal(DupDir, loser.TargetDir);
        var unused = Assert.Single(plan.Unused);
        Assert.Equal("2.0.0", unused.Mod.Version);
        Assert.Equal("2.0.0", plan.Winners["g-d"].Version);
    }

    [Fact]
    public void BuildPlan_GuidMatching_CaseInsensitive()
    {
        var plan = Plan(
            [Entry("g-a"), Entry("g-b"), Entry("g-b", "1.0.0", $@"{ModsDir}\g-b2.zipmod")],
            chara: Usage("G-A"));

        Assert.Empty(plan.SitePlaced); // g-a 被引用（大小写不同也命中）留原地
        Assert.Single(plan.Unused);    // g-b 赢家未使用
        Assert.Single(plan.Duplicates); // g-b 重复落选（同组大小写一致，不重复计）
    }

    [Fact]
    public void BuildPlan_UnusedInUnusedmods_StaysPut()
    {
        var plan = Plan([Entry("g-u", path: $@"{UnusedDir}\u.zipmod")]);

        Assert.Empty(plan.Unused);    // 已在 unusedmods 的未引用 mod 原地不动
        Assert.Empty(plan.SceneOnly);
        Assert.Empty(plan.SitePlaced);
        Assert.Single(plan.Winners);  // 仍是赢家（参与 LocalMods 写回判定）
    }

    [Fact]
    public void BuildPlan_CharaUsed_InUnusedmods_MovesBackToModsRoot()
    {
        var plan = Plan([Entry("g-u", path: $@"{UnusedDir}\u.zipmod")], chara: Usage("g-u"));

        var move = Assert.Single(plan.SitePlaced);
        Assert.Equal(ModsDir, move.TargetDir); // 无索引：移回 mods 根目录
        Assert.Empty(plan.SceneOnly);
    }

    [Fact]
    public void BuildPlan_ShaderUsed_ExemptFromUnused()
    {
        // 提供被卡片使用 shader 的 mod：按人物卡引用同口径，在 mods 原地不动
        var plan = Plan([Entry("g-sh")], shader: Usage("g-sh"));

        Assert.Empty(plan.Unused);
        Assert.Empty(plan.SceneOnly);
        Assert.Empty(plan.SitePlaced);
        Assert.Single(plan.Winners);
    }

    [Fact]
    public void BuildPlan_ShaderUsed_InUnusedmods_MovesBackToModsRoot()
    {
        // 被使用 shader 包误在 unusedmods（本次改造的修复场景）：移回 mods 根目录
        var plan = Plan([Entry("g-sh", path: $@"{UnusedDir}\sh.zipmod")], shader: Usage("g-sh"));

        var move = Assert.Single(plan.SitePlaced);
        Assert.Equal(ModsDir, move.TargetDir);
        Assert.Empty(plan.Unused);
    }

    [Fact]
    public void BuildPlan_ShaderUnused_StillMovedToUnusedmods()
    {
        // 未被任何卡片使用的 shader 包：口径不变，照常判未使用
        var plan = Plan([Entry("g-sh")], shader: Usage("g-other"));

        var move = Assert.Single(plan.Unused);
        Assert.Equal(UnusedDir, move.TargetDir);
    }

    [Fact]
    public void BuildPlan_SceneOnly_InUnusedmods_MovesToSceneMods()
    {
        var plan = Plan([Entry("g-u", path: $@"{UnusedDir}\u.zipmod")], scene: Usage("g-u"));

        var move = Assert.Single(plan.SceneOnly);
        Assert.Equal(SceneModsDir, move.TargetDir); // 无索引仅场景：移回进 scenemods
    }

    [Fact]
    public void BuildPlan_UsedInIndex_SitePlaced()
    {
        var index = new Dictionary<string, string> { ["g-a"] = "Exclusive HS2/a.zipmod" };
        var plan = Plan([Entry("g-a")], chara: Usage("g-a"), siteIndex: index);

        var move = Assert.Single(plan.SitePlaced);
        Assert.Equal(Path.Combine(ModsDir, "Exclusive HS2"), move.TargetDir);
        Assert.Empty(plan.SceneOnly);
    }

    [Fact]
    public void BuildPlan_SceneOnlyInIndex_SiteDirWinsOverSceneMods()
    {
        var index = new Dictionary<string, string> { ["g-b"] = "Studio/b.zipmod" };
        var plan = Plan([Entry("g-b")], scene: Usage("g-b"), siteIndex: index);

        var move = Assert.Single(plan.SitePlaced); // 站点目录优先于 scenemods
        Assert.Equal(Path.Combine(ModsDir, "Studio"), move.TargetDir);
        Assert.Empty(plan.SceneOnly);
    }

    [Fact]
    public void BuildPlan_ReferencedInUnusedmods_WithIndex_MovesToSiteDir()
    {
        var index = new Dictionary<string, string> { ["g-u"] = "Exclusive HS2/u.zipmod" };
        var plan = Plan([Entry("g-u", path: $@"{UnusedDir}\u.zipmod")], chara: Usage("g-u"), siteIndex: index);

        var move = Assert.Single(plan.SitePlaced); // 移回同样适用站点目录规则
        Assert.Equal(Path.Combine(ModsDir, "Exclusive HS2"), move.TargetDir);
    }

    [Fact]
    public void BuildPlan_AlreadyInSiteDir_NotInPlan()
    {
        var index = new Dictionary<string, string> { ["g-a"] = "Exclusive HS2/a.zipmod" };
        var plan = Plan(
            [Entry("g-a", path: $@"{ModsDir}\Exclusive HS2\a.zipmod")],
            chara: Usage("g-a"), siteIndex: index);

        Assert.Empty(plan.SitePlaced); // 已在正确位置不动
        Assert.Empty(plan.Unused);
        Assert.Empty(plan.SceneOnly);
    }

    [Fact]
    public void BuildPlan_IndexPathNoDir_ModsRoot()
    {
        var index = new Dictionary<string, string> { ["g-a"] = "a.zipmod" };
        var plan = Plan([Entry("g-a", path: $@"{ModsDir}\sub\a.zipmod")], chara: Usage("g-a"), siteIndex: index);

        var move = Assert.Single(plan.SitePlaced); // 相对路径无目录部分 → mods 根目录
        Assert.Equal(ModsDir, move.TargetDir);
    }

    [Fact]
    public void BuildPlan_UnusedWithIndex_FlatToUnusedmods()
    {
        var index = new Dictionary<string, string> { ["g-c"] = "Exclusive HS2/c.zipmod" };
        var plan = Plan([Entry("g-c")], siteIndex: index);

        var move = Assert.Single(plan.Unused); // 未使用平铺进 unusedmods，不按站点目录
        Assert.Equal(UnusedDir, move.TargetDir);
        Assert.Empty(plan.SitePlaced);
    }

    [Fact]
    public void BuildPlan_MaliciousSitePath_TreatedAsNotInIndex()
    {
        var index = new Dictionary<string, string>
        {
            ["g-a"] = "../evil/a.zipmod",   // 路径穿越
            ["g-b"] = "C:/evil/b.zipmod",   // rooted
            ["g-c"] = @"mods\..\..\c.zipmod",
        };
        var plan = Plan(
            [Entry("g-a"), Entry("g-b"), Entry("g-c")],
            chara: Usage("g-a", "g-b"), scene: Usage("g-c"), siteIndex: index);

        Assert.Empty(plan.SitePlaced); // 都按不在索引处理：人物卡引用留原地
        var move = Assert.Single(plan.SceneOnly); // 仅场景的仍进 scenemods
        Assert.Equal("g-c", move.Mod.Name);
    }

    [Fact]
    public void BuildPlan_SiteIndexLookup_CaseInsensitive()
    {
        var index = new Dictionary<string, string> { ["G-A"] = "Exclusive HS2/a.zipmod" };
        var plan = Plan([Entry("g-a")], chara: Usage("g-a"), siteIndex: index);

        Assert.Single(plan.SitePlaced); // 索引 GUID 大小写不敏感命中
    }

    [Fact]
    public void BuildPlan_DedupAcrossDirs_LoserInUnusedmods_ToDuplicates()
    {
        // 跨 mods/unusedmods 两目录分组去重：unusedmods 里的落选者同样进 duplicatemods
        var plan = Plan(
            [
                Entry("g-d", "1.0.0", $@"{UnusedDir}\d_old.zipmod"),
                Entry("g-d", "2.0.0", $@"{ModsDir}\d_new.zipmod"),
            ],
            chara: Usage("g-d"));

        Assert.Equal(1, plan.DupGroups);
        var loser = Assert.Single(plan.Duplicates);
        Assert.Equal($@"{UnusedDir}\d_old.zipmod", loser.Mod.Path);
        Assert.Equal(DupDir, loser.TargetDir);
        Assert.Empty(plan.SitePlaced); // 赢家在 mods 且被人物卡引用（无索引）→ 原地不动
    }

    // ==================== VM 集成测试 ====================

    private static ModsWindowViewModel MakeVm(ConfigService config, SideloadDatabaseService? db = null) =>
        new(config, new ScannerService(), db ?? new SideloadDatabaseService(config));

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
        var vm = MakeVm(config); // 无 meta：站点索引不参与，行为同整理初版

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
    public async Task Organize_Unusedmods_Referenced_MovesBack()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var charaDir = Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative);
        var unusedDir = Path.Combine(gameDir, "unusedmods");
        Directory.CreateDirectory(unusedDir);

        // unusedmods 里：g-u 被人物卡引用（应移回 mods 根目录）、g-w 无人引用（原地不动）
        var pathU = TestAssets.WriteZipmod(unusedDir, "u.zipmod", TestAssets.MakeManifest("g-u", "Mod U"));
        var pathW = TestAssets.WriteZipmod(unusedDir, "w.zipmod", TestAssets.MakeManifest("g-w", "Mod W"));
        TestAssets.WritePng(charaDir, "chara1.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(["角色甲"], ["g-u"]));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = MakeVm(config); // 无 meta：移回 mods 根目录

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        var messages = new List<string>();
        vm.OrganizeMessageRequested += (_, msg) => messages.Add(msg);

        await vm.OrganizeCommand.ExecuteAsync(null);
        Assert.NotNull(confirmMsg);
        Assert.Contains("归位/移回", confirmMsg);

        await vm.ConfirmOrganizeAsync();

        var movedBack = Path.Combine(modsDir, "u.zipmod");
        Assert.True(File.Exists(movedBack));
        Assert.False(File.Exists(pathU));
        Assert.True(File.Exists(pathW)); // 未引用的原地不动

        var localMods = config.Settings.Current.LocalMods;
        Assert.Equal(movedBack, localMods["g-u"].Path); // 移回的赢家进 LocalMods
        Assert.False(localMods.ContainsKey("g-w"));     // 留在 unusedmods 的不收录

        Assert.Contains(messages, m => m.Contains("整理完成") && m.Contains("站点归位 1"));
    }

    [Fact]
    public async Task Organize_WithMeta_SitePlacement()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var charaDir = Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative);
        var sceneDir = Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative);

        // g-a 人物卡引用（在索引）/ g-b 仅场景引用（在索引）/ g-c 无人引用（在索引）
        var pathA = TestAssets.WriteZipmod(modsDir, "a.zipmod", TestAssets.MakeManifest("g-a", "Mod A"));
        var pathB = TestAssets.WriteZipmod(modsDir, "b.zipmod", TestAssets.MakeManifest("g-b", "Mod B"));
        var pathC = TestAssets.WriteZipmod(modsDir, "c.zipmod", TestAssets.MakeManifest("g-c", "Mod C"));
        TestAssets.WritePng(charaDir, "chara1.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(["角色甲"], ["g-a"]));
        TestAssets.WritePng(sceneDir, "scene1.png",
            TestAssets.PngPrefix(), TestAssets.BuildSceneDataRegion("名1", "名2", ["g-b"]));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string>
        {
            ["g-a"] = "Exclusive HS2/a.zipmod",
            ["g-b"] = "Studio/b.zipmod",
            ["g-c"] = "Exclusive HS2/c.zipmod",
        });
        db.SaveMeta(new SideloadScanMeta
        {
            LastScanTime = DateTime.Now,
            Status = SideloadScanStatus.Success,
            FoundCount = 3,
        });
        var vm = MakeVm(config, db);

        string? confirmMsg = null;
        vm.OrganizeConfirmationRequested += (_, msg) => confirmMsg = msg;
        var messages = new List<string>();
        vm.OrganizeMessageRequested += (_, msg) => messages.Add(msg);

        await vm.OrganizeCommand.ExecuteAsync(null);
        Assert.NotNull(confirmMsg);
        Assert.Contains("站点目录归位", confirmMsg);

        await vm.ConfirmOrganizeAsync();

        // 在索引的按站点目录归位（仅场景也优先进站点目录，不进 scenemods）
        var siteA = Path.Combine(modsDir, "Exclusive HS2", "a.zipmod");
        var siteB = Path.Combine(modsDir, "Studio", "b.zipmod");
        Assert.True(File.Exists(siteA));
        Assert.True(File.Exists(siteB));
        Assert.False(File.Exists(pathA));
        Assert.False(File.Exists(pathB));
        // 未使用的平铺进 unusedmods（不按站点目录）
        Assert.True(File.Exists(Path.Combine(gameDir, "unusedmods", "c.zipmod")));
        Assert.False(File.Exists(pathC));
        Assert.False(Directory.Exists(Path.Combine(modsDir, "scenemods")));

        var localMods = config.Settings.Current.LocalMods;
        Assert.Equal(2, localMods.Count);
        Assert.Equal(siteA, localMods["g-a"].Path);
        Assert.Equal(siteB, localMods["g-b"].Path);
        Assert.False(localMods.ContainsKey("g-c"));

        Assert.Contains(messages, m => m.Contains("整理完成") && m.Contains("站点归位 2") && m.Contains("未使用 1"));
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
        var vm = MakeVm(config);

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
        var vm = MakeVm(config);

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
    public void Organize_WhileOrganizing_CannotReenter()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.IsOrganizing = true; // 模拟整理中

        Assert.False(vm.OrganizeCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null)); // 三命令互斥
        Assert.False(vm.DedupCommand.CanExecute(null));
        Assert.Equal("整理中...", vm.OrganizeButtonText);
    }
}
