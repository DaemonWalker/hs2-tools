using HS2Tools.Services;

namespace HS2Tools.Tests;

/// <summary>
/// KK/KKS 卡片格式解析（CharaCardParser 多格式自动识别）字节级回归。
/// 夹具与 HS2 用例同一套合成代码（TestAssets），仅标记与 Parameter 字段键不同。
/// </summary>
public class CharaCardParserKkTests
{
    [Fact]
    public void KkCharaBlob_JoinsLastAndFirstName_AndExtractsModIds()
    {
        var region = TestAssets.BuildKkCharaDataRegion("白峰", "一乃", new[] { "com.kk.mod.a", "com.kk.mod.b" });

        var (names, modIds, ok) = CharaCardParser.ParseDataRegion(region);

        Assert.True(ok);
        Assert.Equal(new[] { "白峰 一乃" }, names); // lastname+firstname 空格拼接
        Assert.Equal(new[] { "com.kk.mod.a", "com.kk.mod.b" }, modIds);
    }

    [Fact]
    public void KkCharaBlob_EmptyFirstName_NameFallsBackToLastName()
    {
        // 拼接跳过空白段（firstname 为空时不出尾随空格）
        var region = TestAssets.BuildKkCharaDataRegion("白峰", "", Array.Empty<string>());

        var (names, _, ok) = CharaCardParser.ParseDataRegion(region);

        Assert.True(ok);
        Assert.Equal(new[] { "白峰" }, names);
    }

    [Fact]
    public void KkClothesBlob_NoNames_ModsFromKkexTrailer()
    {
        // KK 坐标卡（ChaFileCoordinate）：无 Parameter/KKEx 块，mod 数据在文件尾 KKEx trailer
        var region = TestAssets.BuildKkClothesDataRegion(new[] { "com.kk.outfit" });

        var (names, modIds, ok) = CharaCardParser.ParseDataRegion(region);

        Assert.True(ok);
        Assert.Empty(names);
        Assert.Equal(new[] { "com.kk.outfit" }, modIds);
    }

    [Fact]
    public void MixedHs2AndKkBlobs_BothParsed()
    {
        // 同一数据区内嵌 HS2 与 KK 两种 blob（场景混入多游戏卡片的情形）
        var region = TestAssets.BuildMixedCharaDataRegion(
            "HS2角色", new[] { "com.hs2.mod" }, "白峰", "一乃", new[] { "com.kk.mod" });

        var (names, modIds, ok) = CharaCardParser.ParseDataRegion(region);

        Assert.True(ok);
        Assert.Equal(new[] { "HS2角色", "白峰 一乃" }, names);
        Assert.Equal(new[] { "com.hs2.mod", "com.kk.mod" }, modIds);
    }
}

// ErrorLog.DirectoryOverride 是静态全局：凡临时改动它的测试类归入同一 collection 串行执行
[Collection("ErrorLogOverride")]
public class CharaCardParserBadBlobTests
{
    /// <summary>坏长度前缀的 KK blob（7bit 前缀与标记长度不符）</summary>
    private static byte[] BadPrefixKkBlob() =>
        new byte[] { 0x64, 0x00, 0x00, 0x00, 0x7F } // productNo + 错误长度前缀（正确应为 0x12）
            .Concat("【KoiKatuChara】"u8.ToArray())
            .Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })
            .ToArray();

    [Fact]
    public void BadLengthPrefixKkBlob_LogsError_DoesNotDragDownOthers()
    {
        // 坏 KK blob + 完好 HS2 blob：坏的记 ErrorLog，好的照常解析
        var region = BadPrefixKkBlob()
            .Concat(TestAssets.BuildCharaDataRegion(new[] { "幸存者" }, Array.Empty<string>()))
            .ToArray();

        var logDir = TestAssets.NewTempDir();
        var prevOverride = ErrorLog.DirectoryOverride;
        try
        {
            ErrorLog.DirectoryOverride = logDir;
            var (names, _, ok) = CharaCardParser.ParseDataRegion(region);

            Assert.True(ok);                            // 好 blob 撑起整体
            Assert.Equal(new[] { "幸存者" }, names);    // 坏 blob 不拖垮整体
            Assert.True(File.Exists(Path.Combine(logDir, "error.log"))); // 失败留痕
        }
        finally
        {
            ErrorLog.DirectoryOverride = prevOverride;
            TestAssets.DeleteDir(logDir);
        }
    }

    [Fact]
    public void BadLengthPrefixKkBlob_Alone_StructuralNotOk()
    {
        // 唯一 blob 解析失败 → StructuralOk=false（调用方走回退字节扫描）
        var (_, _, ok) = CharaCardParser.ParseDataRegion(BadPrefixKkBlob());
        Assert.False(ok);
    }
}
