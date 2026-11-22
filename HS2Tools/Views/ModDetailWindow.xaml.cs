using System.Windows;

namespace HS2Tools.Views;

/// <summary>Mod 文件信息与 manifest.xml 原文窗口。</summary>
public partial class ModDetailWindow : Window
{
    public ModDetailWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
