using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace HS2Tools.Services;

/// <summary>远程 zip 条目（对应 Go sideloader.fileEntry）</summary>
internal sealed class ZipEntryInfo
{
    public required string Name { get; init; }

    /// <summary>条目名原始字节数（对应 Go len(entry.name)，用于本地头解析失败的回退计算）</summary>
    public ushort NameByteLength { get; init; }

    public uint CompressedSize { get; init; }
    public uint UncompressedSize { get; init; }
    public uint Offset { get; init; }
    public ushort CompressionMethod { get; init; }
}

/// <summary>
/// 远程 ZIP 中央目录解析（Go sideloader/zipreader.go 的 1:1 移植，最高风险点）。
/// 所有方法返回 null 表示数据不完整或出错（触发调用方扩大窗口重试）。
/// </summary>
internal static class ZipRemoteReader
{
    public const uint EocdSignature = 0x06054b50;
    public const uint CentralDirSignature = 0x02014b50;
    public const uint LocalFileHeaderSignature = 0x04034b50;
    public const ushort CompressionStore = 0;
    public const ushort CompressionDeflate = 8;

    /// <summary>
    /// 解析中央目录。data 是从文件尾部抓取的窗口，totalSize 是文件总大小。
    /// 返回 null 表示数据不完整或出错；返回非 null（可能是空表）表示窗口已包含完整 EOCD。
    /// </summary>
    public static Dictionary<string, ZipEntryInfo>? ReadCentralDir(byte[]? data, long totalSize)
    {
        if (data is null)
            return null;
        if (data.Length > totalSize)
            return null;

        var eocdOffset = FindEocd(data);
        if (eocdOffset < 0)
            return null;

        var cdEntries = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(eocdOffset + 8));
        var cdSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(eocdOffset + 12));
        var cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(eocdOffset + 16));

        if (cdOffset == 0xFFFFFFFF)
            return null; // ZIP64 不支持

        var dataStartOffset = totalSize - data.Length;
        var cdRelativeOffset = (long)cdOffset - dataStartOffset;

        if (cdRelativeOffset < 0 || cdRelativeOffset + cdSize > data.Length)
            return null;

        var entries = new Dictionary<string, ZipEntryInfo>();
        var cdStart = (int)cdRelativeOffset;
        var cdLen = data.Length - cdStart; // 注意：cdData 切片到窗口末尾（不是 cdSize 长度）
        var offset = 0;

        for (var i = 0; i < cdEntries; i++)
        {
            if (offset + 46 > cdLen)
                break;
            var abs = cdStart + offset;
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(abs)) != CentralDirSignature)
                break;

            var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(abs + 28));
            var extraLen = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(abs + 30));
            var commentLen = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(abs + 32));
            var totalLen = 46 + nameLen + extraLen + commentLen;

            if (offset + totalLen > cdLen)
                break;

            var entry = new ZipEntryInfo
            {
                CompressionMethod = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(abs + 10)),
                CompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(abs + 20)),
                UncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(abs + 24)),
                Offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(abs + 42)),
                NameByteLength = nameLen,
                Name = Encoding.UTF8.GetString(data, abs + 46, nameLen),
            };
            entries[entry.Name] = entry;
            offset += totalLen;
        }

        return entries;
    }

    /// <summary>
    /// 在窗口内反向扫 EOCD 签名，且要求 EOCD 精确位于窗口末尾（含 comment 长度校验，防巧合字节）。
    /// </summary>
    public static int FindEocd(byte[] data)
    {
        if (data.Length < 22)
            return -1;
        for (var i = data.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i)) == EocdSignature)
            {
                var commentLen = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 20));
                if ((long)i + 22 + commentLen == data.Length)
                    return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 解析本地文件头，返回数据区偏移（相对文件头起点）与压缩大小。
    /// 失败返回 false（调用方回退 30 + nameLen 假设无 extra）。
    /// </summary>
    public static bool TryParseLocalHeader(byte[]? data, out long dataOffset, out uint compressedSize)
    {
        dataOffset = 0;
        compressedSize = 0;
        if (data is null || data.Length < 30)
            return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != LocalFileHeaderSignature)
            return false;
        compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(18));
        var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(26));
        var extraLen = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28));
        dataOffset = 30L + nameLen + extraLen;
        return true;
    }

    /// <summary>manifest 解压后大小上限（真实 manifest.xml 仅几 KB，超过即视为异常文件）</summary>
    private const int MaxManifestDecompressedSize = 16 * 1024 * 1024;

    /// <summary>
    /// 从 manifest 压缩数据中提取 &lt;guid&gt;（Store 直接用 / 裸 DEFLATE 解压；strings.Index 暴力截取）。
    /// 失败返回 null。
    /// </summary>
    public static string? ExtractManifestGuid(byte[]? data, ushort compressionMethod)
    {
        if (data is null)
            return null;

        byte[] xmlData;
        switch (compressionMethod)
        {
            case CompressionStore:
                xmlData = data;
                break;
            case CompressionDeflate:
                try
                {
                    // 裸 DEFLATE 流（不能用 ZLibStream）；解压带上限防解压炸弹
                    using var input = new MemoryStream(data);
                    using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    var buffer = new byte[81920];
                    int read;
                    while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        if (output.Length > MaxManifestDecompressedSize)
                            return null;
                    }
                    xmlData = output.ToArray();
                }
                catch
                {
                    return null;
                }
                break;
            default:
                return null; // 不支持的压缩方式
        }

        var text = Encoding.UTF8.GetString(xmlData);
        var startIdx = text.IndexOf("<guid>", StringComparison.Ordinal);
        if (startIdx < 0)
            return null;
        var endIdx = text.IndexOf("</guid>", startIdx + 6, StringComparison.Ordinal);
        if (endIdx < 0)
            return null;
        return text.Substring(startIdx + 6, endIdx - startIdx - 6);
    }
}
