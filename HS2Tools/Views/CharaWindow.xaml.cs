using System.Windows;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>角色卡浏览窗口：网格 + 搜索 + 排序 + 详情面板</summary>
public partial class CharaWindow : Window
{
    public CharaWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is CharaWindowViewModel)
            return;
        if (App.Services is not { } s)
            return; // 测试环境（无 Application）

        var vm = new CharaWindowViewModel(s.Config, s.Scanner, s.Downloads, s.SideloadDb, s.GameLauncher);
        DataContext = vm;
        vm.LoadCardPaths(); // 对应原版挂载时 getAllFiles
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CharaWindowViewModel vm)
            vm.LoadCardPaths(); // ItemsSource 变化 → 控件内部重置重新加载
    }
}
