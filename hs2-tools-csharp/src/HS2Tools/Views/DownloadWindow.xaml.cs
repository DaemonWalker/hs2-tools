using System.Windows;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>下载任务管理窗口：统计三卡 + 四 Tab 任务列表；关闭窗口不中断下载</summary>
public partial class DownloadWindow : Window
{
    public DownloadWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is DownloadWindowViewModel)
            return;
        if (App.Services is not { } s)
            return; // 测试环境（无 Application）

        DataContext = new DownloadWindowViewModel(s.Downloads);
    }
}
