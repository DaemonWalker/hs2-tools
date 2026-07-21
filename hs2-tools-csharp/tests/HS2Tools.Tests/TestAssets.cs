using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HS2Tools.Tests;

/// <summary>测试辅助：临时目录、合成 PNG / zipmod 夹具</summary>
internal static class TestAssets
{
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

    /// <summary>带角色名标记的游戏数据</summary>
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
