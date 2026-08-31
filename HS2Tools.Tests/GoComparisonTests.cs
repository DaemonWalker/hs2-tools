using System.Diagnostics;
using System.Text.Json;
using HS2Tools.Services;
using Xunit.Abstractions;

namespace HS2Tools.Tests;

/// <summary>
/// 与 Go 版 scanner CLI 的输出对照（迁移文档阶段 1 验证策略）。
/// 在临时目录复制 Go 源码构建基准 CLI（不修改原仓库）；构建失败则跳过。
/// 注：卡片/场景解析已主动偏离 Go（结构化解析，基准 BepisPlugins），PNG 名称/Mod/缩略图
/// 的 Go 对照已移除；此处仅保留 ScanDir / ZipMod 对照与真实卡片结构解析终验。
/// </summary>
public class GoComparisonTests : IDisposable
{
    private readonly string _dir = TestAssets.NewTempDir();
    private readonly ScannerService _svc = new();
    private readonly ITestOutputHelper _output;

    public GoComparisonTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => TestAssets.DeleteDir(_dir);

    private static readonly Lazy<string?> GoCli = new(BuildGoScannerCli);

    // ==================== 基准 CLI 构建 ====================

    private static string? BuildGoScannerCli()
    {
        try
        {
            var repoWails = FindRepoWailsDir();
            if (repoWails == null)
                return null;

            var work = Path.Combine(Path.GetTempPath(), "hs2tools-go-baseline");
            var exe = Path.Combine(work, "scanner.exe");
            if (File.Exists(exe))
                return exe; // 同一会话复用

            if (Directory.Exists(work))
                Directory.Delete(work, true);
            Directory.CreateDirectory(work);

            // 只复制构建 scanner CLI 需要的部分（不碰 frontend/node_modules）
            CopyTree(Path.Combine(repoWails, "cmd"), Path.Combine(work, "cmd"));
            CopyTree(Path.Combine(repoWails, "internal"), Path.Combine(work, "internal"));
            File.Copy(Path.Combine(repoWails, "go.mod"), Path.Combine(work, "go.mod"));
            File.Copy(Path.Combine(repoWails, "go.sum"), Path.Combine(work, "go.sum"));

            // 本地 Go 工具链版本兼容（只改临时副本）
            RunProcess("go", "mod edit -go=1.24", work, null, 60);
            RunProcess("go", "build -o scanner.exe ./cmd/scanner", work,
                new Dictionary<string, string> { ["GOTOOLCHAIN"] = "local" }, 180);

            return File.Exists(exe) ? exe : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindRepoWailsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "wails", "go.mod");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "wails");
            dir = dir.Parent;
        }
        return null;
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
        foreach (var sub in Directory.EnumerateDirectories(src))
            CopyTree(sub, Path.Combine(dst, Path.GetFileName(sub)));
    }

    private static string RunProcess(string fileName, string args, string? workDir,
        Dictionary<string, string>? env, int timeoutSec, string? stdin = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workDir ?? "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            UseShellExecute = false,
            // Go 程序输出 UTF-8；默认 ANSI 码页（GBK）会吞掉引号破坏 JSON
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        if (env != null)
            foreach (var (k, v) in env)
                psi.Environment[k] = v;

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start " + fileName);
        if (stdin != null)
        {
            proc.StandardInput.Write(stdin);
            proc.StandardInput.Close();
        }
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(timeoutSec * 1000))
        {
            proc.Kill();
            throw new TimeoutException($"{fileName} {args} timed out");
        }
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} {args} exit {proc.ExitCode}: {stderr}");
        return stdout;
    }

    private static JsonElement RunGo(string args, string? stdin = null)
    {
        var stdout = RunProcess(GoCli.Value!, args, null, null, 60, stdin);
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (!doc.RootElement.GetProperty("success").GetBoolean())
                throw new InvalidOperationException("Go CLI error: " + stdout);
            return doc.RootElement.GetProperty("data").Clone();
        }
        catch (JsonException ex)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(stdout);
            throw new InvalidOperationException(
                $"Go stdout not valid JSON: {ex.Message}\nhex[0..120]: {Convert.ToHexString(bytes[..Math.Min(120, bytes.Length)])}\nraw: {stdout}");
        }
    }

    private void RequireGo()
    {
        Skip.If(GoCli.Value == null, "Go scanner CLI 不可用（构建失败或 Go 工具链缺失）");
    }

    // ==================== 对照测试 ====================

    [SkippableFact]
    public void ZipMod_MatchGo()
    {
        RequireGo();
        var zipmod = TestAssets.WriteZipmod(_dir, "test.zipmod",
            TestAssets.MakeManifest("com.test.go", "Name", "1.0"));

        var go = RunGo($"-action readZipMod -path \"{zipmod}\"");
        var cs = _svc.ReadZipMod(zipmod);

        foreach (var (guid, info) in cs)
        {
            var goInfo = go.GetProperty(guid);
            Assert.Equal(info.Name, goInfo.GetProperty("name").GetString());
            Assert.Equal(info.Version, goInfo.GetProperty("version").GetString());
            Assert.Equal(info.Path, goInfo.GetProperty("path").GetString());
        }
        Assert.Equal(cs.Count, go.EnumerateObject().Count());
    }

    [SkippableFact]
    public void ScanDir_MatchGo_InOrder()
    {
        RequireGo();
        // 夹具目录树：排除目录 + 混合扩展名 + 嵌套
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        Directory.CreateDirectory(Path.Combine(_dir, "hs_tools_skip"));
        File.WriteAllText(Path.Combine(_dir, "b.png"), "x");
        File.WriteAllText(Path.Combine(_dir, "a.PNG"), "x");
        File.WriteAllText(Path.Combine(_dir, "c.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "sub", "d.png"), "x");
        File.WriteAllText(Path.Combine(_dir, "hs_tools_skip", "e.png"), "x");

        var go = RunGo($"-action scanDir -path \"{_dir}\" -exclude hs_tools -ext .png")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        var cs = _svc.ScanDirectory(_dir, new() { ExcludeDir = new() { "hs_tools" }, TargetExtension = new() { ".png" } }).ToArray();

        // 顺序也要求一致（Go filepath.Walk 词典序遍历）
        Assert.Equal(go, cs);
    }

    [SkippableFact]
    public async Task PngModsBatch_MatchGo()
    {
        RequireGo();
        var p1 = TestAssets.WritePng(_dir, "a.png", TestAssets.PngPrefix(), TestAssets.ModMarker("com.mod.1"));
        var p2 = TestAssets.WritePng(_dir, "b.png", TestAssets.PngPrefix(), TestAssets.ModMarker("com.mod.2"));

        var request = JsonSerializer.Serialize(new { action = "readPngModsBatch", paths = new[] { p1, p2 }, concurrency = 8 });
        var go = RunGo("-json", request);
        var goMap = go.EnumerateArray().ToDictionary(
            e => e.GetProperty("path").GetString()!,
            e => e.GetProperty("modIds").EnumerateArray().Select(x => x.GetString()!).OrderBy(x => x).ToArray());

        var csResults = await _svc.ReadPngModsBatchAsync(new[] { p1, p2 });
        var csMap = csResults.ToDictionary(r => r.Path, r => r.ModIDs.OrderBy(x => x).ToArray());

        Assert.Equal(goMap.Count, csMap.Count);
        foreach (var (path, goIds) in goMap)
            Assert.Equal(goIds, csMap[path]);
    }

    // ==================== 阶段 4：真实卡片结构解析终验 ====================

    /// <summary>
    /// 真实卡片结构解析终验：逐卡断言结构解析路径不抛异常，输出结构解析 vs 旧字节扫描
    /// （仅数据区）的差异统计（差异预期存在——结构化解析有 KKEx 命名空间隔离、名字有序去重），
    /// 断言宽松。环境变量 HS2_REAL_CARD_DIRS（分号分隔目录）指向真实卡片目录时执行。
    /// </summary>
    [SkippableFact]
    public void RealCards_StructuralParse()
    {
        var dirsEnv = Environment.GetEnvironmentVariable("HS2_REAL_CARD_DIRS");
        Skip.If(string.IsNullOrWhiteSpace(dirsEnv), "未设置 HS2_REAL_CARD_DIRS（真实卡片目录，分号分隔）");

        var files = 0;
        var structuralOk = 0;
        var nameDiff = 0;
        var modDiff = 0;
        foreach (var dir in dirsEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var path in _svc.ScanDirectory(dir, new() { TargetExtension = { ".png" } }))
            {
                files++;
                byte[] data;
                try
                {
                    data = File.ReadAllBytes(path);
                }
                catch (Exception ex)
                {
                    ErrorLog.Log($"RealCards_StructuralParse read failed: {path}: {ex.Message}");
                    continue;
                }

                var offset = ScannerService.GetDataRegionOffset(data);
                if (offset < 0)
                    continue;

                // 结构解析不得向外抛异常（blob 失败应内部消化并记 ErrorLog）
                var (names, modIds, ok) = CharaCardParser.ParseDataRegion(data.AsSpan(offset));
                if (ok)
                    structuralOk++;

                // 与旧字节扫描（仅数据区）的差异统计
                var region = data[offset..];
                var legacyNames = ScannerService.SearchBuffer("fullname"u8.ToArray(), "personality"u8.ToArray(), region);
                var legacyMods = ScannerService.SearchBuffer("ModID"u8.ToArray(), "Slot"u8.ToArray(), region);
                if (!names.OrderBy(x => x).SequenceEqual(legacyNames.OrderBy(x => x)))
                    nameDiff++;
                if (!modIds.OrderBy(x => x).SequenceEqual(legacyMods.OrderBy(x => x)))
                    modDiff++;
            }
        }

        _output.WriteLine($"{files} cards: structural ok {structuralOk}, name diff {nameDiff}, mod diff {modDiff}");
        Assert.True(files > 0);
        // 宽松断言：大多数卡应能结构解析成功
        Assert.True(structuralOk > files / 2, $"structural hit rate too low: {structuralOk}/{files}");
    }

    [SkippableFact]
    public async Task ZipModBatch_MatchGo()
    {
        RequireGo();
        var z1 = TestAssets.WriteZipmod(_dir, "a.zipmod", TestAssets.MakeManifest("com.batch.a", "A", "1"));
        var z2 = TestAssets.WriteZipmod(_dir, "b.zipmod", TestAssets.MakeManifest("com.batch.b", "B", "2"));

        var request = JsonSerializer.Serialize(new { action = "readZipModBatch", paths = new[] { z1, z2 }, concurrency = 4 });
        var go = RunGo("-json", request);

        var cs = await _svc.ReadZipModBatchAsync(new[] { z1, z2 });

        Assert.Equal(go.EnumerateObject().Count(), cs.Count);
        foreach (var (guid, info) in cs)
        {
            var goInfo = go.GetProperty(guid);
            Assert.Equal(info.Name, goInfo.GetProperty("name").GetString());
            Assert.Equal(info.Version, goInfo.GetProperty("version").GetString());
        }
    }
}
