using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Controls;
using HS2Tools.Models;
using HS2Tools.Services;
using Microsoft.Win32;

namespace HS2Tools.ViewModels;

/// <summary>单卡查看器的 Mod 依赖项（展示本地是否已拥有）</summary>
public partial class CardExplorerModItem : ObservableObject
{
    /// <summary>Mod GUID</summary>
    public required string Guid { get; init; }

    /// <summary>本地已拥有（Config.Settings.LocalMods 中存在）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isLocal;

    public string StatusText => IsLocal ? "已拥有" : "缺失";
}

/// <summary>
/// 单卡查看器 ViewModel（对应原版 CardExplorer.tsx + SmartCardLayout）：
/// 人物卡/场景卡切换、选择 PNG 文件、解析展示大图/角色名/Mod 依赖/游戏数据大小。
/// IO/解析放后台线程，结果回 UI。
/// </summary>
public partial class CardExplorerViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ScannerService _scanner;
    private int _loadVersion;

    public CardExplorerViewModel(ConfigService config, ScannerService scanner)
    {
        _config = config;
        _scanner = scanner;

        // 扫描完成（LocalMods 变化）→ 刷新"已拥有"标记（单例服务与窗口同寿，无需退订）
        _config.Changed += (_, _) => UiDispatch.Run(RefreshModLocalState);
    }

    /// <summary>人物卡模式（false = 场景卡；对应原版 type: chara/scene）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSceneMode))]
    private bool _isCharaMode = true;

    /// <summary>场景卡模式（RadioButton 双向绑定用；设置时等价于反向设置 IsCharaMode）</summary>
    public bool IsSceneMode
    {
        get => !IsCharaMode;
        set => IsCharaMode = !value;
    }

    /// <summary>切换类型时清空文件与展示（对应原版 onChange: setFilePath('') + setShow(false)）</summary>
    partial void OnIsCharaModeChanged(bool value)
    {
        _loadVersion++; // 丢弃进行中的解析结果
        FilePath = "";
        ResetCard();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilePath))]
    private string _filePath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private bool _hasCard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private string? _errorMessage;

    [ObservableProperty] private BitmapImage? _cardImage;
    [ObservableProperty] private string _charaName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _description;

    [ObservableProperty] private string _gameDataSizeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMods))]
    [NotifyPropertyChangedFor(nameof(ModMissingCount))]
    private int _modCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMods))]
    [NotifyPropertyChangedFor(nameof(ModMissingCount))]
    private int _modLocalCount;

    [ObservableProperty] private int _sceneCharaCount;

    /// <summary>场景人物列表（编号在 VM 侧生成，对应原版 {index+1}. {name}）</summary>
    public ObservableCollection<string> SceneCharaNames { get; } = new();

    /// <summary>Mod 依赖列表</summary>
    public ObservableCollection<CardExplorerModItem> ModItems { get; } = new();

    public bool HasFilePath => FilePath.Length > 0;
    public bool HasError => ErrorMessage is not null;
    public bool HasDescription => Description is not null;
    public bool HasMods => ModCount > 0;
    public int ModMissingCount => ModCount - ModLocalCount;

    /// <summary>空态：未选文件 / 文件不存在或不是 PNG（对应原版 show=false 时的占位）</summary>
    public bool ShowEmpty => !HasCard && !IsLoading && !HasError;
    public bool ShowContent => HasCard;

    /// <summary>当前解析任务（测试可等待；文件变化时总是替换为新任务）</summary>
    internal Task? LoadTask { get; private set; }

    /// <summary>文件路径变化（输入框逐键触发，对应原版 onChange + fileExists 门控）</summary>
    partial void OnFilePathChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _loadVersion++;
            ResetCard();
            return;
        }
        LoadTask = LoadCardAsync(value);
    }

    private async Task LoadCardAsync(string path)
    {
        var version = ++_loadVersion;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            // 文件不存在或不是 .png：照原版回到空态（不报错）
            if (!ScannerService.FileExists(path))
            {
                if (version == _loadVersion)
                    ResetCard();
                return;
            }

            var (result, image) = await Task.Run(() =>
            {
                var r = _scanner.ParsePngData(path);
                var img = ThumbnailCache.DecodeBase64(_scanner.ReadPngImage(path));
                return (r, img);
            });

            if (version != _loadVersion)
                return; // 加载期间已切换文件/类型，丢弃过期结果
            ApplyResult(result, image);
        }
        catch (Exception ex)
        {
            if (version != _loadVersion)
                return;
            ErrorLog.Log(ex); // 解析失败留痕
            ResetCard();
            ErrorMessage = $"无法解析卡片文件：{ex.Message}"; // ParsePngData 抛 InvalidDataException
        }
        finally
        {
            if (version == _loadVersion)
                IsLoading = false;
        }
    }

    private void ApplyResult(PngParseResult result, BitmapImage? image)
    {
        HasCard = true;
        CardImage = image;
        CharaName = result.CharaNames.FirstOrDefault() ?? "未知"; // 对应原版 info[0] || '未知'
        Description = result.CharaNames.Count > 1 ? result.CharaNames[1] : null;
        GameDataSizeText = FormatUtils.FormatBytes(result.GameDataLen);

        SceneCharaNames.Clear();
        for (var i = 0; i < result.CharaNames.Count; i++)
            SceneCharaNames.Add($"{i + 1}. {result.CharaNames[i]}");
        SceneCharaCount = result.CharaNames.Count;

        ModItems.Clear();
        foreach (var guid in result.ModIDs)
            ModItems.Add(new CardExplorerModItem { Guid = guid, IsLocal = _config.Settings.LocalMods.ContainsKey(guid) });
        RefreshModCounts();
    }

    /// <summary>LocalMods 变化后刷新"已拥有"标记与统计</summary>
    private void RefreshModLocalState()
    {
        foreach (var item in ModItems)
            item.IsLocal = _config.Settings.LocalMods.ContainsKey(item.Guid);
        RefreshModCounts();
    }

    private void RefreshModCounts()
    {
        ModCount = ModItems.Count;
        ModLocalCount = ModItems.Count(i => i.IsLocal);
    }

    private void ResetCard()
    {
        HasCard = false;
        CardImage = null;
        CharaName = "";
        Description = null;
        GameDataSizeText = "";
        SceneCharaNames.Clear();
        SceneCharaCount = 0;
        ModItems.Clear();
        RefreshModCounts();
        IsLoading = false;
        ErrorMessage = null;
    }

    /// <summary>选择 PNG 文件（默认目录 = 游戏角色/场景目录，存在才设——对应原版 dirExists 检查）</summary>
    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 PNG 卡片文件",
            Filter = "PNG 卡片 (*.png)|*.png",
            CheckFileExists = true,
        };
        var defaultDir = IsCharaMode ? _config.GetCharaDir() : _config.GetSceneDir();
        if (defaultDir is not null && Directory.Exists(defaultDir))
            dialog.InitialDirectory = defaultDir;

        if (dialog.ShowDialog() == true)
            FilePath = dialog.FileName;
    }

    /// <summary>清除选择（对应原版 handleClear）</summary>
    [RelayCommand]
    private void ClearFile() => FilePath = "";
}
