using HS2Tools.Models;
using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class SceneOrganizeViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private string MakeGameDir()
    {
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative));
        File.WriteAllText(Path.Combine(gameDir, GameProfiles.Hs2.GameExeName), "exe");
        return gameDir;
    }

    private string WriteScene(string gameDir, string fileName, string charaName) =>
        TestAssets.WritePng(Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative), fileName,
            TestAssets.PngPrefix(), TestAssets.NameMarker(charaName));

    [Fact]
    public void AddTask_ParsesNamesAndClearsInputs()
    {
        using var config = new ConfigService(_dir);
        var vm = new SceneOrganizeViewModel(config, new ScannerService())
        {
            CharInput = " 角色A \n\n角色B\n",
            FolderInput = " 我的收藏 ",
        };

        Assert.True(vm.AddTaskCommand.CanExecute(null));
        vm.AddTaskCommand.Execute(null);

        var task = Assert.Single(vm.Tasks);
        Assert.Equal(["角色A", "角色B"], task.CharNames);
        Assert.Equal("我的收藏", task.FolderName);
        Assert.Equal(OrganizeTaskStatus.Pending, task.Status);
        Assert.Equal("", vm.CharInput);
        Assert.Equal("", vm.FolderInput);
    }

    [Fact]
    public async Task ExecuteTask_NoGamePath_FailsWithMessage()
    {
        using var config = new ConfigService(_dir); // 未设 GamePath
        var vm = new SceneOrganizeViewModel(config, new ScannerService())
        {
            CharInput = "任意",
            FolderInput = "合集",
        };
        vm.AddTaskCommand.Execute(null);

        await vm.ExecuteTaskCommand.ExecuteAsync(vm.Tasks[0]);

        // 阶段 4：未设路径执行整理给出明确错误（原来静默无反应）
        var task = vm.Tasks[0];
        Assert.Equal(OrganizeTaskStatus.Error, task.Status);
        Assert.Contains("未设置游戏路径", task.Error);
    }

    [Fact]
    public async Task ExecuteTask_MovesMatchingScenes_ToHsToolsFolder()
    {
        var gameDir = MakeGameDir();
        var match1 = WriteScene(gameDir, "s1.png", "艾尔莎的日常");
        var match2 = WriteScene(gameDir, "s2.png", "艾尔与伙伴们");
        var noMatch = WriteScene(gameDir, "s3.png", "其他人");

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new SceneOrganizeViewModel(config, new ScannerService())
        {
            CharInput = "艾尔", // 子串匹配（原版 includes）
            FolderInput = "艾尔合集",
        };
        vm.AddTaskCommand.Execute(null);

        var completed = 0;
        vm.OrganizeCompleted += (_, _) => completed++;

        await vm.ExecuteTaskCommand.ExecuteAsync(vm.Tasks[0]);

        var task = vm.Tasks[0];
        Assert.Equal(OrganizeTaskStatus.Completed, task.Status);
        Assert.Equal(100, task.Progress);
        Assert.Equal(3, task.TotalScenes);
        Assert.Equal(2, task.ResultCount);
        Assert.Equal(1, completed);

        // 修复原版：移动到 hs_tools_<文件夹>/<文件名>（原版恒失败）
        var targetDir = Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative, "hs_tools_艾尔合集");
        Assert.True(File.Exists(Path.Combine(targetDir, "s1.png")));
        Assert.True(File.Exists(Path.Combine(targetDir, "s2.png")));
        Assert.False(File.Exists(match1));
        Assert.False(File.Exists(match2));
        Assert.True(File.Exists(noMatch)); // 未匹配的留在原处
    }

    [Fact]
    public async Task ExecuteTask_ExcludesAlreadyOrganizedDirs()
    {
        var gameDir = MakeGameDir();
        WriteScene(gameDir, "s1.png", "艾尔莎");
        // 已整理目录中的场景不应参与再次整理
        var organizedDir = Path.Combine(gameDir, GameProfiles.Hs2.SceneDirRelative, "hs_tools_已有");
        Directory.CreateDirectory(organizedDir);
        TestAssets.WritePng(organizedDir, "old.png", TestAssets.PngPrefix(), TestAssets.NameMarker("艾尔莎"));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new SceneOrganizeViewModel(config, new ScannerService())
        {
            CharInput = "艾尔",
            FolderInput = "新合集",
        };
        vm.AddTaskCommand.Execute(null);
        await vm.ExecuteTaskCommand.ExecuteAsync(vm.Tasks[0]);

        Assert.Equal(1, vm.Tasks[0].TotalScenes); // hs_tools_已有 被排除
        Assert.Equal(1, vm.Tasks[0].ResultCount);
        Assert.True(File.Exists(Path.Combine(organizedDir, "old.png"))); // 原处不动
    }

    [Fact]
    public async Task ExecuteTask_NoMatch_CompletesWithZero()
    {
        var gameDir = MakeGameDir();
        WriteScene(gameDir, "s1.png", "无关角色");

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new SceneOrganizeViewModel(config, new ScannerService())
        {
            CharInput = "不存在的角色",
            FolderInput = "空合集",
        };
        vm.AddTaskCommand.Execute(null);
        await vm.ExecuteTaskCommand.ExecuteAsync(vm.Tasks[0]);

        Assert.Equal(OrganizeTaskStatus.Completed, vm.Tasks[0].Status);
        Assert.Equal(0, vm.Tasks[0].ResultCount);
    }

    [Fact]
    public async Task ExecuteAll_RunsPendingTasksSerially()
    {
        var gameDir = MakeGameDir();
        WriteScene(gameDir, "s1.png", "角色甲");
        WriteScene(gameDir, "s2.png", "角色乙");

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new SceneOrganizeViewModel(config, new ScannerService())
        {
            CharInput = "角色甲",
            FolderInput = "合集甲",
        };
        vm.AddTaskCommand.Execute(null);
        vm.CharInput = "角色乙";
        vm.FolderInput = "合集乙";
        vm.AddTaskCommand.Execute(null);

        Assert.True(vm.ExecuteAllCommand.CanExecute(null));
        await vm.ExecuteAllCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.CompletedCount);
        Assert.All(vm.Tasks, t => Assert.Equal(OrganizeTaskStatus.Completed, t.Status));
        Assert.False(vm.ExecuteAllCommand.CanExecute(null)); // 无待执行任务
    }

    [Fact]
    public void RemoveTask_And_Clear()
    {
        using var config = new ConfigService(_dir);
        var vm = new SceneOrganizeViewModel(config, new ScannerService())
        {
            CharInput = "a",
            FolderInput = "f",
        };
        vm.AddTaskCommand.Execute(null);
        vm.CharInput = "b";
        vm.FolderInput = "g";
        vm.AddTaskCommand.Execute(null);
        Assert.Equal(2, vm.Tasks.Count);

        vm.RemoveTaskCommand.Execute(vm.Tasks[0]);
        Assert.Single(vm.Tasks);

        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.Tasks);
    }

    [Fact]
    public async Task SceneWindowViewModel_ReloadsPaths_AfterOrganize()
    {
        var gameDir = MakeGameDir();
        WriteScene(gameDir, "s1.png", "艾尔莎");
        WriteScene(gameDir, "s2.png", "其他人");

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.GamePath = gameDir);
        var vm = new SceneWindowViewModel(config, new ScannerService(),
            new DownloadManager(), new SideloadDatabaseService(config), new GameLauncherService(config));
        vm.LoadCardPaths();
        Assert.Equal(2, vm.AllPaths.Count);

        vm.Organize.CharInput = "艾尔";
        vm.Organize.FolderInput = "合集";
        vm.Organize.AddTaskCommand.Execute(null);
        await vm.Organize.ExecuteAllCommand.ExecuteAsync(null);

        // 整理完成后场景目录重扫（不排除已整理目录，与原版一致）：
        // s2 仍在根目录，s1 出现在 hs_tools_ 子目录中
        Assert.Equal(2, vm.AllPaths.Count);
        Assert.Contains(vm.AllPaths, p => p.EndsWith("s2.png"));
        Assert.Contains(vm.AllPaths, p => p.Contains("hs_tools_合集") && p.EndsWith("s1.png"));
    }
}
