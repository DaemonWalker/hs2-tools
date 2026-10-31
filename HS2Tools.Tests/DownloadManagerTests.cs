using HS2Tools.Models;
using HS2Tools.Services;
using static HS2Tools.Tests.TestAssets;

namespace HS2Tools.Tests;

public class DownloadManagerTests : IDisposable
{
    private readonly string _dir = NewTempDir();

    public void Dispose() => DeleteDir(_dir);

    [Fact]
    public void SanitizeFileName_SafeGuid_Unchanged()
    {
        Assert.Equal("com.bepis.sideloader.mod", DownloadManager.SanitizeFileName("com.bepis.sideloader.mod"));
    }

    [Fact]
    public void SanitizeFileName_IllegalChars_Replaced()
    {
        // 真实 GUID：DL_SDS_SJN_https://pixiv.me/darklili（: / 均为 Windows 非法字符）
        var result = DownloadManager.SanitizeFileName("DL_SDS_SJN_https://pixiv.me/darklili");

        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("\\", result);
        Assert.StartsWith("DL_SDS_SJN_https", result);
    }

    [Fact]
    public void SanitizeFileName_PathTraversal_Neutralized()
    {
        var result = DownloadManager.SanitizeFileName("..\\..\\evil");

        Assert.DoesNotContain("\\", result);
        Assert.False(result is "." or "..");
    }

    [Fact]
    public void SanitizeFileName_ReservedNames_Prefixed()
    {
        Assert.StartsWith("_", DownloadManager.SanitizeFileName("CON"));
        Assert.StartsWith("_", DownloadManager.SanitizeFileName("nul.zipmod"));
        Assert.StartsWith("_", DownloadManager.SanitizeFileName("COM1"));
    }

    [Fact]
    public void SanitizeFileName_DifferentGuids_SameSanitized_Base_GetDistinctNames()
    {
        // 清洗后基础名相同的不同 GUID：哈希后缀保证不重名（否则两个任务写同一文件）
        var a = DownloadManager.SanitizeFileName("mod/a");
        var b = DownloadManager.SanitizeFileName("mod:a");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SanitizeFileName_LongName_Truncated()
    {
        var result = DownloadManager.SanitizeFileName(new string('x', 300) + "/");
        Assert.True(result.Length <= 120); // 100 截断 + 哈希后缀
    }

    [Fact]
    public void StartDownload_IllegalGuid_WritesInsideTargetDir()
    {
        // baseUrl 指向不可达地址：只验证入队与落盘路径，不发真实外网请求
        var downloads = new DownloadManager(baseUrl: "http://127.0.0.1:1/");
        var guid = "DL_SDS_SJN_https://pixiv.me/darklili";

        Assert.True(downloads.StartDownload(guid, "dir/mod.zipmod", _dir));
        var task = downloads.Tasks.Single(t => t.Id == guid);

        // 任务 Id 保留原 GUID（展示/去重），落盘路径清洗且仍在目标目录内
        Assert.StartsWith(_dir, task.OutputPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.GetDirectoryName(task.OutputPath), _dir);
        Assert.EndsWith(".zipmod", task.OutputPath);
        downloads.CancelAll();
    }

    [Fact]
    public async Task Download_ValidZipmod_Completes()
    {
        using var server = new TestHttpServer();
        var bytes = File.ReadAllBytes(WriteZipmod(_dir, "ok.zipmod", MakeManifest("com.test.ok"), deflate: false));
        server.MapFile("/ok.zipmod", bytes);

        var downloads = new DownloadManager(baseUrl: server.BaseUrl);
        var finished = new TaskCompletionSource();
        DownloadTask? task = null;
        downloads.TaskFinished += (_, t) =>
        {
            if (t.Status == DownloadTaskStatus.Completed)
            {
                task = t;
                finished.TrySetResult();
            }
        };

        downloads.StartDownload("ok", "ok.zipmod", _dir);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.NotNull(task);
        Assert.True(File.Exists(task!.OutputPath)); // 校验通过，文件保留
    }

    [Fact]
    public async Task Download_HtmlErrorPage_FailsValidation_AndDeletesFile()
    {
        using var server = new TestHttpServer();
        server.MapHtml("/bad.zipmod", "<html>404 Not Found</html>"); // 200 的 HTML 错误页

        var downloads = new DownloadManager(baseUrl: server.BaseUrl);
        var finished = new TaskCompletionSource();
        DownloadTask? task = null;
        downloads.TaskFinished += (_, t) =>
        {
            if (t.Status == DownloadTaskStatus.Failed)
            {
                task = t;
                finished.TrySetResult();
            }
        };

        downloads.StartDownload("bad", "bad.zipmod", _dir);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.NotNull(task);
        Assert.Contains("校验失败", task!.ErrorMessage);
        Assert.False(File.Exists(task.OutputPath)); // 损坏文件不留在 mods 目录
    }
}
