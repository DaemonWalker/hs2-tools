using System.Net;

namespace HS2Tools.Services;

/// <summary>
/// 代理解析（供 DownloaderService / SideloaderService 共用）。
/// 代理串可能带认证（proto://user:pass@host，由 ConfigService.GetProxyString 拼接），
/// .NET 不会自动使用 URI 中的 userinfo，需显式设置 Credentials。
/// </summary>
internal static class ProxyHelper
{
    /// <summary>构建代理。URL 非法时抛异常（对应 Go downloader.NewDownloader 的行为）。</summary>
    public static IWebProxy? BuildProxy(string? proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl))
            return null;

        var uri = new Uri(proxyUrl, UriKind.Absolute);
        var proxy = new WebProxy(uri);

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = Uri.UnescapeDataString(uri.UserInfo).Split(':', 2);
            proxy.Credentials = new NetworkCredential(parts[0], parts.Length > 1 ? parts[1] : "");
        }

        return proxy;
    }

    /// <summary>构建代理，解析失败时返回 null（对应 Go sideloader.NewSideloader 忽略解析错误的行为）。</summary>
    public static IWebProxy? BuildProxyOrNull(string? proxyUrl)
    {
        try
        {
            return BuildProxy(proxyUrl);
        }
        catch
        {
            return null;
        }
    }
}
