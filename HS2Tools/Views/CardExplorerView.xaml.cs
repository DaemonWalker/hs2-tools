using System.Windows.Controls;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>单卡查看页：人物卡/场景卡切换 + 文件选择 + 解析展示</summary>
public partial class CardExplorerView : UserControl
{
    public CardExplorerView()
    {
        InitializeComponent();
        Loaded += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is CardExplorerViewModel)
            return;
        if (App.Services is not { } s)
            return;

        DataContext = new CardExplorerViewModel(s.Config, s.Scanner);
    }
}
