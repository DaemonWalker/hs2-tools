using System.Text.Json;
using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>
/// Sideload 数据库（guid → 远程相对路径），按数据源（GameProfile.SideloadSourceId）分库：
/// 每个数据源一个用户文件 sideload-{sourceId}.json（如 sideload-hs2.json / sideload-kkec.json），
/// KK 与 KKS 共享 KKEC 库故天然落同一文件。
/// 加载顺序：配置目录下用户更新的文件优先；hs2 无用户文件回退程序集内嵌的 sideload.zip，
/// kkec 无内嵌库，回退空字典（用户需运行一次爬虫更新）。
/// 旧版单文件 sideload.json 在构造时迁移为 hs2 数据源的库。
///
/// 注意（对原版的修复）：原版爬虫结果只经 complete 事件发出且前端丢弃，
/// 前端随后 init() 重读的是内嵌旧库——"更新"实际从不生效。
/// C# 版爬取完成后把结果落盘到用户文件，更新真实生效。
/// </summary>
public class SideloadDatabaseService
{
    private readonly ConfigService _config;
    private readonly object _sync = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _cache = new();
    private string _currentSourceId;

    public SideloadDatabaseService(ConfigService config)
    {
        _config = config;
        MigrateLegacyFile();
        _currentSourceId = CurrentSourceId;
        _config.Changed += OnConfigChanged; // 与服务同寿，永久订阅
    }

    /// <summary>当前游戏数据源的数据库（guid → 相对路径，如 "Exclusive HS2/xxx.zipmod"）</summary>
    public IReadOnlyDictionary<string, string> Database => Get(CurrentSourceId);

    /// <summary>数据库被替换（爬虫更新完成 / 切换游戏切到另一数据源）时触发，在调用方的线程上</summary>
    public event EventHandler? Changed;

    private string CurrentSourceId => _config.CurrentProfile.SideloadSourceId;

    private string FilePathFor(string sourceId) =>
        Path.Combine(_config.ConfigDir, $"sideload-{sourceId}.json");

    private string MetaPathFor(string sourceId) =>
        Path.Combine(_config.ConfigDir, $"sideload-{sourceId}.meta.json");

    // ---- 扫描元数据（爬虫最近一次结果，独立 meta 文件）----

    private readonly Dictionary<string, SideloadScanMeta?> _metaCache = new();

    /// <summary>当前数据源的最近扫描元数据；从未扫描 / 文件损坏 → null（损坏留 ErrorLog）</summary>
    public SideloadScanMeta? GetMeta() => GetMetaFor(CurrentSourceId);

    private SideloadScanMeta? GetMetaFor(string sourceId)
    {
        lock (_sync)
        {
            if (!_metaCache.TryGetValue(sourceId, out var meta))
            {
                meta = LoadMeta(sourceId);
                _metaCache[sourceId] = meta;
            }
            return meta;
        }
    }

    private SideloadScanMeta? LoadMeta(string sourceId)
    {
        try
        {
            var path = MetaPathFor(sourceId);
            if (!File.Exists(path))
                return null; // 从未扫描：正常情况不留痕
            return JsonSerializer.Deserialize<SideloadScanMeta>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            // 文件损坏视为无记录（与库文件损坏回退语义一致），留痕
            ErrorLog.Log(ex);
            return null;
        }
    }

    /// <summary>写当前数据源的扫描元数据并落盘，随 Changed 事件一起通知</summary>
    public void SaveMeta(SideloadScanMeta meta)
    {
        lock (_sync)
        {
            var sourceId = CurrentSourceId;
            _metaCache[sourceId] = meta;
            File.WriteAllText(MetaPathFor(sourceId), JsonSerializer.Serialize(meta));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>切换游戏时若数据源 ID 变化：取新数据源缓存（懒加载）并通知 UI 刷新</summary>
    private void OnConfigChanged(object? sender, EventArgs e)
    {
        var sourceId = CurrentSourceId;
        lock (_sync)
        {
            if (sourceId == _currentSourceId)
                return;
            _currentSourceId = sourceId;
        }
        _ = Get(sourceId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>按数据源懒加载并缓存</summary>
    private IReadOnlyDictionary<string, string> Get(string sourceId)
    {
        lock (_sync)
        {
            if (!_cache.TryGetValue(sourceId, out var db))
            {
                db = Load(sourceId);
                _cache[sourceId] = db;
            }
            return db;
        }
    }

    private IReadOnlyDictionary<string, string> Load(string sourceId)
    {
        try
        {
            var filePath = FilePathFor(sourceId);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict is { Count: > 0 })
                    return dict;
            }
        }
        catch (Exception ex)
        {
            // 文件损坏回退内嵌库（与 ConfigService 损坏回退语义一致），留痕
            ErrorLog.Log(ex);
        }

        var bundled = SideloaderService.LoadBundledDatabase(sourceId);
        if (bundled.Count == 0 && sourceId != Models.GameProfiles.Hs2.SideloadSourceId)
        {
            // kkec 等非 hs2 数据源无内嵌库：回退空字典并说明，用户需先运行爬虫更新
            ErrorLog.Log($"sideload 数据源 {sourceId} 无用户库且无内嵌库，返回空数据库（需先运行爬虫更新）");
        }
        return bundled;
    }

    /// <summary>
    /// 旧版单文件迁移：sideload.json → sideload-hs2.json（旧库本就是 HS2 的库）。
    /// 迁移成功与失败都留 ErrorLog 痕迹；失败不抛异常（下次启动重试，旧文件仍在）。
    /// </summary>
    private void MigrateLegacyFile()
    {
        var legacyPath = Path.Combine(_config.ConfigDir, "sideload.json");
        var hs2Path = FilePathFor(Models.GameProfiles.Hs2.SideloadSourceId);
        try
        {
            if (!File.Exists(legacyPath) || File.Exists(hs2Path))
                return;
            File.Move(legacyPath, hs2Path);
            ErrorLog.Log($"旧版 sideload.json 已迁移为 {Path.GetFileName(hs2Path)}");
        }
        catch (Exception ex)
        {
            ErrorLog.Log(ex);
        }
    }

    /// <summary>用爬虫结果替换当前数据源的数据库并立即落盘。</summary>
    public void Update(IDictionary<string, string> database)
    {
        lock (_sync)
        {
            var sourceId = CurrentSourceId;
            var dict = new Dictionary<string, string>(database);
            _cache[sourceId] = dict;
            File.WriteAllText(FilePathFor(sourceId), JsonSerializer.Serialize(dict));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
