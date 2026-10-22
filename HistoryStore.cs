using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clothesline;

public sealed class HistoryEntry
{
    public string FilePath { get; set; } = "";
    public double Angle { get; set; }
    public DateTime RemovedAt { get; set; }

    [JsonIgnore]
    public string Display =>
        $"{Path.GetFileName(FilePath)}    {RemovedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
}

/// <summary>取下但未删除的夹子。文件：%LOCALAPPDATA%\Clothesline\history.json。不要写进 pegs.json。</summary>
public static class HistoryStore
{
    public static string FilePath(string dataDir) => Path.Combine(dataDir, "history.json");

    public static List<HistoryEntry> Load(string dataDir)
    {
        var path = FilePath(dataDir);
        if (!File.Exists(path)) return new List<HistoryEntry>();

        List<HistoryEntry> rows;
        try
        {
            var json = File.ReadAllText(path);
            rows = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions)
                ?? throw new InvalidDataException("history.json 为空: " + path);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException("无法读取 history.json: " + path, ex);
        }

        var kept = new List<HistoryEntry>();
        foreach (var row in rows)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.FilePath)) continue;
            if (!File.Exists(row.FilePath)) continue;
            kept.Add(row);
        }
        return kept;
    }

    /// <summary>最新的在最前。同一路径只留一条。超出 limit 丢掉最旧的。文件不存在则不记。</summary>
    public static void Remember(string dataDir, string filePath, double angle, int limit)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        var rows = LoadOrEmpty(dataDir);
        rows.RemoveAll(row => string.Equals(row.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        rows.Insert(0, new HistoryEntry
        {
            FilePath = filePath,
            Angle = angle,
            RemovedAt = DateTime.UtcNow,
        });
        while (rows.Count > limit) rows.RemoveAt(rows.Count - 1);
        Save(dataDir, rows);
    }

    public static void Trim(string dataDir, int limit)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        if (!File.Exists(FilePath(dataDir))) return;
        var rows = LoadOrEmpty(dataDir);
        if (rows.Count > limit)
        {
            rows.RemoveRange(limit, rows.Count - limit);
        }
        Save(dataDir, rows);
    }

    public static void Forget(string dataDir, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        var path = FilePath(dataDir);
        if (!File.Exists(path)) return;
        var rows = LoadOrEmpty(dataDir);
        int removed = rows.RemoveAll(row => string.Equals(row.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) Save(dataDir, rows);
    }

    public static void Quarantine(string dataDir)
    {
        var path = FilePath(dataDir);
        if (!File.Exists(path)) return;
        var bad = path + ".bad";
        if (File.Exists(bad)) File.Delete(bad);
        File.Move(path, bad);
    }

    private static List<HistoryEntry> LoadOrEmpty(string dataDir)
    {
        try
        {
            return Load(dataDir);
        }
        catch (InvalidDataException)
        {
            Quarantine(dataDir);
            return new List<HistoryEntry>();
        }
    }

    private static void Save(string dataDir, List<HistoryEntry> rows)
    {
        Directory.CreateDirectory(dataDir);
        var path = FilePath(dataDir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(rows, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
