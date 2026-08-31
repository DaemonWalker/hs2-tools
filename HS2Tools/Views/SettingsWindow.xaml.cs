using System.ComponentModel;
using System.Windows;
using HS2Tools.ViewModels;

namespace HS2Tools.Views;

/// <summary>系统设置窗口：代理表单（保存时校验）+ 防休眠开关（即时生效）</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => InitViewModel();
    }

    private void InitViewModel()
    {
        if (DataContext is SettingsWindowViewModel)
            return;
        if (App.Services is not { } s)
            return; // 测试环境（无 Application）

        var vm = new SettingsWindowViewModel(s.Config, s.GameLauncher);
        DataContext = vm;
        // PasswordBox 不支持绑定：VM → 控件 方向手动同步（配置回填/重置时）
        ProxyPasswordBox.Password = vm.ProxyPassword;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsWindowViewModel.ProxyPassword)
            && sender is SettingsWindowViewModel vm
            && ProxyPasswordBox.Password != vm.ProxyPassword)
            ProxyPasswordBox.Password = vm.ProxyPassword;
    }

    /// <summary>PasswordBox 不支持绑定：控件 → VM 方向手动同步</summary>
    private void ProxyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsWindowViewModel vm)
            vm.ProxyPassword = ProxyPasswordBox.Password;
    }
}
