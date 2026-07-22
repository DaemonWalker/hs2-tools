using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

/// <summary>
/// 系统设置窗口 ViewModel（对应原版 SystemSettings.tsx）：
/// 代理表单（保存时校验，非法不写入）+ 阻止 Windows 休眠开关（即时生效并持久化）。
/// Config.Changed 时表单与配置同步（其他窗口改配置的情况）。
/// </summary>
public partial class SettingsWindowViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly GameLauncherService _launcher;
    private bool _loading;

    public SettingsWindowViewModel(ConfigService config, GameLauncherService launcher)
    {
        _config = config;
        _launcher = launcher;

        LoadFromConfig();
        // 外部配置改动 → 表单同步（对应原版 useEffect on settings；单例服务与窗口同寿，无需退订）
        _config.Changed += (_, _) => UiDispatch.Run(LoadFromConfig);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProxyAddress))]
    private string _proxyAddress = "";

    [ObservableProperty] private string _proxyUsername = "";
    [ObservableProperty] private string _proxyPassword = "";

    /// <summary>代理校验错误提示（非法时不写入配置）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProxyError))]
    private string? _proxyError;

    /// <summary>保存/重置结果提示</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    [ObservableProperty] private bool _preventSleep;

    public bool HasProxyError => ProxyError is not null;
    public bool HasStatusMessage => StatusMessage is not null;

    /// <summary>用户名/密码仅在有代理地址时显示（对应原版 shouldUpdate 条件渲染）</summary>
    public bool HasProxyAddress => !string.IsNullOrWhiteSpace(ProxyAddress);

    /// <summary>
    /// 代理地址校验（对应原版 validateProxyUrl：空 = 直连合法）。
    /// 原版只放行 http/https；这里额外放行 socks5——ConfigService.GetProxyString 本就支持
    /// socks5 认证串拼接，放行后设置页与下载/爬虫的实际能力一致。
    /// </summary>
    internal static bool IsValidProxyUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return true;
        return Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var parsed)
            && parsed.Host.Length > 0
            && (parsed.Scheme == Uri.UriSchemeHttp
                || parsed.Scheme == Uri.UriSchemeHttps
                || parsed.Scheme == "socks5");
    }

    /// <summary>防休眠开关：即时生效并持久化（加载回填时不触发）</summary>
    partial void OnPreventSleepChanged(bool value)
    {
        if (_loading)
            return;
        if (value)
            _launcher.PreventSleep();
        else
            _launcher.AllowSleep();
        if (_config.Settings.PreventSleep != value)
            _config.Update(s => s.PreventSleep = value);
    }

    /// <summary>保存代理设置（校验通过才写入；ConfigService 防抖落盘、改动即时生效）</summary>
    [RelayCommand]
    private void Save()
    {
        if (!IsValidProxyUri(ProxyAddress))
        {
            ProxyError = "请输入有效的代理地址（支持 http/https/socks5，留空为直连）";
            StatusMessage = null;
            return;
        }

        ProxyError = null;
        _config.Update(s =>
        {
            s.Proxy.Uri = ProxyAddress.Trim();
            s.Proxy.Username = ProxyUsername;
            s.Proxy.Password = ProxyPassword;
        });
        StatusMessage = "设置已保存";
    }

    /// <summary>重置表单为当前保存的设置（对应原版 handleReset）</summary>
    [RelayCommand]
    private void Reset()
    {
        LoadFromConfig();
        ProxyError = null;
        StatusMessage = "已恢复当前保存的设置";
    }

    /// <summary>从配置回填表单（不触发防休眠写配置）</summary>
    private void LoadFromConfig()
    {
        _loading = true;
        ProxyAddress = _config.Settings.Proxy.Uri;
        ProxyUsername = _config.Settings.Proxy.Username;
        ProxyPassword = _config.Settings.Proxy.Password;
        PreventSleep = _config.Settings.PreventSleep;
        _loading = false;
    }
}
