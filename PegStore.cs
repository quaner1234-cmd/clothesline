using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clothesline;

public sealed class PegRecord
{
    public string FilePath { get; set; } = "";
    public double Angle { get; set; }
}

/// <summary>绳子上的夹子跨启动保留。文件：%LOCALAPPDATA%\Clothesline\pegs.json。
/// 加载时丢掉已经不存在的文件。P1 历史若要做，应另存 history.json，不要塞进这里。</summary>
public static class PegStore
{
    public static string FilePath(string dataDir) => Path.Combine(dataDir, "pegs.json");

    public static List<PegRecord> Load(string dataDir)
    {
        var path = FilePath(dataDir);
        if (!File.Exists(path)) return new List<PegRecord>();

        List<PegRecord> rows;
        try
        {
            var json = File.ReadAllText(path);
            rows = JsonSerializer.Deserialize<List<PegRecord>>(json, JsonOptions)
                ?? throw new InvalidDataException("pegs.json 为空: " + path);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException("无法读取 pegs.json: " + path, ex);
        }

        var kept = new List<PegRecord>();
        foreach (var row in rows)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.FilePath)) continue;
            if (!File.Exists(row.FilePath)) continue;
            kept.Add(row);
        }
        return kept;
    }

    public static void Save(string dataDir, IEnumerable<PegRecord> pegs)
    {
        var rows = new List<PegRecord>();
        foreach (var peg in pegs)
        {
            if (peg == null || string.IsNullOrWhiteSpace(peg.FilePath)) continue;
            if (!File.Exists(peg.FilePath)) continue;
            rows.Add(new PegRecord { FilePath = peg.FilePath, Angle = peg.Angle });
        }

        Directory.CreateDirectory(dataDir);
        var path = FilePath(dataDir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(rows, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>超出容量时丢掉最旧的（列表头部）。</summary>
    public static void TrimOldest<T>(IList<T> items, int maxPegs)
    {
        if (maxPegs < 1) throw new ArgumentOutOfRangeException(nameof(maxPegs));
        while (items.Count > maxPegs) items.RemoveAt(0);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
