using System.Collections.Concurrent;
using System.Buffers;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HS2Tools.Services;
using MessagePack;

namespace HS2Tools.Tests;

/// <summary>测试辅助：临时目录、合成 PNG / zipmod 夹具</summary>
internal static class TestAssets
{
    static TestAssets()
    {
        // 全部测试的 ErrorLog 统一改写到测试目录，避免污染真实 %AppData%/hs2-tools/error.log
        // （具体用例可再临时覆盖 DirectoryOverride，恢复时须还原到本值）
        ErrorLog.DirectoryOverride = NewTempDir();
    }

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hs2tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void DeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }

    // ==================== PNG 夹具 ====================
    // 解析器不做 PNG 结构校验，直接构造字节：签名 + 占位块 + IEND + CRC + 游戏数据（含标记）

    public static byte[] PngPrefix()
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }); // PNG 签名
        ms.Write("IHDR"u8.ToArray());
        ms.Write(new byte[13]);
        ms.Write("IDAT-placeholder"u8.ToArray());
        ms.Write("IEND"u8.ToArray());
        ms.Write(new byte[] { 0xAE, 0x42, 0x60, 0x82 }); // IEND CRC（缩略图截断时被丢弃）
        return ms.ToArray();
    }

    /// <summary>带角色名标记的游戏数据（HS2 回退模式：fullname..personality）</summary>
    public static byte[] NameMarker(string name)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x01);
        ms.Write("fullname"u8.ToArray());
        ms.WriteByte(0x0A);
        ms.Write(Encoding.UTF8.GetBytes(name));
        ms.WriteByte(0x0D);
        ms.Write("personality"u8.ToArray());
        ms.WriteByte(0x02);
        return ms.ToArray();
    }

    /// <summary>带角色名标记的游戏数据（KK 回退模式：lastname..firstname 与 firstname..nickname 两段）</summary>
    public static byte[] KkNameMarker(string lastName, string firstName)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x01);
        ms.Write("lastname"u8.ToArray());
        ms.WriteByte(0x0A);
        ms.Write(Encoding.UTF8.GetBytes(lastName));
        ms.WriteByte(0x0D);
        ms.Write("firstname"u8.ToArray());
        ms.WriteByte(0x0A);
        ms.Write(Encoding.UTF8.GetBytes(firstName));
        ms.WriteByte(0x0D);
        ms.Write("nickname"u8.ToArray());
        ms.WriteByte(0x02);
        return ms.ToArray();
    }

    /// <summary>带 Mod GUID 标记的游戏数据</summary>
    public static byte[] ModMarker(string guid)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x03);
        ms.Write("ModID"u8.ToArray());
        ms.WriteByte(0x05);
        ms.Write(Encoding.UTF8.GetBytes(guid));
        ms.WriteByte(0x06);
        ms.Write("Slot"u8.ToArray());
        ms.WriteByte(0x07);
        return ms.ToArray();
    }

    // ==================== 结构化数据区夹具（MessagePack 合成，IEND+CRC 之后的部分） ====================

    /// <summary>
    /// 合成结构完整的 HS2 角色卡数据区：productNo +【AIS_Chara】+ version + lang/userID/dataID
    /// + BlockHeader（msgpack {lstInfo:[[name,version,pos,size]]}）+ Parameter/Parameter2/KKEx 块。
    /// names[0] → Parameter.fullname，names[1] → Parameter2.fullname；
    /// guids → UAR 插件 ResolveInfo；otherPluginGuids → 其他插件数据（命名空间隔离测试用）。
    /// </summary>
    public static byte[] BuildCharaDataRegion(string[] names, string[] guids, string[]? otherPluginGuids = null)
        => BuildCharaBlob(names, guids, otherPluginGuids);

    /// <summary>合成 KK 角色卡数据区：【KoiKatuChara】头 + Parameter(lastname/firstname) + KKEx 块</summary>
    public static byte[] BuildKkCharaDataRegion(string lastName, string firstName, string[] guids)
        => BuildKkCharaBlob(lastName, firstName, guids);

    /// <summary>合成混合数据区：HS2 角色 blob + KK 角色 blob（场景内嵌多游戏 blob 情形）</summary>
    public static byte[] BuildMixedCharaDataRegion(
        string hs2Name, string[] hs2Guids, string kkLastName, string kkFirstName, string[] kkGuids)
    {
        using var ms = new MemoryStream();
        ms.Write(BuildCharaBlob(new[] { hs2Name }, hs2Guids));
        ms.Write(BuildKkCharaBlob(kkLastName, kkFirstName, kkGuids));
        return ms.ToArray();
    }

    /// <summary>合成坐标卡数据区：【AIS_Clothes】头 + 占位衣着数据 + 文件尾 KKEx trailer</summary>
    public static byte[] BuildClothesDataRegion(string[] guids) => BuildClothesDataRegion("【AIS_Clothes】", guids);

    /// <summary>合成 KK 坐标卡数据区：【KoiKatuClothes】头 + 占位衣着数据 + 文件尾 KKEx trailer</summary>
    public static byte[] BuildKkClothesDataRegion(string[] guids) => BuildClothesDataRegion("【KoiKatuClothes】", guids);

    private static byte[] BuildClothesDataRegion(string marker, string[] guids)
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(100);                 // loadProductNo
        bw.Write(marker);              // 7bit 前缀标记
        bw.Write("1.0.0");             // version
        bw.Write(new byte[] { 1, 2, 3, 4 }); // 占位衣着数据
        bw.Flush();
        ms.Write(BuildKkexTrailer(guids));
        return ms.ToArray();
    }

    /// <summary>合成场景数据区：两个内嵌 chara blob（各一名）+ 场景尾标 + 文件尾 KKEx trailer</summary>
    public static byte[] BuildSceneDataRegion(string name1, string name2, string[] trailerGuids)
    {
        using var ms = new MemoryStream();
        ms.Write(BuildCharaBlob(new[] { name1 }, Array.Empty<string>()));
        ms.Write("【StudioNEOV2】"u8.ToArray()); // 场景尾标
        ms.Write(BuildCharaBlob(new[] { name2 }, Array.Empty<string>()));
        ms.Write(BuildKkexTrailer(trailerGuids));
        return ms.ToArray();
    }

    /// <summary>单个 HS2 ChaFile blob（BinaryWriter 布局，与游戏序列化一致）</summary>
    private static byte[] BuildCharaBlob(string[] names, string[] guids, string[]? otherPluginGuids = null)
    {
        var infos = new List<(string Name, byte[] Data)>();
        if (names.Length > 0)
            infos.Add(("Parameter", BuildFullnameBlock(names[0])));
        if (names.Length > 1)
            infos.Add(("Parameter2", BuildFullnameBlock(names[1])));
        infos.Add(("KKEx", BuildKkexBlock(guids, otherPluginGuids)));
        return BuildBlob("【AIS_Chara】", infos);
    }

    /// <summary>单个 KK ChaFile blob：信封布局与 HS2 相同，标记【KoiKatuChara】，Parameter 用 lastname/firstname</summary>
    private static byte[] BuildKkCharaBlob(string lastName, string firstName, string[] guids)
    {
        var infos = new List<(string Name, byte[] Data)>
        {
            ("Parameter", BuildKkNameBlock(lastName, firstName)),
            ("KKEx", BuildKkexBlock(guids)),
        };
        return BuildBlob("【KoiKatuChara】", infos);
    }

    /// <summary>ChaFile blob 信封（BinaryWriter 布局，各游戏相同；差异仅在标记与块内容）</summary>
    private static byte[] BuildBlob(string marker, List<(string Name, byte[] Data)> infos)
    {
        // BlockHeader msgpack（BlockHeader 是 [MessagePackObject(true)] map；Info 是数组式 4 元素）
        var headerBuffer = new ArrayBufferWriter<byte>();
        {
            var w = new MessagePackWriter(headerBuffer);
            w.WriteMapHeader(1);
            w.Write("lstInfo");
            w.WriteArrayHeader(infos.Count);
            long pos = 0;
            foreach (var (name, data) in infos)
            {
                w.WriteArrayHeader(4);
                w.Write(name);
                w.Write("1.0.0"); // block version
                w.Write(pos);
                w.Write((long)data.Length);
                pos += data.Length;
            }
            w.Flush();
        }
        var headerBytes = headerBuffer.WrittenSpan.ToArray();

        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(100);               // loadProductNo
        bw.Write(marker);            // 7bit 前缀标记
        bw.Write("1.0.0");           // ChaFileVersion
        bw.Write(0);                 // lang
        bw.Write("user");            // userID
        bw.Write("data");            // dataID
        bw.Write(headerBytes.Length);
        bw.Write(headerBytes);
        bw.Write((long)infos.Sum(i => i.Data.Length)); // 块数据总长度
        foreach (var (_, data) in infos)
            bw.Write(data);
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>Parameter 块：msgpack map（字符串键），含 fullname + 干扰键</summary>
    private static byte[] BuildFullnameBlock(string fullname)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new MessagePackWriter(buffer);
        w.WriteMapHeader(3);
        w.Write("lastname");
        w.Write("姓");
        w.Write("fullname");
        w.Write(fullname);
        w.Write("personality");
        w.Write(12);
        w.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>KK Parameter 块：msgpack map（字符串键），含 lastname/firstname + 干扰键（无 fullname）</summary>
    private static byte[] BuildKkNameBlock(string lastName, string firstName)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new MessagePackWriter(buffer);
        w.WriteMapHeader(3);
        w.Write("lastname");
        w.Write(lastName);
        w.Write("firstname");
        w.Write(firstName);
        w.Write("nickname");
        w.Write("昵称");
        w.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// KKEx msgpack：map&lt;插件ID, [version, data]&gt;；UAR data["info"] = array of bin，
    /// 每个 bin 是一条 ResolveInfo msgpack map（"ModID"/"Slot"）。otherPluginGuids 写入
    /// 无关插件（含同名 "ModID" 键），验证命名空间隔离。
    /// </summary>
    private static byte[] BuildKkexBlock(string[] guids, string[]? otherPluginGuids = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new MessagePackWriter(buffer);
        w.WriteMapHeader(otherPluginGuids is { Length: > 0 } ? 2 : 1);
        w.Write("com.bepis.sideloader.universalautoresolver");
        w.WriteArrayHeader(2);
        w.Write(2); // plugin data version
        w.WriteMapHeader(1);
        w.Write("info");
        w.WriteArrayHeader(guids.Length);
        foreach (var guid in guids)
            w.Write(BuildResolveInfo(guid));
        if (otherPluginGuids is { Length: > 0 })
        {
            w.Write("com.other.plugin");
            w.WriteArrayHeader(2);
            w.Write(1);
            w.WriteMapHeader(1);
            w.Write("info");
            w.WriteArrayHeader(otherPluginGuids.Length);
            foreach (var guid in otherPluginGuids)
                w.Write(BuildResolveInfo(guid));
        }
        w.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>ResolveInfo：msgpack map（字符串键）"ModID"/"Slot"</summary>
    private static byte[] BuildResolveInfo(string guid)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new MessagePackWriter(buffer);
        w.WriteMapHeader(2);
        w.Write("ModID");
        w.Write(guid);
        w.Write("Slot");
        w.Write(200001);
        w.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>KKEx trailer：7bit 前缀 "KKEx" + int32 version + int32 length + msgpack map</summary>
    private static byte[] BuildKkexTrailer(string[] guids)
    {
        var payload = BuildKkexBlock(guids);
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write("KKEx");         // 7bit 前缀字符串
        bw.Write(2);              // int32 version
        bw.Write(payload.Length); // int32 length
        bw.Write(payload);
        bw.Flush();
        return ms.ToArray();
    }

    public static string WritePng(string dir, string name, params byte[][] parts)
    {
        var path = Path.Combine(dir, name);
        using var ms = new MemoryStream();
        foreach (var p in parts)
            ms.Write(p);
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    // ==================== zipmod 夹具 ====================

    public static string MakeManifest(string guid, string name = "Test Mod", string version = "1.0.0") =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<manifest>\n<guid>{guid}</guid>\n<name>{name}</name>\n<version>{version}</version>\n</manifest>";

    public static string WriteZipmod(
        string dir, string fileName, string manifest,
        bool deflate = true, string entryName = "manifest.xml")
    {
        var path = Path.Combine(dir, fileName);
        using var fs = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName, deflate ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
        using (var writer = new StreamWriter(entry.Open()))
            writer.Write(manifest);
        return path;
    }

    /// <summary>生成带大量 dummy 条目的 zipmod：中央目录 > 16KB 以强制多块渐进抓取，但 < 256KB 窗口上限</summary>
    public static string WriteBigZipmod(string dir, string fileName, string manifest, int dummyEntries)
    {
        var path = Path.Combine(dir, fileName);
        using var fs = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        var entry = archive.CreateEntry("manifest.xml", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(entry.Open()))
            writer.Write(manifest);

        for (var i = 0; i < dummyEntries; i++)
        {
            var e = archive.CreateEntry(
                $"dummy/file_{i:D5}_with_a_fairly_long_name_to_bulk_up_the_central_directory.bin",
                CompressionLevel.NoCompression);
            using var s = e.Open();
            s.WriteByte(0x42);
        }
        return path;
    }

    /// <summary>在内存中构建 zip，返回字节</summary>
    public static byte[] BuildZipBytes(params (string Name, byte[] Content, bool Deflate)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content, deflate) in entries)
            {
                var entry = archive.CreateEntry(name, deflate ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
                using var s = entry.Open();
                s.Write(content);
            }
        }
        return ms.ToArray();
    }

    /// <summary>给 zip 字节追加 EOCD comment</summary>
    public static byte[] AddZipComment(byte[] zip, string comment)
    {
        var commentBytes = Encoding.UTF8.GetBytes(comment);
        var result = new byte[zip.Length + commentBytes.Length];
        Array.Copy(zip, result, zip.Length);
        Array.Copy(commentBytes, 0, result, zip.Length, commentBytes.Length);

        var eocd = FindEocdOffset(result);
        if (eocd < 0)
            throw new InvalidOperationException("EOCD not found");
        BitConverter.GetBytes((ushort)commentBytes.Length).CopyTo(result, eocd + 20);
        return result;
    }

    public static int FindEocdOffset(byte[] data)
    {
        for (var i = data.Length - 22; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(data, i) == 0x06054b50)
                return i;
        }
        return -1;
    }

    /// <summary>同步版 IProgress（测试断言用，避免 Progress&lt;T&gt; 的异步投递）</summary>
    internal sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;
        public SyncProgress(Action<T> onReport) => _onReport = onReport;
        public void Report(T value) => _onReport(value);
    }
}

/// <summary>记录的请求信息</summary>
internal record RequestLog(string Method, string Path, string? UserAgent, string? Range);

/// <summary>本地 HTTP 测试服务器（支持 Range / HEAD / 慢速流式响应）</summary>
internal sealed class TestHttpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, Func<HttpListenerRequest, HttpListenerResponse, Task>> _routes = new();

    public TestHttpServer()
    {
        Port = GetFreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public int Port { get; }
    public string BaseUrl => $"http://127.0.0.1:{Port}/";
    public ConcurrentBag<RequestLog> Requests { get; } = new();

    public void MapHtml(string path, string html) =>
        _routes[path] = async (req, resp) => await WriteBytes(req, resp, 200, Encoding.UTF8.GetBytes(html));

    public void MapFile(string path, byte[] data, bool supportRange = true) =>
        _routes[path] = (req, resp) => ServeFile(req, resp, data, supportRange);

    public void MapStatus(string path, int status) =>
        _routes[path] = (req, resp) =>
        {
            resp.StatusCode = status;
            resp.Close();
            return Task.CompletedTask;
        };

    /// <summary>慢速分块响应（用于取消测试）</summary>
    public void MapSlow(string path, byte[] data, int chunkSize, int delayMs) =>
        _routes[path] = async (req, resp) =>
        {
            resp.StatusCode = 200;
            resp.SendChunked = true;
            for (var off = 0; off < data.Length; off += chunkSize)
            {
                var len = Math.Min(chunkSize, data.Length - off);
                await resp.OutputStream.WriteAsync(data.AsMemory(off, len));
                await resp.OutputStream.FlushAsync();
                await Task.Delay(delayMs);
            }
            resp.Close();
        };

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _cts.Dispose();
    }

    private static int GetFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                break;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    var path = ctx.Request.Url!.AbsolutePath;
                    Requests.Add(new RequestLog(
                        ctx.Request.HttpMethod, path,
                        ctx.Request.Headers["User-Agent"], ctx.Request.Headers["Range"]));

                    if (_routes.TryGetValue(path, out var handler))
                        await handler(ctx.Request, ctx.Response);
                    else
                    {
                        ctx.Response.StatusCode = 404;
                        ctx.Response.Close();
                    }
                }
                catch
                {
                    try { ctx.Response.Abort(); } catch { }
                }
            });
        }
    }

    private static async Task WriteBytes(HttpListenerRequest req, HttpListenerResponse resp, int status, byte[] data)
    {
        resp.StatusCode = status;
        resp.ContentLength64 = data.Length;
        if (req.HttpMethod != "HEAD")
            await resp.OutputStream.WriteAsync(data);
        resp.Close();
    }

    private static async Task ServeFile(HttpListenerRequest req, HttpListenerResponse resp, byte[] data, bool supportRange)
    {
        if (req.HttpMethod == "HEAD")
        {
            resp.StatusCode = 200;
            resp.ContentLength64 = data.Length;
            resp.Close();
            return;
        }

        var rangeHeader = req.Headers["Range"];
        var m = rangeHeader != null ? Regex.Match(rangeHeader, @"^bytes=(\d+)-(\d*)$") : null;
        if (supportRange && m is { Success: true })
        {
            var start = long.Parse(m.Groups[1].Value);
            var end = m.Groups[2].Value == "" ? data.Length - 1 : long.Parse(m.Groups[2].Value);
            end = Math.Min(end, data.Length - 1);
            var len = (int)(end - start + 1);
            if (len < 0)
                len = 0;
            resp.StatusCode = 206;
            resp.Headers["Content-Range"] = $"bytes {start}-{end}/{data.Length}";
            resp.ContentLength64 = len;
            if (len > 0)
                await resp.OutputStream.WriteAsync(data.AsMemory((int)start, len));
            resp.Close();
        }
        else
        {
            await WriteBytes(req, resp, 200, data);
        }
    }
}
