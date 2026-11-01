using System.Buffers;
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

    /// <summary>ZIP64 已解析为真实值（哨兵 0xFFFFFFFF 从 extra 0x0001 还原）</summary>
    public long CompressedSize { get; init; }
    public long UncompressedSize { get; init; }
    public long Offset { get; init; }
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

        long cdEntries = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(eocdOffset + 8));
        long cdSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(eocdOffset + 12));
        long cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(eocdOffset + 16));

        // ZIP64：EOCD 字段为哨兵时经 locator 找 ZIP64 EOCD 取真值
        //（此前直接 return null，ZIP64 zipmod 整个被跳过）
        if (cdOffset == 0xFFFFFFFF || cdSize == 0xFFFFFFFF || cdEntries == 0xFFFF)
        {
            if (!TryReadZip64Eocd(data, eocdOffset, totalSize, out cdEntries, out cdSize, out cdOffset))
                return null;
        }

        var dataStartOffset = totalSize - data.Length;
        var cdRelativeOffset = cdOffset - dataStartOffset;

        if (cdRelativeOffset < 0 || cdRelativeOffset + cdSize > data.Length)
            return null;

        var entries = new Dictionary<string, ZipEntryInfo>();
        var cdStart = (int)cdRelativeOffset;
        var cdLen = data.Length - cdStart; // 注意：cdData 切片到窗口末尾（不是 cdSize 长度）
        var offset = 0;

        for (long i = 0; i < cdEntries; i++)
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

            var compressed = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(abs + 20));
            var uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(abs + 24));
            var localOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(abs + 42));
            long compressedSize = compressed, uncompressedSize = uncompressed, entryOffset = localOffset;

            // ZIP64 条目：32 位字段为哨兵时从 extra 0x0001 按序还原（uncomp → comp → offset）
            if (compressed == 0xFFFFFFFF || uncompressed == 0xFFFFFFFF || localOffset == 0xFFFFFFFF)
            {
                if (!TryReadZip64Extra(data, abs + 46 + nameLen, extraLen,
                        uncompressed == 0xFFFFFFFF, compressed == 0xFFFFFFFF, localOffset == 0xFFFFFFFF,
                        out var zu, out var zc, out var zo))
                    return null;
                if (uncompressed == 0xFFFFFFFF) uncompressedSize = zu;
                if (compressed == 0xFFFFFFFF) compressedSize = zc;
                if (localOffset == 0xFFFFFFFF) entryOffset = zo;
            }

            var entry = new ZipEntryInfo
            {
                CompressionMethod = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(abs + 10)),
                CompressedSize = compressedSize,
                UncompressedSize = uncompressedSize,
                Offset = entryOffset,
                NameByteLength = nameLen,
                Name = Encoding.UTF8.GetString(data, abs + 46, nameLen),
            };
            entries[entry.Name] = entry;
            offset += totalLen;
        }

        return entries;
    }

    /// <summary>ZIP64 EOCD locator 位于 EOCD 前 20 字节；ZIP64 EOCD 固定部分 56 字节</summary>
    private static bool TryReadZip64Eocd(
        byte[] data, int eocdOffset, long totalSize, out long entries, out long size, out long offset)
    {
        entries = size = offset = 0;
        var locator = eocdOffset - 20;
        if (locator < 0 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(locator)) != 0x07064b50)
            return false;

        var zip64EocdAbs = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(locator + 8));
        var relative = zip64EocdAbs - (totalSize - data.Length);
        if (relative < 0 || relative + 56 > data.Length)
            return false;

        var p = (int)relative;
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p)) != 0x06064b50)
            return false;
        entries = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(p + 32));
        size = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(p + 40));
        offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(p + 48));
        return true;
    }

    /// <summary>从 extra 区找 0x0001（ZIP64），按序读出哨兵对应的 8 字节真值</summary>
    private static bool TryReadZip64Extra(
        byte[] data, int extraStart, int extraLen,
        bool needUncompressed, bool needCompressed, bool needOffset,
        out long uncompressed, out long compressed, out long offset)
    {
        uncompressed = compressed = offset = 0;
        var end = extraStart + extraLen;
        var p = extraStart;
        while (p + 4 <= end && p + 4 <= data.Length)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
            var fieldSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 2));
            var fieldStart = p + 4;
            var fieldEnd = fieldStart + fieldSize;
            if (fieldEnd > end || fieldEnd > data.Length)
                return false;
            if (id == 0x0001)
            {
                var q = fieldStart;
                if (needUncompressed)
                {
                    if (q + 8 > fieldEnd) return false;
                    uncompressed = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(q));
                    q += 8;
                }
                if (needCompressed)
                {
                    if (q + 8 > fieldEnd) return false;
                    compressed = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(q));
                    q += 8;
                }
                if (needOffset)
                {
                    if (q + 8 > fieldEnd) return false;
                    offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(q));
                }
                return true;
            }
            p = fieldEnd;
        }
        return false;
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

        ReadOnlySpan<byte> xmlData;
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
                    var buffer = ArrayPool<byte>.Shared.Rent(81920);
                    try
                    {
                        int read;
                        while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                            if (output.Length > MaxManifestDecompressedSize)
                                return null;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                    xmlData = output.GetBuffer().AsSpan(0, (int)output.Length);
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
