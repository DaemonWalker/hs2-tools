using System.Windows;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>场景库窗口：场景网格 + 智能整理</summary>
public partial class SceneWindow : Window
{
    public SceneWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is SceneWindowViewModel)
            return;
        if (App.Services is not { } s)
            return; // 测试环境（无 Application）

        var vm = new SceneWindowViewModel(s.Config, s.Scanner, s.Downloads, s.SideloadDb, s.GameLauncher);
        DataContext = vm;
        vm.LoadCardPaths(); // 对应原版挂载时 getAllFiles
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SceneWindowViewModel vm)
            vm.LoadCardPaths(); // ItemsSource 变化 → 控件内部重置重新加载
    }
}
