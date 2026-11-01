using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class ModCleanupTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private string MakeGameDir()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative));
        Directory.CreateDirectory(Path.Combine(gameDir, "unusedmods"));
        File.WriteAllText(Path.Combine(gameDir, GameProfiles.Hs2.GameExeName), "exe");
        return gameDir;
    }

    private static ModScanEntry Entry(string guid, string path, params string[] shaders) =>
        new()
        {
            Guid = guid,
            Info = new ModInfo { Name = guid, Path = path, ShaderNames = shaders.ToList() },
        };

    private static string WriteMod(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "zipmod");
        return path;
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyUnusedIndexedNonShaderMods_InModsAndUnusedMods()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var downloadDir = Path.Combine(gameDir, GameProfiles.Hs2.ModDownloadDirRelative);
        var cleanPath = WriteMod(Path.Combine(modsDir, "clean.zipmod"));
        var cleanUnusedPath = WriteMod(Path.Combine(gameDir, "unusedmods", "clean-unused.zipmod"));
        var usedPath = WriteMod(Path.Combine(modsDir, "used.zipmod"));
        var shaderPath = WriteMod(Path.Combine(modsDir, "shader.zipmod"));
        var notIndexedPath = WriteMod(Path.Combine(modsDir, "not-indexed.zipmod"));
        var downloadPath = WriteMod(Path.Combine(downloadDir, "download.zipmod"));

        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.GamePath = gameDir;
            s.Current.LastAnalysisTime = DateTime.Now;
            s.Current.ModUsage["G-USED"] = 1;
            s.Current.ModEntries =
            [
                Entry("g-clean", cleanPath),
                Entry("g-clean-unused", cleanUnusedPath),
                Entry("g-used", usedPath),
                Entry("g-shader", shaderPath, "Standard"),
                Entry("g-not-indexed", notIndexedPath),
                Entry("g-download", downloadPath),
            ];
            s.Current.LocalMods = s.Current.ModEntries
                .Where(e => e.Guid != "g-clean-unused")
                .ToDictionary(e => e.Guid, e => e.Info);
        });
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string>
        {
            ["G-CLEAN"] = "a/clean.zipmod",
            ["g-clean-unused"] = "a/clean-unused.zipmod",
            ["g-used"] = "a/used.zipmod",
            ["g-shader"] = "a/shader.zipmod",
            ["g-download"] = "a/download.zipmod",
        });
        var vm = new ModsWindowViewModel(config, new ScannerService(), db);
        string? confirmation = null;
        string? summary = null;
        vm.CleanupConfirmationRequested += (_, message) => confirmation = message;
        vm.CleanupMessageRequested += (_, message) => summary = message;

        await vm.CleanupCommand.ExecuteAsync(null);

        Assert.NotNull(confirmation);
        Assert.Contains("2 个", confirmation);
        Assert.Contains("永久删除", confirmation);
        Assert.True(File.Exists(cleanPath));
        Assert.True(File.Exists(cleanUnusedPath));

        await vm.ConfirmCleanupAsync();

        Assert.False(File.Exists(cleanPath));
        Assert.False(File.Exists(cleanUnusedPath));
        Assert.True(File.Exists(usedPath));
        Assert.True(File.Exists(shaderPath));
        Assert.True(File.Exists(notIndexedPath));
        Assert.True(File.Exists(downloadPath));
        Assert.Equal("清理完成：已永久删除 2 个文件", summary);
        Assert.False(config.Settings.Current.LocalMods.ContainsKey("g-clean"));
        Assert.True(config.Settings.Current.LocalMods.ContainsKey("g-used"));
        Assert.True(config.Settings.Current.LocalMods.ContainsKey("g-shader"));
        Assert.True(config.Settings.Current.LocalMods.ContainsKey("g-not-indexed"));
        Assert.True(config.Settings.Current.LocalMods.ContainsKey("g-download"));
        Assert.DoesNotContain(config.Settings.Current.ModEntries, e => e.Info.Path == cleanPath);
        Assert.DoesNotContain(config.Settings.Current.ModEntries, e => e.Info.Path == cleanUnusedPath);
        Assert.Equal(4, config.Settings.Current.ModEntries.Count);
        Assert.False(vm.IsCleaning);
    }

    [Fact]
    public async Task Cleanup_MissingFileIsSkippedAndRetainedInCache()
    {
        var gameDir = MakeGameDir();
        var modsDir = Path.Combine(gameDir, GameProfiles.Hs2.ModsDirRelative);
        var existingPath = WriteMod(Path.Combine(modsDir, "existing.zipmod"));
        var missingPath = Path.Combine(modsDir, "missing.zipmod");
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Current.GamePath = gameDir;
            s.Current.LastAnalysisTime = DateTime.Now;
            s.Current.ModEntries = [Entry("g-existing", existingPath), Entry("g-missing", missingPath)];
            s.Current.LocalMods = s.Current.ModEntries.ToDictionary(e => e.Guid, e => e.Info);
        });
        var db = new SideloadDatabaseService(config);
        db.Update(new Dictionary<string, string>
        {
            ["g-existing"] = "a/existing.zipmod",
            ["g-missing"] = "a/missing.zipmod",
        });
        var vm = new ModsWindowViewModel(config, new ScannerService(), db);
        string? summary = null;
        vm.CleanupMessageRequested += (_, message) => summary = message;

        await vm.CleanupCommand.ExecuteAsync(null);
        await vm.ConfirmCleanupAsync();

        Assert.False(File.Exists(existingPath));
        Assert.Equal("清理完成：已永久删除 1 个文件，跳过 1 个（文件已不存在）", summary);
        var remaining = Assert.Single(config.Settings.Current.ModEntries);
        Assert.Equal("g-missing", remaining.Guid);
        Assert.True(config.Settings.Current.LocalMods.ContainsKey("g-missing"));
    }

    [Fact]
    public async Task Cleanup_RequiresCompletedAnalysis()
    {
        var gameDir = MakeGameDir();
        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new ModsWindowViewModel(config, new ScannerService(), new SideloadDatabaseService(config));
        string? message = null;
        vm.CleanupMessageRequested += (_, text) => message = text;

        await vm.CleanupCommand.ExecuteAsync(null);

        Assert.Equal("请先在首页运行「开始分析」", message);
        Assert.False(vm.IsCleaning);
    }

    [Fact]
    public void Cleanup_WhileCleaning_DisablesMaintenanceCommands()
    {
        using var config = new ConfigService(_dir);
        var vm = new ModsWindowViewModel(config, new ScannerService(), new SideloadDatabaseService(config));

        vm.IsCleaning = true;

        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.DedupCommand.CanExecute(null));
        Assert.False(vm.CleanupCommand.CanExecute(null));
        Assert.False(vm.OrganizeCommand.CanExecute(null));
        Assert.Equal("清理中...", vm.CleanupButtonText);
    }
}
