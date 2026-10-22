using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Clothesline;

/// <summary>独立置顶贴图。关闭它不删文件，也不从绳子上取下夹子。</summary>
public partial class PinWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    // 外圈留给阴影，内圈是玻璃描边和内边距。工具栏一行加状态行的空隙。
    private const double HorizontalChrome = 14 * 2 + 10 * 2 + 2;
    private const double ToolbarAndChrome = 14 * 2 + 10 * 2 + 2 + 36 + 8;
    private const double ClickThroughOpacity = 0.5;

    private readonly double _aspect;
    private bool _locked;
    private bool _clickThrough;

    public string FilePath { get; }
    public string TitleText { get; }
    public bool ClickThrough => _clickThrough;

    public event Action<string, string>? Notice;

    public PinWindow(string path, int offset)
    {
        InitializeComponent();
        FilePath = path;
        TitleText = Path.GetFileName(path);
        Title = "贴图 — " + TitleText;

        var bmp = LoadBitmap(path);
        if (bmp == null)
        {
            _aspect = 4.0 / 3.0;
            Status.Text = "图片无法显示。";
        }
        else
        {
            Photo.Source = bmp;
            _aspect = Math.Max(0.2, bmp.PixelWidth / (double)Math.Max(1, bmp.PixelHeight));
        }

        ApplyImageWidth(360);
        var area = SystemParameters.WorkArea;
        Left = area.Left + 72 + (offset % 8) * 28;
        Top = area.Top + 72 + (offset % 8) * 28;
    }

    public void SetClickThrough(bool on)
    {
        ThroughBox.IsChecked = on;
    }

    public void Recognize() => _ = RecognizeAsync();

    private void Lock_Changed(object sender, RoutedEventArgs e)
    {
        _locked = LockBox.IsChecked == true;
        Photo.Cursor = _locked ? Cursors.Arrow : Cursors.SizeAll;
        Sizer.IsEnabled = !_locked;
    }

    private void Through_Changed(object sender, RoutedEventArgs e)
    {
        _clickThrough = ThroughBox.IsChecked == true;
        Dispatcher.BeginInvoke(() =>
        {
            ApplyClickThrough(_clickThrough);
            if (_clickThrough)
            {
                Notice?.Invoke("鼠标穿透", "点击会穿过这张贴图。要关掉，用托盘菜单「已打开的贴图」。");
            }
        });
    }

    private void Photo_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_locked || _clickThrough || e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* 按钮已松开 */ }
    }

    private void Photo_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_locked || _clickThrough) return;
        double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        double imageWidth = Math.Max(80, Width - HorizontalChrome);
        ApplyImageWidth(imageWidth * factor);
        e.Handled = true;
    }

    private void Sizer_Drag(object sender, DragDeltaEventArgs e)
    {
        if (_locked || _clickThrough) return;
        double imageWidth = Math.Max(80, Width - HorizontalChrome);
        ApplyImageWidth(imageWidth + e.HorizontalChange);
    }

    private void ApplyImageWidth(double imageWidth)
    {
        imageWidth = Math.Clamp(imageWidth, 140, 1400);
        Width = imageWidth + HorizontalChrome;
        Height = imageWidth / _aspect + ToolbarAndChrome;
    }

    private async void Ocr_Click(object sender, RoutedEventArgs e) => await RecognizeAsync();

    private async Task RecognizeAsync()
    {
        Status.Text = "正在识别…";
        int generation = OcrTextWindow.Claim();
        var outcome = await OcrText.ReadAsync(FilePath);
        if (!OcrTextWindow.IsCurrent(generation)) return;
        if (!outcome.Available)
        {
            Status.Text = outcome.Error ?? "无法识别。";
            Notice?.Invoke("无法识别", Status.Text);
            return;
        }
        if (string.IsNullOrWhiteSpace(outcome.Text))
        {
            Status.Text = "没有识别到文字。";
            Notice?.Invoke("识别文字", Status.Text);
            return;
        }

        OcrTextWindow.Present(outcome.Text, generation);
        Status.Text = "识别结果已打开。";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>只开关 WS_EX_TRANSPARENT，保留原有扩展样式（含分层窗口位），避免整窗变白。</summary>
    private void ApplyClickThrough(bool on)
    {
        Opacity = on ? ClickThroughOpacity : 1;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var ex = GetExStyle(hwnd);
            long value = ex.ToInt64();
            value = on ? (value | WsExTransparent) : (value & ~WsExTransparent);
            SetExStyle(hwnd, new IntPtr(value));
        }
        catch (Exception ex)
        {
            Logger.Log("click-through: " + ex.Message);
            Status.Text = "无法切换鼠标穿透。";
        }
    }

    private static IntPtr GetExStyle(IntPtr hwnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, GwlExStyle) : new IntPtr(GetWindowLong(hwnd, GwlExStyle));

    private static void SetExStyle(IntPtr hwnd, IntPtr value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr(hwnd, GwlExStyle, value);
        else SetWindowLong(hwnd, GwlExStyle, value.ToInt32());
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Logger.Log("pin image: " + ex.Message);
            return null;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
