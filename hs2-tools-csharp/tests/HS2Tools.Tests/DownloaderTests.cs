using HS2Tools.Models;
using HS2Tools.Services;
using static HS2Tools.Tests.TestAssets;

namespace HS2Tools.Tests;

public class DownloaderTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private readonly TestHttpServer _server = new();

    public void Dispose()
    {
        _server.Dispose();
        DeleteDir(_dir);
    }

    private static byte[] Payload(int size)
    {
        var rnd = new Random(42);
        var data = new byte[size];
        rnd.NextBytes(data);
        return data;
    }

    [Fact]
    public async Task Download_Full_WritesFileAndCompletes()
    {
        var payload = Payload(100 * 1024);
        _server.MapFile("/file.zipmod", payload);
        var output = Path.Combine(_dir, "out.zipmod");
        var messages = new List<DownloadProgress>();

        await new DownloaderService().DownloadAsync(
            _server.BaseUrl + "file.zipmod", output, resume: true,
            new SyncProgress<DownloadProgress>(messages.Add));

        Assert.Equal(payload, File.ReadAllBytes(output));
        var complete = Assert.Single(messages, m => m.Type == DownloadMessageType.Complete);
        Assert.Equal(payload.Length, complete.Total);
        Assert.Equal(output, complete.Path);
        Assert.Contains(messages, m => m.Type == DownloadMessageType.Progress);
    }

    [Fact]
    public async Task Download_Resume_SendsRangeAndAppends()
    {
        var payload = Payload(100 * 1024);
        _server.MapFile("/file.zipmod", payload, supportRange: true);
        var output = Path.Combine(_dir, "out.zipmod");

        // 预置前 100 字节（模拟已下载部分）
        File.WriteAllBytes(output, payload[..100]);

        await new DownloaderService().DownloadAsync(
            _server.BaseUrl + "file.zipmod", output, resume: true,
            new SyncProgress<DownloadProgress>(_ => { }));

        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.Contains(_server.Requests, r => r.Range == "bytes=100-");
    }

    [Fact]
    public async Task Download_ServerWithoutRangeSupport_Restarts()
    {
        var payload = Payload(50 * 1024);
        _server.MapFile("/file.zipmod", payload, supportRange: false);
        var output = Path.Combine(_dir, "out.zipmod");

        // 预置垃圾部分文件
        File.WriteAllBytes(output, new byte[200]);

        var messages = new List<DownloadProgress>();
        await new DownloaderService().DownloadAsync(
            _server.BaseUrl + "file.zipmod", output, resume: true,
            new SyncProgress<DownloadProgress>(messages.Add));

        // 服务器返回 200 时 startPos 被重置并整文件重写（原版 "restarting" 提示为不可达死代码，不断言）
        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.Contains(_server.Requests, r => r.Range == "bytes=200-");
    }

    [Fact]
    public async Task Download_NoResume_IgnoresExistingFile()
    {
        var payload = Payload(10 * 1024);
        _server.MapFile("/file.zipmod", payload);
        var output = Path.Combine(_dir, "out.zipmod");
        File.WriteAllBytes(output, new byte[500]);

        await new DownloaderService().DownloadAsync(
            _server.BaseUrl + "file.zipmod", output, resume: false);

        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.DoesNotContain(_server.Requests, r => r.Range != null);
    }

    [Fact]
    public async Task Download_HttpError_Throws()
    {
        _server.MapStatus("/missing.zipmod", 404);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new DownloaderService().DownloadAsync(_server.BaseUrl + "missing.zipmod", Path.Combine(_dir, "x.zipmod"), true));
        Assert.Contains("server returned status 404", ex.Message);
    }

    [Fact]
    public async Task Download_SendsUserAgent()
    {
        _server.MapFile("/file.zipmod", Payload(1024));
        await new DownloaderService().DownloadAsync(
            _server.BaseUrl + "file.zipmod", Path.Combine(_dir, "x.zipmod"), false);
        Assert.Contains(_server.Requests, r => r.UserAgent == "hs2-tools-downloader/1.0");
    }

    [Fact]
    public async Task Download_Cancel_ThrowsCancelled()
    {
        var payload = Payload(2 * 1024 * 1024);
        _server.MapSlow("/slow.zipmod", payload, chunkSize: 4096, delayMs: 20);
        using var cts = new CancellationTokenSource(150);

        await Assert.ThrowsAsync<DownloadCancelledException>(() =>
            new DownloaderService().DownloadAsync(
                _server.BaseUrl + "slow.zipmod", Path.Combine(_dir, "x.zipmod"), false, null, cts.Token));
    }

    [Theory]
    [InlineData("bytes 0-99/200", 200)]
    [InlineData("bytes 100-199/1024", 1024)]
    [InlineData("bytes 0-999/12345", 12345)]
    [InlineData("bytes 0-99/*", -1)]
    [InlineData("", -1)]
    [InlineData("garbage", -1)]
    public void ParseContentRangeTotal_Cases(string input, long expected)
    {
        Assert.Equal(expected, DownloaderService.ParseContentRangeTotal(input));
    }

    [Fact]
    public void Ctor_InvalidProxy_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DownloaderService("http://bad url with spaces/"));
    }
}
