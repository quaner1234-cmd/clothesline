namespace Clothesline;

/// <summary>晾衣绳滚轮：夹子上缩放这一张，空白处横向滚动。基准缩略图 184×216。</summary>
internal static class PegZoom
{
    public const double BaseWidth = 184;
    public const double BaseHeight = 216;
    public const double Min = 0.5;
    public const double Max = 2;
    public const double Default = 1;
    public const double Step = 1.1;
    public const double DefaultLineHeight = (BaseHeight + CardExtra) * Default + WindowChrome + RopeCurve.MaxHang;

    /// <summary>玻璃卡片上下留白：外边距 18+8，内边距 8+8。</summary>
    public const double CardExtra = 42;

    /// <summary>绳子和阴影占用的高度，不含中间下垂。横向滚动条不显示，这段高度只是留给卡片的余量。</summary>
    public const double WindowChrome = 56;

    public enum Gesture
    {
        ZoomPeg,
        ScrollLine,
    }

    public static Gesture Decide(bool pointerOverPeg) =>
        pointerOverPeg ? Gesture.ZoomPeg : Gesture.ScrollLine;

    public static double Next(double current, int wheelDelta)
    {
        double value = double.IsFinite(current) ? current : Default;
        if (wheelDelta > 0) value *= Step;
        else if (wheelDelta < 0) value /= Step;
        return Math.Clamp(value, Min, Max);
    }

    public static double LineHeight(double maxZoom) =>
        (BaseHeight + CardExtra) * Next(maxZoom, 0) + WindowChrome + RopeCurve.MaxHang;
}
