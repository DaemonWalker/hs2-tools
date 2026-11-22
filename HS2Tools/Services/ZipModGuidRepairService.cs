using System.IO.Compression;
using System.Xml.Linq;

namespace HS2Tools.Services;

public sealed record ZipModGuidRepairResult(
    string SourcePath,
    string OutputPath,
    string NewGuid,
    bool IsCopy);

public static class ZipModGuidRepairService
{
    public static ZipModGuidRepairResult RewriteGuid(string sourcePath, string newGuid, bool copy,
        string? requestedOutputPath = null)
    {
        string outputPath = copy
            ? ScannerService.UniqueTargetPath(
                Path.GetDirectoryName(requestedOutputPath ?? sourcePath) ?? ".", requestedOutputPath ?? sourcePath)
            : sourcePath;
        string tempPath = outputPath + $".guidrepair-{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(sourcePath, tempPath, true);
            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Update))
            {
                var entry = archive.Entries.FirstOrDefault(e =>
                        string.Equals(e.Name, "manifest.xml", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("manifest.xml not found in zipmod");
                XDocument document;
                using (var stream = entry.Open())
                using (var reader = new StreamReader(stream))
                    document = XDocument.Parse(reader.ReadToEnd(), LoadOptions.PreserveWhitespace);
                var root = document.Root ?? throw new InvalidDataException("manifest.xml has no root element");
                var guidElement = root.Elements().FirstOrDefault(e => e.Name.LocalName == "guid")
                    ?? root.Elements().FirstOrDefault(e =>
                        string.Equals(e.Name.LocalName, "guid", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("manifest.xml missing guid field");
                guidElement.Value = newGuid;
                using (var stream = entry.Open())
                {
                    stream.SetLength(0);
                    document.Save(stream, SaveOptions.DisableFormatting);
                }
            }
            File.Move(tempPath, outputPath, !copy);
            return new ZipModGuidRepairResult(sourcePath, outputPath, newGuid, copy);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }
}
