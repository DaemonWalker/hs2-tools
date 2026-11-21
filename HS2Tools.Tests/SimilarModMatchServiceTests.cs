using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.Tests;

public class SimilarModMatchServiceTests
{
    [Theory]
    [InlineData("abc", "abc001", SimilarModMatchKind.CandidateHasRevision)]
    [InlineData("abc001", "abc", SimilarModMatchKind.CandidateMissingRevision)]
    [InlineData("abc", "abc_v2", SimilarModMatchKind.CandidateHasRevision)]
    [InlineData("abc001", "abc002", SimilarModMatchKind.DifferentRevision)]
    public void BuildMatches_BidirectionalRevisionRules(string required, string candidate,
        SimilarModMatchKind expectedKind)
    {
        var matches = SimilarModMatchService.BuildMatches(
            Local(candidate), new[] { required }, new Dictionary<string, string>());

        var match = Assert.Single(matches);
        Assert.Equal(required, match.RequiredGuid);
        Assert.Equal(candidate, match.CandidateGuid);
        Assert.Equal(expectedKind, match.Kind);
        Assert.True(match.IsLocal);
    }

    [Theory]
    [InlineData("abc001", "bc")]
    [InlineData("abc001", "c001")]
    [InlineData("abc001_extra", "abc")]
    [InlineData("abc", "abd")]
    [InlineData("a001", "a")]
    [InlineData("abc12345", "abc1")]
    [InlineData("abc", "abc2")]
    public void BuildMatches_RejectsSubstringAndUnsafeRevisionMatches(string required, string candidate)
    {
        var matches = SimilarModMatchService.BuildMatches(
            Local(candidate), new[] { required }, new Dictionary<string, string>());

        Assert.Empty(matches);
    }

    [Fact]
    public void BuildMatches_ExactLocalOrSideloadMatchTakesPriority()
    {
        var local = Local("abc001");
        var sideload = new Dictionary<string, string> { ["abc002"] = "mods/abc002.zipmod" };

        Assert.Empty(SimilarModMatchService.BuildMatches(local, new[] { "abc001" }, sideload));
        Assert.Empty(SimilarModMatchService.BuildMatches(local, new[] { "abc002" }, sideload));
    }

    [Fact]
    public void BuildMatches_IncludesLocalAndSideloadCandidatesWithSourceData()
    {
        var local = Local("abc001");
        var sideload = new Dictionary<string, string> { ["abc"] = "mods/abc.zipmod" };

        var matches = SimilarModMatchService.BuildMatches(local, new[] { "abc002" }, sideload);

        Assert.Equal(2, matches.Count);
        Assert.Equal("mods/abc.zipmod", matches.Single(m => !m.IsLocal).RelativeUrl);
        Assert.Equal("abc001", matches.Single(m => m.IsLocal).LocalMod!.Name);
    }

    [Fact]
    public void BuildMatches_MultipleCandidatesAreDeterministicallyOrdered()
    {
        var local = Local("abc002", "abc001");
        var matches = SimilarModMatchService.BuildMatches(
            local, new[] { "abc" }, new Dictionary<string, string>());

        Assert.Equal(new[] { "abc001", "abc002" }, matches.Select(m => m.CandidateGuid));
    }

    private static Dictionary<string, ModInfo> Local(params string[] guids) =>
        guids.ToDictionary(guid => guid, guid => new ModInfo { Name = guid, Path = guid + ".zipmod" });
}
