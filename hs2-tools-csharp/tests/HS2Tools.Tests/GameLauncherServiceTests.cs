using HS2Tools.Services;

namespace HS2Tools.Tests;

public class GameLauncherServiceTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    [Fact]
    public void Launch_WithoutGamePath_Throws()
    {
        using var config = new ConfigService(_dir);
        var launcher = new GameLauncherService(config);

        var ex = Assert.Throws<InvalidOperationException>(() => launcher.LaunchGame());
        Assert.Equal("游戏路径未设置", ex.Message);
        Assert.Throws<InvalidOperationException>(() => launcher.LaunchStudio());
    }

    [Fact]
    public void SleepPrevention_DoesNotThrow()
    {
        using var config = new ConfigService(_dir);
        var launcher = new GameLauncherService(config);

        launcher.PreventSleep();
        launcher.AllowSleep();
    }
}
