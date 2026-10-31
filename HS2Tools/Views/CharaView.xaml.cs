using System.Windows;
using System.Windows.Controls;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>角色卡浏览页：网格 + 搜索 + 排序 + 详情面板</summary>
public partial class CharaView : UserControl
{
    public CharaView()
    {
        InitializeComponent();
        Loaded += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is CharaWindowViewModel)
            return;
        if (App.Services is not { } s)
            return;

        var vm = new CharaWindowViewModel(s.Config, s.Scanner, s.Downloads, s.SideloadDb, s.GameLauncher, s.AnalysisCache);
        DataContext = vm;
        vm.LoadCardPaths();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CharaWindowViewModel vm)
        {
            vm.LoadCardPaths();
            Grid.Reload();
        }
    }
}
