using HS2Tools.Services;

namespace HS2Tools.Tests;

public class ConfigServiceTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var config = new ConfigService(_dir);
        Assert.Equal("", config.Settings.GamePath);
        Assert.False(config.Settings.PreventSleep);
        Assert.Empty(config.Settings.Favorites);
        Assert.Empty(config.Settings.LocalMods);
        Assert.Empty(config.Settings.ModUsage);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json !!!");
        using var config = new ConfigService(_dir);
        Assert.Equal("", config.Settings.GamePath);
    }

    [Fact]
    public void Load_CorruptFile_LogsError()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json !!!");
        var logDir = TestAssets.NewTempDir();
        var prevOverride = ErrorLog.DirectoryOverride;
        try
        {
            ErrorLog.DirectoryOverride = logDir;
            using var config = new ConfigService(_dir);
            Assert.Equal("", config.Settings.GamePath); // 损坏回退空配置
            Assert.True(File.Exists(Path.Combine(logDir, "error.log"))); // 回退留痕
        }
        finally
        {
            ErrorLog.DirectoryOverride = prevOverride;
            TestAssets.DeleteDir(logDir);
        }
    }

    [Fact]
    public void Save_WriteFailure_DoesNotThrow_AndLogs()
    {
        // settings.json 是个目录 → WriteAllText 必抛（模拟磁盘/权限故障）
        Directory.CreateDirectory(Path.Combine(_dir, "settings.json"));
        var logDir = TestAssets.NewTempDir();
        var prevOverride = ErrorLog.DirectoryOverride;
        try
        {
            ErrorLog.DirectoryOverride = logDir;
            using var config = new ConfigService(_dir);
            config.Update(s => s.GamePath = @"C:\HS2");
            config.Save(); // 不抛出
            Assert.True(File.Exists(Path.Combine(logDir, "error.log")));
        }
        finally
        {
            ErrorLog.DirectoryOverride = prevOverride;
            TestAssets.DeleteDir(logDir);
        }
    }

    [Fact]
    public async Task Flush_WriteFailure_DoesNotCrashProcess()
    {
        // 防抖 Flush 跑在线程池 Timer 回调上：写盘失败若抛出则进程崩溃（阶段 4 修复）
        Directory.CreateDirectory(Path.Combine(_dir, "settings.json"));
        var logDir = TestAssets.NewTempDir();
        var prevOverride = ErrorLog.DirectoryOverride;
        try
        {
            ErrorLog.DirectoryOverride = logDir;
            using var config = new ConfigService(_dir);
            config.Update(s => s.GamePath = @"C:\HS2");

            var logPath = Path.Combine(logDir, "error.log");
            var deadline = DateTime.Now.AddSeconds(3);
            while (!File.Exists(logPath) && DateTime.Now < deadline)
                await Task.Delay(50);

            Assert.True(File.Exists(logPath)); // Flush 失败已记日志且进程存活
        }
        finally
        {
            ErrorLog.DirectoryOverride = prevOverride;
            TestAssets.DeleteDir(logDir);
        }
    }

    [Fact]
    public void Update_FiresChanged_AndPersistsRoundtrip()
    {
        var changed = 0;
        using (var config = new ConfigService(_dir))
        {
            config.Changed += (_, _) => changed++;
            config.Update(s =>
            {
                s.GamePath = @"C:\Games\HS2";
                s.Proxy.Uri = "http://127.0.0.1:7890";
                s.Proxy.Username = "u";
                s.Proxy.Password = "p";
                s.PreventSleep = true;
                s.Favorites.Add(@"C:\cards\a.png");
                s.LocalMods["com.test"] = new() { Name = "Test", Version = "1.0", Path = @"C:\mods\a.zipmod" };
                s.ModUsage["com.test"] = 3;
            });
            config.Save();
        }
        Assert.Equal(1, changed);

        using var reloaded = new ConfigService(_dir);
        Assert.Equal(@"C:\Games\HS2", reloaded.Settings.GamePath);
        Assert.Equal("http://127.0.0.1:7890", reloaded.Settings.Proxy.Uri);
        Assert.Equal("u", reloaded.Settings.Proxy.Username);
        Assert.True(reloaded.Settings.PreventSleep);
        Assert.Equal(new[] { @"C:\cards\a.png" }, reloaded.Settings.Favorites);
        Assert.Equal("Test", reloaded.Settings.LocalMods["com.test"].Name);
        Assert.Equal(3, reloaded.Settings.ModUsage["com.test"]);
    }

    [Fact]
    public void Update_DebouncedSave_WritesFile()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.GamePath = @"D:\HS2");

        // 防抖 500ms，轮询等待落盘
        var path = config.SettingsPath;
        var deadline = DateTime.Now.AddSeconds(3);
        while (!File.Exists(path) && DateTime.Now < deadline)
            Thread.Sleep(50);

        Assert.True(File.Exists(path));
        Assert.Contains("D:\\\\HS2", File.ReadAllText(path));
    }

    [Theory]
    // 有认证 → proto://user:pass@host
    [InlineData("http://127.0.0.1:7890", "u", "p", "http://u:p@127.0.0.1:7890")]
    [InlineData("https://proxy:8080", "u", "p", "https://u:p@proxy:8080")]
    [InlineData("socks5://127.0.0.1:1080", "u", "p", "socks5://u:p@127.0.0.1:1080")]
    // 无认证 → 原样
    [InlineData("http://127.0.0.1:7890", "", "", "http://127.0.0.1:7890")]
    [InlineData("http://127.0.0.1:7890", "u", "", "http://127.0.0.1:7890")]
    // 空地址 → 空
    [InlineData("", "u", "p", "")]
    // 无协议前缀 → 原样（不插入认证）
    [InlineData("127.0.0.1:7890", "u", "p", "127.0.0.1:7890")]
    public void GetProxyString_Cases(string uri, string user, string pass, string expected)
    {
        using var config = new ConfigService(_dir);
        config.Update(s =>
        {
            s.Proxy.Uri = uri;
            s.Proxy.Username = user;
            s.Proxy.Password = pass;
        });
        Assert.Equal(expected, config.GetProxyString());
    }

    [Fact]
    public void DerivedPaths_NullWhenGamePathEmpty()
    {
        using var config = new ConfigService(_dir);
        Assert.Null(config.GetCharaDir());
        Assert.Null(config.GetSceneDir());
        Assert.Null(config.GetModsDir());
        Assert.Null(config.GetModDownloadDir());
    }

    [Fact]
    public void DerivedPaths_CombinedWhenGamePathSet()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.GamePath = @"C:\HS2");
        Assert.Equal(@"C:\HS2\UserData\chara\female", config.GetCharaDir());
        Assert.Equal(@"C:\HS2\UserData\Studio\scene", config.GetSceneDir());
        Assert.Equal(@"C:\HS2\mods", config.GetModsDir());
        Assert.Equal(@"C:\HS2\mods\hs2-tool-download", config.GetModDownloadDir());
    }
}
