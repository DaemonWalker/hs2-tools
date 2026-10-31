using System.Text.Json;
using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>
/// Sideload 数据库（guid → 远程相对路径 + 增量指纹 + last-seen），按数据源
/// （GameProfile.SideloadSourceId）分库：每个数据源一个用户文件 sideload-{sourceId}.json。
/// KK 与 KKS 共享 KKEC 库故天然落同一文件。
/// 加载顺序：配置目录下用户更新的文件优先；hs2 无用户文件回退程序集内嵌的 sideload.zip，
/// kkec 无内嵌库，回退空字典（用户需运行一次爬虫更新）。
/// 旧版单文件 sideload.json 在构造时迁移为 hs2 数据源的库；
/// 旧版 v1 格式（guid → url 平铺字典）加载时自动升级为 v2（指纹置空、LastSeen 置当前时间）。
///
/// 库格式 v2：{ Version, LastFullScan, Entries: { guid: { Url, SizeText, MtimeText, LastSeen } } }。
/// Update 为合并语义（仅完整成功的扫描才会调用，见 MainWindowViewModel 的 A 策略）：
/// 本轮见到的条目刷新 LastSeen；连续两次完整扫描未见到的条目才淘汰，单次漏抓不误删。
///
/// 注意（对原版的修复）：原版爬虫结果只经 complete 事件发出且前端丢弃，
/// 前端随后 init() 重读的是内嵌旧库——"更新"实际从不生效。
/// C# 版爬取完成后把结果落盘到用户文件，更新真实生效。
/// </summary>
public class SideloadDatabaseService
{
    private readonly ConfigService _config;
    private readonly object _sync = new();
    private readonly Dictionary<string, Dictionary<string, SideloadEntry>> _entries = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _projection = new();
    private readonly Dictionary<string, DateTime?> _lastFullScan = new();

    /// <summary>当前缓存来自内嵌/空回退（无用户文件）：首次 Update 从空开始覆盖种子库，不做合并</summary>
    private readonly Dictionary<string, bool> _fromBundled = new();
    private string _currentSourceId;

    public SideloadDatabaseService(ConfigService config)
    {
        _config = config;
        MigrateLegacyFile();
        _currentSourceId = CurrentSourceId;
        _config.Changed += OnConfigChanged; // 与服务同寿，永久订阅
    }

    /// <summary>当前游戏数据源的数据库（guid → 相对路径投影，消费方与 v1 一致）</summary>
    public IReadOnlyDictionary<string, string> Database => GetProjection(CurrentSourceId);

    /// <summary>数据库被替换（爬虫更新完成 / 切换游戏切到另一数据源）时触发，在调用方的线程上</summary>
    public event EventHandler? Changed;

    /// <summary>仅扫描元数据变化（与 Changed 分离：调用方只刷新"上次扫描"文案，不做整表重建）</summary>
    public event EventHandler? MetaChanged;

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

    /// <summary>写当前数据源的扫描元数据并落盘，随 MetaChanged 事件通知（不触发整表重建）</summary>
    public void SaveMeta(SideloadScanMeta meta)
    {
        lock (_sync)
        {
            var sourceId = CurrentSourceId;
            _metaCache[sourceId] = meta;
            WriteAllTextAtomic(MetaPathFor(sourceId), JsonSerializer.Serialize(meta));
        }
        MetaChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>同目录临时文件 + 原子替换：防止写一半崩溃/断电留下截断 JSON（旧库一并损坏）</summary>
    private static void WriteAllTextAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
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
        _ = GetProjection(sourceId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- 库条目（v2）----

    /// <summary>当前数据源旧库按相对路径索引（增量爬取输入）</summary>
    public IReadOnlyDictionary<string, KnownSideloadMod> GetKnownMods()
    {
        lock (_sync)
        {
            var entries = GetEntries(CurrentSourceId);
            var result = new Dictionary<string, KnownSideloadMod>(StringComparer.Ordinal);
            foreach (var (guid, entry) in entries)
            {
                if (!string.IsNullOrEmpty(entry.Url))
                    result[entry.Url] = new KnownSideloadMod(guid, entry.SizeText, entry.MtimeText);
            }
            return result;
        }
    }

    private IReadOnlyDictionary<string, string> GetProjection(string sourceId)
    {
        lock (_sync)
        {
            GetEntries(sourceId);
            return _projection[sourceId];
        }
    }

    private Dictionary<string, SideloadEntry> GetEntries(string sourceId)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(sourceId, out var entries))
            {
                entries = Load(sourceId, out var lastFullScan, out var fromBundled);
                _entries[sourceId] = entries;
                _lastFullScan[sourceId] = lastFullScan;
                _fromBundled[sourceId] = fromBundled;
                _projection[sourceId] = Project(entries);
            }
            return entries;
        }
    }

    private static Dictionary<string, string> Project(Dictionary<string, SideloadEntry> entries)
    {
        var result = new Dictionary<string, string>(entries.Count);
        foreach (var (guid, entry) in entries)
            result[guid] = entry.Url;
        return result;
    }

    /// <summary>库文件 v2 结构</summary>
    private sealed class DatabaseFile
    {
        public int Version { get; set; } = 2;
        public DateTime? LastFullScan { get; set; }
        public Dictionary<string, SideloadEntry> Entries { get; set; } = new();
    }

    private Dictionary<string, SideloadEntry> Load(string sourceId, out DateTime? lastFullScan, out bool fromBundled)
    {
        lastFullScan = null;
        fromBundled = false;
        try
        {
            var filePath = FilePathFor(sourceId);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("Entries", out _))
                {
                    var file = JsonSerializer.Deserialize<DatabaseFile>(json);
                    if (file is { Entries.Count: > 0 })
                    {
                        lastFullScan = file.LastFullScan;
                        return NormalizeEntries(file.Entries);
                    }
                }
                else
                {
                    // v1 平铺字典（guid → url）：升级为 v2，指纹置空（下轮扫描补）、LastSeen 置当前
                    var legacy = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (legacy is { Count: > 0 })
                        return FromFlatDictionary(legacy);
                }
            }
        }
        catch (Exception ex)
        {
            // 文件损坏回退内嵌库（与 ConfigService 损坏回退语义一致），留痕
            ErrorLog.Log(ex);
        }

        fromBundled = true;
        var bundled = SideloaderService.LoadBundledDatabase(sourceId);
        if (bundled.Count == 0 && sourceId != Models.GameProfiles.Hs2.SideloadSourceId)
        {
            // kkec 等非 hs2 数据源无内嵌库：回退空字典并说明，用户需先运行爬虫更新
            ErrorLog.Log($"sideload 数据源 {sourceId} 无用户库且无内嵌库，返回空数据库（需先运行爬虫更新）");
        }
        return FromFlatDictionary(bundled);
    }

    private static Dictionary<string, SideloadEntry> FromFlatDictionary(Dictionary<string, string> flat)
    {
        var now = DateTime.Now;
        var result = new Dictionary<string, SideloadEntry>(flat.Count);
        foreach (var (guid, url) in flat)
            result[ScannerService.NormalizeGuid(guid)] = new SideloadEntry { Url = url, LastSeen = now };
        return result;
    }

    private static Dictionary<string, SideloadEntry> NormalizeEntries(Dictionary<string, SideloadEntry> entries)
    {
        var result = new Dictionary<string, SideloadEntry>(entries.Count);
        foreach (var (guid, entry) in entries)
            result[ScannerService.NormalizeGuid(guid)] = entry;
        return result;
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

    /// <summary>平铺字典（guid → url）合并更新：指纹置空，下轮扫描补齐（兼容旧调用与测试）</summary>
    public void Update(IDictionary<string, string> database) =>
        Update(database.ToDictionary(kv => kv.Key, kv => new SideloadEntry { Url = kv.Value }));

    /// <summary>
    /// 用完整成功的扫描结果合并更新当前数据源并立即落盘（调用方保证：空结果/有失败不调用——A 策略）。
    /// 本轮见到的条目刷新 LastSeen 并覆盖；连续两次完整扫描未见到的条目才淘汰。
    /// </summary>
    public void Update(IDictionary<string, SideloadEntry> scanned)
    {
        lock (_sync)
        {
            var sourceId = CurrentSourceId;
            var entries = GetEntries(sourceId);
            var now = DateTime.Now;

            // 当前库来自内嵌/空回退（首次用户更新）：从空开始覆盖种子库，不把过时内嵌条目并进来
            if (_fromBundled.TryGetValue(sourceId, out var fromBundled) && fromBundled)
            {
                entries = new Dictionary<string, SideloadEntry>();
                _entries[sourceId] = entries;
                _fromBundled[sourceId] = false;
                _lastFullScan[sourceId] = null;
            }

            foreach (var (guid, entry) in scanned)
            {
                entries[ScannerService.NormalizeGuid(guid)] = new SideloadEntry
                {
                    Url = entry.Url,
                    SizeText = entry.SizeText,
                    MtimeText = entry.MtimeText,
                    LastSeen = now,
                };
            }

            // 淘汰：仅当有上一轮完整扫描基准时进行；上轮之前就未见的条目本轮仍未见 → 连续两轮淘汰
            var previousFullScan = _lastFullScan.TryGetValue(sourceId, out var t) ? t : null;
            if (previousFullScan != null)
            {
                foreach (var (guid, entry) in entries.ToArray())
                {
                    if (entry.LastSeen < previousFullScan.Value)
                        entries.Remove(guid);
                }
            }

            _lastFullScan[sourceId] = now;
            _projection[sourceId] = Project(entries);
            WriteAllTextAtomic(FilePathFor(sourceId), JsonSerializer.Serialize(
                new DatabaseFile { LastFullScan = now, Entries = entries }));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
