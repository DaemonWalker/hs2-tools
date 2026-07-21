using System.Windows;

namespace HS2Tools.Views;

/// <summary>主窗口：首页 + 导航（常驻，真正关闭时退出应用）</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void NavChara_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<CharaWindow>();
    private void NavScene_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<SceneWindow>();
    private void NavCardExplorer_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<CardExplorerWindow>();
    private void NavMods_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<ModsWindow>();
    private void NavSideload_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<SideloadWindow>();
    private void NavDownload_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<DownloadWindow>();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => App.Services.Windows.Show<SettingsWindow>();
}
