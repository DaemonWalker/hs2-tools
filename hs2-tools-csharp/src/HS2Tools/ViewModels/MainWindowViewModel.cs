using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Services;
using Microsoft.Win32;

namespace HS2Tools.ViewModels;

/// <summary>主窗口（首页）ViewModel：游戏路径设置 + 校验</summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly ConfigService _config;

    [ObservableProperty]
    private string _gamePath = "";

    [ObservableProperty]
    private bool _isGamePathValid;

    [ObservableProperty]
    private string _pathStatusText = "";

    public MainWindowViewModel(ConfigService config)
    {
        _config = config;
        GamePath = config.Settings.GamePath; // setter 链触发校验
    }

    partial void OnGamePathChanged(string value)
    {
        IsGamePathValid = ValidateGamePath(value);
        PathStatusText = IsGamePathValid
            ? $"已验证：找到 {ConfigService.GameExeName}"
            : $"未找到 {ConfigService.GameExeName}，请选择游戏目录";

        // 与已加载值相同则不重复写盘（避免每次启动重写配置文件）
        if (value != _config.Settings.GamePath)
            _config.Update(s => s.GamePath = value);
    }

    /// <summary>选择游戏 exe（对应原版 SelectPath：选 exe 取目录）</summary>
    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = $"选择 {ConfigService.GameExeName}",
            Filter = $"{ConfigService.GameExeName}|{ConfigService.GameExeName}|可执行文件 (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
            GamePath = Path.GetDirectoryName(dialog.FileName) ?? "";
    }

    /// <summary>游戏路径校验：目录下存在 HoneySelect2.exe</summary>
    public static bool ValidateGamePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, ConfigService.GameExeName));
}
