using System.ComponentModel;
using System.Windows;
using HS2Tools.Views;

namespace HS2Tools.Windows;

/// <summary>
/// 窗口单例管理（迁移方案 §4）。
/// 每个页面 Window 只创建一次：导航 = Show + Activate；
/// 用户点关闭 = 取消 Closing + Hide —— 保持状态（滚动位置、筛选条件、已加载缩略图）。
/// MainWindow 真正关闭时应用程序退出（ShutdownMode.OnMainWindowClose）。
/// </summary>
public class WindowManager
{
    private readonly Dictionary<Type, Window> _windows = new();
    private readonly Func<Type, Window> _factory;

    /// <param name="factory">窗口创建工厂（测试可注入替身；默认反射无参构造）</param>
    public WindowManager(Func<Type, Window>? factory = null)
    {
        _factory = factory ?? (t => (Window)Activator.CreateInstance(t)!);
    }

    /// <summary>获取窗口单例（不存在则创建）</summary>
    public T Get<T>() where T : Window => (T)Get(typeof(T));

    public Window Get(Type windowType)
    {
        if (_windows.TryGetValue(windowType, out var window))
            return window;

        window = _factory(windowType);
        // 子窗口关闭 = 隐藏保持状态；MainWindow 走真实关闭（应用退出）
        if (window is not MainWindow)
            window.Closing += OnChildClosing;
        _windows[windowType] = window;
        return window;
    }

    /// <summary>导航到窗口：Show + Activate</summary>
    public T Show<T>() where T : Window
    {
        var window = Get<T>();
        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        return window;
    }

    private static void OnChildClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        ((Window)sender!).Hide();
    }
}
