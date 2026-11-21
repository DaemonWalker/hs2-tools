using CommunityToolkit.Mvvm.ComponentModel;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

public partial class SimilarModMatchItem : ObservableObject
{
    public required SimilarModMatch Match { get; init; }

    public string RequiredGuid => Match.RequiredGuid;
    public string CandidateGuid => Match.CandidateGuid;
    public string MatchDescription => Match.MatchDescription;
    public string Source => Match.IsLocal ? "本地" : "Sideload";
    public string Operation => Match.IsLocal ? "复制并改写" : "下载并改写";
    public string Name => Match.LocalMod?.Name ?? "—";
    public string Version => Match.LocalMod?.Version ?? "—";
    public string Location => Match.LocalMod?.Path ?? Match.RelativeUrl ?? "";

    [ObservableProperty]
    private bool _isSelected;

    internal Action<SimilarModMatchItem>? SelectionChanged { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            SelectionChanged?.Invoke(this);
    }
}
