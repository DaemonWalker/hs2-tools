namespace HS2Tools.Services;

/// <summary>
/// 统一错误日志：追加写入配置目录（见 ConfigService.DefaultConfigDir）下的 error.log。
/// 迁移约定（方案 §9）：原版"静默吞错"点位迁移时至少记日志。
/// 自身永不抛出（日志失败不二次抛错）。
/// </summary>
public static class ErrorLog
{
    /// <summary>测试用：非 null 时改写该目录下的 error.log</summary>
    internal static string? DirectoryOverride;

    private static string LogPath =>
        Path.Combine(DirectoryOverride ?? ConfigService.DefaultConfigDir, "error.log");

    public static void Log(Exception ex) => Write(ex.ToString());

    public static void Log(string message) => Write(message);

    private static void Write(string content)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {content}\n\n");
        }
        catch
        {
            // 日志失败不二次抛错
        }
    }
}
