using System.Windows;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>本地模组窗口：统计三卡 + 筛选 + DataGrid + 刷新重扫</summary>
public partial class ModsWindow : Window
{
    public ModsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is ModsWindowViewModel)
            return;
        if (App.Services is not { } s)
            return; // 测试环境（无 Application）

        DataContext = new ModsWindowViewModel(s.Config, s.Scanner);
    }
}
