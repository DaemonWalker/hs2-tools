using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class ModsWindowViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private string MakeGameDir()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative));
        File.WriteAllText(Path.Combine(gameDir, GameProfiles.Hs2.GameExeName), "exe");
        return gameDir;
    }

    private static ModsWindowViewModel MakeVm(ConfigService config) =>
        new(config, new ScannerService(), new SideloadDatabaseService(config));

    [Fact]
    public void Stats_FromConfigSettings()
    {
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.LocalMods["g1"] = new ModInfo { Name = "M1" };
            s.Current.LocalMods["g2"] = new ModInfo { Name = "M2" };
            s.Current.LocalMods["g3"] = new ModInfo { Name = "M3" };
            s.Current.ModUsage["g1"] = 2;
            s.Current.ModUsage["g-other"] = 3; // 本地不存在也计入统计（与首页口径一致）
        });

        var vm = MakeVm(config);

        Assert.Equal(3, vm.ModCount);
        Assert.Equal(2, vm.UsageCount);
        Assert.Equal(5, vm.TotalRefs);
        Assert.Equal(3, vm.Mods.Count);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Rows_UsageMatchedCaseInsensitive()
    {
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.LocalMods["G-ABC"] = new ModInfo { Name = "Upper", Version = "1.0", Path = @"mods\a.zipmod" };
            s.Current.LocalMods["g-def"] = new ModInfo { Name = "Lower" };
            s.Current.LocalMods["g-none"] = new ModInfo { Name = "NoUse" };
            s.Current.ModUsage["g-abc"] = 5; // 大小写不同也命中（原版 toLowerCase 匹配）
            s.Current.ModUsage["G-DEF"] = 1;
        });

        var vm = MakeVm(config);

        Assert.Equal(5, vm.Mods.Single(m => m.Guid == "G-ABC").UsedCount);
        Assert.Equal(1, vm.Mods.Single(m => m.Guid == "g-def").UsedCount);
        Assert.Equal(0, vm.Mods.Single(m => m.Guid == "g-none").UsedCount); // 未命中为 0
    }

    [Fact]
    public void Rows_SortedByGuidThenVersionThenPath()
    {
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.LocalMods["g-b"] = new ModInfo();
            s.Current.LocalMods["g-a"] = new ModInfo { Version = "2.0" };
            s.Current.LocalMods["G-A"] = new ModInfo { Version = "1.0" }; // guid 排序大小写不敏感，版本再分先后
        });

        var vm = MakeVm(config);

        Assert.Equal(["G-A", "g-a", "g-b"], vm.Mods.Select(m => m.Guid).ToArray());
    }

    [Fact]
    public void Filter_UnusedOnly_ShowsZeroUsage()
    {
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.LocalMods["g-used"] = new ModInfo();
            s.Current.LocalMods["g-unused"] = new ModInfo();
            s.Current.ModUsage["g-used"] = 1;
        });
        var vm = MakeVm(config);

        vm.ShowUnusedOnly = true;

        var row = Assert.Single(vm.Mods);
        Assert.Equal("g-unused", row.Guid);

        // 筛选后无结果时的空态文案
        vm.ShowUnusedOnly = false;
        vm.ShowUnusedOnly = true;
        config.Update(s => s.Current.ModUsage["g-unused"] = 9); // Changed → Reload：两个 Mod 均被引用
        Assert.True(vm.IsEmpty);
        Assert.Equal("没有未使用的 Mods", vm.EmptyText);

        vm.ShowUnusedOnly = false;
        Assert.Equal(2, vm.Mods.Count);
    }

    [Fact]
    public void Filter_DuplicateGuidsOnly_UsesAllAnalysisEntries()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var unusedDir = Path.Combine(gameDir, "unusedmods");
        string ModPath(string name) => Path.Combine(modsDir, name);
        ModScanEntry Entry(string guid, string path) => new()
        {
            Guid = guid,
            Info = new ModInfo { Name = "Same Name", Path = path },
        };

        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.GamePath = gameDir;
            s.Current.LastAnalysisTime = DateTime.Now;
            foreach (var guid in new[] { "com.example.hair", "com.example.clothes", "com.exact", "com.unique" })
                s.Current.LocalMods[guid] = new ModInfo { Name = "Same Name", Path = ModPath($"{guid}.zipmod") };
            s.Current.ModEntries =
            [
                Entry(" com.example.hair ", ModPath("hair-a.zipmod")), // 仅 Trim 后相同
                Entry("com.example.hair", ModPath("hair-b.zipmod")),
                Entry("COM.EXAMPLE.HAIR", ModPath("hair-c.zipmod")), // Trim 并忽略大小写后相同
                Entry(" com.example.clothes", ModPath("clothes-a.zipmod")),
                Entry("COM.EXAMPLE.CLOTHES ", ModPath("clothes-b.zipmod")),
                Entry("com.exact", ModPath("exact-a.zipmod")), // 完全相同的 GUID 也保留全部文件
                Entry("com.exact", ModPath("exact-b.zipmod")),
                Entry("com.unique", ModPath("unique.zipmod")), // 名称相同但 GUID 唯一，不误报
                Entry("", ModPath("empty-a.zipmod")),
                Entry(" ", ModPath("empty-b.zipmod")),
                Entry("com.unused", Path.Combine(unusedDir, "unused-a.zipmod")),
                Entry("COM.UNUSED", Path.Combine(unusedDir, "unused-b.zipmod")),
            ];
        });
        var vm = MakeVm(config);

        vm.ShowDuplicateGuidsOnly = true;

        var expected = new[]
        {
            " com.example.hair ", "com.example.hair", "COM.EXAMPLE.HAIR",
            " com.example.clothes", "COM.EXAMPLE.CLOTHES ", "com.exact", "com.exact",
        };
        Assert.Equal(expected.OrderBy(g => g, StringComparer.Ordinal),
            vm.Mods.Select(m => m.Guid).OrderBy(g => g, StringComparer.Ordinal).ToArray());
        Assert.All(vm.Mods, m => Assert.StartsWith(modsDir, m.Path, StringComparison.OrdinalIgnoreCase));

        vm.SearchText = "clothes";
        Assert.Equal(2, vm.Mods.Count);

        vm.ShowDuplicateGuidsOnly = false;
        Assert.Single(vm.Mods); // 普通列表仍使用折叠后的 LocalMods，并保留搜索条件
    }

    [Fact]
    public void Filter_DuplicateGuidsOnly_WithoutAnalysis_ShowsHint()
    {
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.LocalMods[" guid "] = new ModInfo { Name = "One" };
            s.Current.LocalMods["GUID"] = new ModInfo { Name = "Two" };
        });
        var vm = MakeVm(config);

        vm.ShowDuplicateGuidsOnly = true;

        Assert.True(vm.IsEmpty);
        Assert.Equal("请先在首页运行「开始分析」以检测 GUID 冲突", vm.EmptyText);
    }

    [Fact]
    public void Filter_DuplicateGuidsOnly_NoMatches_ShowsHint()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.GamePath = gameDir;
            s.Current.LastAnalysisTime = DateTime.Now;
            s.Current.LocalMods["g1"] = new ModInfo { Name = "Same Name" };
            s.Current.LocalMods["g2"] = new ModInfo { Name = "Same Name" };
            s.Current.ModEntries =
            [
                new ModScanEntry { Guid = "g1", Info = new ModInfo { Path = Path.Combine(modsDir, "a.zipmod") } },
                new ModScanEntry { Guid = "g2", Info = new ModInfo { Path = Path.Combine(modsDir, "b.zipmod") } },
                new ModScanEntry { Guid = "", Info = new ModInfo { Path = Path.Combine(modsDir, "c.zipmod") } },
                new ModScanEntry { Guid = " ", Info = new ModInfo { Path = Path.Combine(modsDir, "d.zipmod") } },
            ];
        });
        var vm = MakeVm(config);

        vm.ShowDuplicateGuidsOnly = true;

        Assert.True(vm.IsEmpty);
        Assert.Equal("没有发现可能重复的 GUID", vm.EmptyText);
    }

    [Fact]
    public void Search_MatchesGuidNamePathAndSideloadUrl()
    {
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.LocalMods["guid-alpha"] = new ModInfo { Name = "Hair Pack", Path = @"C:\mods\alpha.zipmod" };
            s.Current.LocalMods["guid-beta"] = new ModInfo { Name = "Clothes", Path = @"D:\library\beta.zipmod" };
        });
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string> { ["guid-beta"] = "https://example.com/download/beta.zipmod" });
        var vm = new ModsWindowViewModel(config, new ScannerService(), db);

        vm.SearchText = "GUID-ALPHA";
        Assert.Equal("guid-alpha", Assert.Single(vm.Mods).Guid);
        vm.SearchText = "hair pack";
        Assert.Equal("guid-alpha", Assert.Single(vm.Mods).Guid);
        vm.SearchText = @"d:\library";
        Assert.Equal("guid-beta", Assert.Single(vm.Mods).Guid);
        vm.SearchText = "download/beta";
        Assert.Equal("guid-beta", Assert.Single(vm.Mods).Guid);

        vm.SearchText = "not-found";
        Assert.True(vm.IsEmpty);
        Assert.Equal("没有符合搜索条件的 Mods", vm.EmptyText);

        db.Update(new Dictionary<string, string> { ["guid-alpha"] = "https://example.com/not-found.zipmod" });
        Assert.Equal("guid-alpha", Assert.Single(vm.Mods).Guid);
    }

    [Fact]
    public void Empty_NoLocalMods_ShowsHint()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);

        Assert.True(vm.IsEmpty);
        Assert.Contains("暂无本地 Mods", vm.EmptyText);
    }

    [Fact]
    public async Task Refresh_RescansModsDir_AndUpdatesConfig()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        TestAssets.WriteZipmod(modsDir, "m1.zipmod", TestAssets.MakeManifest("g-new", "New Mod", "1.2.3"));
        TestAssets.WriteZipmod(modsDir, "m2.zipmod", TestAssets.MakeManifest("g-other", "Other Mod"));
        File.WriteAllText(Path.Combine(modsDir, "note.txt"), "x"); // 非 zipmod 不收录

        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.GamePath = gameDir;
            s.Current.LocalMods["g-stale"] = new ModInfo { Name = "Stale" }; // 旧扫描结果应被整体替换
        });
        var vm = MakeVm(config);
        Assert.Equal(1, vm.ModCount);

        Assert.True(vm.RefreshCommand.CanExecute(null));
        await vm.RefreshCommand.ExecuteAsync(null);

        // Config.Settings.Current.LocalMods 被重扫结果替换（对应原版 scanMods → setMods）
        Assert.False(config.Settings.Current.LocalMods.ContainsKey("g-stale"));
        Assert.Equal(2, config.Settings.Current.LocalMods.Count);
        Assert.Equal("New Mod", config.Settings.Current.LocalMods["g-new"].Name);
        Assert.Equal("1.2.3", config.Settings.Current.LocalMods["g-new"].Version);

        // Changed 事件驱动列表与统计刷新
        Assert.Equal(2, vm.ModCount);
        Assert.Equal(2, vm.Mods.Count);
        Assert.False(vm.IsRefreshing);
        Assert.Equal("刷新模组列表", vm.RefreshButtonText);
    }

    [Fact]
    public void Refresh_WhileRefreshing_CannotReenter()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.IsRefreshing = true; // 模拟刷新中（异步扫描进行中不可重入）

        Assert.False(vm.RefreshCommand.CanExecute(null)); // 刷新中防重入
        Assert.Equal("扫描中...", vm.RefreshButtonText);
        Assert.Equal("正在扫描本地 Mods...", vm.EmptyText);
    }

    [Fact]
    public async Task Refresh_NoGamePath_ClearsLocalMods()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.LocalMods["g1"] = new ModInfo()); // 未设置 GamePath
        var vm = MakeVm(config);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(config.Settings.Current.LocalMods);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void ConfigChanged_ReloadsListAndStats()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        Assert.True(vm.IsEmpty);

        // 模拟其他窗口完成分析（Changed 事件在调用线程触发，测试环境 UiDispatch 直跑）
        config.Update(s =>
        {
            s.Current.LocalMods["g1"] = new ModInfo { Name = "M1" };
            s.Current.ModUsage["g1"] = 4;
        });

        Assert.Equal(1, vm.ModCount);
        Assert.Equal(4, vm.TotalRefs);
        var row = Assert.Single(vm.Mods);
        Assert.Equal(4, row.UsedCount);
    }
}
