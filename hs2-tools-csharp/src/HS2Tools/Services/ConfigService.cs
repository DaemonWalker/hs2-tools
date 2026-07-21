using System.Text.Json;
using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>
/// 统一配置服务（对应迁移方案 §7）。
/// 读写 %AppData%/hs2-tools/settings.json，强类型 AppSettings；
/// 改动即发 Changed 事件并防抖落盘。
/// </summary>
public class ConfigService : IDisposable
{
    // 相对路径常量与 exe 名照搬原版
    public const string GameExeName = "HoneySelect2.exe";
    public const string StudioExeName = "StudioNEOV2.exe";
    public const string SceneDirRelative = @"UserData\Studio\scene";
    public const string CharaDirRelative = @"UserData\chara\female";
    public const string ModsDirRelative = "mods";
    public const string ModDownloadDirRelative = @"mods\hs2-tool-download";
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
        Settings = Load();
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static string DefaultConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hs2-tools");

    public string SettingsPath => Path.Combine(_configDir, SettingsFileName);

    /// <summary>当前配置（启动时加载；通过 Update 修改）</summary>
    public AppSettings Settings { get; }

    /// <summary>配置改动事件（在调用 Update 的线程上触发，订阅方自行封送 UI 线程）</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 加载配置。原版语义：文件不存在返回空配置而非异常；
    /// 文件损坏时同样回退空配置（避免应用无法启动）。
    /// </summary>
    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
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
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
        _dirty = false;
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

    // ---- 派生路径（GamePath 未设置时返回 null）----

    public string? GetCharaDir() => GamePathOrNull(CharaDirRelative);
    public string? GetSceneDir() => GamePathOrNull(SceneDirRelative);
    public string? GetModsDir() => GamePathOrNull(ModsDirRelative);
    public string? GetModDownloadDir() => GamePathOrNull(ModDownloadDirRelative);

    private string? GamePathOrNull(string relative) =>
        string.IsNullOrEmpty(Settings.GamePath) ? null : Path.Combine(Settings.GamePath, relative);

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
