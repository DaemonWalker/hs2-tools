using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private string MakeGameDir()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, ConfigService.GameExeName), "exe");
        return gameDir;
    }

    [Fact]
    public void Ctor_LoadsGamePathFromConfig()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.GamePath = @"C:\HS2");

        var vm = new MainWindowViewModel(config);

        Assert.Equal(@"C:\HS2", vm.GamePath);
        Assert.False(vm.IsGamePathValid); // 路径不存在 → 未通过校验
        Assert.Contains("未找到", vm.PathStatusText);
    }

    [Fact]
    public void Ctor_DoesNotRewriteSettingsFile()
    {
        // 加载已有配置时 GamePath 未变 → 不触发写盘
        using var config = new ConfigService(_dir);
        _ = new MainWindowViewModel(config);
        Assert.False(File.Exists(config.SettingsPath));
    }

    [Fact]
    public void SetValidGamePath_ValidatesAndPersists()
    {
        var gameDir = MakeGameDir();
        using var config = new ConfigService(_dir);
        var vm = new MainWindowViewModel(config);

        vm.GamePath = gameDir;

        Assert.True(vm.IsGamePathValid);
        Assert.Contains("已验证", vm.PathStatusText);
        config.Save();

        using var reloaded = new ConfigService(_dir);
        Assert.Equal(gameDir, reloaded.Settings.GamePath);
    }

    [Fact]
    public void SetInvalidGamePath_DoesNotValidate()
    {
        using var config = new ConfigService(_dir);
        var vm = new MainWindowViewModel(config);

        vm.GamePath = Path.Combine(_dir, "no-such-dir");

        Assert.False(vm.IsGamePathValid);
        Assert.Contains("未找到", vm.PathStatusText);
    }

    [Fact]
    public void ValidateGamePath_Cases()
    {
        Assert.False(MainWindowViewModel.ValidateGamePath(null));
        Assert.False(MainWindowViewModel.ValidateGamePath(""));
        Assert.False(MainWindowViewModel.ValidateGamePath(_dir)); // 目录存在但无 exe
        Assert.True(MainWindowViewModel.ValidateGamePath(MakeGameDir()));
    }
}
