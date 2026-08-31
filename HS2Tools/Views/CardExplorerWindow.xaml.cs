using System.Windows;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>单卡查看窗口：人物卡/场景卡切换 + 文件选择 + 解析展示</summary>
public partial class CardExplorerWindow : Window
{
    public CardExplorerWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is CardExplorerViewModel)
            return;
        if (App.Services is not { } s)
            return; // 测试环境（无 Application）

        DataContext = new CardExplorerViewModel(s.Config, s.Scanner);
    }
}
