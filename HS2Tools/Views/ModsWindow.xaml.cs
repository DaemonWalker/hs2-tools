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

        var vm = new ModsWindowViewModel(s.Config, s.Scanner, s.SideloadDb);

        // 去重确认框（仿主窗口停止爬虫确认模式）
        vm.DedupConfirmationRequested += (_, msg) =>
        {
            var result = MessageBox.Show(this, msg,
                "确认去重？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                _ = vm.ConfirmDedupAsync();
        };
        vm.DedupMessageRequested += (_, msg) =>
            MessageBox.Show(this, msg, "Mod 去重", MessageBoxButton.OK, MessageBoxImage.Information);

        // 整理确认框（与去重同一交互模式）
        vm.OrganizeConfirmationRequested += (_, msg) =>
        {
            var result = MessageBox.Show(this, msg,
                "确认整理？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                _ = vm.ConfirmOrganizeAsync();
        };
        vm.OrganizeMessageRequested += (_, msg) =>
            MessageBox.Show(this, msg, "整理 Mods", MessageBoxButton.OK, MessageBoxImage.Information);

        DataContext = vm;
    }
}
