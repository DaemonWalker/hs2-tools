using System.Globalization;

namespace HS2Tools.Controls;

/// <summary>卡片排序方式（对应原版 SortType）</summary>
public enum CardSortType
{
    /// <summary>收藏优先（默认）：收藏组在前，两组内各自按名称排序</summary>
    Favorite,
    /// <summary>名称 A-Z</summary>
    NameAsc,
    /// <summary>名称 Z-A</summary>
    NameDesc,
    /// <summary>路径排序</summary>
    Path,
}

/// <summary>
/// 卡片网格的过滤/排序/收藏辅助（纯函数，对应原版 CardGrid.tsx 的 sortedPaths 与 toggleFavorite）。
/// 排序键为文件名（不含扩展名），中文比较用 zh-CN 区域对齐原版 localeCompare 行为。
/// </summary>
public static class CardSortHelper
{
    /// <summary>zh-CN 比较器（对齐原版 localeCompare(name, 'zh-CN')）</summary>
    private static readonly StringComparer ZhCnComparer =
        StringComparer.Create(new CultureInfo("zh-CN"), CompareOptions.None);

    /// <summary>路径规范化：反斜杠转正斜杠并小写（收藏匹配大小写/分隔符不敏感，对应原版 normalizePath）</summary>
    public static string NormalizePath(string path) => path.Replace('\\', '/').ToLowerInvariant();

    /// <summary>文件名（不含扩展名），排序与搜索的键</summary>
    public static string FileNameKey(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>搜索过滤（文件名包含，大小写不敏感）+ 排序，返回新列表</summary>
    public static List<string> FilterAndSort(
        IEnumerable<string> paths, string? searchText, CardSortType sortType, IReadOnlyCollection<string>? favorites)
    {
        var query = (searchText ?? "").Trim();
        var filtered = paths;
        if (query.Length > 0)
        {
            filtered = filtered.Where(p =>
                FileNameKey(p).Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return sortType switch
        {
            CardSortType.NameAsc => filtered.OrderBy(FileNameKey, ZhCnComparer).ToList(),
            CardSortType.NameDesc => filtered.OrderByDescending(FileNameKey, ZhCnComparer).ToList(),
            CardSortType.Path => filtered.OrderBy(p => p, ZhCnComparer).ToList(),
            _ => SortFavoriteFirst(filtered, favorites),
        };
    }

    /// <summary>收藏优先：规范化匹配的收藏组在前，两组内各自按名称 zh-CN 排序</summary>
    private static List<string> SortFavoriteFirst(IEnumerable<string> paths, IReadOnlyCollection<string>? favorites)
    {
        var normalizedFavorites = new HashSet<string>((favorites ?? (IReadOnlyCollection<string>)Array.Empty<string>())
            .Select(NormalizePath));

        var fav = new List<string>();
        var rest = new List<string>();
        foreach (var p in paths)
        {
            if (normalizedFavorites.Contains(NormalizePath(p)))
                fav.Add(p);
            else
                rest.Add(p);
        }

        return fav.OrderBy(FileNameKey, ZhCnComparer)
            .Concat(rest.OrderBy(FileNameKey, ZhCnComparer))
            .ToList();
    }

    /// <summary>
    /// 切换收藏（对应原版 toggleFavorite）：匹配按规范化路径（大小写/分隔符不敏感）；
    /// 已收藏则移除，未收藏则插到最前。返回新列表。
    /// </summary>
    public static List<string> ToggleFavorite(IReadOnlyList<string> favorites, string path)
    {
        var norm = NormalizePath(path);
        var exists = favorites.Any(p => NormalizePath(p) == norm);
        if (exists)
            return favorites.Where(p => NormalizePath(p) != norm).ToList();

        var result = new List<string> { path };
        result.AddRange(favorites);
        return result;
    }
}
