using System.ComponentModel;
using System.Windows;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

public partial class SimilarModMatchWindow : Window
{
    public SimilarModMatchWindow() => InitializeComponent();

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (!vm.SimilarModMatchItems.Any(i => i.IsSelected))
        {
            MessageBox.Show(this, "请先勾选需要执行的匹配。", "相近 Mod 匹配",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ConfirmButton.IsEnabled = false;
        try
        {
            var (copied, queued, failed) = await vm.ConfirmSimilarModMatchesAsync();
            MessageBox.Show(this,
                $"已复制 {copied} 个本地 Mod，已加入 {queued} 个下载任务" +
                (failed > 0 ? $"，失败 {failed} 个（详见 error.log）。" : "。"),
                "相近 Mod 匹配结果", MessageBoxButton.OK,
                failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            if (!vm.HasSimilarModMatches)
                Close();
        }
        finally
        {
            ConfirmButton.IsEnabled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel { IsApplyingSimilarMatches: true })
            e.Cancel = true;
    }
}
