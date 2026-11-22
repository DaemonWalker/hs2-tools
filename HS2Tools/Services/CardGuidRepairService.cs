using HS2Tools.Models;

namespace HS2Tools.Services;

public sealed record CardGuidRepairPlan(
    string Path,
    string Scope,
    IReadOnlyList<string> OriginalModIds,
    IReadOnlyDictionary<string, string> Replacements,
    long FileLength,
    long LastWriteTimeUtcTicks);

public sealed record CardGuidRepairResult(
    string SourcePath,
    string BackupPath,
    int ReplacementCount);

public static class CardGuidRepairService
{
    public static List<CardGuidRepairPlan> BuildPlans(
        IEnumerable<PngParseResult> cards, string scope,
        IReadOnlyDictionary<string, ModInfo> localMods)
    {
        var plans = new List<CardGuidRepairPlan>();
        foreach (var card in cards.Where(c => c.StructuralOk)
                     .OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
        {
            var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var requiredGuid in card.ModIDs)
            {
                if (localMods.ContainsKey(requiredGuid))
                    continue;
                var trimmed = requiredGuid.Trim();
                if (trimmed.Length == 0)
                    continue;
                var candidates = localMods.Keys.Where(guid =>
                        string.Equals(guid.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(guid => guid, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (candidates.Count == 0)
                    continue;

                string? target;
                if (localMods.ContainsKey(trimmed))
                    target = trimmed;
                else if (candidates.Count == 1)
                    target = candidates[0];
                else
                {
                    ErrorLog.Log($"卡片 GUID 修复跳过（多个本地候选）: {card.Path}: [{requiredGuid}]");
                    continue;
                }
                if (!string.Equals(requiredGuid, target, StringComparison.Ordinal))
                    replacements[requiredGuid] = target;
            }

            if (replacements.Count > 0)
            {
                plans.Add(new CardGuidRepairPlan(card.Path, scope, card.ModIDs.ToArray(), replacements,
                    card.FileLength, card.LastWriteTimeUtcTicks));
            }
        }
        return plans;
    }

    public static CardGuidRepairResult Repair(CardGuidRepairPlan plan)
    {
        EnsureCurrent(plan);
        var original = File.ReadAllBytes(plan.Path);
        var offset = ScannerService.FindPngEnd(original);
        if (offset < 0)
            throw new InvalidDataException("找不到 PNG 数据终点");

        var originalRegion = original.AsMemory(offset);
        var before = CharaCardParser.ParseDataRegion(originalRegion);
        if (!before.StructuralOk)
            throw new InvalidDataException("卡片不是可安全重写的结构化格式");
        if (!before.ModIDs.SequenceEqual(plan.OriginalModIds, StringComparer.Ordinal))
            throw new InvalidDataException("卡片引用已变化，请重新分析");

        var (newRegion, replacementCount) = CharaCardParser.RewriteGuids(originalRegion, plan.Replacements);
        if (replacementCount == 0)
            throw new InvalidDataException("未找到可替换的 GUID");
        var output = new byte[offset + newRegion.Length];
        original.AsSpan(0, offset).CopyTo(output);
        newRegion.CopyTo(output.AsMemory(offset));
        Verify(output, before.Names, before.ModIDs, plan.Replacements);

        var backupPath = CreateBackup(plan.Path);
        var tempPath = plan.Path + $".guidrepair-{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(output);
                stream.Flush(true);
            }
            Verify(File.ReadAllBytes(tempPath), before.Names, before.ModIDs, plan.Replacements);
            EnsureCurrent(plan);
            File.Replace(tempPath, plan.Path, null);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
        return new CardGuidRepairResult(plan.Path, backupPath, replacementCount);
    }

    private static void EnsureCurrent(CardGuidRepairPlan plan)
    {
        var info = new FileInfo(plan.Path);
        if (!info.Exists)
            throw new FileNotFoundException("卡片文件不存在", plan.Path);
        if (info.Length != plan.FileLength || info.LastWriteTimeUtc.Ticks != plan.LastWriteTimeUtcTicks)
            throw new InvalidDataException("卡片文件已变化，请重新分析");
    }

    private static string CreateBackup(string sourcePath)
    {
        var backupPath = $"{sourcePath}.{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.guid-backup.bak";
        File.Copy(sourcePath, backupPath, false);
        return backupPath;
    }

    private static void Verify(
        byte[] fileData, IReadOnlyList<string> originalNames, IReadOnlyList<string> originalModIds,
        IReadOnlyDictionary<string, string> replacements)
    {
        var offset = ScannerService.FindPngEnd(fileData);
        if (offset < 0)
            throw new InvalidDataException("修复后的 PNG 数据无效");
        var parsed = CharaCardParser.ParseDataRegion(fileData.AsMemory(offset));
        if (!parsed.StructuralOk)
            throw new InvalidDataException("修复后的卡片结构校验失败");
        if (!parsed.Names.SequenceEqual(originalNames, StringComparer.Ordinal))
            throw new InvalidDataException("修复后的角色名称发生变化");

        var expectedModIds = new List<string>();
        foreach (var modId in originalModIds)
        {
            var expected = replacements.TryGetValue(modId, out var replacement) ? replacement : modId;
            if (!expectedModIds.Contains(expected, StringComparer.Ordinal))
                expectedModIds.Add(expected);
        }
        if (!parsed.ModIDs.SequenceEqual(expectedModIds, StringComparer.Ordinal))
            throw new InvalidDataException("修复后的 Mod GUID 校验失败");
    }
}
