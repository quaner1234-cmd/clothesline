namespace Clothesline;

/// <summary>
/// 识别结果不自动进剪贴板。复制用选区，没有选区则用全文。
/// 翻译用默认浏览器打开必应翻译，源语言自动检测，目标为简体中文。
/// </summary>
internal static class OcrActions
{
    public const int TranslateLimit = 1000;

    public static string Portion(string? text, int selectionStart, int selectionLength)
    {
        text ??= "";
        if (selectionLength > 0
            && selectionStart >= 0
            && selectionStart <= text.Length
            && selectionLength <= text.Length - selectionStart)
        {
            return text.Substring(selectionStart, selectionLength);
        }

        return text;
    }

    public static string BingSimplifiedChinese(string? text, out bool truncated)
    {
        text ??= "";
        truncated = text.Length > TranslateLimit;
        if (truncated) text = text.Substring(0, TranslateLimit);
        return "https://www.bing.com/translator?from=auto&to=zh-Hans&text=" + Uri.EscapeDataString(text);
    }
}
