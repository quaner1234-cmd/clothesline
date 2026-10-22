using System.Runtime.InteropServices;

namespace Clothesline;

/// <summary>手写 Shell_NotifyIcon 托盘图标，零 NuGet 依赖。图标在代码里逐像素画（灰绳 + 铝夹）。</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    private const uint NIIF_INFO = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public int biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, int planes, int bitsPerPixel, byte[]? data);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO icon);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    private NOTIFYICONDATA _nid;
    private bool _added;
    private readonly IntPtr _hIcon;

    public TrayIcon(IntPtr hwnd, uint callbackMessage, string tooltip)
    {
        _hIcon = CreatePegHIcon();
        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 0x1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = callbackMessage,
            hIcon = _hIcon,
            szTip = tooltip,
        };
        _added = Shell_NotifyIcon(NIM_ADD, ref _nid);
        if (!_added) Logger.Log("tray icon add FAILED");
    }

    public void SetTip(string tooltip)
    {
        if (!_added) return;
        _nid.szTip = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip;
        _nid.uFlags = NIF_TIP;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    }

    public void ShowBalloon(string title, string text)
    {
        if (!_added) return;
        _nid.uFlags = NIF_INFO;
        _nid.szInfoTitle = title.Length > 63 ? title.Substring(0, 63) : title;
        _nid.szInfo = text.Length > 255 ? text.Substring(0, 255) : text;
        _nid.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        _nid.szInfo = "";
        _nid.szInfoTitle = "";
    }

    /// <summary>逐像素画一枚 32x32 图标：一道细灰弧 + 一只铝质圆头夹。两端少画一截，看起来淡出。</summary>
    private static IntPtr CreatePegHIcon()
    {
        try
        {
            const int S = 32;
            var px = new byte[S * S * 4];

            void Dot(int x, int y, byte b, byte g, byte r)
            {
                if (x < 0 || y < 0 || x >= S || y >= S) return;
                int i = (y * S + x) * 4;
                px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
            }

            for (int x = 2; x <= 29; x++)
            {
                int y = 20 + (int)Math.Round(4.0 * Math.Sin(Math.PI * (x - 2) / 27.0));
                bool fade = x < 5 || x > 26;
                if (fade) Dot(x, y, 196, 201, 206);          // 两端变淡
                else
                {
                    Dot(x, y - 1, 232, 236, 240);            // 高光
                    Dot(x, y, 184, 176, 167);                // 绳芯 #A7B0B8 (BGRA)
                }
            }
            for (int y = 6; y <= 23; y++)
            {
                for (int x = 14; x <= 18; x++)
                {
                    bool corner = (y <= 7 || y >= 22) && (x == 14 || x == 18);
                    if (corner) continue;
                    if (y >= 9 && y <= 10 && x >= 15 && x <= 17)
                        Dot(x, y, 108, 101, 92);             // 绳槽 #5C656C
                    else if (x == 14 || x == 18 || y == 6 || y == 23)
                        Dot(x, y, 143, 136, 126);            // 铝边
                    else if (x == 15)
                        Dot(x, y, 248, 246, 243);            // 拉丝高光
                    else
                        Dot(x, y, 214, 206, 197);            // 铝身 #C5CED6
                }
            }

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = S;
            bmi.bmiHeader.biHeight = -S; // top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB

            IntPtr bits;
            IntPtr hbmColor = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (hbmColor == IntPtr.Zero) return FallbackIcon();
            Marshal.Copy(px, 0, bits, px.Length);

            IntPtr hbmMask = CreateBitmap(S, S, 1, 1, new byte[S * S / 8]);
            var info = new ICONINFO { fIcon = 1, xHotspot = 0, yHotspot = 0, hbmMask = hbmMask, hbmColor = hbmColor };
            IntPtr hIcon = CreateIconIndirect(ref info);
            if (hbmColor != IntPtr.Zero) DeleteObject(hbmColor);
            if (hbmMask != IntPtr.Zero) DeleteObject(hbmMask);
            return hIcon != IntPtr.Zero ? hIcon : FallbackIcon();
        }
        catch
        {
            return FallbackIcon();
        }
    }

    private static IntPtr FallbackIcon() => LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION

    public void Dispose()
    {
        if (_added) Shell_NotifyIcon(NIM_DELETE, ref _nid);
        _added = false;
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
    }
}
