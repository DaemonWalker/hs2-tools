using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class ModDetailViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    [Fact]
    public void BuildSideloadPageUrl_OpensContainingDirectoryNotFile()
    {
        var relative = ModDetailViewModel.BuildSideloadPageUrl(
            "Sideloader Modpack/Clothes/mod v1.zipmod", GameProfiles.Kk.SideloadBaseUrl);
        var absolute = ModDetailViewModel.BuildSideloadPageUrl(
            "https://example.com/mods/pack/mod.zipmod", null);

        Assert.Equal("https://sideload.betterrepack.com/download/KKEC/Sideloader%20Modpack/Clothes/", relative);
        Assert.Equal("https://example.com/mods/pack/", absolute);
        Assert.DoesNotContain(".zipmod", relative);
        Assert.Null(ModDetailViewModel.BuildSideloadPageUrl(null, GameProfiles.Kk.SideloadBaseUrl));
    }

    [Fact]
    public async Task Load_ReadsOriginalManifestAndDisplaysMetadata()
    {
        var manifest = "<?xml version=\"1.0\"?>\n<manifest schema-ver=\"1\">\n" +
                       "  <guid>guid.test</guid>\n  <name>Test Mod</name>\n  <version>1.2.3</version>\n" +
                       "  <description><![CDATA[<MaterialEditor><Shader Name=\"Test\"/>]]></description>\n</manifest>";
        var path = TestAssets.WriteZipmod(_dir, "test.zipmod", manifest);
        var info = new ModInfo
        {
            Name = "Test Mod",
            Version = "1.2.3",
            Path = path,
            FileLength = new FileInfo(path).Length,
            LastWriteTimeUtcTicks = File.GetLastWriteTimeUtc(path).Ticks,
        };
        using var config = new ConfigService(_dir);
        var vm = new ModDetailViewModel("guid.test", info, "https://example.com/test.zipmod",
            new GameLauncherService(config));

        await vm.LoadAsync();

        Assert.Equal(manifest, vm.ManifestText);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal("Test Mod", vm.DisplayName);
        Assert.Equal("1.2.3", vm.DisplayVersion);
        Assert.Equal(path, vm.DisplayFilePath);
        Assert.Equal("https://example.com/test.zipmod", vm.DisplaySideloadUrl);
        Assert.True(vm.OpenInFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task Load_MissingLocalMod_ShowsFallbackAndError()
    {
        using var config = new ConfigService(_dir);
        var vm = new ModDetailViewModel("guid.missing", null, null, new GameLauncherService(config));

        await vm.LoadAsync();

        Assert.Equal("-", vm.DisplayFilePath);
        Assert.Equal("-", vm.DisplaySideloadUrl);
        Assert.Contains("未找到此 Mod", vm.ErrorMessage);
        Assert.False(vm.OpenInFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task Load_InvalidZip_ReportsVisibleError()
    {
        var path = Path.Combine(_dir, "invalid.zipmod");
        File.WriteAllText(path, "not a zip");
        using var config = new ConfigService(_dir);
        var vm = new ModDetailViewModel("guid.bad", new ModInfo { Path = path }, null,
            new GameLauncherService(config));

        await vm.LoadAsync();

        Assert.Contains("读取 manifest.xml 失败", vm.ErrorMessage);
        Assert.False(vm.IsLoading);
    }
}
