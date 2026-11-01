using System.Windows;
using System.Windows.Controls;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>应用主壳：全局游戏切换、左侧导航与单页面内容宿主。</summary>
public partial class MainWindow : Window
{
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => InitShell();
    }

    private void InitShell()
    {
        if (_initialized || App.Services is not { } services)
            return;
        _initialized = true;

        var vm = new MainWindowViewModel(
            services.Config, services.Scanner, services.Downloads,
            services.GameLauncher, services.SideloadDb, services.CreateSideloader, services.AnalysisCache);
        DataContext = vm;
        SubscribeConfirmations(vm);

        services.Pages.Navigated += (_, _) => PageHost.Content = services.Pages.CurrentPage;
        NavHome.IsChecked = true;
        services.Pages.Show<HomeView>();
    }

    private void SubscribeConfirmations(MainWindowViewModel vm)
    {
        vm.StopConfirmationRequested += (_, _) =>
        {
            var result = MessageBox.Show(this,
                "停止后，当前的更新进度将丢失，下次需要重新开始。是否确认停止？",
                "确认停止更新？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                vm.ConfirmStopSideloader();
        };

        vm.PartialResultConfirmationRequested += (_, msg) =>
        {
            var result = MessageBox.Show(this, msg,
                "使用部分抓取的 Sideload 结果？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            vm.ConfirmPartialSideloaderResult(result == MessageBoxResult.Yes);
        };

        vm.MoveBackConfirmationRequested += async (_, msg) =>
        {
            var result = MessageBox.Show(this, msg,
                "移回 unusedmods 中被引用的 Mod？", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;
            var (moved, failed) = await vm.ConfirmMoveBackAsync();
            MessageBox.Show(this,
                failed > 0 ? $"已移回 {moved} 个，失败 {failed} 个（详见 error.log）" : $"已移回 {moved} 个",
                "移回完成", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        vm.GuidRepairConfirmationRequested += async (_, msg) =>
        {
            var result = MessageBox.Show(this, msg,
                "修复 zipmod 的 GUID 首尾空格？", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                vm.DismissGuidRepair();
                return;
            }
            var (modified, copied, failed) = await vm.ConfirmGuidRepairAsync();
            MessageBox.Show(this,
                $"已修改 {modified} 个源文件，复制 {copied} 个 zipmod" +
                (failed > 0 ? $"，失败 {failed} 个（详见 error.log）" : "。"),
                "GUID 修复完成", MessageBoxButton.OK,
                failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        };
    }

    private static void Navigate<T>() where T : UserControl => App.Services.Pages.Show<T>();

    private void NavHome_Click(object sender, RoutedEventArgs e) => Navigate<HomeView>();
    private void NavChara_Click(object sender, RoutedEventArgs e) => Navigate<CharaView>();
    private void NavScene_Click(object sender, RoutedEventArgs e) => Navigate<SceneView>();
    private void NavCardExplorer_Click(object sender, RoutedEventArgs e) => Navigate<CardExplorerView>();
    private void NavMods_Click(object sender, RoutedEventArgs e) => Navigate<ModsView>();
    private void NavSideload_Click(object sender, RoutedEventArgs e) => Navigate<SideloadView>();
    private void NavDownload_Click(object sender, RoutedEventArgs e) => Navigate<DownloadView>();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => Navigate<SettingsView>();

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeRestoreIcon.Text = maximized ? "\uE923" : "\uE922";
        MaximizeRestoreButton.ToolTip = maximized ? "还原" : "最大化";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeRestoreButton, maximized ? "还原" : "最大化");
    }
}
