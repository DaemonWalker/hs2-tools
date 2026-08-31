namespace HS2Tools.ViewModels;

/// <summary>字节/速度/时间格式化（对应原版 utils/format.ts，1024 进制、2 位小数）</summary>
public static class FormatUtils
{
    private static readonly string[] Sizes = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>对应原版 formatBytes：0 → "0 B"；负数 → "Unknown"</summary>
    public static string FormatBytes(double bytes, int decimals = 2)
    {
        if (bytes == 0)
            return "0 B";
        if (bytes < 0)
            return "Unknown";

        var i = (int)Math.Floor(Math.Log(bytes) / Math.Log(1024));
        i = Math.Min(i, Sizes.Length - 1);
        var value = Math.Round(bytes / Math.Pow(1024, i), decimals);
        return $"{value} {Sizes[i]}";
    }

    /// <summary>对应原版 formatSpeed</summary>
    public static string FormatSpeed(double bytesPerSecond) => FormatBytes(bytesPerSecond) + "/s";

    /// <summary>对应原版 formatTime：负/非有限 → "--:--"；超 99 分钟 → "99:59+"</summary>
    public static string FormatTime(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            return "--:--";
        var mins = (int)Math.Floor(seconds / 60);
        if (mins > 99)
            return "99:59+";
        var secs = (int)Math.Floor(seconds % 60);
        return $"{mins:D2}:{secs:D2}";
    }

    /// <summary>对应原版 estimateRemainingTime（total/speed 非正返回 -1）</summary>
    public static double EstimateRemainingTime(long downloaded, long total, double speed)
    {
        if (total <= 0 || speed <= 0)
            return -1;
        return (total - downloaded) / speed;
    }
}
