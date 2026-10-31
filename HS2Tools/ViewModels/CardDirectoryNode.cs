using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HS2Tools.ViewModels;

public partial class CardDirectoryNode : ObservableObject
{
    public CardDirectoryNode(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<CardDirectoryNode> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public event EventHandler? Selected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            Selected?.Invoke(this, EventArgs.Empty);
    }
}

internal static class CardDirectoryTree
{
    public static CardDirectoryNode Build(string rootPath, IReadOnlyList<string> directoryPaths)
    {
        var root = new CardDirectoryNode("根目录", rootPath);
        var nodes = new Dictionary<string, CardDirectoryNode>(StringComparer.OrdinalIgnoreCase)
        {
            [rootPath] = root,
        };

        foreach (var path in directoryPaths.OrderBy(p => p.Length))
        {
            if (nodes.ContainsKey(path))
                continue;
            var node = new CardDirectoryNode(Path.GetFileName(path), path);
            nodes.Add(path, node);
            var parentPath = Path.GetDirectoryName(path);
            if (parentPath is not null && nodes.TryGetValue(parentPath, out var parent))
                parent.Children.Add(node);
        }

        SortChildren(root);
        return root;
    }

    public static List<string> FilterDirectPaths(IEnumerable<string> paths, string directoryPath) =>
        paths.Where(path => string.Equals(
                Path.GetDirectoryName(path), directoryPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static void SortChildren(CardDirectoryNode node)
    {
        var sorted = node.Children.OrderBy(child => child.Name, StringComparer.Ordinal).ToList();
        node.Children.Clear();
        foreach (var child in sorted)
        {
            node.Children.Add(child);
            SortChildren(child);
        }
    }
}
