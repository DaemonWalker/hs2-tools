using System.Text.Json;

namespace HS2Tools.Services;

/// <summary>
/// Sideload 数据库（guid → 远程相对路径）。
/// 加载顺序：配置目录下用户更新的 sideload.json 优先，否则用程序集内嵌的 sideload.zip。
///
/// 注意（对原版的修复）：原版爬虫结果只经 complete 事件发出且前端丢弃，
/// 前端随后 init() 重读的是内嵌旧库——"更新"实际从不生效。
/// C# 版爬取完成后把结果落盘到 %AppData%/hs2-tools/sideload.json，更新真实生效。
/// </summary>
public class SideloadDatabaseService
{
    private readonly string _filePath;
    private readonly object _sync = new();

    public SideloadDatabaseService(string configDir)
    {
        _filePath = Path.Combine(configDir, "sideload.json");
        Database = Load();
    }

    /// <summary>当前数据库（guid → 相对路径，如 "Exclusive HS2/xxx.zipmod"）</summary>
    public IReadOnlyDictionary<string, string> Database { get; private set; }

    /// <summary>数据库被替换（爬虫更新完成）时触发，在调用 Update 的线程上</summary>
    public event EventHandler? Changed;

    private IReadOnlyDictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
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
        return SideloaderService.LoadBundledDatabase();
    }

    /// <summary>用爬虫结果替换数据库并立即落盘。</summary>
    public void Update(IDictionary<string, string> database)
    {
        lock (_sync)
        {
            Database = new Dictionary<string, string>(database);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Database));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
