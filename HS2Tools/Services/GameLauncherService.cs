using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HS2Tools.Services;

/// <summary>
/// 启动游戏/工作室、打开所在文件夹、防休眠（对应 app.go 系统功能）。
/// 配置统一后，启动游戏从 ConfigService 读路径——修掉原版配置双轨制导致功能不可用的问题（A1）。
/// </summary>
public class GameLauncherService
{
    private readonly ConfigService _config;

    public GameLauncherService(ConfigService config)
    {
        _config = config;
    }

    /// <summary>启动当前游戏（exe 名取当前 GameProfile，工作目录 = 游戏目录）</summary>
    public void LaunchGame() => Launch(_config.CurrentProfile.GameExeName);

    /// <summary>启动当前游戏的工作室</summary>
    public void LaunchStudio() => Launch(_config.CurrentProfile.StudioExeName);

    private void Launch(string exeName)
    {
        var gamePath = _config.Settings.Current.GamePath;
        if (string.IsNullOrEmpty(gamePath))
            throw new InvalidOperationException("游戏路径未设置");

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(gamePath, exeName),
            WorkingDirectory = gamePath,
            UseShellExecute = true,
        });
    }

    /// <summary>在文件管理器中显示文件（explorer /select,）</summary>
    public void OpenInFolder(string filePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = true,
        });
    }

    // ==================== 防休眠（替代原版 F15 按键 hack） ====================

    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    /// <summary>阻止 Windows 休眠（SetThreadExecutionState ES_CONTINUOUS | ES_SYSTEM_REQUIRED）</summary>
    public virtual void PreventSleep() => SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);

    /// <summary>恢复 Windows 休眠（ES_CONTINUOUS）</summary>
    public virtual void AllowSleep() => SetThreadExecutionState(ES_CONTINUOUS);
}
