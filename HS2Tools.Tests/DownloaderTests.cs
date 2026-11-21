using System.Text.RegularExpressions;
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
            new SyncProgress<DownloadProgress>(messages.Add), TestContext.Current.CancellationToken);

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
            new SyncProgress<DownloadProgress>(_ => { }), TestContext.Current.CancellationToken);

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
            new SyncProgress<DownloadProgress>(messages.Add), TestContext.Current.CancellationToken);

        // 服务器返回 200 时 startPos 被重置并整文件重写（原版 "restarting" 提示为不可达死代码，不断言）
        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.Contains(_server.Requests, r => r.Range == "bytes=200-");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task Download_ResumeRangeNotSatisfiable_Restarts(int extraBytes)
    {
        var payload = Payload(50 * 1024);
        _server.MapFile("/file.zipmod", payload, supportRange: true);
        var output = Path.Combine(_dir, "out.zipmod");
        File.WriteAllBytes(output, payload.Concat(new byte[extraBytes]).ToArray());

        await new DownloaderService().DownloadAsync(
            _server.BaseUrl + "file.zipmod", output, resume: true,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.Contains(_server.Requests, r => r.Range == $"bytes={payload.Length + extraBytes}-");
        Assert.Contains(_server.Requests, r => r.Path == "/file.zipmod" && r.Range == null);
        Assert.Equal(2, _server.Requests.Count(r => r.Path == "/file.zipmod"));
    }

    [Fact]
    public async Task Download_NoResume_IgnoresExistingFile()
    {
        var payload = Payload(10 * 1024);
        _server.MapFile("/file.zipmod", payload);
        var output = Path.Combine(_dir, "out.zipmod");
        File.WriteAllBytes(output, new byte[500]);

        await new DownloaderService().DownloadAsync(
            _server.BaseUrl + "file.zipmod", output, resume: false,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.DoesNotContain(_server.Requests, r => r.Range != null);
    }

    [Fact]
    public async Task Download_LargeFile_UsesSegmentsAndWritesExactFile()
    {
        const int segmentSize = 32 * 1024;
        var payload = Payload(segmentSize * 2 + 123);
        _server.MapFile("/large.zipmod", payload);
        var output = Path.Combine(_dir, "large.zipmod");
        var messages = new List<DownloadProgress>();

        await new DownloaderService(null, segmentSize, 4).DownloadAsync(
            _server.BaseUrl + "large.zipmod", output, resume: true,
            new SyncProgress<DownloadProgress>(messages.Add), TestContext.Current.CancellationToken);

        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.Contains(_server.Requests, r => r.Range == "bytes=0-0");
        Assert.Contains(_server.Requests, r => r.Range == $"bytes=0-{segmentSize - 1}");
        Assert.Contains(_server.Requests, r => r.Range == $"bytes={segmentSize}-{segmentSize * 2 - 1}");
        Assert.Contains(_server.Requests, r => r.Range == $"bytes={segmentSize * 2}-{payload.Length - 1}");
        Assert.Contains(messages, m => m.Type == DownloadMessageType.Progress
            && m.Total == payload.Length && m.Downloaded > 0);
        Assert.Equal(payload.Length, Assert.Single(messages, m => m.Type == DownloadMessageType.Complete).Total);
        Assert.False(File.Exists(output + ".part"));
        Assert.False(File.Exists(output + ".part.json"));
    }

    [Fact]
    public async Task Download_LargeFile_RespectsConcurrencyLimit()
    {
        const int segmentSize = 16 * 1024;
        var payload = Payload(segmentSize * 8);
        _server.MapFile("/limited.zipmod", payload, delayMs: 10);
        var output = Path.Combine(_dir, "limited.zipmod");

        await new DownloaderService(null, segmentSize, 2).DownloadAsync(
            _server.BaseUrl + "limited.zipmod", output, false,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.InRange(_server.MaxConcurrentSegmentDownloads, 1, 2);
    }

    [Fact]
    public async Task Download_LargeFileWithoutRangeSupport_FallsBackToSingleStream()
    {
        const int segmentSize = 32 * 1024;
        var payload = Payload(segmentSize * 2 + 17);
        _server.MapFile("/fallback.zipmod", payload, supportRange: false);
        var output = Path.Combine(_dir, "fallback.zipmod");

        await new DownloaderService(null, segmentSize, 4).DownloadAsync(
            _server.BaseUrl + "fallback.zipmod", output, true,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.Contains(_server.Requests, r => r.Range == "bytes=0-0");
        Assert.Contains(_server.Requests, r => r.Method == "GET" && r.Range == null);
        Assert.False(File.Exists(output + ".part"));
    }

    [Fact]
    public async Task Download_SegmentRangeIgnoredAfterProbe_RestartsWithSingleStream()
    {
        const int segmentSize = 32 * 1024;
        var payload = Payload(segmentSize * 2);
        _server.MapFileWithExplicitRangeIgnored("/mid-fallback.zipmod", payload);
        var output = Path.Combine(_dir, "mid-fallback.zipmod");

        await new DownloaderService(null, segmentSize, 4).DownloadAsync(
            _server.BaseUrl + "mid-fallback.zipmod", output, true,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.Contains(_server.Requests, r => r.Range == $"bytes=0-{segmentSize - 1}");
        Assert.Contains(_server.Requests, r => r.Method == "GET" && r.Range == null);
        Assert.False(File.Exists(output + ".part"));
        Assert.False(File.Exists(output + ".part.json"));
    }

    [Fact]
    public async Task Download_SegmentedCancel_RetryResumesConfirmedOffsets()
    {
        const int segmentSize = 32 * 1024;
        var payload = Payload(segmentSize * 3);
        _server.MapFile("/resume.zipmod", payload, delayMs: 30);
        var output = Path.Combine(_dir, "resume.zipmod");
        using var cts = new CancellationTokenSource(150);

        await Assert.ThrowsAsync<DownloadCancelledException>(() =>
            new DownloaderService(null, segmentSize, 4).DownloadAsync(
                _server.BaseUrl + "resume.zipmod", output, true, null, cts.Token));

        Assert.True(File.Exists(output + ".part"));
        Assert.True(File.Exists(output + ".part.json"));
        var priorRequests = _server.Requests.ToHashSet();
        _server.MapFile("/resume.zipmod", payload);

        await new DownloaderService(null, segmentSize, 4).DownloadAsync(
            _server.BaseUrl + "resume.zipmod", output, true,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(payload, File.ReadAllBytes(output));
        Assert.Contains(_server.Requests.Where(r => !priorRequests.Contains(r)), r =>
        {
            var match = Regex.Match(r.Range ?? "", @"^bytes=(\d+)-");
            return match.Success && long.Parse(match.Groups[1].Value) % segmentSize != 0;
        });
        Assert.False(File.Exists(output + ".part"));
        Assert.False(File.Exists(output + ".part.json"));
    }

    [Fact]
    public async Task Download_SegmentedValidation_RunsBeforeCommit()
    {
        const int segmentSize = 16 * 1024;
        var payload = Payload(segmentSize * 2);
        _server.MapFile("/validated.zipmod", payload);
        var output = Path.Combine(_dir, "validated.zipmod");
        var validatedPath = "";

        await new DownloaderService(null, segmentSize, 2).DownloadWithValidationAsync(
            _server.BaseUrl + "validated.zipmod", output, false, path =>
            {
                validatedPath = path;
                Assert.Equal(output + ".part", path);
                Assert.False(File.Exists(output));
                Assert.Equal(payload, File.ReadAllBytes(path));
            }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(output + ".part", validatedPath);
        Assert.Equal(payload, File.ReadAllBytes(output));
    }

    [Fact]
    public async Task Download_SegmentedValidationFailure_DeletesArtifactsWithoutCommit()
    {
        const int segmentSize = 16 * 1024;
        var payload = Payload(segmentSize * 2);
        _server.MapFile("/invalid.zipmod", payload);
        var output = Path.Combine(_dir, "invalid.zipmod");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new DownloaderService(null, segmentSize, 2).DownloadWithValidationAsync(
                _server.BaseUrl + "invalid.zipmod", output, false,
                _ => throw new InvalidDataException("bad zip"), ct: TestContext.Current.CancellationToken));

        Assert.False(File.Exists(output));
        Assert.False(File.Exists(output + ".part"));
        Assert.False(File.Exists(output + ".part.json"));
    }

    [Fact]
    public async Task Download_HttpError_Throws()
    {
        _server.MapStatus("/missing.zipmod", 404);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new DownloaderService().DownloadAsync(_server.BaseUrl + "missing.zipmod",
                Path.Combine(_dir, "x.zipmod"), true, ct: TestContext.Current.CancellationToken));
        Assert.Contains("server returned status 404", ex.Message);
    }

    [Fact]
    public async Task Download_SendsUserAgent()
    {
        _server.MapFile("/file.zipmod", Payload(1024));
        await new DownloaderService().DownloadAsync(
            _server.BaseUrl + "file.zipmod", Path.Combine(_dir, "x.zipmod"), false,
            ct: TestContext.Current.CancellationToken);
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
