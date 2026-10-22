using System.IO;

namespace Clothesline;

public enum DropKind
{
    None,
    Folder,
    RecycleBin,
    Other,
}

/// <summary>把资源管理器 LocationURL 分成回收站、文件夹或其他。不含 UI 自动化。</summary>
public static class DropLocation
{
    public const string RecycleClsid = "645FF040-5081-101B-9F08-00AA002F954E";

    public static DropKind FromShellUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return DropKind.Other;
        if (url.Contains(RecycleClsid, StringComparison.OrdinalIgnoreCase)) return DropKind.RecycleBin;
        if (!url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return DropKind.Other;
        try
        {
            var path = new Uri(url).LocalPath;
            if (Directory.Exists(path)) return DropKind.Folder;
        }
        catch (UriFormatException)
        {
            return DropKind.Other;
        }
        return DropKind.Other;
    }
}
