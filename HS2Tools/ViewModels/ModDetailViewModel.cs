using System.IO.Compression;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

/// <summary>本地 Mod 的 manifest 原文、文件信息和 Sideload 地址。</summary>
public partial class ModDetailViewModel : ObservableObject
{
    private readonly GameLauncherService _launcher;

    public ModDetailViewModel(string guid, ModInfo? mod, string? sideloadUrl, GameLauncherService launcher,
        string? sideloadBaseUrl = null)
    {
        Guid = guid;
        Name = mod?.Name ?? "";
        Version = mod?.Version ?? "";
        FilePath = mod?.Path ?? "";
        SideloadUrl = sideloadUrl ?? "";
        SideloadPageUrl = BuildSideloadPageUrl(sideloadUrl, sideloadBaseUrl);
        FileSize = mod is { FileLength: > 0 } ? FormatUtils.FormatBytes(mod.FileLength) : "-";
        Modified = mod is { LastWriteTimeUtcTicks: > 0 }
            ? new DateTime(mod.LastWriteTimeUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "-";
        HasLocalFile = !string.IsNullOrEmpty(FilePath);
        _launcher = launcher;
    }

    public string Guid { get; }
    public string Name { get; }
    public string Version { get; }
    public string FilePath { get; }
    public string SideloadUrl { get; }
    public string? SideloadPageUrl { get; }
    public string FileSize { get; }
    public string Modified { get; }
    public bool HasLocalFile { get; }

    public string DisplayName => string.IsNullOrEmpty(Name) ? "未命名 Mod" : Name;
    public string DisplayVersion => string.IsNullOrEmpty(Version) ? "-" : Version;
    public string DisplayFilePath => HasLocalFile ? FilePath : "-";
    public string DisplaySideloadUrl => string.IsNullOrEmpty(SideloadUrl) ? "-" : SideloadUrl;

    [ObservableProperty] private string _manifestText = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => ErrorMessage is not null;

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        if (!HasLocalFile)
        {
            ManifestText = "";
            ErrorMessage = "本地模组列表中未找到此 Mod，无法读取 manifest.xml。";
            IsLoading = false;
            return;
        }

        try
        {
            ManifestText = await Task.Run(() => ReadManifest(FilePath));
        }
        catch (Exception ex)
        {
            ManifestText = "";
            ErrorMessage = $"读取 manifest.xml 失败：{ex.Message}";
            ErrorLog.Log(new Exception($"Mod detail failed: {FilePath}", ex));
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal static string ReadManifest(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(
            e => string.Equals(e.Name, "manifest.xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("压缩包内缺少 manifest.xml");
        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        return reader.ReadToEnd();
    }

    internal static string? BuildSideloadPageUrl(string? modUrl, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(modUrl))
            return null;
        try
        {
            var fileUri = Uri.TryCreate(modUrl, UriKind.Absolute, out var absolute)
                ? absolute
                : baseUrl is not null && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
                    ? new Uri(baseUri, modUrl.Replace('\\', '/'))
                    : null;
            return fileUri is not null &&
                   (fileUri.Scheme == Uri.UriSchemeHttp || fileUri.Scheme == Uri.UriSchemeHttps)
                ? new Uri(fileUri, ".").AbsoluteUri
                : null;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private bool CanOpenSideloadPage() => SideloadPageUrl is not null;

    [RelayCommand(CanExecute = nameof(CanOpenSideloadPage))]
    private void OpenSideloadPage() => _launcher.OpenWebPage(SideloadPageUrl!);

    [RelayCommand(CanExecute = nameof(HasLocalFile))]
    private void OpenInFolder() => _launcher.OpenInFolder(FilePath);
}
