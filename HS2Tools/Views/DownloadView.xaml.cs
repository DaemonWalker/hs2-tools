using System.Windows.Controls;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>下载任务页：统计 + 四类任务列表；切换页面不中断下载</summary>
public partial class DownloadView : UserControl
{
    public DownloadView()
    {
        InitializeComponent();
        Loaded += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is DownloadWindowViewModel)
            return;
        if (App.Services is not { } s)
            return;

        DataContext = new DownloadWindowViewModel(s.Downloads);
    }
}
