using HS2Tools.Services;

namespace HS2Tools.Tests;

public class SideloadDatabaseServiceTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    [Fact]
    public void Load_FallsBackToBundledDatabase()
    {
        var db = new SideloadDatabaseService(_dir);
        Assert.True(db.Database.Count > 1000); // 内嵌 sideload.zip 全量库
        Assert.False(File.Exists(Path.Combine(_dir, "sideload.json")));
    }

    [Fact]
    public void Update_PersistsAndReloads_OverridingBundled()
    {
        var db = new SideloadDatabaseService(_dir);
        var changed = 0;
        db.Changed += (_, _) => changed++;

        db.Update(new Dictionary<string, string> { ["g1"] = "dir/g1.zipmod" });

        Assert.Equal(1, changed);
        Assert.True(File.Exists(Path.Combine(_dir, "sideload.json")));

        var reloaded = new SideloadDatabaseService(_dir);
        Assert.Single(reloaded.Database); // 用户库覆盖内嵌库
        Assert.Equal("dir/g1.zipmod", reloaded.Database["g1"]);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToBundled()
    {
        File.WriteAllText(Path.Combine(_dir, "sideload.json"), "not-json{");
        var db = new SideloadDatabaseService(_dir);
        Assert.True(db.Database.Count > 1000);
    }
}
