using System.Windows.Media.Imaging;

namespace HS2Tools.Controls;

/// <summary>
/// 缩略图 LRU 缓存（控内存：网格可滚动数千张卡片，解码后的位图只保留最近使用的若干张）。
/// 非线程安全——只在 UI 线程使用（控件回调路径）。
/// </summary>
public class ThumbnailCache
{
    /// <summary>默认容量：500 张缩略图（约 500 × 几百 KB 解码位图，内存量级百 MB 内可控）</summary>
    public const int DefaultCapacity = 500;

    private readonly int _capacity;
    private readonly LinkedList<(string Key, BitmapImage Value)> _lru = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, BitmapImage Value)>> _map =
        new(StringComparer.OrdinalIgnoreCase);

    public ThumbnailCache(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public int Count => _map.Count;

    /// <summary>命中则提到最前；未命中返回 null</summary>
    public BitmapImage? Get(string key)
    {
        if (!_map.TryGetValue(key, out var node))
            return null;
        _lru.Remove(node);
        _lru.AddFirst(node);
        return node.Value.Value;
    }

    /// <summary>写入（已存在则刷新并提到最前）；超容量从末尾淘汰</summary>
    public void Set(string key, BitmapImage value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            existing.Value = (key, value);
            _lru.Remove(existing);
            _lru.AddFirst(existing);
            return;
        }

        var node = _lru.AddFirst((key, value));
        _map[key] = node;

        while (_map.Count > _capacity)
        {
            var last = _lru.Last!;
            _lru.RemoveLast();
            _map.Remove(last.Value.Key);
        }
    }

    public void Clear()
    {
        _lru.Clear();
        _map.Clear();
    }

    /// <summary>base64 PNG → BitmapImage（Freeze 后跨线程可用）；数据非法返回 null</summary>
    public static BitmapImage? DecodeBase64(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
            return null;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // 立即解码，流随即释放
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null; // 乱码/截断卡片按"暂无预览"处理（对应原版 img onError）
        }
    }
}
