using HS2Tools.Views;
using HS2Tools.Windows;

namespace HS2Tools.Tests;

/// <summary>
/// WindowManager 与窗口冒烟测试。
/// WPF 窗口创建需要 STA 线程；在无 Application 环境下 DynamicResource 解析为静默失败，安全。
/// </summary>
public class WindowManagerTests
{
    [Fact]
    public void Get_ReturnsSingletons_And_AllWindowsInstantiate()
    {
        RunInSta(() =>
        {
            var wm = new WindowManager();

            var c1 = wm.Get<CharaWindow>();
            Assert.Same(c1, wm.Get<CharaWindow>());
            Assert.NotSame(c1, wm.Get<SceneWindow>());

            // 全部 8 个窗口类型可实例化（XAML 解析无误）
            _ = wm.Get<MainWindow>();
            _ = wm.Get<CardExplorerWindow>();
            _ = wm.Get<ModsWindow>();
            _ = wm.Get<SideloadWindow>();
            _ = wm.Get<DownloadWindow>();
            _ = wm.Get<SettingsWindow>();
        });
    }

    [Fact]
    public void ChildClosing_HidesInsteadOfClosing_KeepsState()
    {
        RunInSta(() =>
        {
            var wm = new WindowManager();
            var w = wm.Get<CharaWindow>();

            w.Show();
            Assert.True(w.IsVisible);

            w.Close(); // 应被拦截 → Hide（保持状态）
            Assert.False(w.IsVisible);

            // 单例未销毁，重开复用
            Assert.Same(w, wm.Get<CharaWindow>());
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }
}
