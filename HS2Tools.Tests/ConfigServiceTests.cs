using System.Text.Json;
using HS2Tools.Services;

namespace HS2Tools.Tests;

// ErrorLog.DirectoryOverride 是静态全局：凡临时改动它的测试类归入同一 collection 串行执行
[Collection("ErrorLogOverride")]
public class ConfigServiceTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var config = new ConfigService(_dir);
        Assert.Equal("", config.Settings.Current.GamePath);
        Assert.False(config.Settings.PreventSleep);
        Assert.Empty(config.Settings.Current.Favorites);
        Assert.Empty(config.Settings.Current.LocalMods);
        Assert.Empty(config.Settings.Current.ModUsage);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json !!!");
        using var config = new ConfigService(_dir);
        Assert.Equal("", config.Settings.Current.GamePath);
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
            Assert.Equal("", config.Settings.Current.GamePath); // 损坏回退空配置
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
            config.Update(s => s.Current.GamePath = @"C:\HS2");
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
            config.Update(s => s.Current.GamePath = @"C:\HS2");

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
                s.Current.GamePath = @"C:\Games\HS2";
                s.Proxy.Uri = "http://127.0.0.1:7890";
                s.Proxy.Username = "u";
                s.Proxy.Password = "p";
                s.PreventSleep = true;
                s.Current.Favorites.Add(@"C:\cards\a.png");
                s.Current.LocalMods["com.test"] = new() { Name = "Test", Version = "1.0", Path = @"C:\mods\a.zipmod" };
                s.Current.ModUsage["com.test"] = 3;
            });
            config.Save();
        }
        Assert.Equal(1, changed);

        using var reloaded = new ConfigService(_dir);
        Assert.Equal(@"C:\Games\HS2", reloaded.Settings.Current.GamePath);
        Assert.Equal("http://127.0.0.1:7890", reloaded.Settings.Proxy.Uri);
        Assert.Equal("u", reloaded.Settings.Proxy.Username);
        Assert.True(reloaded.Settings.PreventSleep);
        Assert.Equal(new[] { @"C:\cards\a.png" }, reloaded.Settings.Current.Favorites);
        Assert.Equal("Test", reloaded.Settings.Current.LocalMods["com.test"].Name);
        Assert.Equal(3, reloaded.Settings.Current.ModUsage["com.test"]);
    }

    [Fact]
    public void Update_DebouncedSave_WritesFile()
    {
        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = @"D:\HS2");

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
        config.Update(s => s.Current.GamePath = @"C:\HS2");
        Assert.Equal(@"C:\HS2\UserData\chara\female", config.GetCharaDir());
        Assert.Equal(@"C:\HS2\UserData\Studio\scene", config.GetSceneDir());
        Assert.Equal(@"C:\HS2\mods", config.GetModsDir());
        Assert.Equal(@"C:\HS2\mods\hs2-tool-download", config.GetModDownloadDir());
    }

    // ==================== 多游戏：旧 schema 迁移 ====================

    [Fact]
    public void Load_LegacySchema_MigratesToHs2_AndRewritesFile()
    {
        // 旧版单游戏 schema：顶层 gamePath/favorites/localMods/modUsage
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            {
              "gamePath": "C:\\Games\\HS2",
              "favorites": ["C:\\cards\\a.png", "C:\\cards\\b.png"],
              "localMods": { "com.test.mod": { "name": "Mod A", "version": "1.0", "path": "C:\\mods\\a.zipmod" } },
              "modUsage": { "com.test.mod": 5 },
              "preventSleep": true
            }
            """);

        using var config = new ConfigService(_dir);

        Assert.Equal("hs2", config.Settings.CurrentGame);
        var entry = Assert.Single(config.Settings.Games);
        Assert.Equal("hs2", entry.Key);
        Assert.Equal(@"C:\Games\HS2", entry.Value.GamePath);
        Assert.Equal(new[] { @"C:\cards\a.png", @"C:\cards\b.png" }, entry.Value.Favorites);
        Assert.Equal("Mod A", entry.Value.LocalMods["com.test.mod"].Name);
        Assert.Equal(5, entry.Value.ModUsage["com.test.mod"]);
        Assert.Equal(@"C:\Games\HS2", config.Settings.Current.GamePath); // Current 指向迁移结果
        Assert.True(config.Settings.PreventSleep); // 顶层字段不受影响

        // 迁移后立即落盘为新 schema：顶层不再有 gamePath 等旧字段，改由 games.hs2 承载
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("gamePath", out _));
        Assert.False(root.TryGetProperty("favorites", out _));
        Assert.False(root.TryGetProperty("localMods", out _));
        Assert.False(root.TryGetProperty("modUsage", out _));
        Assert.True(root.TryGetProperty("games", out var games));
        Assert.Equal(@"C:\Games\HS2", games.GetProperty("hs2").GetProperty("gamePath").GetString());
    }

    [Fact]
    public void Load_NewSchema_NotMigrated_FileUntouched()
    {
        // 已含 "games" 字段即视为新 schema：不迁移（顶层旧字段被忽略），也不重写文件
        var path = Path.Combine(_dir, "settings.json");
        var json = """
            {
              "currentGame": "kk",
              "games": { "kk": { "gamePath": "D:\\KK", "modUsage": { "com.kk.mod": 2 } } },
              "gamePath": "C:\\LegacyShouldBeIgnored"
            }
            """;
        File.WriteAllText(path, json);

        using var config = new ConfigService(_dir);

        Assert.Equal("kk", config.Settings.CurrentGame);
        Assert.Equal(@"D:\KK", config.Settings.Current.GamePath);
        Assert.Equal(2, config.Settings.Current.ModUsage["com.kk.mod"]);
        Assert.False(config.Settings.Games.ContainsKey("hs2"));
        Assert.Equal(json, File.ReadAllText(path)); // 未触发迁移落盘
    }

    [Fact]
    public void Load_EmptyObject_NoMigration_FileUntouched()
    {
        // 空对象：无 games 也无旧字段，不迁移、不重写
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{}");

        using var config = new ConfigService(_dir);

        Assert.Equal("hs2", config.Settings.CurrentGame);
        Assert.Equal("", config.Settings.Current.GamePath); // Current 就地创建空数据
        Assert.Equal("{}", File.ReadAllText(path));
    }

    // ---- 绿色版数据目录 ----

    [Fact]
    public void ResolveConfigDir_WritableDir_ReturnsIt_AndCleansProbe()
    {
        var installDir = Path.Combine(_dir, "data");
        Assert.Equal(installDir, ConfigService.ResolveConfigDir(installDir));
        Assert.True(Directory.Exists(installDir));
        Assert.Empty(Directory.GetFiles(installDir)); // 探测文件已删
    }

    [Fact]
    public void ResolveConfigDir_Unwritable_FallsBackToAppData()
    {
        // 用一个文件当父路径使 CreateDirectory 必失败，模拟无写权限。
        // 日志走 ErrorLog（全局静态、并行测试可能并发写），此处只断言回退结果，日志留痕由 ErrorLog 自身保证。
        var blocker = Path.Combine(_dir, "file-not-dir");
        File.WriteAllText(blocker, "");
        Assert.Equal(ConfigService.AppDataConfigDir, ConfigService.ResolveConfigDir(Path.Combine(blocker, "data")));
    }

    [Fact]
    public void MigrateFromAppData_CopiesSettingsAndSideload_SkipsExisting()
    {
        var src = Path.Combine(_dir, "appdata");
        var dst = Path.Combine(_dir, "data");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(src, "settings.json"), """{"currentGame":"kk"}""");
        File.WriteAllText(Path.Combine(src, "sideload-hs2.json"), "[]");
        File.WriteAllText(Path.Combine(dst, "settings.json"), """{"currentGame":"hs2"}"""); // 已有则不覆盖

        ConfigService.MigrateFromAppData(dst, src);

        Assert.Equal("""{"currentGame":"hs2"}""", File.ReadAllText(Path.Combine(dst, "settings.json")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(dst, "sideload-hs2.json")));
    }

    [Fact]
    public void MigrateFromAppData_SameDir_NoOp()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{}");
        ConfigService.MigrateFromAppData(_dir, _dir); // 回退目录即源目录：不自我复制、不抛错
        Assert.Equal("{}", File.ReadAllText(Path.Combine(_dir, "settings.json")));
    }
}
