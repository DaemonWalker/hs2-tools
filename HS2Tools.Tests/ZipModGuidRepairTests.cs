using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class ZipModGuidRepairTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    [Fact]
    public void BuildPlans_ExactMatch_DoesNotPrompt()
    {
        var local = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Blue Archive"] = new() { Path = "blue.zipmod" },
        };

        var plans = ZipModGuidRepairService.BuildPlans(local, new[] { "Blue Archive" });

        Assert.Empty(plans);
    }

    [Fact]
    public void BuildPlans_UnusedSource_ModifiesFirstVariantAndCopiesRest()
    {
        var local = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [" mod"] = new() { Path = "sample.zipmod" },
        };

        var plan = Assert.Single(ZipModGuidRepairService.BuildPlans(local, new[] { "mod ", "mod" }));

        Assert.Equal("mod ", plan.DirectGuid);
        Assert.Equal(new[] { "mod" }, plan.CopyGuids);
    }

    [Fact]
    public void BuildPlans_SourceGuidIsReferenced_KeepsSourceAndCopiesAllTargets()
    {
        var local = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [" mod"] = new() { Path = "sample.zipmod" },
        };

        var plan = Assert.Single(ZipModGuidRepairService.BuildPlans(local, new[] { " mod", "mod ", "mod" }));

        Assert.Null(plan.DirectGuid);
        Assert.Equal(new[] { "mod ", "mod" }, plan.CopyGuids);
    }

    [Fact]
    public async Task ScanAndConfirm_TwoCardsWithDifferentWhitespace_CreatesBothGuids()
    {
        var gameDir = MakeGameDir();
        var source = TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "sample.zipmod",
            TestAssets.MakeManifest(" mod", "Sample"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "a.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("mod "));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "b.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("mod"));
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.GamePath = gameDir;
        string? confirmation = null;
        vm.GuidRepairConfirmationRequested += (_, message) => confirmation = message;

        await vm.ScanCommand.ExecuteAsync(null);
        Assert.NotNull(confirmation);
        Assert.True(config.Settings.Current.ModUsage.Count == 2,
            string.Join(" | ", config.Settings.Current.ModUsage.Keys.Select(k => $"[{k}]")));
        Assert.Contains("复制 1", confirmation);
        Assert.True(config.Settings.Current.ModUsage.ContainsKey("mod"));
        Assert.True(config.Settings.Current.ModUsage.ContainsKey("mod "));
        Assert.False(config.Settings.Current.LocalMods.ContainsKey("mod"));
        Assert.False(config.Settings.Current.LocalMods.ContainsKey("mod "));

        var (modified, copied, failed) = await vm.ConfirmGuidRepairAsync();

        Assert.Equal((1, 1, 0), (modified, copied, failed));
        Assert.True(config.Settings.Current.LocalMods.ContainsKey("mod "));
        Assert.True(config.Settings.Current.LocalMods.ContainsKey("mod"));
        Assert.False(config.Settings.Current.LocalMods.ContainsKey(" mod"));
        var files = Directory.GetFiles(Path.Combine(gameDir, "mods"), "*.zipmod");
        Assert.Equal(2, files.Length);
        var scanner = new ScannerService();
        Assert.Equal("mod ", Assert.Single(scanner.ReadZipMod(source)).Key);
        var copy = files.Single(path => !path.Equals(source, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("mod", Assert.Single(scanner.ReadZipMod(copy)).Key);
    }

    [Fact]
    public async Task Scan_ExactGuidMatch_DoesNotRequestRepair()
    {
        var gameDir = MakeGameDir();
        TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "blue.zipmod",
            TestAssets.MakeManifest("Blue Archive", "Blue"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "card.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("Blue Archive"));
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.GamePath = gameDir;
        var prompted = false;
        vm.GuidRepairConfirmationRequested += (_, _) => prompted = true;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(prompted);
        Assert.Equal((0, 0, 0), await vm.ConfirmGuidRepairAsync());
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
