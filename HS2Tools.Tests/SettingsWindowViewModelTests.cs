using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class SettingsWindowViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    /// <summary>记录防休眠调用的替身（P/Invoke 不真正触发）</summary>
    private sealed class FakeLauncher(ConfigService config) : GameLauncherService(config)
    {
        public int PreventCalls;
        public int AllowCalls;
        public override void PreventSleep() => PreventCalls++;
        public override void AllowSleep() => AllowCalls++;
    }

    private static (SettingsWindowViewModel vm, FakeLauncher launcher) MakeVm(ConfigService config)
    {
        var launcher = new FakeLauncher(config);
        return (new SettingsWindowViewModel(config, launcher), launcher);
    }

    // ==================== 代理地址校验 ====================

    [Theory]
    [InlineData(null, true)] // 空 = 直连合法
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("http://127.0.0.1:7890", true)]
    [InlineData("https://proxy.example.com:443", true)]
    [InlineData("socks5://127.0.0.1:1080", true)]
    [InlineData("ftp://proxy:21", false)] // 不支持的协议
    [InlineData("not-a-url", false)]
    [InlineData("127.0.0.1:7890", false)] // 缺协议
    [InlineData("http://", false)] // 缺主机
    public void IsValidProxyUri_Cases(string? uri, bool expected) =>
        Assert.Equal(expected, SettingsWindowViewModel.IsValidProxyUri(uri));

    // ==================== 表单加载与保存 ====================

    [Fact]
    public void Ctor_LoadsFormFromConfig_WithoutCallingLauncher()
    {
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Proxy.Uri = "http://127.0.0.1:7890";
            s.Proxy.Username = "u";
            s.Proxy.Password = "p";
            s.PreventSleep = true;
        });

        var (vm, launcher) = MakeVm(config);

        Assert.Equal("http://127.0.0.1:7890", vm.ProxyAddress);
        Assert.Equal("u", vm.ProxyUsername);
        Assert.Equal("p", vm.ProxyPassword);
        Assert.True(vm.PreventSleep);
        Assert.True(vm.HasProxyAddress);
        // 加载回填不触发防休眠调用（启动恢复由 App.RestorePreventSleep 负责）
        Assert.Equal(0, launcher.PreventCalls);
        Assert.Equal(0, launcher.AllowCalls);
    }

    [Fact]
    public void Save_Valid_WritesConfigAndPersists()
    {
        using var config = new ConfigService(_dir);
        var (vm, _) = MakeVm(config);
        vm.ProxyAddress = "http://127.0.0.1:7890";
        vm.ProxyUsername = "user";
        vm.ProxyPassword = "pass";

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.ProxyError);
        Assert.Equal("设置已保存", vm.StatusMessage);
        Assert.Equal("http://127.0.0.1:7890", config.Settings.Proxy.Uri);
        Assert.Equal("user", config.Settings.Proxy.Username);
        Assert.Equal("pass", config.Settings.Proxy.Password);

        // 落盘后可被新实例读到（ConfigService 防抖，测试里显式 Save）
        config.Save();
        using var reloaded = new ConfigService(_dir);
        Assert.Equal("http://127.0.0.1:7890", reloaded.Settings.Proxy.Uri);
        Assert.Equal("user", reloaded.Settings.Proxy.Username);
    }

    [Fact]
    public void Save_Invalid_ShowsErrorAndDoesNotWrite()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.Proxy.Uri = "http://old:1");
        var (vm, _) = MakeVm(config);

        vm.ProxyAddress = "not-a-url";
        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.ProxyError);
        Assert.Null(vm.StatusMessage);
        Assert.Equal("http://old:1", config.Settings.Proxy.Uri); // 未写入
    }

    [Fact]
    public void Save_Empty_ClearsProxyToDirect()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.Proxy.Uri = "http://old:1");
        var (vm, _) = MakeVm(config);

        vm.ProxyAddress = ""; // 留空 = 直连
        vm.SaveCommand.Execute(null);

        Assert.Null(vm.ProxyError);
        Assert.Equal("", config.Settings.Proxy.Uri);
    }

    [Fact]
    public void Reset_RestoresSavedValues()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.Proxy.Uri = "http://saved:2");
        var (vm, _) = MakeVm(config);

        vm.ProxyAddress = "http://edited:3";
        vm.ResetCommand.Execute(null);

        Assert.Equal("http://saved:2", vm.ProxyAddress);
        Assert.Equal("已恢复当前保存的设置", vm.StatusMessage);
    }

    // ==================== 防休眠开关 ====================

    [Fact]
    public void PreventSleep_Toggle_CallsLauncherAndPersists()
    {
        using var config = new ConfigService(_dir);
        var (vm, launcher) = MakeVm(config);

        vm.PreventSleep = true;
        Assert.Equal(1, launcher.PreventCalls);
        Assert.True(config.Settings.PreventSleep);

        vm.PreventSleep = false;
        Assert.Equal(1, launcher.AllowCalls);
        Assert.False(config.Settings.PreventSleep);

        config.Save();
        using var reloaded = new ConfigService(_dir);
        Assert.False(reloaded.Settings.PreventSleep);
    }

    [Fact]
    public void PreventSleep_PersistedTrue_RestoredOnStartup()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.PreventSleep = true);
        var launcher = new FakeLauncher(config);

        App.RestorePreventSleep(config, launcher); // 启动恢复注入点
        Assert.Equal(1, launcher.PreventCalls);

        var launcher2 = new FakeLauncher(config);
        config.Update(s => s.PreventSleep = false);
        App.RestorePreventSleep(config, launcher2);
        Assert.Equal(0, launcher2.PreventCalls); // 配置关闭时不调用
    }

    // ==================== 外部配置同步 ====================

    [Fact]
    public void ConfigChanged_SyncsForm()
    {
        using var config = new ConfigService(_dir);
        var (vm, _) = MakeVm(config);

        // 其他窗口改配置 → 表单同步
        config.Update(s =>
        {
            s.Proxy.Uri = "http://ext:9";
            s.PreventSleep = true;
        });

        Assert.Equal("http://ext:9", vm.ProxyAddress);
        Assert.True(vm.PreventSleep);
    }
}
