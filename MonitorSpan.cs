using System.Runtime.InteropServices;

namespace Clothesline;

/// <summary>窗口所在显示器的工作区（物理像素）和该窗口的 DPI 缩放。</summary>
internal static class MonitorSpan
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    public readonly struct Pixels
    {
        public int Left { get; init; }
        public int Top { get; init; }
        public int Right { get; init; }
        public int Bottom { get; init; }
        public double Scale { get; init; }
        public int Width => Math.Max(1, Right - Left);
    }

    public static bool TryMeasure(IntPtr hwnd, out Pixels area)
    {
        area = default;
        if (hwnd == IntPtr.Zero) return false;
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi >= 96 ? dpi / 96.0 : 1;
        area = new Pixels
        {
            Left = info.rcWork.Left,
            Top = info.rcWork.Top,
            Right = info.rcWork.Right,
            Bottom = info.rcWork.Bottom,
            Scale = scale,
        };
        return true;
    }

    /// <summary>HWND 的物理宽度已经盖住工作区时返回 true。否则只改左右和宽度，不动垂直位置。</summary>
    public static bool AlignWidth(IntPtr hwnd, int left, int width)
    {
        if (hwnd == IntPtr.Zero || width < 1) return true;
        if (!GetWindowRect(hwnd, out var rect)) return true;
        int actual = rect.Right - rect.Left;
        if (Math.Abs(rect.Left - left) <= 2 && Math.Abs(actual - width) <= 2) return true;
        int height = rect.Bottom - rect.Top;
        if (height < 1) height = 1;
        SetWindowPos(hwnd, IntPtr.Zero, left, rect.Top, width, height, SwpNoZOrder | SwpNoActivate);
        return false;
    }
}
