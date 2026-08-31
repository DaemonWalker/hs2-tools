using HS2Tools.Models;

namespace HS2Tools.Tests;

/// <summary>GameProfiles 静态注册表：多游戏档案的写死知识集中点</summary>
public class GameProfilesTests
{
    [Fact]
    public void All_ThreeProfiles_UniqueIds()
    {
        Assert.Equal(3, GameProfiles.All.Count);
        Assert.Equal(3, GameProfiles.All.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void All_ExeAndDirs_NonEmpty()
    {
        Assert.All(GameProfiles.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.GameExeName));
            Assert.False(string.IsNullOrWhiteSpace(p.StudioExeName));
            Assert.False(string.IsNullOrWhiteSpace(p.CharaDirRelative));
            Assert.False(string.IsNullOrWhiteSpace(p.SceneDirRelative));
            Assert.False(string.IsNullOrWhiteSpace(p.ModsDirRelative));
            Assert.False(string.IsNullOrWhiteSpace(p.ModDownloadDirRelative));
            Assert.False(string.IsNullOrWhiteSpace(p.CharaMarker));
            Assert.False(string.IsNullOrWhiteSpace(p.ClothesMarker));
            Assert.NotEmpty(p.NameKeys);
        });
    }

    [Theory]
    [InlineData("hs2")]
    [InlineData("kk")]
    [InlineData("kks")]
    public void Get_KnownId_ReturnsMatchingProfile(string id)
        => Assert.Equal(id, GameProfiles.Get(id).Id);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Get_UnknownId_FallsBackToHs2(string? id)
        => Assert.Same(GameProfiles.Hs2, GameProfiles.Get(id));

    [Fact]
    public void SideloadSourceId_KkKksShareKkec_Hs2Exclusive()
    {
        Assert.Equal("hs2", GameProfiles.Hs2.SideloadSourceId);
        Assert.Equal("kkec", GameProfiles.Kk.SideloadSourceId);
        Assert.Equal("kkec", GameProfiles.Kks.SideloadSourceId);
    }

    [Fact]
    public void KkKks_ShareCardFormat_Hs2Distinct()
    {
        // CharaCardParser 格式表按 CharaMarker 去重的前提：KK/KKS 标记相同，HS2 不同
        Assert.Equal(GameProfiles.Kk.CharaMarker, GameProfiles.Kks.CharaMarker);
        Assert.Equal(GameProfiles.Kk.ClothesMarker, GameProfiles.Kks.ClothesMarker);
        Assert.NotEqual(GameProfiles.Hs2.CharaMarker, GameProfiles.Kk.CharaMarker);
        Assert.NotEqual(GameProfiles.Hs2.ClothesMarker, GameProfiles.Kk.ClothesMarker);
    }
}
