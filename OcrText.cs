using System.IO;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Storage;

namespace Clothesline;

/// <summary>Windows.Media.Ocr。没有语言包或接口不可用时返回说明，不抛到界面。</summary>
internal static class OcrText
{
    internal readonly record struct Outcome(bool Available, string Text, string? Error);

    public static async Task<Outcome> ReadAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new Outcome(false, "", "文件不在了。");

        try
        {
            var engine = CreateEngine();
            if (engine == null)
            {
                return new Outcome(false, "", "本机没有可用的 OCR 语言包。可在 Windows「设置 → 时间和语言 → 语言和区域」里为当前语言添加光学字符识别。");
            }

            using var bitmap = await LoadBitmapAsync(path);
            var result = await engine.RecognizeAsync(bitmap);
            return new Outcome(true, result?.Text?.Trim() ?? "", null);
        }
        catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException or PlatformNotSupportedException or NotSupportedException or EntryPointNotFoundException)
        {
            return new Outcome(false, "", "这台系统没有 Windows OCR。");
        }
        catch (Exception ex)
        {
            return new Outcome(false, "", "识别失败：" + ex.Message);
        }
    }

    private static OcrEngine? CreateEngine()
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine != null) return engine;
        foreach (var tag in new[] { "zh-Hans", "zh-Hant", "en-US", "en" })
        {
            try
            {
                engine = OcrEngine.TryCreateFromLanguage(new Language(tag));
                if (engine != null) return engine;
            }
            catch
            {
                // 语言标记无效或未安装时换下一个
            }
        }
        return null;
    }

    private static async Task<SoftwareBitmap> LoadBitmapAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        const uint maxEdge = 4000;
        uint width = decoder.PixelWidth;
        uint height = decoder.PixelHeight;
        if (width <= maxEdge && height <= maxEdge)
        {
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }

        double scale = maxEdge / (double)Math.Max(width, height);
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(width * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(height * scale)),
        };
        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
    }
}
