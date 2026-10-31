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
}
