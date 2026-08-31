using HS2Tools.Services;
using HS2Tools.ViewModels;

namespace HS2Tools.Tests;

public class CardExplorerViewModelTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();

    public void Dispose() => TestAssets.DeleteDir(_dir);

    /// <summary>合法 1x1 透明 PNG（完整文件，IEND 在前，游戏数据标记追加在后）</summary>
    private static byte[] RealPng() => new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63,
        0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    };

    private CardExplorerViewModel MakeVm(ConfigService config) =>
        new(config, new ScannerService());

    [Fact]
    public async Task LoadCharaCard_PopulatesInfoModsAndGameData()
    {
        var card = TestAssets.WritePng(_dir, "c1.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("张三"),
            TestAssets.ModMarker("g1"), TestAssets.ModMarker("g2"));

        using var config = new ConfigService(_dir);
        config.Update(s => s.Current.LocalMods["g1"] = new() { Name = "Owned" });
        var vm = MakeVm(config);

        vm.FilePath = card;
        await vm.LoadTask!;

        Assert.True(vm.HasCard);
        Assert.True(vm.ShowContent);
        Assert.False(vm.ShowEmpty);
        Assert.False(vm.HasError);
        Assert.Equal("张三", vm.CharaName);
        Assert.False(vm.HasDescription); // 只有一个名称 → 无描述
        Assert.EndsWith("B", vm.GameDataSizeText); // FormatBytes 单位
        Assert.Equal(2, vm.ModCount);
        Assert.Equal(1, vm.ModLocalCount);
        Assert.Equal(1, vm.ModMissingCount);
        Assert.True(vm.ModItems.Single(i => i.Guid == "g1").IsLocal);
        Assert.Equal("已拥有", vm.ModItems.Single(i => i.Guid == "g1").StatusText);
        Assert.Equal("缺失", vm.ModItems.Single(i => i.Guid == "g2").StatusText);
    }

    [Fact]
    public async Task LoadSceneCard_PopulatesNumberedCharaList()
    {
        var card = TestAssets.WritePng(_dir, "s1.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("甲"), TestAssets.NameMarker("乙"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.IsSceneMode = true;
        Assert.False(vm.IsCharaMode);

        vm.FilePath = card;
        await vm.LoadTask!;

        Assert.True(vm.HasCard);
        Assert.Equal(2, vm.SceneCharaCount);
        Assert.Equal(2, vm.SceneCharaNames.Count);
        // 名称为无序集合，编号随解析顺序；两种顺序都接受
        var joined = string.Join("|", vm.SceneCharaNames);
        Assert.Contains("甲", joined);
        Assert.Contains("乙", joined);
        Assert.All(vm.SceneCharaNames, n => Assert.Matches(@"^\d+\. ", n));
    }

    [Fact]
    public async Task SwitchMode_ClearsFileAndCard()
    {
        var card = TestAssets.WritePng(_dir, "c1.png",
            TestAssets.PngPrefix(), TestAssets.NameMarker("张三"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.FilePath = card;
        await vm.LoadTask!;
        Assert.True(vm.HasCard);

        vm.IsSceneMode = true; // 对应原版切换类型时清空文件与展示

        Assert.Equal("", vm.FilePath);
        Assert.False(vm.HasCard);
        Assert.True(vm.ShowEmpty);
        Assert.Equal(0, vm.ModCount);
        Assert.Equal(0, vm.SceneCharaCount);
    }

    [Fact]
    public async Task NonExistentPath_ShowsEmptyWithoutError()
    {
        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);

        vm.FilePath = Path.Combine(_dir, "missing.png");
        await vm.LoadTask!;

        Assert.True(vm.ShowEmpty); // 照原版：文件不存在回空态，不报错
        Assert.False(vm.HasError);
        Assert.False(vm.HasCard);
    }

    [Fact]
    public async Task InvalidPng_ShowsError()
    {
        // 有 .png 扩展名但无 IEND 标记 → ParsePngData 抛 InvalidDataException
        var bad = TestAssets.WritePng(_dir, "bad.png", TestAssets.NameMarker("坏"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);

        vm.FilePath = bad;
        await vm.LoadTask!;

        Assert.True(vm.HasError);
        Assert.Contains("无法解析卡片文件", vm.ErrorMessage);
        Assert.False(vm.HasCard);
        Assert.False(vm.ShowEmpty);
    }

    [Fact]
    public async Task RealPng_DecodesPreviewImage()
    {
        var card = TestAssets.WritePng(_dir, "real.png", RealPng(), TestAssets.NameMarker("爱"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);

        vm.FilePath = card;
        await vm.LoadTask!;

        Assert.True(vm.HasCard);
        Assert.NotNull(vm.CardImage); // base64 → BitmapImage 解码成功
        Assert.Equal("爱", vm.CharaName);
    }

    [Fact]
    public async Task ConfigChanged_RefreshesModLocalState()
    {
        var card = TestAssets.WritePng(_dir, "c1.png",
            TestAssets.PngPrefix(), TestAssets.ModMarker("g1"), TestAssets.ModMarker("g2"));

        using var config = new ConfigService(_dir);
        var vm = MakeVm(config);
        vm.FilePath = card;
        await vm.LoadTask!;
        Assert.Equal(0, vm.ModLocalCount);

        // 其他窗口扫描完成 → LocalMods 变化 → "已拥有"标记刷新
        config.Update(s => s.Current.LocalMods["g2"] = new() { Name = "New" });

        Assert.Equal(1, vm.ModLocalCount);
        Assert.True(vm.ModItems.Single(i => i.Guid == "g2").IsLocal);
    }
}
