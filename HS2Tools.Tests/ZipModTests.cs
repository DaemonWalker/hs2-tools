using HS2Tools.Services;

namespace HS2Tools.Tests;

public class ZipModTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();
    private readonly ScannerService _svc = new();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadZipMod_Basic(bool deflate)
    {
        var path = TestAssets.WriteZipmod(_dir, "test.zipmod",
            TestAssets.MakeManifest("com.test.mod", "测试 Mod", "1.2.3"), deflate);

        var result = _svc.ReadZipMod(path);

        var info = Assert.Single(result);
        Assert.Equal("com.test.mod", info.Key);
        Assert.Equal("Mod", info.Value.Name); // 中文被 cleanString 截掉
        Assert.Equal("1.2.3", info.Value.Version);
        Assert.Equal(path, info.Value.Path);
    }

    [Fact]
    public void ReadZipMod_ManifestEntryName_CaseInsensitive()
    {
        var path = TestAssets.WriteZipmod(_dir, "test.zipmod",
            TestAssets.MakeManifest("com.test.case"), entryName: "MANIFEST.XML");
        var result = _svc.ReadZipMod(path);
        Assert.Single(result);
    }

    [Fact]
    public void ReadZipMod_MaterialEditorShaders_Extracted()
    {
        // 与 xukmi Vanilla Plus 真实 manifest 同构：<MaterialEditor><Shader Name="..."/>
        var manifest = "<?xml version=\"1.0\"?>\n<manifest>\n" +
                       "<guid>xukmi.Shaders.VanillaPlus</guid>\n<name>Vanilla Plus</name>\n<version>1.5.3</version>\n" +
                       "<MaterialEditor>\n" +
                       "<Shader Name=\"xukmi/SkinPlus\" AssetBundle=\"chara/xukmi/shaders/vanillaplus.unity3d\" Asset=\"a_SkinPlus\" />\n" +
                       "<Shader Name=\"xukmi/MainOpaquePlus\" />\n" +
                       "</MaterialEditor>\n</manifest>";
        var path = TestAssets.WriteZipmod(_dir, "shader.zipmod", manifest);

        var result = _svc.ReadZipMod(path);

        var info = Assert.Single(result);
        Assert.Equal(new[] { "xukmi/SkinPlus", "xukmi/MainOpaquePlus" }, info.Value.ShaderNames);
    }

    [Fact]
    public void ReadZipMod_NoMaterialEditor_EmptyShaderNames()
    {
        var path = TestAssets.WriteZipmod(_dir, "plain.zipmod", TestAssets.MakeManifest("com.test.plain"));

        var result = _svc.ReadZipMod(path);

        Assert.Empty(Assert.Single(result).Value.ShaderNames);
    }

    [Fact]
    public void ReadZipMod_CleansGuidAndName_ButNotVersion()
    {
        // guid/name 含控制字符与非 ASCII → 清洗；version 含制表符/换行 → 原样保留
        var manifest = "<?xml version=\"1.0\"?>\n<manifest>\n" +
                       "<guid> com.test.dirty\t测 </guid>\n" +
                       "<name>My\nMod😀Name</name>\n" +
                       "<version>\t1.0.0\n</version>\n" +
                       "</manifest>";
        var path = TestAssets.WriteZipmod(_dir, "dirty.zipmod", manifest);

        var result = _svc.ReadZipMod(path);

        var info = Assert.Single(result);
        Assert.Equal("com.test.dirty", info.Key);
        Assert.Equal("MyModName", info.Value.Name);
        Assert.Equal("\t1.0.0\n", info.Value.Version);
    }

    [Fact]
    public void ReadZipMod_MissingManifest_Throws()
    {
        // 造一个不含 manifest.xml 的 zip
        var path = Path.Combine(_dir, "nomanifest.zipmod");
        using (var fs = new FileStream(path, FileMode.Create))
        using (var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            archive.CreateEntry("other.txt");
        }

        var ex = Assert.Throws<InvalidDataException>(() => _svc.ReadZipMod(path));
        Assert.Contains("manifest.xml not found", ex.Message);
    }

    [Fact]
    public void ReadZipMod_MissingGuid_Throws()
    {
        var path = TestAssets.WriteZipmod(_dir, "noguid.zipmod",
            "<?xml version=\"1.0\"?><manifest><name>x</name></manifest>");
        var ex = Assert.Throws<InvalidDataException>(() => _svc.ReadZipMod(path));
        Assert.Contains("missing guid", ex.Message);
    }

    [Fact]
    public void ReadZipMod_WrongRoot_Throws()
    {
        var path = TestAssets.WriteZipmod(_dir, "wrongroot.zipmod",
            "<?xml version=\"1.0\"?><notmanifest><guid>x</guid></notmanifest>");
        Assert.Throws<InvalidDataException>(() => _svc.ReadZipMod(path));
    }

    [Fact]
    public void ReadZipMod_NotAZip_Throws()
    {
        var path = Path.Combine(_dir, "fake.zipmod");
        File.WriteAllText(path, "not a zip file");
        Assert.Throws<InvalidDataException>(() => _svc.ReadZipMod(path));
    }

    [Fact]
    public void CleanString_KeepsOnlyAsciiVisible()
    {
        // 含控制字符 NUL、DEL、拉丁字符、emoji（代理对）→ 只保留 ASCII 可见字符
        var input = "ab" + (char)0x00 + "cd" + (char)0x7F + "é😀 z";
        Assert.Equal("abcd z", ScannerService.CleanString(input));
        Assert.Equal("", ScannerService.CleanString("　")); // 全角空格也会被过滤掉
        Assert.Equal("trim", ScannerService.CleanString("  trim  "));
    }

    [Fact]
    public async Task ReadZipModBatch_MergesAndSkipsCorrupt()
    {
        var good1 = TestAssets.WriteZipmod(_dir, "a.zipmod", TestAssets.MakeManifest("com.test.a"));
        var good2 = TestAssets.WriteZipmod(_dir, "b.zipmod", TestAssets.MakeManifest("com.test.b"));
        var corrupt = Path.Combine(_dir, "c.zipmod");
        File.WriteAllText(corrupt, "junk");

        var errors = new List<string>();
        var result = await _svc.ReadZipModBatchAsync(new[] { good1, good2, corrupt }, onError: errors.Add);

        Assert.Equal(2, result.Count);
        Assert.Contains("com.test.a", result.Keys);
        Assert.Contains("com.test.b", result.Keys);
        Assert.Single(errors);
    }
}
