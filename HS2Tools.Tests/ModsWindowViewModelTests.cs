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
        new(config, new ScannerService());

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
