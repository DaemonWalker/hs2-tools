using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class SceneWindowViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private static SceneWindowViewModel MakeVm(ConfigService config) =>
        new(config, new ScannerService(), new DownloadManager(),
            new SideloadDatabaseService(config), new GameLauncherService(config));

    [Fact]
    public void ConfigChanged_GameSwitch_RescansNewGameDir()
    {
        // hs2 / kk 场景目录相对路径相同，仅游戏根不同
        var hs2Dir = Path.Combine(_dir, "game-hs2");
        Directory.CreateDirectory(Path.Combine(hs2Dir, GameProfiles.Hs2.SceneDirRelative));
        File.WriteAllText(Path.Combine(hs2Dir, GameProfiles.Hs2.GameExeName), "exe");
        TestAssets.WritePng(Path.Combine(hs2Dir, GameProfiles.Hs2.SceneDirRelative), "s1.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("场景一"));

        var kkDir = Path.Combine(_dir, "game-kk");
        Directory.CreateDirectory(Path.Combine(kkDir, GameProfiles.Kk.SceneDirRelative));
        File.WriteAllText(Path.Combine(kkDir, GameProfiles.Kk.GameExeName), "exe");
        TestAssets.WritePng(Path.Combine(kkDir, GameProfiles.Kk.SceneDirRelative), "k1.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("场景二"));
        TestAssets.WritePng(Path.Combine(kkDir, GameProfiles.Kk.SceneDirRelative), "k2.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("场景三"));

        using var config = new ConfigService(_dir);
        config.Update(s => { s.CurrentGame = "hs2"; s.Current.GamePath = hs2Dir; });
        config.Update(s => { s.CurrentGame = "kk"; s.Current.GamePath = kkDir; });
        config.Update(s => s.CurrentGame = "hs2");
        var vm = MakeVm(config);

        vm.LoadCardPaths();
        Assert.Single(vm.AllPaths);

        // 非游戏切换的 Changed（收藏）不重扫
        var before = vm.AllPaths;
        config.Update(s => s.Current.Favorites.Add(before[0]));
        Assert.Same(before, vm.AllPaths);

        // 切换游戏 → 用新游戏目录重扫，旧选中清空
        vm.SelectedPath = before[0];
        config.Update(s => s.CurrentGame = "kk"); // 测试环境 UiDispatch 同步执行
        Assert.Null(vm.SelectedPath);
        Assert.Equal(2, vm.AllPaths.Count);
        Assert.All(vm.AllPaths, p => Assert.StartsWith(kkDir, p, StringComparison.OrdinalIgnoreCase));
    }
}
