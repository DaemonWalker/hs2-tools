using HS2Tools.Views;
using HS2Tools.Windows;

namespace HS2Tools.Tests;

/// <summary>单窗口页面管理与视图冒烟测试。</summary>
public class PageManagerTests
{
    [Fact]
    public void Get_ReturnsSingletons_And_AllViewsInstantiate()
    {
        RunInSta(() =>
        {
            var pm = new PageManager();

            var c1 = pm.Get<CharaView>();
            Assert.Same(c1, pm.Get<CharaView>());
            Assert.NotSame(c1, pm.Get<SceneView>());

            _ = pm.Get<HomeView>();
            _ = pm.Get<CardExplorerView>();
            _ = pm.Get<ModsView>();
            _ = pm.Get<SideloadView>();
            _ = pm.Get<DownloadView>();
            _ = pm.Get<SettingsView>();
            _ = new MainWindow();
        });
    }

    [Fact]
    public void Show_SwitchesCurrentPage_AndKeepsCachedInstances()
    {
        RunInSta(() =>
        {
            var pm = new PageManager();
            var chara = pm.Show<CharaView>();
            Assert.Same(chara, pm.CurrentPage);

            var scene = pm.Show<SceneView>();
            Assert.Same(scene, pm.CurrentPage);

            Assert.Same(chara, pm.Show<CharaView>());
            Assert.Same(chara, pm.CurrentPage);
        });
    }

    private static void RunInSta(Action action) => ThumbnailCacheTests.RunInSta(action);
}
