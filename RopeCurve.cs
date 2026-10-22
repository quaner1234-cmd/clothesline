namespace Clothesline;

/// <summary>
/// 绳子是一条二次贝塞尔。两端高、中间下垂。
/// 控制点比弧底再低一倍，这样弧本身的中点正好下垂 Sag。
/// </summary>
internal static class RopeCurve
{
    public const double EndY = 12;
    public const double Sag = 16;
    public const double NotchY = 4;
    public const double ControlY = EndY + 2 * Sag;
    public const double MaxHang = EndY + Sag - NotchY;

    public static double Y(double x, double width)
    {
        if (!double.IsFinite(width) || width <= 1) return EndY + Sag;
        if (!double.IsFinite(x)) x = 0;
        double t = Math.Clamp(x / width, 0, 1);
        double u = 1 - t;
        return u * u * EndY + 2 * u * t * ControlY + t * t * EndY;
    }
}
