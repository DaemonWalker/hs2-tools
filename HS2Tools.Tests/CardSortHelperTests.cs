using HS2Tools.Controls;

namespace HS2Tools.Tests;

public class CardSortHelperTests
{
    private static readonly string[] Paths =
    [
        @"D:\cards\beta.png",
        @"D:\cards\Alpha.png",
        @"D:\cards\子.png",
        @"D:\cards\啊.png",
    ];

    [Fact]
    public void NormalizePath_SlashesAndCase()
    {
        Assert.Equal("d:/cards/alpha.png", CardSortHelper.NormalizePath(@"D:\cards\Alpha.png"));
    }

    [Fact]
    public void Filter_FileNameContains_CaseInsensitive()
    {
        var result = CardSortHelper.FilterAndSort(Paths, "ALPHA", CardSortType.NameAsc, null);
        Assert.Equal([@"D:\cards\Alpha.png"], result);
    }

    [Fact]
    public void Filter_EmptyQuery_KeepsAll()
    {
        var result = CardSortHelper.FilterAndSort(Paths, "  ", CardSortType.NameAsc, null);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Sort_NameAsc_UsesZhCn()
    {
        var result = CardSortHelper.FilterAndSort(Paths, "", CardSortType.NameAsc, null);
        // ICU zh-CN 排序：汉字按拼音（啊 a < 子 zi）排在拉丁字母前；与浏览器 localeCompare 一致
        Assert.Equal(
            [@"D:\cards\啊.png", @"D:\cards\子.png", @"D:\cards\Alpha.png", @"D:\cards\beta.png"],
            result);
    }

    [Fact]
    public void Sort_NameDesc_Reverses()
    {
        var result = CardSortHelper.FilterAndSort(Paths, "", CardSortType.NameDesc, null);
        Assert.Equal(
            [@"D:\cards\beta.png", @"D:\cards\Alpha.png", @"D:\cards\子.png", @"D:\cards\啊.png"],
            result);
    }

    [Fact]
    public void Sort_Path_UsesZhCn()
    {
        var result = CardSortHelper.FilterAndSort(
            new[] { @"D:\b\2.png", @"D:\a\2.png" }, "", CardSortType.Path, null);
        Assert.Equal([@"D:\a\2.png", @"D:\b\2.png"], result);
    }

    [Fact]
    public void Sort_FavoriteFirst_BothGroupsNameSorted()
    {
        var favorites = new[] { @"d:/cards/子.png" }; // 规范化匹配：大小写/分隔符不敏感
        var result = CardSortHelper.FilterAndSort(Paths, "", CardSortType.Favorite, favorites);

        Assert.Equal(@"D:\cards\子.png", result[0]); // 收藏组在前
        Assert.Equal(
            [@"D:\cards\啊.png", @"D:\cards\Alpha.png", @"D:\cards\beta.png"],
            result.Skip(1).ToArray());
    }

    [Fact]
    public void ToggleFavorite_Add_Prepends()
    {
        var result = CardSortHelper.ToggleFavorite(["b.png"], "a.png");
        Assert.Equal(["a.png", "b.png"], result);
    }

    [Fact]
    public void ToggleFavorite_Remove_CaseInsensitive()
    {
        var result = CardSortHelper.ToggleFavorite([@"D:\Cards\A.PNG"], @"d:\cards\a.png");
        Assert.Empty(result);
    }
}
