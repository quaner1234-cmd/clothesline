namespace Clothesline;

/// <summary>一块屏的工作区，单位是 WPF 设备无关像素。</summary>
internal readonly struct WorkAreaDip
{
    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }

    public WorkAreaDip(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// 把监视器工作区的物理像素换成 WPF 宽度。
/// 缩放 125% 时 1920 物理像素是 1536 DIP；若把 DIP 当成物理像素，绳子会短一截。
/// </summary>
internal static class WorkAreaSpan
{
    public static WorkAreaDip ToDip(int left, int top, int right, int bottom, double scaleX, double scaleY)
    {
        if (!double.IsFinite(scaleX) || scaleX <= 0) scaleX = 1;
        if (!double.IsFinite(scaleY) || scaleY <= 0) scaleY = 1;
        int width = right - left;
        int height = bottom - top;
        if (width < 1) width = 1;
        if (height < 1) height = 1;
        return new WorkAreaDip(left / scaleX, top / scaleY, width / scaleX, height / scaleY);
    }
}
