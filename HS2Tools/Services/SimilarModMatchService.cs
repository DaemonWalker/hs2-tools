using System.Text.RegularExpressions;
using HS2Tools.Models;

namespace HS2Tools.Services;

public enum SimilarModMatchKind
{
    CandidateHasRevision,
    CandidateMissingRevision,
    DifferentRevision,
}

public sealed record SimilarModMatch(
    string RequiredGuid,
    string CandidateGuid,
    SimilarModMatchKind Kind,
    ModInfo? LocalMod,
    string? RelativeUrl)
{
    public bool IsLocal => LocalMod is not null;

    public string MatchDescription => Kind switch
    {
        SimilarModMatchKind.CandidateHasRevision => "候选带附加修订号",
        SimilarModMatchKind.CandidateMissingRevision => "候选缺少目标修订号",
        _ => "基础名相同、修订形式不同",
    };
}

public static partial class SimilarModMatchService
{
    private sealed record GuidParts(string Stem, string? Revision);

    public static List<SimilarModMatch> BuildMatches(
        IReadOnlyDictionary<string, ModInfo> localMods,
        IEnumerable<string> referencedGuids,
        IReadOnlyDictionary<string, string> sideloadMods)
    {
        var localLookup = localMods.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sideloadLookup = sideloadMods.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localCandidates = localMods
            .Select(kv => (Guid: kv.Key, Parts: Parse(kv.Key), Info: kv.Value))
            .Where(c => c.Parts is not null)
            .ToList();
        var sideloadCandidates = sideloadMods
            .Select(kv => (Guid: kv.Key, Parts: Parse(kv.Key), Url: kv.Value))
            .Where(c => c.Parts is not null)
            .ToList();
        var matches = new List<SimilarModMatch>();

        foreach (var requiredGuid in referencedGuids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (localLookup.Contains(requiredGuid) || sideloadLookup.Contains(requiredGuid))
                continue;
            var required = Parse(requiredGuid);
            if (required is null)
                continue;

            foreach (var candidate in localCandidates)
            {
                var kind = GetMatchKind(required, candidate.Parts!);
                if (kind is not null)
                    matches.Add(new SimilarModMatch(requiredGuid, candidate.Guid, kind.Value,
                        candidate.Info, null));
            }
            foreach (var candidate in sideloadCandidates)
            {
                var kind = GetMatchKind(required, candidate.Parts!);
                if (kind is not null)
                    matches.Add(new SimilarModMatch(requiredGuid, candidate.Guid, kind.Value,
                        null, candidate.Url));
            }
        }

        return matches
            .OrderBy(m => m.RequiredGuid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Kind)
            .ThenByDescending(m => m.IsLocal)
            .ThenBy(m => m.CandidateGuid, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SimilarModMatchKind? GetMatchKind(GuidParts required, GuidParts candidate)
    {
        if (!required.Stem.Equals(candidate.Stem, StringComparison.OrdinalIgnoreCase))
            return null;
        if (required.Revision is null && candidate.Revision is not null)
            return SimilarModMatchKind.CandidateHasRevision;
        if (required.Revision is not null && candidate.Revision is null)
            return SimilarModMatchKind.CandidateMissingRevision;
        if (required.Revision is not null && candidate.Revision is not null
            && !required.Revision.Equals(candidate.Revision, StringComparison.OrdinalIgnoreCase))
            return SimilarModMatchKind.DifferentRevision;
        return null;
    }

    private static GuidParts? Parse(string guid)
    {
        var marked = MarkedRevisionRegex().Match(guid);
        if (marked.Success)
            return ValidParts(marked.Groups["stem"].Value, marked.Groups["revision"].Value);

        var plain = PlainRevisionRegex().Match(guid);
        if (plain.Success && !char.IsDigit(plain.Groups["stem"].Value[^1]))
            return ValidParts(plain.Groups["stem"].Value, plain.Groups["revision"].Value);

        return ValidParts(guid, null);
    }

    private static GuidParts? ValidParts(string stem, string? revision) =>
        stem.Length >= 3 && stem.Any(char.IsLetter) ? new GuidParts(stem, revision) : null;

    [GeneratedRegex("^(?<stem>.+?)(?:[_\\-. ](?:v|ver|rev)?|(?:v|ver|rev))(?<revision>\\d{1,4})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkedRevisionRegex();

    [GeneratedRegex("^(?<stem>.+?)(?<revision>\\d{2,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainRevisionRegex();
}
