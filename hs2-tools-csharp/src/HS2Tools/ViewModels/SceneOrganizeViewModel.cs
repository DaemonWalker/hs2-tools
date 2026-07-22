using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

public enum OrganizeTaskStatus
{
    Pending,
    Scanning,
    Analyzing,
    Moving,
    Completed,
    Error,
}

/// <summary>一个场景整理任务（对应原版 OrganizeTask）</summary>
public partial class OrganizeTaskViewModel : ObservableObject
{
    /// <summary>匹配的角色名清单（按子串匹配，大小写敏感——与原版 includes 一致）</summary>
    public required IReadOnlyList<string> CharNames { get; init; }

    /// <summary>角色名展示（逗号分隔）</summary>
    public string CharNamesText => string.Join("、", CharNames);

    [ObservableProperty]
    private string _folderName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private OrganizeTaskStatus _status = OrganizeTaskStatus.Pending;

    /// <summary>进度 0-100：0~50 扫描分析 / 50~100 移动（与原版一致）</summary>
    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _totalScenes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _processedScenes;

    /// <summary>整理成功的场景数</summary>
    [ObservableProperty]
    private int _resultCount;

    /// <summary>移动失败的场景数</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoveFailText))]
    private int _moveFailCount;

    /// <summary>完成文案的失败追加（无失败时为空串）</summary>
    public string MoveFailText => MoveFailCount > 0 ? $"（{MoveFailCount} 个失败）" : "";

    [ObservableProperty]
    private string? _error;

    public string StatusText => Status switch
    {
        OrganizeTaskStatus.Pending => "待执行",
        OrganizeTaskStatus.Scanning => "扫描中",
        OrganizeTaskStatus.Analyzing => "分析中",
        OrganizeTaskStatus.Moving => "移动中",
        OrganizeTaskStatus.Completed => "已完成",
        _ => "失败",
    };

    public bool IsPending => Status == OrganizeTaskStatus.Pending;
    public bool IsRunning => Status is OrganizeTaskStatus.Scanning or OrganizeTaskStatus.Analyzing or OrganizeTaskStatus.Moving;
    public bool IsCompleted => Status == OrganizeTaskStatus.Completed;
    public bool HasError => Status == OrganizeTaskStatus.Error;

    public string ProgressText => Status switch
    {
        OrganizeTaskStatus.Scanning => "正在扫描场景文件...",
        OrganizeTaskStatus.Analyzing => $"正在分析场景 ({ProcessedScenes}/{TotalScenes})...",
        OrganizeTaskStatus.Moving => "正在移动文件...",
        _ => "",
    };
}

/// <summary>
/// 场景智能整理（对应原版 SceneOrganizer）。
/// 按角色名包含匹配场景卡 → 移动到 scene/hs_tools_&lt;文件夹名&gt;（排除已整理目录）。
///
/// 对原版的修复：原版 moveFile(scenePath, targetFolder) 把目录当目标路径传入，
/// os.Rename(file, 已存在目录) 恒失败且错误只进 console——移动从未生效。
/// C# 版目标路径补全文件名：hs_tools_&lt;文件夹名&gt;/&lt;场景文件名&gt;。
/// </summary>
public partial class SceneOrganizeViewModel : ObservableObject
{
    /// <summary>分析阶段批大小（每批并行解析名称后更新进度）</summary>
    private const int AnalyzeBatchSize = 24;

    private readonly ConfigService _config;
    private readonly ScannerService _scanner;

    public SceneOrganizeViewModel(ConfigService config, ScannerService scanner)
    {
        _config = config;
        _scanner = scanner;
    }

    /// <summary>有任务完成（窗口据此刷新网格）</summary>
    public event EventHandler? OrganizeCompleted;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    private string _charInput = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    private string _folderInput = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private bool _isProcessing;

    public ObservableCollection<OrganizeTaskViewModel> Tasks { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompletedCount))]
    private int _tasksVersion; // Tasks 内容变化时递增以刷新统计（避免引入集合事件监听）

    public int CompletedCount => Tasks.Count(t => t.Status == OrganizeTaskStatus.Completed);

    private bool CanAddTask() =>
        !IsProcessing && !string.IsNullOrWhiteSpace(CharInput) && !string.IsNullOrWhiteSpace(FolderInput);

    /// <summary>添加任务（角色名每行一个，trim 去空——与原版一致）</summary>
    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private void AddTask()
    {
        var names = CharInput.Split('\n').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        var folder = FolderInput.Trim();
        if (names.Count == 0 || folder.Length == 0)
            return;

        Tasks.Add(new OrganizeTaskViewModel { CharNames = names, FolderName = folder });
        CharInput = "";
        FolderInput = "";
        TasksVersion++;
    }

    /// <summary>处理中禁止增删任务（按钮置灰；方法内判断作兜底）</summary>
    private bool CanModifyTasks() => !IsProcessing;

    [RelayCommand(CanExecute = nameof(CanModifyTasks))]
    private void RemoveTask(OrganizeTaskViewModel task)
    {
        if (IsProcessing)
            return;
        Tasks.Remove(task);
        TasksVersion++;
    }

    [RelayCommand(CanExecute = nameof(CanModifyTasks))]
    private void Clear()
    {
        if (IsProcessing)
            return;
        Tasks.Clear();
        TasksVersion++;
    }

    [RelayCommand]
    private async Task ExecuteTaskAsync(OrganizeTaskViewModel task)
    {
        if (IsProcessing || task.Status != OrganizeTaskStatus.Pending)
            return;
        IsProcessing = true;
        try
        {
            await RunTaskAsync(task);
        }
        finally
        {
            IsProcessing = false;
            TasksVersion++;
        }
    }

    private bool CanExecuteAll() => !IsProcessing && Tasks.Any(t => t.Status == OrganizeTaskStatus.Pending);

    /// <summary>执行所有待执行任务（串行，与原版一致）</summary>
    [RelayCommand(CanExecute = nameof(CanExecuteAll))]
    private async Task ExecuteAllAsync()
    {
        IsProcessing = true;
        try
        {
            foreach (var task in Tasks.Where(t => t.Status == OrganizeTaskStatus.Pending).ToList())
                await RunTaskAsync(task);
        }
        finally
        {
            IsProcessing = false;
            TasksVersion++;
        }
    }

    internal async Task RunTaskAsync(OrganizeTaskViewModel task)
    {
        var sceneDir = _config.GetSceneDir();
        if (sceneDir is null)
        {
            // 未设游戏路径时给出明确错误（原来静默无反应）
            task.Status = OrganizeTaskStatus.Error;
            task.Error = "未设置游戏路径，请先在首页设置游戏目录";
            return;
        }

        try
        {
            task.Status = OrganizeTaskStatus.Scanning;
            var targetDir = Path.Combine(sceneDir, $"hs_tools_{task.FolderName}");
            _scanner.CheckTargetDir(targetDir);

            // 排除已整理目录（子串匹配，与原版 excludeDir: ['hs_tools_'] 一致）
            var scenes = _scanner.ScanDirectory(sceneDir, new ScanOptions
            {
                ExcludeDir = { "hs_tools_" },
                TargetExtension = { ".png" },
            });
            task.TotalScenes = scenes.Count;
            task.Status = OrganizeTaskStatus.Analyzing;

            // 分析：批量解析角色名，按子串匹配（进度 0~50）
            var matched = new List<string>();
            for (var i = 0; i < scenes.Count; i += AnalyzeBatchSize)
            {
                var batch = scenes.GetRange(i, Math.Min(AnalyzeBatchSize, scenes.Count - i));
                var results = await _scanner.ReadPngNamesBatchAsync(batch,
                    onError: msg => App.LogException(new Exception(msg)));
                foreach (var item in results)
                {
                    if (item.Names.Count == 0)
                        continue;
                    var hit = task.CharNames.Any(target =>
                        item.Names.Any(n => n.Contains(target, StringComparison.Ordinal)));
                    if (hit)
                        matched.Add(item.Path);
                }

                task.ProcessedScenes = Math.Min(i + AnalyzeBatchSize, scenes.Count);
                task.Progress = scenes.Count == 0 ? 50 : (int)Math.Round(task.ProcessedScenes * 50.0 / scenes.Count);
            }

            // 移动（进度 50~100；单文件失败记日志后继续——与原版一致）
            task.Status = OrganizeTaskStatus.Moving;
            task.Progress = 50;
            var moved = 0;
            var moveFailed = 0;
            foreach (var scene in matched)
            {
                try
                {
                    // 修复原版：目标须为完整文件路径（原版传目录，移动从未生效）
                    _scanner.MoveFile(scene, Path.Combine(targetDir, Path.GetFileName(scene)));
                    moved++;
                }
                catch (Exception ex)
                {
                    moveFailed++;
                    App.LogException(ex);
                }
                task.Progress = 50 + (matched.Count == 0 ? 50 : (int)Math.Round((moved + 1) * 50.0 / matched.Count));
            }

            task.ResultCount = moved;
            task.MoveFailCount = moveFailed;
            task.Progress = 100;
            task.Status = OrganizeTaskStatus.Completed;
            OrganizeCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            task.Status = OrganizeTaskStatus.Error;
            task.Error = $"整理失败：{ex.Message}"; // 统一"XX失败：原因"风格
            App.LogException(ex);
        }
    }
}
