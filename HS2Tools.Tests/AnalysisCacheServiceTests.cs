using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class AnalysisCacheServiceTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    [Fact]
    public void CardCache_PersistsAndInvalidatesChangedFile()
    {
        var path = TestAssets.WritePng(_dir, "card.png", TestAssets.PngPrefix(),
            TestAssets.BuildKkCharaDataRegion("白峰", "一乃", ["com.example.mod"]));
        var scanner = new ScannerService();
        var parsed = scanner.ParsePngData(path);

        using (var cache = new AnalysisCacheService(_dir))
            cache.ReplaceCards("kk", "chara", [CardAnalysisEntry.FromResult("chara", parsed)]);

        using (var cache = new AnalysisCacheService(_dir))
        {
            var hit = cache.TryGetCurrentCard("kk", "chara", path);
            Assert.NotNull(hit);
            Assert.Equal(parsed.CharaNames, hit.CharaNames);
            Assert.Equal(parsed.ModIDs, hit.ModIDs);
            Assert.Equal(parsed.GameDataLen, hit.GameDataLen);
        }

        File.AppendAllText(path, "changed");
        using (var cache = new AnalysisCacheService(_dir))
            Assert.Null(cache.TryGetCurrentCard("kk", "chara", path));
    }

    [Fact]
    public async Task SecondAnalysis_UsesPersistentCacheWithoutReadingUnchangedFiles()
    {
        var gameDir = Path.Combine(_dir, "game");
        var modsDir = Path.Combine(gameDir, "mods");
        var sceneDir = Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative);
        var charaDir = Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative);
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(sceneDir);
        Directory.CreateDirectory(charaDir);
        File.WriteAllText(Path.Combine(gameDir, GameProfiles.Hs2.GameExeName), "exe");
        var mod = TestAssets.WriteZipmod(modsDir, "a.zipmod", TestAssets.MakeManifest("g-a", "A", "1.0"));
        var scene = TestAssets.WritePng(sceneDir, "scene.png", TestAssets.PngPrefix(), TestAssets.ModMarker("g-a"));
        var chara = TestAssets.WritePng(charaDir, "chara.png", TestAssets.PngPrefix(),
            TestAssets.BuildKkCharaDataRegion("白峰", "一乃", ["g-a"]));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        using (var cache = new AnalysisCacheService(_dir))
            await RunAnalysis(config, cache);

        using (var cache = new AnalysisCacheService(_dir))
        {
            await using var modLock = new FileStream(mod, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await using var sceneLock = new FileStream(scene, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await using var charaLock = new FileStream(chara, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await RunAnalysis(config, cache);
        }
    }

    private static async Task RunAnalysis(ConfigService config, AnalysisCacheService cache)
    {
        var vm = new MainWindowViewModel(config, new ScannerService(), new DownloadManager(),
            new GameLauncherService(config), new SideloadDatabaseService(config),
            () => new NoopSideloader(), cache);
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.ScanCompleted, vm.ScanError);
        Assert.Equal(1, vm.ModCount);
        Assert.Equal(1, vm.TotalRefs);
    }

    private sealed class NoopSideloader : ISideloaderService
    {
        public bool IsRunning => false;
        public Task<SideloadRunResult> RunAsync(
            Action<string>? onLog = null, IProgress<SideloaderProgress>? onProgress = null,
            IReadOnlyDictionary<string, KnownSideloadMod>? known = null) =>
            Task.FromResult(new SideloadRunResult { Mods = new Dictionary<string, SideloadEntry>() });
        public void Cancel() { }
    }
}
