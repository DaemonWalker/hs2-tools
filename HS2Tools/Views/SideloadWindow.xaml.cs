using System.Windows;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>BetterRepack 数据库浏览窗口：统计三卡 + 防抖搜索 + DataGrid + 单条下载</summary>
public partial class SideloadWindow : Window
{
    public SideloadWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is SideloadWindowViewModel)
            return;
        if (App.Services is not { } s)
            return; // 测试环境（无 Application）

        DataContext = new SideloadWindowViewModel(s.Config, s.Downloads, s.SideloadDb);
    }
}
