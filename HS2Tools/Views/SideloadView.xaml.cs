using System.Windows.Controls;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>BetterRepack 数据库页：统计 + 防抖搜索 + 单条下载</summary>
public partial class SideloadView : UserControl
{
    public SideloadView()
    {
        InitializeComponent();
        Loaded += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is SideloadWindowViewModel)
            return;
        if (App.Services is not { } s)
            return;

        DataContext = new SideloadWindowViewModel(s.Config, s.Downloads, s.SideloadDb);
    }
}
