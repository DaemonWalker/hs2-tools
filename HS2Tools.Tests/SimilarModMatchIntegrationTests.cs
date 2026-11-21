using System.Text;
using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class SimilarModMatchIntegrationTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    [Fact]
    public async Task ScanAndConfirm_LocalBaseGuid_CopiesAndRewritesToRequiredRevision()
    {
        var gameDir = MakeGameDir();
        var source = TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "source.zipmod",
            TestAssets.MakeManifest("abc", "Original name", "1.2"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "card.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("abc001"));
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));
        vm.GamePath = gameDir;
        await vm.ScanCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.SimilarModMatchItems);
        Assert.Equal("abc001", item.RequiredGuid);
        Assert.Equal("abc", item.CandidateGuid);
        Assert.False(item.IsSelected);
        item.IsSelected = true;

        var result = await vm.ConfirmSimilarModMatchesAsync();

        Assert.Equal((1, 0, 0), result);
        Assert.Equal("abc", new ScannerService().ReadZipMod(source).Single().Key);
        var output = config.Settings.Current.LocalMods["abc001"].Path;
        Assert.NotEqual(source, output);
        var copied = new ScannerService().ReadZipMod(output).Single();
        Assert.Equal("abc001", copied.Key);
        Assert.Equal("Original name", copied.Value.Name);
        Assert.Equal("1.2", copied.Value.Version);
        Assert.Contains(config.Settings.Current.ModEntries,
            e => e.Guid == "abc001" && e.Info.Path == output);
        Assert.False(vm.HasSimilarModMatches);
    }

    [Fact]
    public async Task SimilarCandidates_SameRequiredGuid_AreMutuallyExclusive()
    {
        var gameDir = MakeGameDir();
        TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "base.zipmod",
            TestAssets.MakeManifest("abc"));
        TestAssets.WriteZipmod(Path.Combine(gameDir, "mods"), "newer.zipmod",
            TestAssets.MakeManifest("abc002"));
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "card.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("abc001"));
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config, new SideloadDatabaseService(config));
        vm.GamePath = gameDir;
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.SimilarModMatchItems.Count);
        var first = vm.SimilarModMatchItems[0];
        var second = vm.SimilarModMatchItems[1];
        first.IsSelected = true;
        second.IsSelected = true;

        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
    }

    [Fact]
    public async Task ScanAndConfirm_SideloadBaseGuid_DownloadsAndRewritesToRequiredRevision()
    {
        var gameDir = MakeGameDir();
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.CharaDirRelative), "card.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("abc001"));
        using var server = new TestHttpServer();
        var bytes = TestAssets.BuildZipBytes(
            ("manifest.xml", Encoding.UTF8.GetBytes(TestAssets.MakeManifest("abc", "Remote")), true));
        server.MapFile("/mods/abc.zipmod", bytes);
        using var config = new ConfigService(_dir);
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string> { ["abc"] = "mods/abc.zipmod" });
        var downloads = new DownloadManager(baseUrl: server.BaseUrl);
        var vm = MakeVm(config, db, downloads);
        vm.DownloadBaseUrlOverride = server.BaseUrl;
        vm.GamePath = gameDir;
        await vm.ScanCommand.ExecuteAsync(null);
        var item = Assert.Single(vm.SimilarModMatchItems);
        item.IsSelected = true;
        var finished = new TaskCompletionSource();
        downloads.TaskFinished += (_, t) =>
        {
            if (t.Status == DownloadTaskStatus.Completed)
                finished.TrySetResult();
        };

        var result = await vm.ConfirmSimilarModMatchesAsync();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Equal((0, 1, 0), result);
        var task = downloads.Tasks.Single(t => t.Id == "abc001");
        Assert.Equal("abc", task.SourceGuid);
        var downloaded = new ScannerService().ReadZipMod(task.OutputPath).Single();
        Assert.Equal("abc001", downloaded.Key);
        Assert.Equal("Remote", downloaded.Value.Name);
        Assert.False(vm.HasSimilarModMatches);
    }

    [Fact]
    public void Ctor_ExistingAnalysis_RestoresSimilarCandidates()
    {
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.LocalMods["abc"] = new ModInfo { Name = "Base", Path = "abc.zipmod" };
            s.Current.ModUsage["abc001"] = 1;
        });

        var vm = MakeVm(config, new SideloadDatabaseService(config));

        var item = Assert.Single(vm.SimilarModMatchItems);
        Assert.Equal("abc", item.CandidateGuid);
        Assert.Equal("abc001", item.RequiredGuid);
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

    private static MainWindowViewModel MakeVm(ConfigService config, SideloadDatabaseService db,
        DownloadManager? downloads = null) =>
        new(config, new ScannerService(), downloads ?? new DownloadManager(),
            new GameLauncherService(config), db, () => new NoopSideloader());

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
