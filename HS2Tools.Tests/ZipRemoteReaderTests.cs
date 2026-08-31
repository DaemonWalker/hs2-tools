using System.IO.Compression;
using System.Text;
using HS2Tools.Services;

namespace HS2Tools.Tests;

public class ZipRemoteReaderTests
{
    private static readonly byte[] Manifest = Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\"?><manifest><guid>com.test.remote</guid><name>Remote</name><version>1.0</version></manifest>");

    [Fact]
    public void ReadCentralDir_ParsesRealZip()
    {
        var zip = TestAssets.BuildZipBytes(
            ("manifest.xml", Manifest, false),
            ("data.bin", new byte[100], true));
        var total = zip.Length;

        var entries = ZipRemoteReader.ReadCentralDir(zip, total);

        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        var manifest = entries["manifest.xml"];
        Assert.Equal(ZipRemoteReader.CompressionStore, manifest.CompressionMethod);
        Assert.Equal(Manifest.Length, (int)manifest.UncompressedSize);
        // 本地头偏移处确实是本地文件头
        Assert.Equal(ZipRemoteReader.LocalFileHeaderSignature, BitConverter.ToUInt32(zip, (int)manifest.Offset));
    }

    [Fact]
    public void ReadCentralDir_TailWindow_Works()
    {
        var zip = TestAssets.BuildZipBytes(("manifest.xml", Manifest, false));
        // 只给尾部窗口（中央目录 + EOCD 都在窗口内）
        var windowSize = Math.Min(zip.Length, 512);
        var window = zip[^windowSize..];

        var entries = ZipRemoteReader.ReadCentralDir(window, zip.Length);

        Assert.NotNull(entries);
        Assert.True(entries.ContainsKey("manifest.xml"));
    }

    [Fact]
    public void ReadCentralDir_TruncatedWindow_ReturnsNull()
    {
        var zip = TestAssets.BuildZipBytes(("manifest.xml", Manifest, false));
        // 窗口只有 EOCD（中央目录在窗口外）
        var window = zip[^22..];

        Assert.Null(ZipRemoteReader.ReadCentralDir(window, zip.Length));
    }

    [Fact]
    public void ReadCentralDir_Zip64_ReturnsNull()
    {
        // 手工构造 EOCD：cdOffset = 0xFFFFFFFF
        var eocd = new byte[22];
        BitConverter.GetBytes(ZipRemoteReader.EocdSignature).CopyTo(eocd, 0);
        BitConverter.GetBytes((ushort)0).CopyTo(eocd, 8);
        BitConverter.GetBytes((uint)0).CopyTo(eocd, 12);
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(eocd, 16);

        Assert.Null(ZipRemoteReader.ReadCentralDir(eocd, eocd.Length));
    }

    [Fact]
    public void ReadCentralDir_DataLongerThanTotal_ReturnsNull()
    {
        var zip = TestAssets.BuildZipBytes(("manifest.xml", Manifest, false));
        Assert.Null(ZipRemoteReader.ReadCentralDir(zip, zip.Length - 1));
    }

    [Fact]
    public void ReadCentralDir_ZeroEntries_ReturnsEmptyNotNull()
    {
        // 原版行为：解析出 0 个条目也返回非 nil 的空表（调用方停止扩大窗口）
        var eocd = new byte[22];
        BitConverter.GetBytes(ZipRemoteReader.EocdSignature).CopyTo(eocd, 0);
        BitConverter.GetBytes((ushort)0).CopyTo(eocd, 8);  // cdEntries = 0
        BitConverter.GetBytes((uint)0).CopyTo(eocd, 12);   // cdSize = 0
        BitConverter.GetBytes((uint)0).CopyTo(eocd, 16);   // cdOffset = 0

        var entries = ZipRemoteReader.ReadCentralDir(eocd, eocd.Length);

        Assert.NotNull(entries);
        Assert.Empty(entries);
    }

    [Fact]
    public void FindEocd_RequiresExactWindowEnd()
    {
        var zip = TestAssets.BuildZipBytes(("manifest.xml", Manifest, false));

        // EOCD 在窗口末尾 → 找到
        Assert.True(ZipRemoteReader.FindEocd(zip) >= 0);

        // 尾部有多余字节（comment 长度校验失败）→ 找不到
        var withJunk = zip.Concat(new byte[] { 1, 2, 3 }).ToArray();
        Assert.Equal(-1, ZipRemoteReader.FindEocd(withJunk));
    }

    [Fact]
    public void FindEocd_WithComment_Works()
    {
        var zip = TestAssets.BuildZipBytes(("manifest.xml", Manifest, false));
        var withComment = TestAssets.AddZipComment(zip, "hello comment");

        var offset = ZipRemoteReader.FindEocd(withComment);

        Assert.True(offset >= 0);
        Assert.NotNull(ZipRemoteReader.ReadCentralDir(withComment, withComment.Length));
    }

    [Fact]
    public void FindEocd_TooShort_ReturnsMinusOne()
    {
        Assert.Equal(-1, ZipRemoteReader.FindEocd(new byte[10]));
    }

    [Fact]
    public void TryParseLocalHeader_Valid()
    {
        var zip = TestAssets.BuildZipBytes(("manifest.xml", Manifest, false));
        var entries = ZipRemoteReader.ReadCentralDir(zip, zip.Length)!;
        var offset = (int)entries["manifest.xml"].Offset;
        var headerData = zip[offset..(offset + 201)];

        var ok = ZipRemoteReader.TryParseLocalHeader(headerData, out var dataOffset, out var compressedSize);

        Assert.True(ok);
        Assert.Equal(30 + "manifest.xml".Length, (int)dataOffset);
        Assert.True(compressedSize > 0);
    }

    [Fact]
    public void TryParseLocalHeader_Invalid()
    {
        Assert.False(ZipRemoteReader.TryParseLocalHeader(null, out _, out _));
        Assert.False(ZipRemoteReader.TryParseLocalHeader(new byte[10], out _, out _));
        Assert.False(ZipRemoteReader.TryParseLocalHeader(new byte[30], out _, out _)); // 全 0 → 签名不符
    }

    [Fact]
    public void ExtractManifestGuid_Store()
    {
        var guid = ZipRemoteReader.ExtractManifestGuid(Manifest, ZipRemoteReader.CompressionStore);
        Assert.Equal("com.test.remote", guid);
    }

    [Fact]
    public void ExtractManifestGuid_Deflate()
    {
        // 裸 DEFLATE 压缩
        using var input = new MemoryStream(Manifest);
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionMode.Compress, leaveOpen: true))
            input.CopyTo(deflate);
        var compressed = output.ToArray();

        var guid = ZipRemoteReader.ExtractManifestGuid(compressed, ZipRemoteReader.CompressionDeflate);

        Assert.Equal("com.test.remote", guid);
    }

    [Fact]
    public void ExtractManifestGuid_UnsupportedMethod_ReturnsNull()
    {
        Assert.Null(ZipRemoteReader.ExtractManifestGuid(Manifest, 9));
    }

    [Fact]
    public void ExtractManifestGuid_CorruptDeflate_ReturnsNull()
    {
        Assert.Null(ZipRemoteReader.ExtractManifestGuid(new byte[] { 1, 2, 3, 4 }, ZipRemoteReader.CompressionDeflate));
    }

    [Fact]
    public void ExtractManifestGuid_NoGuid_ReturnsNull()
    {
        Assert.Null(ZipRemoteReader.ExtractManifestGuid(
            Encoding.UTF8.GetBytes("<manifest><name>x</name></manifest>"), ZipRemoteReader.CompressionStore));
    }

    [Fact]
    public void ReadCentralDir_NullData_ReturnsNull()
    {
        Assert.Null(ZipRemoteReader.ReadCentralDir(null, 100));
    }
}
