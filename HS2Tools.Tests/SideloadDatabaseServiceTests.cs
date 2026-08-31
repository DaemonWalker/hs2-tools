using System.Text.Json;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.Tests;

// ErrorLog.DirectoryOverride 是静态全局：凡临时改动它的测试类归入同一 collection 串行执行
[Collection("ErrorLogOverride")]
public class SideloadDatabaseServiceTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private ConfigService NewConfig() => new(_dir);

    private string SourcePath(string sourceId) => Path.Combine(_dir, $"sideload-{sourceId}.json");

    [Fact]
    public void Load_FallsBackToBundledDatabase()
    {
        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);
        Assert.True(db.Database.Count > 1000); // hs2 无用户文件 → 内嵌 sideload.zip 全量库
        Assert.False(File.Exists(SourcePath("hs2")));
    }

    [Fact]
    public void Update_PersistsAndReloads_OverridingBundled()
    {
        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);
        var changed = 0;
        db.Changed += (_, _) => changed++;

        db.Update(new Dictionary<string, string> { ["g1"] = "dir/g1.zipmod" });

        Assert.Equal(1, changed);
        Assert.True(File.Exists(SourcePath("hs2")));

        using var config2 = NewConfig();
        var reloaded = new SideloadDatabaseService(config2);
        Assert.Single(reloaded.Database); // 用户库覆盖内嵌库
        Assert.Equal("dir/g1.zipmod", reloaded.Database["g1"]);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToBundled()
    {
        File.WriteAllText(SourcePath("hs2"), "not-json{");
        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);
        Assert.True(db.Database.Count > 1000);
    }

    [Fact]
    public void Sources_AreIsolated_PerSourceFiles()
    {
        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);

        // hs2 数据源更新落 sideload-hs2.json
        db.Update(new Dictionary<string, string> { ["g-hs2"] = "a/g-hs2.zipmod" });
        // 切到 kk（kkec 数据源）更新落 sideload-kkec.json
        config.Update(s => s.CurrentGame = "kk");
        db.Update(new Dictionary<string, string> { ["g-kk"] = "b/g-kk.zipmod" });

        Assert.True(File.Exists(SourcePath("hs2")));
        Assert.True(File.Exists(SourcePath("kkec")));

        // 切回 hs2 / 切到 kks（与 kk 共享 kkec 库）：各自读到自己的库，互不干扰
        config.Update(s => s.CurrentGame = "hs2");
        Assert.Equal("a/g-hs2.zipmod", Assert.Single(db.Database).Value);
        config.Update(s => s.CurrentGame = "kks");
        Assert.Equal("b/g-kk.zipmod", Assert.Single(db.Database).Value);
    }

    [Fact]
    public void GameSwitch_RaisesChanged_AndSwapsDatabase()
    {
        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);
        var changed = 0;
        db.Changed += (_, _) => changed++;

        config.Update(s => s.CurrentGame = "kk"); // hs2 → kkec：数据源变化
        Assert.Equal(1, changed);
        Assert.Empty(db.Database); // kkec 无用户文件 → 空库

        config.Update(s => s.CurrentGame = "kks"); // kk → kks：同数据源，不重复触发
        Assert.Equal(1, changed);

        config.Update(s => s.PreventSleep = true); // 与游戏无关的改动不触发
        Assert.Equal(1, changed);

        config.Update(s => s.CurrentGame = "hs2"); // 切回：再次触发
        Assert.Equal(2, changed);
        Assert.True(db.Database.Count > 1000); // 回到内嵌全量库
    }

    [Fact]
    public void LegacyFile_MigratesToHs2Source()
    {
        File.WriteAllText(Path.Combine(_dir, "sideload.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["g-legacy"] = "old/g.zipmod" }));

        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);

        Assert.False(File.Exists(Path.Combine(_dir, "sideload.json"))); // 旧文件已改名
        Assert.True(File.Exists(SourcePath("hs2")));
        Assert.Equal("old/g.zipmod", Assert.Single(db.Database).Value); // 内容成为 hs2 数据源的库
    }

    [Fact]
    public void LegacyFile_NotMigratedWhenHs2FileExists()
    {
        // 已有 hs2 用户文件时旧文件不动（防覆盖新库）
        File.WriteAllText(Path.Combine(_dir, "sideload.json"), "{}");
        File.WriteAllText(SourcePath("hs2"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["g-new"] = "new/g.zipmod" }));

        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);

        Assert.True(File.Exists(Path.Combine(_dir, "sideload.json")));
        Assert.Equal("new/g.zipmod", Assert.Single(db.Database).Value);
    }

    // ==================== 扫描元数据（sideload-{sourceId}.meta.json） ====================

    private string MetaPath(string sourceId) => Path.Combine(_dir, $"sideload-{sourceId}.meta.json");

    [Fact]
    public void Kkec_NoUserFile_FallsBackToEmpty_AndLogs()
    {
        var logDir = TestAssets.NewTempDir();
        var prevOverride = ErrorLog.DirectoryOverride;
        try
        {
            ErrorLog.DirectoryOverride = logDir;
            using var config = NewConfig();
            config.Update(s => s.CurrentGame = "kk");
            var db = new SideloadDatabaseService(config);

            Assert.Empty(db.Database); // kkec 无内嵌库 → 空字典
            Assert.Contains("kkec", File.ReadAllText(Path.Combine(logDir, "error.log"))); // 回退留痕
        }
        finally
        {
            ErrorLog.DirectoryOverride = prevOverride;
            TestAssets.DeleteDir(logDir);
        }
    }

    [Fact]
    public void Meta_NoFile_ReturnsNull()
    {
        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);
        Assert.Null(db.GetMeta()); // 从未扫描
    }

    [Fact]
    public void Meta_SaveAndGet_RoundTrip_PersistsAcrossInstances()
    {
        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);
        var changed = 0;
        db.Changed += (_, _) => changed++;
        var meta = new SideloadScanMeta
        {
            LastScanTime = new DateTime(2025, 1, 2, 3, 4, 0),
            Status = SideloadScanStatus.Success,
            FoundCount = 42,
        };

        db.SaveMeta(meta);

        Assert.Equal(1, changed); // 随 Changed 通知
        Assert.True(File.Exists(MetaPath("hs2")));
        Assert.Equal(42, db.GetMeta()!.FoundCount); // 缓存命中

        using var config2 = NewConfig();
        var reloaded = new SideloadDatabaseService(config2);
        var loaded = reloaded.GetMeta(); // 跨实例从磁盘读回
        Assert.NotNull(loaded);
        Assert.Equal(SideloadScanStatus.Success, loaded.Status);
        Assert.Equal(meta.LastScanTime, loaded.LastScanTime);
        Assert.Equal(42, loaded.FoundCount);
    }

    [Fact]
    public void Meta_IsolatedPerSource()
    {
        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);
        db.SaveMeta(new SideloadScanMeta
        {
            LastScanTime = DateTime.Now, Status = SideloadScanStatus.Success, FoundCount = 1,
        });

        config.Update(s => s.CurrentGame = "kk");
        Assert.Null(db.GetMeta()); // kkec 数据源无 meta
        Assert.False(File.Exists(MetaPath("kkec")));

        db.SaveMeta(new SideloadScanMeta
        {
            LastScanTime = DateTime.Now, Status = SideloadScanStatus.Stopped, FoundCount = 7,
        });
        Assert.True(File.Exists(MetaPath("kkec")));

        config.Update(s => s.CurrentGame = "hs2");
        Assert.Equal(SideloadScanStatus.Success, db.GetMeta()!.Status); // 各数据源独立
    }

    [Fact]
    public void Meta_CorruptFile_ReturnsNull()
    {
        File.WriteAllText(MetaPath("hs2"), "not-json{");
        using var config = NewConfig();
        var db = new SideloadDatabaseService(config);
        Assert.Null(db.GetMeta()); // 损坏容错视为无记录
    }
}
