using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HS2Tools.Controls;

/// <summary>网格中一张已加载卡片的视图模型（名称/缩略图/收藏/选中）</summary>
public partial class CardItemViewModel : ObservableObject
{
    /// <summary>卡片 PNG 完整路径</summary>
    public required string Path { get; init; }

    /// <summary>展示名（PNG 解析出的角色名；解析不到时回退文件名）</summary>
    [ObservableProperty]
    private string _displayName = "";

    /// <summary>缩略图（无图/解码失败为 null → 占位显示）</summary>
    [ObservableProperty]
    private BitmapImage? _thumbnail;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isSelected;
}
