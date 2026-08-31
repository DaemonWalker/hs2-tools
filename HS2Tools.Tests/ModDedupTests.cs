using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class ModDedupTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    // ==================== CompareVersions ====================

    [Fact]
    public void CompareVersions_SemanticOrder()
    {
        Assert.True(ScannerService.CompareVersions("1.2.3", "1.10.0") < 0);
        Assert.True(ScannerService.CompareVersions("2.0", "1.9.9") > 0);
        Assert.Equal(0, ScannerService.CompareVersions("1.2", "1.2.0")); // 缺段补 0
    }

    [Fact]
    public void CompareVersions_LeadingV_AndWhitespace()
    {
        Assert.True(ScannerService.CompareVersions("v2.0", "1.9") > 0);
        Assert.Equal(0, ScannerService.CompareVersions(" 1.0 \n", "\t1.0")); // version 不清洗，含空白也能比
    }

    [Fact]
    public void CompareVersions_Unparseable_ReturnsZero()
    {
        Assert.Equal(0, ScannerService.CompareVersions("beta", "1.0")); // 一方不可解析
        Assert.Equal(0, ScannerService.CompareVersions("beta", "alpha")); // 双方不可解析
        Assert.Equal(0, ScannerService.CompareVersions("", "1.0"));
    }

    // ==================== CompareModsForKeep ====================

    private string WriteFile(string name, int size, DateTime? lastWrite = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[size]);
        if (lastWrite is { } t)
            File.SetLastWriteTime(path, t);
        return path;
    }

    [Fact]
    public void CompareModsForKeep_VersionBeatsSize()
    {
        var small = WriteFile("small.zipmod", 100);
        var big = WriteFile("big.zipmod", 10000);
        var a = new ModInfo { Version = "2.0", Path = small };
        var b = new ModInfo { Version = "1.0", Path = big };
        Assert.True(ScannerService.CompareModsForKeep(a, b) < 0); // 版本高者优
    }

    [Fact]
    public void CompareModsForKeep_SizeBeatsDate()
    {
        var now = DateTime.Now;
        var big = WriteFile("big.zipmod", 10000, now.AddDays(-10));
        var small = WriteFile("small.zipmod", 100, now);
        var a = new ModInfo { Version = "1.0", Path = big };
        var b = new ModInfo { Version = "1.0", Path = small };
        Assert.True(ScannerService.CompareModsForKeep(a, b) < 0); // 体积大者优
    }

    [Fact]
    public void CompareModsForKeep_NewerDateWins()
    {
        var now = DateTime.Now;
        var newer = WriteFile("newer.zipmod", 100, now);
        var older = WriteFile("older.zipmod", 100, now.AddDays(-1));
        var a = new ModInfo { Version = "1.0", Path = newer };
        var b = new ModInfo { Version = "1.0", Path = older };
        Assert.True(ScannerService.CompareModsForKeep(a, b) < 0); // 日期新者优
    }

    [Fact]
    public void CompareModsForKeep_AllTied_PathTiebreak()
    {
        var now = DateTime.Now;
        var p1 = WriteFile("a.zipmod", 100, now);
        var p2 = WriteFile("b.zipmod", 100, now);
        var a = new ModInfo { Version = "1.0", Path = p1 };
        var b = new ModInfo { Version = "1.0", Path = p2 };
        Assert.True(ScannerService.CompareModsForKeep(a, b) < 0); // 路径词典序兜底（确定性）
        Assert.True(ScannerService.CompareModsForKeep(b, a) > 0);
    }

    // ==================== 批量解析去重 ====================

    [Fact]
    public async Task ReadZipModBatch_DuplicateGuid_KeepsHigherVersion()
    {
        var scanner = new ScannerService();
        TestAssets.WriteZipmod(_dir, "old.zipmod", TestAssets.MakeManifest("g-dup", "Old", "1.0"));
        var newer = TestAssets.WriteZipmod(_dir, "new.zipmod", TestAssets.MakeManifest("g-dup", "New", "2.0"));

        var result = await scanner.ReadZipModBatchAsync(Directory.GetFiles(_dir, "*.zipmod"));

        var info = Assert.Single(result);
        Assert.Equal(newer, info.Value.Path); // 同 guid 按规则裁决，不再是随机覆盖
        Assert.Equal("2.0", info.Value.Version);
    }

    [Fact]
    public async Task ReadZipModBatchList_KeepsAllEntries()
    {
        var scanner = new ScannerService();
        TestAssets.WriteZipmod(_dir, "a.zipmod", TestAssets.MakeManifest("g-dup", "A", "1.0"));
        TestAssets.WriteZipmod(_dir, "b.zipmod", TestAssets.MakeManifest("g-dup", "B", "2.0"));
        TestAssets.WriteZipmod(_dir, "c.zipmod", TestAssets.MakeManifest("g-uniq", "C"));

        var entries = await scanner.ReadZipModBatchListAsync(Directory.GetFiles(_dir, "*.zipmod"));

        Assert.Equal(3, entries.Count); // 不折叠重复 guid
        Assert.Equal(2, entries.Count(kv => kv.Key == "g-dup"));
    }

    // ==================== VM 去重流程 ====================

    private string MakeGameDir()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative));
        File.WriteAllText(Path.Combine(gameDir, GameProfiles.Hs2.GameExeName), "exe");
        return gameDir;
    }

    [Fact]
    public async Task Dedup_Confirmed_MovesLosersToDuplicateMods()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var winner = TestAssets.WriteZipmod(modsDir, "keep.zipmod", TestAssets.MakeManifest("g-dup", "Keep", "2.0"));
        var loser = TestAssets.WriteZipmod(modsDir, "drop.zipmod", TestAssets.MakeManifest("g-dup", "Drop", "1.0"));
        var uniq = TestAssets.WriteZipmod(modsDir, "uniq.zipmod", TestAssets.MakeManifest("g-uniq", "Uniq"));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new ModsWindowViewModel(config, new ScannerService());

        string? confirmMsg = null, infoMsg = null;
        vm.DedupConfirmationRequested += (_, msg) => confirmMsg = msg;
        vm.DedupMessageRequested += (_, msg) => infoMsg = msg;

        await vm.DedupCommand.ExecuteAsync(null);

        Assert.NotNull(confirmMsg);
        Assert.Contains("1 组重复", confirmMsg);
        Assert.Contains("1 个落选文件", confirmMsg);
        Assert.Null(infoMsg);
        Assert.True(File.Exists(loser)); // 确认前不动文件

        await vm.ConfirmDedupAsync();

        // 落选文件移入 duplicatemods，winner 与无关 mod 留在原位
        Assert.False(File.Exists(loser));
        Assert.True(File.Exists(Path.Combine(gameDir, "duplicatemods", "drop.zipmod")));
        Assert.True(File.Exists(winner));
        Assert.True(File.Exists(uniq));

        // LocalMods 指向各 guid 最优
        Assert.Equal(2, config.Settings.Current.LocalMods.Count);
        Assert.Equal(winner, config.Settings.Current.LocalMods["g-dup"].Path);

        // Changed 事件驱动列表刷新 + 完成汇总
        Assert.Equal(2, vm.ModCount);
        Assert.NotNull(infoMsg);
        Assert.Contains("已移动 1 个文件", infoMsg);
    }

    [Fact]
    public async Task Dedup_NoDuplicates_OnlyShowsMessage()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        TestAssets.WriteZipmod(modsDir, "a.zipmod", TestAssets.MakeManifest("g-a", "A"));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new ModsWindowViewModel(config, new ScannerService());

        string? confirmMsg = null, infoMsg = null;
        vm.DedupConfirmationRequested += (_, msg) => confirmMsg = msg;
        vm.DedupMessageRequested += (_, msg) => infoMsg = msg;

        await vm.DedupCommand.ExecuteAsync(null);

        Assert.Null(confirmMsg); // 无重复不弹确认
        Assert.Equal("没有发现重复的 Mods", infoMsg);
        Assert.False(Directory.Exists(Path.Combine(gameDir, "duplicatemods")));
    }

    [Fact]
    public async Task Dedup_TargetNameCollision_AppendsSuffix()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        TestAssets.WriteZipmod(modsDir, "keep.zipmod", TestAssets.MakeManifest("g-dup", "Keep", "2.0"));
        var loser = TestAssets.WriteZipmod(modsDir, "drop.zipmod", TestAssets.MakeManifest("g-dup", "Drop", "1.0"));

        var dupDir = Path.Combine(gameDir, "duplicatemods");
        Directory.CreateDirectory(dupDir);
        File.WriteAllText(Path.Combine(dupDir, "drop.zipmod"), "existing"); // 目标重名

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new ModsWindowViewModel(config, new ScannerService());
        vm.DedupConfirmationRequested += (_, _) => { };

        await vm.DedupCommand.ExecuteAsync(null);
        await vm.ConfirmDedupAsync();

        Assert.False(File.Exists(loser));
        Assert.True(File.Exists(Path.Combine(dupDir, "drop_2.zipmod"))); // 追加后缀
        Assert.Equal("existing", File.ReadAllText(Path.Combine(dupDir, "drop.zipmod"))); // 原文件不被覆盖
    }

    [Fact]
    public async Task Dedup_NoGamePath_ShowsHint()
    {
        using var config = new ConfigService(_dir);
        var vm = new ModsWindowViewModel(config, new ScannerService());

        string? infoMsg = null;
        vm.DedupMessageRequested += (_, msg) => infoMsg = msg;

        await vm.DedupCommand.ExecuteAsync(null);

        Assert.Equal("请先设置游戏目录", infoMsg);
    }

    [Fact]
    public void Dedup_WhileBusy_CannotReenter()
    {
        using var config = new ConfigService(_dir);
        var vm = new ModsWindowViewModel(config, new ScannerService());

        vm.IsDeduping = true;
        Assert.False(vm.DedupCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null)); // 与刷新互斥
        Assert.Equal("去重中...", vm.DedupButtonText);

        vm.IsDeduping = false;
        vm.IsRefreshing = true;
        Assert.False(vm.DedupCommand.CanExecute(null));
    }
}
