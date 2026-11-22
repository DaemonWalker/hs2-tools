using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class CardGuidRepairTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    [Fact]
    public void RewriteGuids_Hs2Chara_UpdatesMessagePackAndBlockLengths()
    {
        var oldGuid = new string('a', 31) + " ";
        var newGuid = new string('a', 31);
        var region = TestAssets.BuildCharaDataRegion(new[] { "角色" }, new[] { oldGuid },
            new[] { "com.other.plugin.value" });

        var (rewritten, count) = CharaCardParser.RewriteGuids(region,
            new Dictionary<string, string> { [oldGuid] = newGuid });
        var (names, modIds, ok) = CharaCardParser.ParseDataRegion(rewritten);

        Assert.Equal(1, count);
        Assert.True(ok);
        Assert.Equal(new[] { "角色" }, names);
        Assert.Equal(new[] { newGuid }, modIds);
        Assert.True(rewritten.AsSpan().IndexOf("com.other.plugin.value"u8.ToArray()) >= 0);
    }

    [Fact]
    public void RewriteGuids_SameLengthGuid_StillReplacesBlockData()
    {
        var region = TestAssets.BuildCharaDataRegion(new[] { "角色" }, new[] { "mod " });

        var (rewritten, count) = CharaCardParser.RewriteGuids(region,
            new Dictionary<string, string> { ["mod "] = " mod" });
        var parsed = CharaCardParser.ParseDataRegion(rewritten);

        Assert.Equal(1, count);
        Assert.True(parsed.StructuralOk);
        Assert.Equal(new[] { " mod" }, parsed.ModIDs);
    }

    [Fact]
    public void RewriteGuids_KkCharaWithKkexFirst_UpdatesFollowingBlockPosition()
    {
        var region = TestAssets.BuildKkCharaDataRegionWithKkexFirst("白峰", "一乃", new[] { " com.kk.mod" });

        var (rewritten, count) = CharaCardParser.RewriteGuids(region,
            new Dictionary<string, string> { [" com.kk.mod"] = "com.kk.mod" });
        var (names, modIds, ok) = CharaCardParser.ParseDataRegion(rewritten);

        Assert.Equal(1, count);
        Assert.True(ok);
        Assert.Equal(new[] { "白峰 一乃" }, names);
        Assert.Equal(new[] { "com.kk.mod" }, modIds);
    }

    [Fact]
    public void RewriteGuids_StudioScene_CoversCharaItemsPatternsAndMap()
    {
        var chara = TestAssets.BuildCharaDataRegion(new[] { "角色甲" }, new[] { " chara " });
        var scene = TestAssets.BuildStudioSceneDataRegion("角色乙", new[] { " item " },
            new[] { " pattern " }, " map ");
        var region = chara.Concat(scene).ToArray();
        var replacements = new Dictionary<string, string>
        {
            [" chara "] = "chara",
            [" item "] = "item",
            [" pattern "] = "pattern",
            [" map "] = "map",
        };

        var (rewritten, count) = CharaCardParser.RewriteGuids(region, replacements);
        var (names, modIds, ok) = CharaCardParser.ParseDataRegion(rewritten);

        Assert.Equal(4, count);
        Assert.True(ok);
        Assert.Equal(new[] { "角色甲", "角色乙" }, names);
        Assert.Equal(new[] { "chara", "item", "pattern", "map" }, modIds);
    }

    [Fact]
    public void Repair_TwoChanges_KeepsBothIndependentBackups()
    {
        var path = TestAssets.WritePng(_dir, "card.png", TestAssets.PngPrefix(),
            TestAssets.BuildCharaDataRegion(new[] { "角色" }, new[] { " Blue Archive " }));
        var original = File.ReadAllBytes(path);
        var firstPlan = MakePlan(path, new Dictionary<string, string>
        {
            [" Blue Archive "] = "Blue Archive",
        });

        var first = CardGuidRepairService.Repair(firstPlan);
        var secondPlan = MakePlan(path, new Dictionary<string, string>
        {
            ["Blue Archive"] = " Blue Archive ",
        });
        var second = CardGuidRepairService.Repair(secondPlan);

        Assert.NotEqual(first.BackupPath, second.BackupPath);
        Assert.True(File.Exists(first.BackupPath));
        Assert.True(File.Exists(second.BackupPath));
        Assert.Equal(original, File.ReadAllBytes(first.BackupPath));
        Assert.Equal(2, Directory.GetFiles(_dir, "*.guid-backup.bak").Length);
        Assert.Equal(new[] { " Blue Archive " }, new ScannerService().ParsePngData(path).ModIDs);
    }

    [Fact]
    public void Repair_FileChangedAfterScan_DoesNotTouchCardOrCreateBackup()
    {
        var path = TestAssets.WritePng(_dir, "card.png", TestAssets.PngPrefix(),
            TestAssets.BuildCharaDataRegion(new[] { "角色" }, new[] { " mod " }));
        var plan = MakePlan(path, new Dictionary<string, string> { [" mod "] = "mod" });
        File.AppendAllText(path, "changed");
        var changed = File.ReadAllBytes(path);

        Assert.Throws<InvalidDataException>(() => CardGuidRepairService.Repair(plan));

        Assert.Equal(changed, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.guid-backup.bak"));
    }

    [Fact]
    public void BuildPlans_TrimmedLocalGuidExists_PrefersCanonicalTarget()
    {
        var card = new PngParseResult
        {
            Path = "card.png",
            StructuralOk = true,
            ModIDs = new List<string> { "mod " },
        };
        var local = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["mod"] = new(),
            [" mod"] = new(),
        };

        var plan = Assert.Single(CardGuidRepairService.BuildPlans(new[] { card }, "chara", local));

        Assert.Equal("mod", plan.Replacements["mod "]);
    }

    [Fact]
    public void AnalysisCache_StructuralFlag_RoundTripsForRepairPlanning()
    {
        var path = TestAssets.WritePng(_dir, "card.png", TestAssets.PngPrefix(),
            TestAssets.BuildCharaDataRegion(new[] { "角色" }, new[] { "mod " }));
        var parsed = new ScannerService().ParsePngData(path);
        using var cache = new AnalysisCacheService(_dir);

        cache.UpsertCard("hs2", "chara", CardAnalysisEntry.FromResult("chara", parsed));

        Assert.True(cache.GetCards("hs2", "chara")[path].StructuralOk);
        Assert.True(cache.TryGetCurrentCard("hs2", "chara", path)!.StructuralOk);
    }

    [Fact]
    public async Task ScanAndConfirm_TwoCards_RewritesCardsWithoutChangingZipmod()
    {
        var gameDir = MakeGameDir();
        var source = TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "sample.zipmod",
            TestAssets.MakeManifest(" mod", "Sample"));
        var originalZipmod = File.ReadAllBytes(source);
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "a.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(new[] { "甲" }, new[] { "mod " }));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "b.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(new[] { "乙" }, new[] { "mod" }));
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.GamePath = gameDir;
        IReadOnlyList<CardGuidRepairPlan>? plans = null;
        vm.CardGuidRepairConfirmationRequested += (_, pending) => plans = pending;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.NotNull(plans);
        Assert.Equal(2, plans.Count);
        Assert.All(plans, plan => Assert.Equal(" mod", plan.Replacements.Values.Single()));
        foreach (var plan in plans)
        {
            var result = await vm.ConfirmCardGuidRepairAsync(plan);
            Assert.True(result.Repaired, result.Error);
            Assert.True(File.Exists(result.BackupPath));
        }

        var scanner = new ScannerService();
        Assert.Equal(" mod", Assert.Single(scanner.ReadZipMod(source)).Key);
        Assert.Equal(originalZipmod, File.ReadAllBytes(source));
        Assert.Single(Directory.GetFiles(Path.Combine(gameDir, "mods"), "*.zipmod"));
        foreach (var name in new[] { "a.png", "b.png" })
        {
            var path = Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative, name);
            Assert.Equal(new[] { " mod" }, scanner.ParsePngData(path).ModIDs);
        }
        Assert.Equal(2, Directory.GetFiles(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative),
            "*.guid-backup.bak").Length);
        Assert.Equal(2, config.Settings.Current.ModUsage[" mod"]);
        Assert.False(config.Settings.Current.ModUsage.ContainsKey("mod"));
        Assert.False(config.Settings.Current.ModUsage.ContainsKey("mod "));
    }

    [Fact]
    public async Task Scan_ExactGuidMatch_DoesNotRequestRepair()
    {
        var gameDir = MakeGameDir();
        TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "blue.zipmod",
            TestAssets.MakeManifest("Blue Archive", "Blue"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "card.png",
            TestAssets.PngPrefix(), TestAssets.BuildCharaDataRegion(new[] { "角色" }, new[] { "Blue Archive" }));
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.GamePath = gameDir;
        var prompted = false;
        vm.CardGuidRepairConfirmationRequested += (_, _) => prompted = true;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(prompted);
    }

    [Fact]
    public async Task Scan_FallbackParsedCard_DoesNotOfferUnsafeRewrite()
    {
        var gameDir = MakeGameDir();
        TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "blue.zipmod",
            TestAssets.MakeManifest("Blue Archive", "Blue"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "card.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("Blue Archive "));
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.GamePath = gameDir;
        var prompted = false;
        vm.CardGuidRepairConfirmationRequested += (_, _) => prompted = true;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(prompted);
    }

    private static CardGuidRepairPlan MakePlan(string path, Dictionary<string, string> replacements)
    {
        var parsed = new ScannerService().ParsePngData(path);
        return new CardGuidRepairPlan(path, "chara", parsed.ModIDs, replacements,
            parsed.FileLength, parsed.LastWriteTimeUtcTicks);
    }

    private string MakeGameDir()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative));
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative));
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative));
        File.WriteAllText(Path.Combine(gameDir, GameProfiles.Hs2.GameExeName), "exe");
        return gameDir;
    }

    private static MainWindowViewModel MakeVm(ConfigService config)
    {
        var db = new SideloadDatabaseService(config);
        return new MainWindowViewModel(config, new ScannerService(), new DownloadManager(),
            new GameLauncherService(config), db, () => new NoopSideloader());
    }

    private sealed class NoopSideloader : ISideloaderService
    {
        public bool IsRunning => false;
        public Task<SideloadRunResult> RunAsync(Action<string>? onLog = null,
            IProgress<SideloaderProgress>? onProgress = null,
            IReadOnlyDictionary<string, KnownSideloadMod>? known = null) =>
            Task.FromResult(new SideloadRunResult { Mods = new Dictionary<string, SideloadEntry>() });
        public void Cancel() { }
    }
}
