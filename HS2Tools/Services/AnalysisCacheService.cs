using System.Text.Json;
using HS2Tools.Models;
using Microsoft.Data.Sqlite;

namespace HS2Tools.Services;

/// <summary>可重建的 Mod/卡片分析结果缓存；文件指纹或缓存版本不匹配时回退完整解析。</summary>
public sealed class AnalysisCacheService : IDisposable
{
    private const int CacheVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly object _sync = new();
    private SqliteConnection? _connection;

    public AnalysisCacheService(string configDir)
    {
        try
        {
            Directory.CreateDirectory(configDir);
            _connection = new SqliteConnection($"Data Source={Path.Combine(configDir, "analysis-cache.db")}");
            _connection.Open();
            Initialize();
        }
        catch (Exception ex)
        {
            ErrorLog.Log($"分析缓存初始化失败，本次运行禁用缓存: {ex.Message}");
            _connection?.Dispose();
            _connection = null;
        }
    }

    private void Initialize()
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar());
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS card_cache (
                game_id TEXT NOT NULL,
                scope TEXT NOT NULL,
                path TEXT NOT NULL COLLATE NOCASE,
                file_length INTEGER NOT NULL,
                last_write_utc_ticks INTEGER NOT NULL,
                names_json TEXT NOT NULL,
                mod_ids_json TEXT NOT NULL,
                game_data_len INTEGER NOT NULL,
                PRIMARY KEY (game_id, scope, path)
            );
            CREATE TABLE IF NOT EXISTS zipmod_cache (
                game_id TEXT NOT NULL,
                scope TEXT NOT NULL,
                path TEXT NOT NULL COLLATE NOCASE,
                file_length INTEGER NOT NULL,
                last_write_utc_ticks INTEGER NOT NULL,
                guid TEXT NOT NULL,
                info_json TEXT NOT NULL,
                PRIMARY KEY (game_id, scope, path)
            );
            """;
        command.ExecuteNonQuery();
        if (version != CacheVersion)
        {
            command.CommandText = "DELETE FROM card_cache; DELETE FROM zipmod_cache;";
            command.ExecuteNonQuery();
            command.CommandText = $"PRAGMA user_version={CacheVersion};";
            command.ExecuteNonQuery();
        }
    }

    public static bool IsCurrent(string path, long fileLength, long lastWriteTimeUtcTicks)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == fileLength && info.LastWriteTimeUtc.Ticks == lastWriteTimeUtcTicks;
        }
        catch
        {
            return false;
        }
    }

    public Dictionary<string, CardAnalysisEntry> GetCards(string gameId, string scope)
    {
        var results = new Dictionary<string, CardAnalysisEntry>(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            if (_connection is null)
                return results;
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = """
                    SELECT path, file_length, last_write_utc_ticks, names_json, mod_ids_json, game_data_len
                    FROM card_cache WHERE game_id=$gameId AND scope=$scope;
                    """;
                command.Parameters.AddWithValue("$gameId", gameId);
                command.Parameters.AddWithValue("$scope", scope);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var entry = new CardAnalysisEntry
                    {
                        Scope = scope,
                        Path = reader.GetString(0),
                        FileLength = reader.GetInt64(1),
                        LastWriteTimeUtcTicks = reader.GetInt64(2),
                        Names = JsonSerializer.Deserialize<List<string>>(reader.GetString(3), JsonOptions) ?? new(),
                        ModIDs = JsonSerializer.Deserialize<List<string>>(reader.GetString(4), JsonOptions) ?? new(),
                        GameDataLen = reader.GetInt32(5),
                    };
                    results[entry.Path] = entry;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"读取卡片分析缓存失败，回退完整解析: {ex.Message}");
                results.Clear();
            }
        }
        return results;
    }

    public PngParseResult? TryGetCurrentCard(string gameId, string scope, string path)
    {
        lock (_sync)
        {
            if (_connection is null)
                return null;
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = """
                    SELECT file_length, last_write_utc_ticks, names_json, mod_ids_json, game_data_len
                    FROM card_cache WHERE game_id=$gameId AND scope=$scope AND path=$path;
                    """;
                command.Parameters.AddWithValue("$gameId", gameId);
                command.Parameters.AddWithValue("$scope", scope);
                command.Parameters.AddWithValue("$path", path);
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    return null;
                var length = reader.GetInt64(0);
                var ticks = reader.GetInt64(1);
                if (!IsCurrent(path, length, ticks))
                    return null;
                return new PngParseResult
                {
                    Path = path,
                    FileLength = length,
                    LastWriteTimeUtcTicks = ticks,
                    CharaNames = JsonSerializer.Deserialize<List<string>>(reader.GetString(2), JsonOptions) ?? new(),
                    ModIDs = JsonSerializer.Deserialize<List<string>>(reader.GetString(3), JsonOptions) ?? new(),
                    GameDataLen = reader.GetInt32(4),
                };
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"读取单卡缓存失败，回退完整解析: {ex.Message}");
                return null;
            }
        }
    }

    public void ReplaceCards(string gameId, string scope, IReadOnlyCollection<CardAnalysisEntry> entries)
    {
        lock (_sync)
        {
            if (_connection is null)
                return;
            try
            {
                using var transaction = _connection.BeginTransaction();
                using (var delete = _connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM card_cache WHERE game_id=$gameId AND scope=$scope;";
                    delete.Parameters.AddWithValue("$gameId", gameId);
                    delete.Parameters.AddWithValue("$scope", scope);
                    delete.ExecuteNonQuery();
                }
                foreach (var entry in entries)
                    UpsertCardCore(gameId, scope, entry, transaction);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"写入卡片分析缓存失败: {ex.Message}");
            }
        }
    }

    public void UpsertCard(string gameId, string scope, CardAnalysisEntry entry)
    {
        lock (_sync)
        {
            if (_connection is null)
                return;
            try
            {
                UpsertCardCore(gameId, scope, entry, transaction: null);
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"写入单卡缓存失败: {ex.Message}");
            }
        }
    }

    private void UpsertCardCore(string gameId, string scope, CardAnalysisEntry entry, SqliteTransaction? transaction)
    {
        using var command = _connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO card_cache
                (game_id, scope, path, file_length, last_write_utc_ticks, names_json, mod_ids_json, game_data_len)
            VALUES ($gameId, $scope, $path, $length, $ticks, $names, $modIds, $gameDataLen)
            ON CONFLICT(game_id, scope, path) DO UPDATE SET
                file_length=$length, last_write_utc_ticks=$ticks, names_json=$names,
                mod_ids_json=$modIds, game_data_len=$gameDataLen;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$path", entry.Path);
        command.Parameters.AddWithValue("$length", entry.FileLength);
        command.Parameters.AddWithValue("$ticks", entry.LastWriteTimeUtcTicks);
        command.Parameters.AddWithValue("$names", JsonSerializer.Serialize(entry.Names, JsonOptions));
        command.Parameters.AddWithValue("$modIds", JsonSerializer.Serialize(entry.ModIDs, JsonOptions));
        command.Parameters.AddWithValue("$gameDataLen", entry.GameDataLen);
        command.ExecuteNonQuery();
    }

    public Dictionary<string, ZipModAnalysisEntry> GetZipMods(string gameId, string scope)
    {
        var results = new Dictionary<string, ZipModAnalysisEntry>(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            if (_connection is null)
                return results;
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = """
                    SELECT path, file_length, last_write_utc_ticks, guid, info_json
                    FROM zipmod_cache WHERE game_id=$gameId AND scope=$scope;
                    """;
                command.Parameters.AddWithValue("$gameId", gameId);
                command.Parameters.AddWithValue("$scope", scope);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var entry = new ZipModAnalysisEntry
                    {
                        Scope = scope,
                        Path = reader.GetString(0),
                        FileLength = reader.GetInt64(1),
                        LastWriteTimeUtcTicks = reader.GetInt64(2),
                        Guid = reader.GetString(3),
                        Info = JsonSerializer.Deserialize<ModInfo>(reader.GetString(4), JsonOptions) ?? new(),
                    };
                    entry.Info.Path = entry.Path;
                    entry.Info.FileLength = entry.FileLength;
                    entry.Info.LastWriteTimeUtcTicks = entry.LastWriteTimeUtcTicks;
                    results[entry.Path] = entry;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"读取 Mod 分析缓存失败，回退完整解析: {ex.Message}");
                results.Clear();
            }
        }
        return results;
    }

    public void ReplaceZipMods(string gameId, string scope, IReadOnlyCollection<ZipModAnalysisEntry> entries)
    {
        lock (_sync)
        {
            if (_connection is null)
                return;
            try
            {
                using var transaction = _connection.BeginTransaction();
                using (var delete = _connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM zipmod_cache WHERE game_id=$gameId AND scope=$scope;";
                    delete.Parameters.AddWithValue("$gameId", gameId);
                    delete.Parameters.AddWithValue("$scope", scope);
                    delete.ExecuteNonQuery();
                }
                foreach (var entry in entries)
                {
                    using var command = _connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO zipmod_cache
                            (game_id, scope, path, file_length, last_write_utc_ticks, guid, info_json)
                        VALUES ($gameId, $scope, $path, $length, $ticks, $guid, $info);
                        """;
                    command.Parameters.AddWithValue("$gameId", gameId);
                    command.Parameters.AddWithValue("$scope", scope);
                    command.Parameters.AddWithValue("$path", entry.Path);
                    command.Parameters.AddWithValue("$length", entry.FileLength);
                    command.Parameters.AddWithValue("$ticks", entry.LastWriteTimeUtcTicks);
                    command.Parameters.AddWithValue("$guid", entry.Guid);
                    command.Parameters.AddWithValue("$info", JsonSerializer.Serialize(entry.Info, JsonOptions));
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"写入 Mod 分析缓存失败: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
