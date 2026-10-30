using System.Windows;
using System.Windows.Controls;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>场景库页：场景网格 + 智能整理</summary>
public partial class SceneView : UserControl
{
    public SceneView()
    {
        InitializeComponent();
        Loaded += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is SceneWindowViewModel)
            return;
        if (App.Services is not { } s)
            return;

        var vm = new SceneWindowViewModel(s.Config, s.Scanner, s.Downloads, s.SideloadDb, s.GameLauncher);
        DataContext = vm;
        vm.LoadCardPaths();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SceneWindowViewModel vm)
            vm.LoadCardPaths();
    }
}
