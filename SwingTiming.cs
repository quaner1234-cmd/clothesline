namespace Clothesline;

/// <summary>
/// 下落和晃动的时间。晃动必须在栏落地之前已经开始，
/// 不能等 300ms 的下落结束才轮到后面的夹子。
/// </summary>
internal static class SwingTiming
{
    public const int SlideDownMs = 300;
    public const int SlideUpMs = 230;
    public const int SwingMs = 950;

    /// <summary>第 index 张（从 0 计，共 count 张）相对下落开始的延迟。</summary>
    public static int DelayMs(int index, int count)
    {
        if (count < 1) count = 1;
        if (index < 0) index = 0;
        if (index >= count) index = count - 1;
        // 最晚一张也在落地前约 80ms 就开始晃。
        int latest = Math.Max(0, SlideDownMs - 80);
        if (count == 1) return 0;
        return (int)Math.Round(latest * (index / (double)(count - 1)));
    }
}
