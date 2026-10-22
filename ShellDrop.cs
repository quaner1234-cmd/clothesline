using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace Clothesline;

/// <summary>
/// 拖拽落点：资源管理器文件夹取下（文件留下），回收站删除并取下。
/// 认不出落点时不改绳子，交给右键「取下 / 取下并删除文件」。
/// </summary>
internal static class ShellDrop
{
    private static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

    private const uint FoDelete = 3;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoErrorUi = 0x0400;
    private const uint ShgfiDisplayName = 0x000000200;

    public static DropKind ClassifyAtCursor()
    {
        if (!GetCursorPos(out var pt)) return DropKind.Other;
        return Classify(pt.X, pt.Y);
    }

    public static DropKind Classify(int x, int y)
    {
        if (PointIsRecycleBin(x, y)) return DropKind.RecycleBin;
        return ClassifyShellWindow(x, y);
    }

    /// <summary>移到回收站。文件已经不在时视为成功。</summary>
    public static bool TryRecycle(string path)
    {
        if (!File.Exists(path)) return true;
        IntPtr from = Marshal.StringToHGlobalUni(path + "\0");
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FoDelete,
                pFrom = from,
                fFlags = (ushort)(FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi),
            };
            int result = SHFileOperation(ref op);
            if (result != 0 || op.fAnyOperationsAborted != 0)
            {
                Logger.Log($"recycle failed code={result} abort={op.fAnyOperationsAborted} {path}");
                return false;
            }
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    private static bool PointIsRecycleBin(int x, int y)
    {
        try
        {
            var el = AutomationElement.FromPoint(new Point(x, y));
            if (el == null) return false;
            string expected = RecycleDisplayName();
            var walker = TreeWalker.ControlViewWalker;
            for (var cur = el; cur != null; cur = walker.GetParent(cur))
            {
                string name;
                try { name = cur.Current.Name ?? ""; }
                catch { break; }
                if (name.Length == 0) continue;
                if (name.Contains(DropLocation.RecycleClsid, StringComparison.OrdinalIgnoreCase)) return true;
                if (name.Equals(expected, StringComparison.CurrentCultureIgnoreCase)) return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Log("recycle hit: " + ex.Message);
        }
        return false;
    }

    private static string RecycleDisplayName()
    {
        var info = new SHFILEINFO();
        SHGetFileInfo("::{" + DropLocation.RecycleClsid + "}", 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), ShgfiDisplayName);
        return string.IsNullOrWhiteSpace(info.szDisplayName) ? "Recycle Bin" : info.szDisplayName;
    }

    private static DropKind ClassifyShellWindow(int x, int y)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ShellWindowsClsid);
            if (type == null) return DropKind.Other;
            dynamic windows = Activator.CreateInstance(type)!;
            int count = (int)windows.Count;
            DropKind best = DropKind.Other;
            long bestArea = long.MaxValue;
            for (int i = 0; i < count; i++)
            {
                object window = windows.Item(i);
                if (!TryWindowRect(window, out RECT rect)) continue;
                if (x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom) continue;
                long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (area >= bestArea) continue;
                string url = "";
                try { url = (string)((dynamic)window).LocationURL; } catch { /* 非文件夹窗口 */ }
                var kind = DropLocation.FromShellUrl(url);
                if (kind == DropKind.Other) continue;
                best = kind;
                bestArea = area;
            }
            return best;
        }
        catch (Exception ex)
        {
            Logger.Log("shell hit: " + ex.Message);
            return DropKind.Other;
        }
    }

    private static bool TryWindowRect(object window, out RECT rect)
    {
        rect = default;
        try
        {
            dynamic dyn = window;
            var hwnd = new IntPtr(Convert.ToInt64(dyn.HWND));
            return hwnd != IntPtr.Zero && GetWindowRect(hwnd, out rect);
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
