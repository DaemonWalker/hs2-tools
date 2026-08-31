using System.Text;
using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class DownloadWindowViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private static byte[] ZipBytes() => TestAssets.BuildZipBytes(
        ("manifest.xml", Encoding.UTF8.GetBytes(TestAssets.MakeManifest("x")), true));

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not met in time");
            await Task.Delay(20);
        }
    }

    private DownloadTaskItemViewModel Item(DownloadWindowViewModel vm, string id) =>
        vm.AllTasks.Single(i => i.Id == id);

    // ==================== 事件驱动与统计 ====================

    [Fact]
    public async Task TaskAdded_SameId_ReplacesRow_NoDuplicate()
    {
        using var server = new TestHttpServer();
        server.MapStatus("/m/bad.zipmod", 404);
        server.MapFile("/m/good.zipmod", ZipBytes());

        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = new DownloadWindowViewModel(downloads);
        downloads.StartDownload("dup", "m/bad.zipmod", _dir);
        await WaitFor(() => Item(vm, "dup").Status == DownloadTaskStatus.Failed);

        // 终态同名任务重新触发：管理器替换任务并重发 TaskAdded（阶段 4：列表按 Id 去重）
        downloads.StartDownload("dup", "m/good.zipmod", _dir);
        await WaitFor(() => Item(vm, "dup").Status == DownloadTaskStatus.Completed);

        Assert.Equal(1, vm.TotalCount);
        Assert.Single(vm.AllTasks);
        Assert.Single(vm.CompletedTasks);
    }

    [Fact]
    public async Task Tasks_FlowIntoTabs_AndStatisticsAddUp()
    {
        using var server = new TestHttpServer();
        server.MapSlow("/m/slow.zipmod", ZipBytes(), chunkSize: 10, delayMs: 50); // 保持下载中
        server.MapFile("/m/fast.zipmod", ZipBytes());

        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = new DownloadWindowViewModel(downloads);

        downloads.StartDownload("slow", "m/slow.zipmod", _dir);
        downloads.StartDownload("fast", "m/fast.zipmod", _dir);

        // TaskAdded 事件同步到达（无需等待网络）
        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(2, vm.ActiveCount);
        Assert.Equal(2, vm.AllTasks.Count);
        Assert.Equal(2, vm.ActiveTasks.Count);
        Assert.False(vm.IsAllEmpty);

        await WaitFor(() => Item(vm, "fast").Status == DownloadTaskStatus.Completed);

        Assert.Equal(1, vm.ActiveCount);
        Assert.Equal(1, vm.CompletedCount);
        Assert.Equal(0, vm.FailedCount);
        Assert.Single(vm.ActiveTasks);
        Assert.Single(vm.CompletedTasks);
        Assert.Empty(vm.FailedTasks);
        Assert.True(vm.ShowCancelAll);

        // 总速度 = 活跃任务 Speed 求和；排序：下载中优先
        Assert.Equal("slow", vm.AllTasks[0].Id);
        Assert.True(vm.TotalSpeed >= 0);
        Assert.EndsWith("/s", vm.TotalSpeedText);

        downloads.CancelAll(); // 清理慢速任务
        await WaitFor(() => Item(vm, "slow").Status == DownloadTaskStatus.Cancelled);
    }

    [Fact]
    public async Task Progress_UpdatesItemFieldsWithoutResorting()
    {
        using var server = new TestHttpServer();
        server.MapSlow("/m/slow.zipmod", ZipBytes(), chunkSize: 10, delayMs: 20);

        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = new DownloadWindowViewModel(downloads);
        downloads.StartDownload("slow", "m/slow.zipmod", _dir);

        await WaitFor(() => Item(vm, "slow").Downloaded > 0);

        var item = Item(vm, "slow");
        Assert.Equal(DownloadTaskStatus.Downloading, item.Status);
        Assert.Contains("·", item.StatusText); // 大小 · 百分比 · 速度
        Assert.True(item.ShowCancel);
        Assert.False(item.ShowRetry);

        downloads.CancelAll();
        await WaitFor(() => item.Status == DownloadTaskStatus.Cancelled);
    }

    // ==================== 取消 / 重试 / 全部取消 / 清除完成 ====================

    [Fact]
    public async Task Cancel_ThenRetry_RestartsDownload()
    {
        using var server = new TestHttpServer();
        server.MapSlow("/m/slow.zipmod", ZipBytes(), chunkSize: 10, delayMs: 30);

        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = new DownloadWindowViewModel(downloads);
        downloads.StartDownload("slow", "m/slow.zipmod", _dir);

        vm.CancelCommand.Execute(Item(vm, "slow"));
        await WaitFor(() => Item(vm, "slow").Status == DownloadTaskStatus.Cancelled);

        var item = Item(vm, "slow");
        Assert.Equal("已取消", item.StatusText);
        Assert.True(item.ShowRetry);
        // 原版口径：失败 Tab 仅含 Failed，已取消不进失败 Tab
        Assert.Empty(vm.FailedTasks);
        Assert.Single(vm.AllTasks); // 已取消仍在「全部」Tab

        vm.RetryCommand.Execute(item);
        await WaitFor(() => Item(vm, "slow").Status == DownloadTaskStatus.Downloading);
        Assert.Single(vm.ActiveTasks); // 重试后回到下载中 Tab

        // 重试用断点续传，最终能完成
        await WaitFor(() => Item(vm, "slow").Status == DownloadTaskStatus.Completed);
        Assert.True(File.Exists(Path.Combine(_dir, "slow.zipmod")));
    }

    [Fact]
    public async Task CancelAll_AndClearFinished()
    {
        using var server = new TestHttpServer();
        server.MapSlow("/m/a.zipmod", ZipBytes(), chunkSize: 10, delayMs: 50);
        server.MapSlow("/m/b.zipmod", ZipBytes(), chunkSize: 10, delayMs: 50);

        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = new DownloadWindowViewModel(downloads);
        downloads.StartDownload("a", "m/a.zipmod", _dir);
        downloads.StartDownload("b", "m/b.zipmod", _dir);

        vm.CancelAllCommand.Execute(null);
        await WaitFor(() => vm.ActiveCount == 0 && vm.TotalCount == 2);

        Assert.False(vm.ShowCancelAll);
        Assert.True(vm.ShowClearFinished); // 存在非下载中任务 → 可清除

        vm.ClearFinishedCommand.Execute(null);
        Assert.Equal(0, vm.TotalCount);
        Assert.Empty(vm.AllTasks);
        Assert.True(vm.IsAllEmpty);
        Assert.False(vm.ShowClearFinished);
    }

    [Fact]
    public async Task FailedDownload_GoesToFailedTab_WithErrorText()
    {
        using var server = new TestHttpServer();
        server.MapStatus("/m/bad.zipmod", 404);

        var downloads = new DownloadManager(null, server.BaseUrl);
        var vm = new DownloadWindowViewModel(downloads);
        downloads.StartDownload("bad", "m/bad.zipmod", _dir);

        await WaitFor(() => Item(vm, "bad").Status == DownloadTaskStatus.Failed);

        Assert.Equal(1, vm.FailedCount);
        Assert.Single(vm.FailedTasks);
        var item = Item(vm, "bad");
        Assert.NotEqual("下载失败", item.StatusText); // 带真实错误信息
        Assert.NotEmpty(item.StatusText);
        Assert.True(item.ShowRetry);
        Assert.True(item.IsFailed);
    }

    // ==================== 排序与状态文本（纯函数） ====================

    [Fact]
    public void CompareItems_DownloadingFirst_ThenNewestFirst()
    {
        var old = new DownloadTaskItemViewModel { Id = "old", CreatedAt = new DateTime(2026, 1, 1) };
        old.Update(MakeTask("old", DownloadTaskStatus.Completed));
        var newer = new DownloadTaskItemViewModel { Id = "new", CreatedAt = new DateTime(2026, 1, 2) };
        newer.Update(MakeTask("new", DownloadTaskStatus.Completed));
        var downloading = new DownloadTaskItemViewModel { Id = "dl", CreatedAt = new DateTime(2026, 1, 1) };
        downloading.Update(MakeTask("dl", DownloadTaskStatus.Downloading));

        Assert.True(DownloadWindowViewModel.CompareItems(downloading, newer) < 0); // 下载中优先
        Assert.True(DownloadWindowViewModel.CompareItems(newer, old) < 0); // 时间倒序
        Assert.True(DownloadWindowViewModel.CompareItems(old, newer) > 0);
    }

    [Fact]
    public void StatusText_MatchesOriginalFormat()
    {
        var item = new DownloadTaskItemViewModel { Id = "t", CreatedAt = DateTime.Now };
        item.Update(MakeTask("t", DownloadTaskStatus.Downloading, downloaded: 512, total: 2048, speed: 256));
        Assert.Equal("512 B / 2 KB · 25% · 256 B/s · 00:06", item.StatusText);

        // Total 未知（-1）：只显示已下载，无剩余时间
        item.Update(MakeTask("t", DownloadTaskStatus.Downloading, downloaded: 512, total: -1, speed: 0));
        Assert.Equal("512 B · 0% · 0 B/s", item.StatusText);

        item.Update(MakeTask("t", DownloadTaskStatus.Completed, total: 2048));
        Assert.Equal("2 KB", item.StatusText);

        item.Update(MakeTask("t", DownloadTaskStatus.Failed, error: "boom"));
        Assert.Equal("boom", item.StatusText);

        item.Update(MakeTask("t", DownloadTaskStatus.Cancelled));
        Assert.Equal("已取消", item.StatusText);
    }

    private static DownloadTask MakeTask(
        string id, DownloadTaskStatus status,
        long downloaded = 0, long total = -1, double speed = 0, string? error = null)
    {
        var percent = total > 0 ? (double)downloaded / total * 100 : 0;
        return new DownloadTask
        {
            Id = id,
            Url = "http://localhost/x",
            OutputPath = "x.zipmod",
            Status = status,
            Downloaded = downloaded,
            Total = total,
            Speed = speed,
            Percent = percent,
            ErrorMessage = error,
        };
    }
}
