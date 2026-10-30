using System.Windows.Controls;
using HS2Tools.Views;

namespace HS2Tools.Windows;

/// <summary>
/// 单窗口页面管理：每个页面只创建一次，导航只替换主窗口的内容宿主。
/// 页面实例、ViewModel、滚动位置和筛选条件在切换后保留。
/// </summary>
public class PageManager
{
    private readonly Dictionary<Type, UserControl> _pages = new();
    private readonly Func<Type, UserControl> _factory;

    public PageManager(Func<Type, UserControl>? factory = null)
    {
        _factory = factory ?? (t => (UserControl)Activator.CreateInstance(t)!);
    }

    public UserControl? CurrentPage { get; private set; }

    public event EventHandler? Navigated;

    public T Get<T>() where T : UserControl => (T)Get(typeof(T));

    public UserControl Get(Type pageType)
    {
        if (_pages.TryGetValue(pageType, out var page))
            return page;

        page = _factory(pageType);
        _pages[pageType] = page;
        return page;
    }

    public T Show<T>() where T : UserControl
    {
        var page = Get<T>();
        CurrentPage = page;
        Navigated?.Invoke(this, EventArgs.Empty);
        return page;
    }
}
