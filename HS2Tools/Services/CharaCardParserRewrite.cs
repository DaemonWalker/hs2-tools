using System.Buffers;
using System.Text;
using MessagePack;

namespace HS2Tools.Services;

internal static partial class CharaCardParser
{
    public static (byte[] Data, int ReplacementCount) RewriteGuids(
        ReadOnlyMemory<byte> region, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
            return (region.ToArray(), 0);

        var patches = new List<Patch>();
        var replacementCount = 0;
        foreach (var (pos, marker, format) in FindMarkers(region.Span))
        {
            try
            {
                var rewritten = RewriteCharaBlob(region, pos, marker, format, replacements);
                if (rewritten is null)
                    continue;
                patches.Add(new Patch(rewritten.Start, rewritten.Length, rewritten.Data));
                replacementCount += rewritten.ReplacementCount;
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"CharaCardParser blob GUID rewrite failed at +{pos}: {ex.Message}");
            }
        }

        RewriteKkexTrailer(region, replacements, patches, ref replacementCount);
        return (ApplyPatches(region, patches), replacementCount);
    }

    private static BlobRewrite? RewriteCharaBlob(
        ReadOnlyMemory<byte> region, int markerPos, byte[] marker, CardFormat format,
        IReadOnlyDictionary<string, string> replacements)
    {
        var span = region.Span;
        if (markerPos < 5 || span[markerPos - 1] != marker.Length)
            throw new InvalidDataException("invalid marker envelope");
        if (marker == format.ClothesMarker)
            return null;

        var p = markerPos + marker.Length;
        _ = Read7BitString(span, ref p);
        if (format.HasFacePng)
        {
            var faceLen = ReadInt32LE(span, ref p);
            if (faceLen < 0 || faceLen > region.Length - p)
                throw new InvalidDataException("face png length out of bounds");
            p += faceLen;
        }
        else
        {
            _ = ReadInt32LE(span, ref p);
            _ = Read7BitString(span, ref p);
            _ = Read7BitString(span, ref p);
        }

        var headerLengthPos = p;
        var headerLength = ReadInt32LE(span, ref p);
        if (headerLength <= 0 || headerLength > region.Length - p)
            throw new InvalidDataException("block header length out of bounds");
        var headerStart = p;
        var header = region.Slice(headerStart, headerLength);
        var infos = ParseBlockHeader(new ReadOnlySequence<byte>(header));
        p += headerLength;

        var totalLength = p + 8 <= region.Length
            ? BitConverter.ToInt64(span.Slice(p, 8))
            : throw new InvalidDataException("no room for block data length");
        p += 8;
        var blocksStart = p;
        if (totalLength < 0 || totalLength > region.Length - blocksStart)
            throw new InvalidDataException("block data length out of bounds");
        var blobEnd = blocksStart + checked((int)totalLength);

        var previousEnd = 0L;
        foreach (var info in infos.OrderBy(i => i.Pos))
        {
            if (info.Pos < 0 || info.Size < 0 || info.Pos > totalLength || info.Size > totalLength - info.Pos)
                throw new InvalidDataException($"block {info.Name} pos/size out of bounds");
            if (info.Pos < previousEnd)
                throw new InvalidDataException("block data ranges overlap");
            previousEnd = info.Pos + info.Size;
        }

        var rewrittenBlocks = new Dictionary<int, byte[]>();
        var replacementCount = 0;
        for (var i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            if (info.Name != "KKEx")
                continue;
            var block = region.Slice(blocksStart + checked((int)info.Pos), checked((int)info.Size));
            var (newBlock, count) = RewriteKkexPayload(block, replacements);
            if (count == 0)
                continue;
            rewrittenBlocks[i] = newBlock;
            replacementCount += count;
        }
        if (rewrittenBlocks.Count == 0)
            return null;

        var headerPatches = new List<Patch>();
        var blockPatches = new List<Patch>();
        var newTotalLength = totalLength;
        for (var i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            var newPos = info.Pos;
            for (var j = 0; j < infos.Count; j++)
            {
                if (infos[j].Pos < info.Pos && rewrittenBlocks.TryGetValue(j, out var earlier))
                    newPos += earlier.Length - infos[j].Size;
            }

            var newSize = rewrittenBlocks.TryGetValue(i, out var newBlock) ? newBlock.Length : info.Size;
            if (newPos != info.Pos)
            {
                if (info.PosStart < 0)
                    throw new InvalidDataException($"block {info.Name} has no pos field");
                headerPatches.Add(new Patch(info.PosStart, info.PosLength, EncodeInt64(newPos)));
            }
            if (newSize != info.Size)
            {
                if (info.SizeStart < 0)
                    throw new InvalidDataException($"block {info.Name} has no size field");
                headerPatches.Add(new Patch(info.SizeStart, info.SizeLength, EncodeInt64(newSize)));
                newTotalLength += newSize - info.Size;
            }
            if (newBlock is not null)
                blockPatches.Add(new Patch(checked((int)info.Pos), checked((int)info.Size), newBlock));
        }

        var newHeader = ApplyPatches(header, headerPatches);
        var newBlocks = ApplyPatches(region.Slice(blocksStart, checked((int)totalLength)), blockPatches);
        var blobStart = markerPos - 5;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(region.Span.Slice(blobStart, headerLengthPos - blobStart));
            writer.Write(newHeader.Length);
            writer.Write(newHeader);
            writer.Write(newTotalLength);
            writer.Write(newBlocks);
        }
        return new BlobRewrite(blobStart, blobEnd - blobStart, stream.ToArray(), replacementCount);
    }

    private static (byte[] Data, int ReplacementCount) RewriteKkexPayload(
        ReadOnlyMemory<byte> payload, IReadOnlyDictionary<string, string> replacements)
    {
        var patches = new List<Patch>();
        var replacementCount = 0;
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(payload));
        var pluginCount = reader.ReadMapHeader();
        for (var i = 0; i < pluginCount; i++)
        {
            var pluginId = reader.ReadString();
            var isUar = pluginId is UarPluginId or UarPluginIdLegacy;
            if (reader.NextMessagePackType != MessagePackType.Array)
            {
                reader.Skip();
                continue;
            }

            var fieldCount = reader.ReadArrayHeader();
            for (var k = 0; k < fieldCount; k++)
            {
                if (!isUar || k != 1 || reader.NextMessagePackType != MessagePackType.Map)
                {
                    reader.Skip();
                    continue;
                }

                var mapCount = reader.ReadMapHeader();
                for (var m = 0; m < mapCount; m++)
                {
                    var key = reader.ReadString();
                    if (key == "mapInfoGUID" && reader.NextMessagePackType == MessagePackType.String)
                    {
                        var start = CheckedOffset(reader.Consumed);
                        var guid = reader.ReadString();
                        if (guid is not null && replacements.TryGetValue(guid, out var newGuid))
                        {
                            patches.Add(new Patch(start, CheckedOffset(reader.Consumed) - start, EncodeString(newGuid)));
                            replacementCount++;
                        }
                        continue;
                    }
                    if (key is not ("info" or "itemInfo" or "patternInfo") ||
                        reader.NextMessagePackType != MessagePackType.Array)
                    {
                        reader.Skip();
                        continue;
                    }

                    var infoCount = reader.ReadArrayHeader();
                    for (var j = 0; j < infoCount; j++)
                    {
                        if (reader.NextMessagePackType != MessagePackType.Binary)
                        {
                            reader.Skip();
                            continue;
                        }
                        var start = CheckedOffset(reader.Consumed);
                        var bin = reader.ReadBytes();
                        if (bin is null)
                            continue;
                        var (newInfo, count) = RewriteResolveInfo(bin.Value.ToArray(), replacements);
                        if (count == 0)
                            continue;
                        patches.Add(new Patch(start, CheckedOffset(reader.Consumed) - start, EncodeBinary(newInfo)));
                        replacementCount += count;
                    }
                }
            }
        }
        return (ApplyPatches(payload, patches), replacementCount);
    }

    private static (byte[] Data, int ReplacementCount) RewriteResolveInfo(
        ReadOnlyMemory<byte> info, IReadOnlyDictionary<string, string> replacements)
    {
        var patches = new List<Patch>();
        var replacementCount = 0;
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(info));
        var mapCount = reader.ReadMapHeader();
        for (var i = 0; i < mapCount; i++)
        {
            var key = reader.ReadString();
            if (key == "ModID" && reader.NextMessagePackType == MessagePackType.String)
            {
                var start = CheckedOffset(reader.Consumed);
                var guid = reader.ReadString();
                if (guid is not null && replacements.TryGetValue(guid, out var newGuid))
                {
                    patches.Add(new Patch(start, CheckedOffset(reader.Consumed) - start, EncodeString(newGuid)));
                    replacementCount++;
                }
            }
            else
            {
                reader.Skip();
            }
        }
        return (ApplyPatches(info, patches), replacementCount);
    }

    private static void RewriteKkexTrailer(
        ReadOnlyMemory<byte> region, IReadOnlyDictionary<string, string> replacements,
        List<Patch> patches, ref int replacementCount)
    {
        var span = region.Span;
        var pos = 0;
        while (true)
        {
            var idx = span[pos..].IndexOf(KkexMark);
            if (idx < 0)
                return;
            idx += pos;
            pos = idx + 1;
            if (idx < 1 || span[idx - 1] != KkexMark.Length)
                continue;
            var p = idx + KkexMark.Length;
            if (p + 8 > region.Length)
                continue;
            var version = BitConverter.ToInt32(span.Slice(p, 4));
            var length = BitConverter.ToInt32(span.Slice(p + 4, 4));
            p += 8;
            if (version < 0 || version > 1000 || length <= 0 || p + length != region.Length)
                continue;

            var (newPayload, count) = RewriteKkexPayload(region.Slice(p, length), replacements);
            if (count > 0)
            {
                patches.Add(new Patch(p - 4, 4, BitConverter.GetBytes(newPayload.Length)));
                patches.Add(new Patch(p, length, newPayload));
                replacementCount += count;
            }
            return;
        }
    }

    private static byte[] EncodeString(string value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.Write(value);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeBinary(byte[] value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.Write(value);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeInt64(long value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.Write(value);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] ApplyPatches(ReadOnlyMemory<byte> source, List<Patch> patches)
    {
        if (patches.Count == 0)
            return source.ToArray();
        patches.Sort((a, b) => a.Start.CompareTo(b.Start));
        using var stream = new MemoryStream();
        var cursor = 0;
        foreach (var patch in patches)
        {
            if (patch.Start < cursor || patch.Start < 0 || patch.Length < 0 ||
                patch.Start > source.Length - patch.Length)
                throw new InvalidDataException("overlapping or out-of-bounds GUID rewrite patches");
            stream.Write(source.Span.Slice(cursor, patch.Start - cursor));
            stream.Write(patch.Data);
            cursor = patch.Start + patch.Length;
        }
        stream.Write(source.Span[cursor..]);
        return stream.ToArray();
    }
}
