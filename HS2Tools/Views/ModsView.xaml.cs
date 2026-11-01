using System.Windows;
using System.Windows.Controls;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>本地模组页：统计 + 筛选 + DataGrid + 维护操作</summary>
public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
        Loaded += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is ModsWindowViewModel)
            return;
        if (App.Services is not { } s)
            return;

        var vm = new ModsWindowViewModel(s.Config, s.Scanner, s.SideloadDb);
        var owner = Window.GetWindow(this);

        vm.DedupConfirmationRequested += (_, msg) =>
        {
            var result = MessageBox.Show(owner, msg,
                "确认去重？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                _ = vm.ConfirmDedupAsync();
        };
        vm.DedupMessageRequested += (_, msg) =>
            MessageBox.Show(owner, msg, "Mod 去重", MessageBoxButton.OK, MessageBoxImage.Information);

        vm.CleanupConfirmationRequested += (_, msg) =>
        {
            var result = MessageBox.Show(owner, msg,
                "确认永久删除？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                _ = vm.ConfirmCleanupAsync();
        };
        vm.CleanupMessageRequested += (_, msg) =>
            MessageBox.Show(owner, msg, "清理 Mods", MessageBoxButton.OK, MessageBoxImage.Information);

        vm.OrganizeConfirmationRequested += (_, msg) =>
        {
            var result = MessageBox.Show(owner, msg,
                "确认整理？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                _ = vm.ConfirmOrganizeAsync();
        };
        vm.OrganizeMessageRequested += (_, msg) =>
            MessageBox.Show(owner, msg, "整理 Mods", MessageBoxButton.OK, MessageBoxImage.Information);

        DataContext = vm;
    }
}
