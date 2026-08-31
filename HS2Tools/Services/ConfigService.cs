using System.Text.Json;
using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>
/// 统一配置服务（对应迁移方案 §7）。
/// 读写 %AppData%/hs2-tools/settings.json，强类型 AppSettings；
/// 改动即发 Changed 事件并防抖落盘。
/// 多游戏：exe 名/相对目录等游戏特定知识已收编到 GameProfiles（Models/GameProfile.cs），
/// 本类只按当前游戏（Settings.CurrentGame）求值。
/// </summary>
public class ConfigService : IDisposable
{
    public const string SettingsFileName = "settings.json";

    /// <summary>防抖落盘间隔</summary>
    private const int SaveDebounceMs = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _configDir;
    private readonly object _sync = new();
    private readonly Timer _saveTimer;
    private bool _dirty;
    private bool _disposed;

    public ConfigService(string? configDir = null)
    {
        _configDir = configDir ?? DefaultConfigDir;
        Directory.CreateDirectory(_configDir);
        var migrated = false;
        Settings = Load(ref migrated);
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        // 旧 schema 迁移后立即落盘，避免崩溃丢迁移结果
        if (migrated)
            Save();
    }

    public static string DefaultConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hs2-tools");

    /// <summary>配置目录（settings.json 与 sideload.json 所在目录）</summary>
    public string ConfigDir => _configDir;

    public string SettingsPath => Path.Combine(_configDir, SettingsFileName);

    /// <summary>当前配置（启动时加载；通过 Update 修改）</summary>
    public AppSettings Settings { get; }

    /// <summary>配置改动事件（在调用 Update 的线程上触发，订阅方自行封送 UI 线程）</summary>
    public event EventHandler? Changed;

    /// <summary>当前游戏档案（未知 CurrentGame 回退 HS2）</summary>
    public GameProfile CurrentProfile => GameProfiles.Get(Settings.CurrentGame);

    /// <summary>
    /// 加载配置。原版语义：文件不存在返回空配置而非异常；
    /// 文件损坏时同样回退空配置（避免应用无法启动）。
    /// 旧版单游戏 schema（顶层 gamePath/favorites/localMods/modUsage）自动迁移到 Games["hs2"]。
    /// </summary>
    private AppSettings Load(ref bool migrated)
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            migrated = MigrateLegacy(json, settings);
            return settings;
        }
        catch (Exception ex)
        {
            ErrorLog.Log(ex); // 配置损坏回退空配置留痕
            return new AppSettings();
        }
    }

    /// <summary>
    /// 旧版单游戏配置迁移：顶层 gamePath/favorites/localMods/modUsage → Games["hs2"]。
    /// 已含 "games" 字段即视为新 schema，不动。返回是否发生了迁移。
    /// </summary>
    private static bool MigrateLegacy(string json, AppSettings settings)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("games", out _))
                return false;

            var data = new GameData();
            var any = false;
            if (root.TryGetProperty("gamePath", out var gp) && gp.ValueKind == JsonValueKind.String)
            {
                data.GamePath = gp.GetString() ?? "";
                any = true;
            }
            if (root.TryGetProperty("favorites", out var fav) && fav.ValueKind == JsonValueKind.Array)
            {
                data.Favorites = fav.Deserialize<List<string>>(JsonOptions) ?? new();
                any = true;
            }
            if (root.TryGetProperty("localMods", out var lm) && lm.ValueKind == JsonValueKind.Object)
            {
                data.LocalMods = lm.Deserialize<Dictionary<string, ModInfo>>(JsonOptions) ?? new();
                any = true;
            }
            if (root.TryGetProperty("modUsage", out var mu) && mu.ValueKind == JsonValueKind.Object)
            {
                data.ModUsage = mu.Deserialize<Dictionary<string, int>>(JsonOptions) ?? new();
                any = true;
            }

            if (!any)
                return false;
            settings.Games[GameProfiles.Hs2.Id] = data;
            settings.CurrentGame = GameProfiles.Hs2.Id;
            return true;
        }
        catch (Exception ex)
        {
            // 迁移失败不阻断启动（旧字段本就会被忽略），但留痕
            ErrorLog.Log($"settings.json legacy migration failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>修改配置：触发 Changed 事件并防抖落盘。</summary>
    public void Update(Action<AppSettings> mutate)
    {
        lock (_sync)
        {
            mutate(Settings);
            _dirty = true;
            _saveTimer.Change(SaveDebounceMs, Timeout.Infinite);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>立即落盘。</summary>
    public void Save()
    {
        lock (_sync)
        {
            _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
            WriteLocked();
        }
    }

    private void Flush()
    {
        lock (_sync)
        {
            if (_disposed || !_dirty)
                return;
            WriteLocked();
        }
    }

    private void WriteLocked()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
            _dirty = false;
        }
        catch (Exception ex)
        {
            // 写盘失败保持脏标记（下次 Update 的防抖会重试）并记日志，永不抛出——
            // Flush 跑在线程池 Timer 回调上，抛出即进程崩溃；Save/Dispose 同理吞掉
            ErrorLog.Log(ex);
        }
    }

    /// <summary>代理串（含认证），复刻 app.go getProxyString：替换 protocol:// 为 protocol://user:pass@</summary>
    public string GetProxyString()
    {
        var proxy = Settings.Proxy.Uri;
        var auth = "";
        if (!string.IsNullOrEmpty(Settings.Proxy.Username) && !string.IsNullOrEmpty(Settings.Proxy.Password))
            auth = $"{Settings.Proxy.Username}:{Settings.Proxy.Password}";

        if (auth != "" && proxy != "")
        {
            foreach (var proto in new[] { "http://", "https://", "socks5://" })
            {
                if (proxy.StartsWith(proto, StringComparison.Ordinal))
                    return proto + auth + "@" + proxy[proto.Length..];
            }
        }
        return proxy;
    }

    // ---- 派生路径（当前游戏路径未设置时返回 null）----

    public string? GetCharaDir() => GamePathOrNull(CurrentProfile.CharaDirRelative);
    public string? GetSceneDir() => GamePathOrNull(CurrentProfile.SceneDirRelative);
    public string? GetModsDir() => GamePathOrNull(CurrentProfile.ModsDirRelative);
    public string? GetModDownloadDir() => GamePathOrNull(CurrentProfile.ModDownloadDirRelative);

    private string? GamePathOrNull(string relative)
    {
        var gamePath = Settings.Current.GamePath;
        return string.IsNullOrEmpty(gamePath) ? null : Path.Combine(gamePath, relative);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _saveTimer.Dispose();
            // 退出前把未落盘的改动写掉
            if (_dirty)
                WriteLocked();
        }
        GC.SuppressFinalize(this);
    }
}
