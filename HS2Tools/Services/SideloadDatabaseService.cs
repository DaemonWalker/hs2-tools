using System.Text.Json;
using HS2Tools.Models;
using Microsoft.Data.Sqlite;

namespace HS2Tools.Services;

/// <summary>
/// Sideload 数据库（guid → 远程相对路径 + 增量指纹 + last-seen），按数据源
/// （GameProfile.SideloadSourceId）分库：同一 analysis-cache.db 内按 source_id 隔离。
/// KK 与 KKS 共享 KKEC 库故天然使用同一批记录。
/// hs2 无用户记录时回退程序集内嵌的 sideload.zip，kkec 无内嵌库时回退空字典。
/// 旧版 JSON（含单文件 sideload.json）首次启动自动迁移到 SQLite，原文件保留为 .migrated 备份。
///
/// Update 为合并语义（仅完整成功或用户确认使用的扫描才会调用）：
/// 本轮见到的条目刷新 LastSeen；连续两次已应用扫描未见到的条目才淘汰，单次漏抓不误删。
/// </summary>
public class SideloadDatabaseService
{
    private readonly ConfigService _config;
    private readonly string _dbPath;
    private readonly bool _storageAvailable;
    private readonly object _sync = new();
    private readonly Dictionary<string, Dictionary<string, SideloadEntry>> _entries = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _projection = new();
    private readonly Dictionary<string, DateTime?> _lastFullScan = new();
    private readonly Dictionary<string, SideloadScanMeta?> _metaCache = new();

    /// <summary>当前缓存来自内嵌/空回退（无用户记录）：首次 Update 从空开始覆盖种子库，不做合并</summary>
    private readonly Dictionary<string, bool> _fromBundled = new();
    private string _currentSourceId;

    public SideloadDatabaseService(ConfigService config)
    {
        _config = config;
        _dbPath = Path.Combine(config.ConfigDir, "analysis-cache.db");
        _storageAvailable = InitializeStorage();
        if (_storageAvailable)
            MigrateJsonFiles();
        _currentSourceId = CurrentSourceId;
        _config.Changed += OnConfigChanged; // 与服务同寿，永久订阅
    }

    /// <summary>当前游戏数据源的数据库（guid → 相对路径投影）</summary>
    public IReadOnlyDictionary<string, string> Database => GetProjection(CurrentSourceId);

    /// <summary>数据库被替换（爬虫更新完成 / 切换游戏切到另一数据源）时触发，在调用方的线程上</summary>
    public event EventHandler? Changed;

    /// <summary>仅扫描元数据变化（与 Changed 分离：调用方只刷新"上次扫描"文案，不做整表重建）</summary>
    public event EventHandler? MetaChanged;

    private string CurrentSourceId => _config.CurrentProfile.SideloadSourceId;

    private string LegacyJsonPathFor(string sourceId) =>
        Path.Combine(_config.ConfigDir, $"sideload-{sourceId}.json");

    private string LegacyMetaPathFor(string sourceId) =>
        Path.Combine(_config.ConfigDir, $"sideload-{sourceId}.meta.json");

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private bool InitializeStorage()
    {
        try
        {
            Directory.CreateDirectory(_config.ConfigDir);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            command.ExecuteNonQuery();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS sideload_sources (
                    source_id TEXT PRIMARY KEY,
                    last_full_scan_ticks INTEGER NULL
                );
                CREATE TABLE IF NOT EXISTS sideload_entries (
                    source_id TEXT NOT NULL,
                    guid TEXT NOT NULL,
                    url TEXT NOT NULL,
                    size_text TEXT NULL,
                    mtime_text TEXT NULL,
                    last_seen_ticks INTEGER NOT NULL,
                    PRIMARY KEY (source_id, guid)
                );
                CREATE TABLE IF NOT EXISTS sideload_scan_meta (
                    source_id TEXT PRIMARY KEY,
                    last_scan_time_ticks INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    found_count INTEGER NOT NULL,
                    failed_count INTEGER NOT NULL,
                    error TEXT NULL,
                    result_applied INTEGER NOT NULL DEFAULT 0
                );
                """;
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Log($"Sideload SQLite 初始化失败，本次运行只能读取旧 JSON 或内嵌库: {ex.Message}");
            return false;
        }
    }

    // ---- 扫描元数据 ----

    /// <summary>当前数据源的最近扫描元数据；从未扫描 / 记录损坏 → null（损坏留 ErrorLog）</summary>
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
            if (_storageAvailable)
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT last_scan_time_ticks, status, found_count, failed_count, error, result_applied
                    FROM sideload_scan_meta WHERE source_id=$sourceId;
                    """;
                command.Parameters.AddWithValue("$sourceId", sourceId);
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    return null;
                return new SideloadScanMeta
                {
                    LastScanTime = new DateTime(reader.GetInt64(0)),
                    Status = (SideloadScanStatus)reader.GetInt32(1),
                    FoundCount = reader.GetInt32(2),
                    FailedCount = reader.GetInt32(3),
                    Error = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ResultApplied = reader.GetInt64(5) != 0,
                };
            }

            var path = LegacyMetaPathFor(sourceId);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SideloadScanMeta>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex)
        {
            ErrorLog.Log(ex);
            return null;
        }
    }

    /// <summary>写当前数据源的扫描元数据，随 MetaChanged 事件通知（不触发整表重建）</summary>
    public void SaveMeta(SideloadScanMeta meta)
    {
        if (!_storageAvailable)
            throw new InvalidOperationException("Sideload SQLite 存储不可用");
        lock (_sync)
        {
            var sourceId = CurrentSourceId;
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sideload_scan_meta
                    (source_id, last_scan_time_ticks, status, found_count, failed_count, error, result_applied)
                VALUES ($sourceId, $time, $status, $found, $failed, $error, $applied)
                ON CONFLICT(source_id) DO UPDATE SET
                    last_scan_time_ticks=$time, status=$status, found_count=$found,
                    failed_count=$failed, error=$error, result_applied=$applied;
                """;
            command.Parameters.AddWithValue("$sourceId", sourceId);
            command.Parameters.AddWithValue("$time", meta.LastScanTime.Ticks);
            command.Parameters.AddWithValue("$status", (int)meta.Status);
            command.Parameters.AddWithValue("$found", meta.FoundCount);
            command.Parameters.AddWithValue("$failed", meta.FailedCount);
            command.Parameters.AddWithValue("$error", (object?)meta.Error ?? DBNull.Value);
            command.Parameters.AddWithValue("$applied", meta.ResultApplied ? 1 : 0);
            command.ExecuteNonQuery();
            _metaCache[sourceId] = meta;
        }
        MetaChanged?.Invoke(this, EventArgs.Empty);
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

    // ---- 库条目 ----

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

    private Dictionary<string, SideloadEntry> Load(string sourceId, out DateTime? lastFullScan, out bool fromBundled)
    {
        lastFullScan = null;
        fromBundled = false;
        try
        {
            if (_storageAvailable)
            {
                using var connection = OpenConnection();
                if (TryGetLastFullScan(connection, sourceId, out lastFullScan))
                    return ReadEntries(connection, sourceId);
            }
            else if (TryReadLegacyDatabase(sourceId, out var legacyEntries, out lastFullScan))
            {
                return legacyEntries;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Log(ex);
        }

        fromBundled = true;
        var bundled = SideloaderService.LoadBundledDatabase(sourceId);
        if (bundled.Count == 0 && sourceId != GameProfiles.Hs2.SideloadSourceId)
        {
            ErrorLog.Log($"sideload 数据源 {sourceId} 无用户库且无内嵌库，返回空数据库（需先运行爬虫更新）");
        }
        return FromFlatDictionary(bundled);
    }

    private static Dictionary<string, SideloadEntry> ReadEntries(
        SqliteConnection connection, string sourceId, SqliteTransaction? transaction = null)
    {
        var entries = new Dictionary<string, SideloadEntry>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT guid, url, size_text, mtime_text, last_seen_ticks
            FROM sideload_entries WHERE source_id=$sourceId;
            """;
        command.Parameters.AddWithValue("$sourceId", sourceId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries[ScannerService.NormalizeGuid(reader.GetString(0))] = new SideloadEntry
            {
                Url = reader.GetString(1),
                SizeText = reader.IsDBNull(2) ? null : reader.GetString(2),
                MtimeText = reader.IsDBNull(3) ? null : reader.GetString(3),
                LastSeen = new DateTime(reader.GetInt64(4)),
            };
        }
        return entries;
    }

    private static bool TryGetLastFullScan(
        SqliteConnection connection, string sourceId, out DateTime? lastFullScan)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_full_scan_ticks FROM sideload_sources WHERE source_id=$sourceId;";
        command.Parameters.AddWithValue("$sourceId", sourceId);
        var value = command.ExecuteScalar();
        lastFullScan = value is null or DBNull ? null : new DateTime((long)value);
        return value is not null;
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

    /// <summary>平铺字典（guid → url）合并更新：指纹置空，下轮扫描补齐（兼容旧调用与测试）</summary>
    public void Update(IDictionary<string, string> database) =>
        Update(database.ToDictionary(kv => kv.Key, kv => new SideloadEntry { Url = kv.Value }));

    /// <summary>
    /// 用完整成功或用户确认使用的扫描结果合并更新当前数据源并立即落盘。
    /// 本轮见到的条目刷新 LastSeen；连续两次已应用扫描未见到的条目才淘汰。
    /// </summary>
    public void Update(IDictionary<string, SideloadEntry> scanned)
    {
        if (!_storageAvailable)
            throw new InvalidOperationException("Sideload SQLite 存储不可用");
        lock (_sync)
        {
            var sourceId = CurrentSourceId;
            var entries = GetEntries(sourceId);
            var now = DateTime.Now;

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

            var previousFullScan = _lastFullScan.TryGetValue(sourceId, out var t) ? t : null;
            if (previousFullScan != null)
            {
                foreach (var (guid, entry) in entries.ToArray())
                {
                    if (entry.LastSeen < previousFullScan.Value)
                        entries.Remove(guid);
                }
            }

            PersistSnapshot(sourceId, entries, now);
            _lastFullScan[sourceId] = now;
            _projection[sourceId] = Project(entries);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void PersistSnapshot(
        string sourceId, Dictionary<string, SideloadEntry> entries, DateTime? lastFullScan)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM sideload_entries WHERE source_id=$sourceId;";
            delete.Parameters.AddWithValue("$sourceId", sourceId);
            delete.ExecuteNonQuery();
        }
        foreach (var (guid, entry) in entries)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO sideload_entries
                    (source_id, guid, url, size_text, mtime_text, last_seen_ticks)
                VALUES ($sourceId, $guid, $url, $size, $mtime, $lastSeen);
                """;
            command.Parameters.AddWithValue("$sourceId", sourceId);
            command.Parameters.AddWithValue("$guid", guid);
            command.Parameters.AddWithValue("$url", entry.Url);
            command.Parameters.AddWithValue("$size", (object?)entry.SizeText ?? DBNull.Value);
            command.Parameters.AddWithValue("$mtime", (object?)entry.MtimeText ?? DBNull.Value);
            command.Parameters.AddWithValue("$lastSeen", entry.LastSeen.Ticks);
            command.ExecuteNonQuery();
        }
        using (var source = connection.CreateCommand())
        {
            source.Transaction = transaction;
            source.CommandText = """
                INSERT INTO sideload_sources (source_id, last_full_scan_ticks) VALUES ($sourceId, $time)
                ON CONFLICT(source_id) DO UPDATE SET last_full_scan_ticks=$time;
                """;
            source.Parameters.AddWithValue("$sourceId", sourceId);
            source.Parameters.AddWithValue("$time", (object?)lastFullScan?.Ticks ?? DBNull.Value);
            source.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    // ---- 旧 JSON 迁移与 SQLite 不可用时的只读回退 ----

    private sealed class DatabaseFile
    {
        public int Version { get; set; } = 2;
        public DateTime? LastFullScan { get; set; }
        public Dictionary<string, SideloadEntry> Entries { get; set; } = new();
    }

    private void MigrateJsonFiles()
    {
        foreach (var sourceId in GameProfiles.All.Select(p => p.SideloadSourceId).Distinct(StringComparer.Ordinal))
        {
            var jsonPath = LegacyJsonPathFor(sourceId);
            if (!File.Exists(jsonPath) && sourceId == GameProfiles.Hs2.SideloadSourceId)
                jsonPath = Path.Combine(_config.ConfigDir, "sideload.json");
            MigrateDatabaseJson(sourceId, jsonPath);
            MigrateMetaJson(sourceId, LegacyMetaPathFor(sourceId));
        }
    }

    private void MigrateDatabaseJson(string sourceId, string path)
    {
        if (!File.Exists(path) || HasSource(sourceId))
            return;
        try
        {
            if (!TryReadDatabaseJson(path, out var entries, out var lastFullScan))
                return;
            PersistSnapshot(sourceId, entries, lastFullScan);
            MarkMigrated(path);
        }
        catch (Exception ex)
        {
            ErrorLog.Log($"迁移 Sideload JSON 失败（{path}）: {ex.Message}");
        }
    }

    private void MigrateMetaJson(string sourceId, string path)
    {
        if (!File.Exists(path) || HasMeta(sourceId))
            return;
        try
        {
            var meta = JsonSerializer.Deserialize<SideloadScanMeta>(File.ReadAllText(path));
            if (meta is null)
                return;
            SaveMetaFor(sourceId, meta);
            MarkMigrated(path);
        }
        catch (Exception ex)
        {
            ErrorLog.Log($"迁移 Sideload 扫描元数据失败（{path}）: {ex.Message}");
        }
    }

    private bool HasSource(string sourceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sideload_sources WHERE source_id=$sourceId;";
        command.Parameters.AddWithValue("$sourceId", sourceId);
        return command.ExecuteScalar() is not null;
    }

    private bool HasMeta(string sourceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sideload_scan_meta WHERE source_id=$sourceId;";
        command.Parameters.AddWithValue("$sourceId", sourceId);
        return command.ExecuteScalar() is not null;
    }

    private void SaveMetaFor(string sourceId, SideloadScanMeta meta)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sideload_scan_meta
                (source_id, last_scan_time_ticks, status, found_count, failed_count, error, result_applied)
            VALUES ($sourceId, $time, $status, $found, $failed, $error, $applied);
            """;
        command.Parameters.AddWithValue("$sourceId", sourceId);
        command.Parameters.AddWithValue("$time", meta.LastScanTime.Ticks);
        command.Parameters.AddWithValue("$status", (int)meta.Status);
        command.Parameters.AddWithValue("$found", meta.FoundCount);
        command.Parameters.AddWithValue("$failed", meta.FailedCount);
        command.Parameters.AddWithValue("$error", (object?)meta.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$applied", meta.ResultApplied ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private static void MarkMigrated(string path)
    {
        var backup = path + ".migrated";
        if (!File.Exists(backup))
            File.Move(path, backup);
    }

    private bool TryReadLegacyDatabase(
        string sourceId, out Dictionary<string, SideloadEntry> entries, out DateTime? lastFullScan)
    {
        var path = LegacyJsonPathFor(sourceId);
        if (!File.Exists(path) && sourceId == GameProfiles.Hs2.SideloadSourceId)
            path = Path.Combine(_config.ConfigDir, "sideload.json");
        return TryReadDatabaseJson(path, out entries, out lastFullScan);
    }

    private static bool TryReadDatabaseJson(
        string path, out Dictionary<string, SideloadEntry> entries, out DateTime? lastFullScan)
    {
        entries = new();
        lastFullScan = null;
        if (!File.Exists(path))
            return false;
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("Entries", out _))
        {
            var file = JsonSerializer.Deserialize<DatabaseFile>(json);
            if (file is null)
                return false;
            entries = NormalizeEntries(file.Entries);
            lastFullScan = file.LastFullScan;
            return true;
        }
        var legacy = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (legacy is null)
            return false;
        entries = FromFlatDictionary(legacy);
        return true;
    }
}
