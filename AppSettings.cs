using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clothesline;

/// <summary>本机设置。文件：%LOCALAPPDATA%\Clothesline\settings.json。</summary>
public sealed class AppSettings
{
    public const int DefaultMaxPegs = 20;
    public const int MaxPegsLimit = 200;
    public const int DefaultHistoryLimit = 50;
    public const int HistoryLimitMax = 500;

    public int MaxPegs { get; set; } = DefaultMaxPegs;
    public int HistoryLimit { get; set; } = DefaultHistoryLimit;
    public bool ClipboardWatchEnabled { get; set; } = true;
    public int ClipboardMinWidth { get; set; }
    public int ClipboardMinHeight { get; set; }

    public bool HotkeyCtrl { get; set; } = true;
    public bool HotkeyAlt { get; set; } = true;
    public bool HotkeyShift { get; set; }
    public bool HotkeyWin { get; set; }
    public string HotkeyKey { get; set; } = "T";

    public List<string> WatchFolders { get; set; } = new();

    public static string FilePath(string dataDir) => Path.Combine(dataDir, "settings.json");

    /// <summary>文件不存在时写入默认值（容量 20、剪贴板开、热键 Ctrl+Alt+T，并监视 defaultWatchFolder）。
    /// JSON 损坏时抛出，避免用默认值覆盖用户文件。</summary>
    public static AppSettings Load(string dataDir, string? defaultWatchFolder)
    {
        var path = FilePath(dataDir);
        if (!File.Exists(path))
        {
            var created = new AppSettings();
            if (!string.IsNullOrWhiteSpace(defaultWatchFolder))
                created.WatchFolders.Add(defaultWatchFolder);
            created.Normalize();
            created.Save(dataDir);
            return created;
        }

        AppSettings settings;
        try
        {
            var json = File.ReadAllText(path);
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new InvalidDataException("settings.json 为空: " + path);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException("无法读取 settings.json: " + path, ex);
        }

        settings.Normalize();
        return settings;
    }

    public void Normalize()
    {
        if (MaxPegs < 1) MaxPegs = 1;
        if (MaxPegs > MaxPegsLimit) MaxPegs = MaxPegsLimit;
        if (HistoryLimit < 1) HistoryLimit = 1;
        if (HistoryLimit > HistoryLimitMax) HistoryLimit = HistoryLimitMax;
        if (ClipboardMinWidth < 0) ClipboardMinWidth = 0;
        if (ClipboardMinHeight < 0) ClipboardMinHeight = 0;
        HotkeyKey = string.IsNullOrWhiteSpace(HotkeyKey) ? "T" : HotkeyKey.Trim();
        WatchFolders ??= new List<string>();

        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in WatchFolders)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var full = Path.GetFullPath(raw.Trim());
            if (seen.Add(full)) folders.Add(full);
        }
        WatchFolders = folders;
    }

    public void Save(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var path = FilePath(dataDir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    public AppSettings Clone()
    {
        var copy = new AppSettings
        {
            MaxPegs = MaxPegs,
            HistoryLimit = HistoryLimit,
            ClipboardWatchEnabled = ClipboardWatchEnabled,
            ClipboardMinWidth = ClipboardMinWidth,
            ClipboardMinHeight = ClipboardMinHeight,
            HotkeyCtrl = HotkeyCtrl,
            HotkeyAlt = HotkeyAlt,
            HotkeyShift = HotkeyShift,
            HotkeyWin = HotkeyWin,
            HotkeyKey = HotkeyKey,
            WatchFolders = new List<string>(WatchFolders ?? new List<string>()),
        };
        return copy;
    }

    /// <summary>剪贴板图片是否接收。关闭监听，或任一边小于最小宽/高（0 表示不限制）时拒绝。</summary>
    public bool ClipboardAccepts(int width, int height)
    {
        if (!ClipboardWatchEnabled) return false;
        if (width <= 0 || height <= 0) return false;
        if (ClipboardMinWidth > 0 && width < ClipboardMinWidth) return false;
        if (ClipboardMinHeight > 0 && height < ClipboardMinHeight) return false;
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
