using System.Windows;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>主窗口：首页 + 导航（常驻，真正关闭时退出应用）</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // 在句柄创建时挂接 ViewModel（只触发一次）；
        // 测试在无 Application 环境下实例化窗口时不走此路径
        SourceInitialized += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is MainWindowViewModel)
            return;
        if (App.Services is not { } s)
            return; // 测试环境（无 Application）

        var vm = new MainWindowViewModel(
            s.Config, s.Scanner, s.Downloads, s.GameLauncher, s.SideloadDb, s.CreateSideloader);

        // 停止爬虫确认框（原版 Modal.confirm 语义）
        vm.StopConfirmationRequested += (_, _) =>
        {
            var result = MessageBox.Show(this,
                "停止后，当前的更新进度将丢失，下次需要重新开始。是否确认停止？",
                "确认停止更新？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                vm.ConfirmStopSideloader();
        };

        DataContext = vm;
    }

    private void NavChara_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<CharaWindow>();
    private void NavScene_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<SceneWindow>();
    private void NavCardExplorer_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<CardExplorerWindow>();
    private void NavMods_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<ModsWindow>();
    private void NavSideload_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<SideloadWindow>();
    private void NavDownload_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<DownloadWindow>();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<SettingsWindow>();
}
