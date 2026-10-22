using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clothesline;

public partial class LineWindow : Window
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_TRAYICON = 0x8000 + 2; // WM_APP + 2
    private const int WM_DISPLAYCHANGE = 0x007E;

    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const int HOTKEY_ID = 1;

    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan PeekHold = TimeSpan.FromSeconds(2.8);
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(350);

    private readonly ObservableCollection<PegItem> _pegs = new();
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _peekTimer;
    private readonly DispatcherTimer _edgeTimer;
    private readonly EdgeHold _edgeHold = new();
    private readonly MenuHold _menuHold = new();
    private readonly Random _rng = new();

    private AppSettings _settings;
    private HwndSource? _src;
    private FolderWatchSet? _watchers;
    private TrayIcon? _tray;
    private Window? _hotZone;

    // 与上一张挂出的内容（6 秒内）同像素则跳过：
    // 一次 Win+Shift+S 会同时进剪贴板和自动保存，剪贴板消息也可能连发两次
    private string? _lastHash;
    private DateTime _lastHashAt;
    private DateTime _suppressClipboardUntil; // 自己复制时别再监听自己

    private bool _shown;
    private bool _mouseInside;
    private bool _clipboardListening;
    private bool _suspendPersist;
    private bool _pegStoreBroken;
    private bool _demo;
    private bool _hanging;
    private bool _hangQueued;
    private bool _fitting;
    private bool _physicalWidthOk;
    private int _physicalFixes;
    private int _menuHoldGeneration;
    private double _anchorTop;
    private double _dipScaleX = 1;
    private double _ropeDrawnWidth = -1;
    private bool _hotkeyFellBack;
    private string _hotkeyLabel = "不可用";
    private readonly List<PinWindow> _pins = new();
    private HistoryWindow? _historyWindow;
    private int _pinOffset;

    // 拖拽状态
    private Point? _pressPoint;
    private PegItem? _pressItem;
    private bool _dragging;

    public LineWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Width = SystemParameters.PrimaryScreenWidth;
        Height = PegZoom.LineHeight(PegZoom.Default);
        Top = -Height; // 初始完全移出屏幕（白底窗口不能压在桌面上）
        ApplyRope(Width);
        Pegs.ItemsSource = _pegs;
        _pegs.CollectionChanged += Pegs_CollectionChanged;
        SizeChanged += (_, _) => OnWindowSizeChanged();
        Loaded += (_, _) => ScheduleHang();
        PegScroll.ScrollChanged += (_, _) => ScheduleHang();
        DpiChanged += (_, _) => FitToAnchorScreen();

        _hideTimer = new DispatcherTimer { Interval = HideDelay };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); if (_menuHold.AllowHide(_mouseInside)) SlideTo(false); };
        _peekTimer = new DispatcherTimer { Interval = PeekHold };
        _peekTimer.Tick += (_, _) => { _peekTimer.Stop(); if (_menuHold.AllowHide(_mouseInside)) SlideTo(false); };
        _edgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(EdgeHold.Milliseconds) };
        _edgeTimer.Tick += (_, _) =>
        {
            _edgeTimer.Stop();
            if (_edgeHold.Due() && !_shown) Peek();
        };

        MouseEnter += (_, _) => { _mouseInside = true; _hideTimer.Stop(); };
        MouseLeave += (_, _) => NotePointerLeft();
        SourceInitialized += (_, _) => OnHwndReady();
    }

    /// <summary>三条绳子共用一条冻结的弧。宽度不变时不重画，避免布局回调来回触发。</summary>
    private void ApplyRope(double width)
    {
        if (!double.IsFinite(width) || width <= 1) return;
        if (RopeCore.Data != null && Math.Abs(_ropeDrawnWidth - width) < 0.5) return;
        _ropeDrawnWidth = width;
        double end = RopeCurve.EndY;
        var segment = new QuadraticBezierSegment(
            new Point(width / 2, RopeCurve.ControlY), new Point(width, end), true);
        var figure = new PathFigure(new Point(0, end), new PathSegment[] { segment }, false);
        var geo = new PathGeometry(new[] { figure });
        geo.Freeze();
        RopeShadow.Data = geo;
        RopeHighlight.Data = geo;
        RopeCore.Data = geo;
    }

    /// <summary>
    /// 夹子挂点跟着绳子走。用布局坐标（不含 RenderTransform），
    /// 否则 Hang 自己会进下一次计算。平移放在旋转和缩放之后，单位是窗口像素。
    /// </summary>
    private void HangPegs()
    {
        if (_hanging) return;
        _hanging = true;
        try
        {
            double width = ActualWidth > 1 ? ActualWidth : Width;
            ApplyRope(width);
            if (!IsLoaded || _pegs.Count == 0) return;
            for (int i = 0; i < _pegs.Count; i++)
            {
                if (Pegs.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                    continue;
                if (container.ActualWidth <= 0) continue;
                // 容器没有 RenderTransform。子元素的平移如果算进去，下一次会把上次的下垂再减一次。
                double x = container.ActualWidth / 2;
                if (VisualTreeHelper.GetChildrenCount(container) > 0 &&
                    VisualTreeHelper.GetChild(container, 0) is FrameworkElement child &&
                    child.ActualWidth > 0)
                {
                    x = child.Margin.Left + child.ActualWidth / 2;
                }
                Point notch;
                try
                {
                    notch = container.TranslatePoint(new Point(x, RopeCurve.NotchY), Root);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                double drop = RopeCurve.Y(notch.X, width) - notch.Y;
                if (Math.Abs(_pegs[i].Hang.Y - drop) > 0.25)
                    _pegs[i].Hang.Y = drop;
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _hanging = false;
        }
    }

    /// <summary>挂点只在布局或滚动之后算一次。LayoutUpdated 每次布局都进，动画时会把界面拖卡。</summary>
    private void ScheduleHang()
    {
        if (_hangQueued) return;
        _hangQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _hangQueued = false;
            HangPegs();
        }, DispatcherPriority.Loaded);
    }

    private void OnWindowSizeChanged()
    {
        ScheduleHang();
        if (!_physicalWidthOk)
            Dispatcher.BeginInvoke(EnsurePhysicalWidths, DispatcherPriority.Background);
    }

    /// <summary>
    /// 按窗口所在屏幕的工作区铺满顶边。SystemParameters 的宽度在分层窗口上会少一截
    /// （常见是把 DIP 当成物理像素，125% 缩放时大约只剩 4/5，看起来像 3/4）。
    /// </summary>
    private void FitToAnchorScreen()
    {
        if (_fitting) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!MonitorSpan.TryMeasure(hwnd, out var px)) return;
        var dip = WorkAreaSpan.ToDip(px.Left, px.Top, px.Right, px.Bottom, px.Scale, px.Scale);
        _fitting = true;
        try
        {
            _anchorTop = dip.Top;
            _dipScaleX = px.Scale;
            Left = dip.Left;
            Width = dip.Width;
            if (_hotZone != null)
            {
                _hotZone.Left = dip.Left;
                _hotZone.Width = dip.Width;
                _hotZone.Top = dip.Top;
            }
            CacheElement(RopeHost);
            RefreshPegCaches();
            if (!_shown)
            {
                BeginAnimation(TopProperty, null);
                Top = HiddenTop;
            }
            _physicalWidthOk = false;
            _physicalFixes = 0;
            Logger.Log($"span work px={px.Width} dip={dip.Width:0.#} scale={px.Scale:0.##} left={dip.Left:0.#}");
        }
        finally
        {
            _fitting = false;
        }
        Dispatcher.BeginInvoke(EnsurePhysicalWidths, DispatcherPriority.Loaded);
    }

    private void EnsurePhysicalWidths()
    {
        if (_physicalWidthOk || _physicalFixes >= 4) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!MonitorSpan.TryMeasure(hwnd, out var px)) return;
        bool lineOk = MonitorSpan.AlignWidth(hwnd, px.Left, px.Width);
        bool hotOk = true;
        if (_hotZone != null)
        {
            var hot = new WindowInteropHelper(_hotZone).Handle;
            hotOk = MonitorSpan.AlignWidth(hot, px.Left, px.Width);
        }
        if (!lineOk || !hotOk) _physicalFixes++;
        if (lineOk && hotOk) _physicalWidthOk = true;
        else Logger.Log("span hwnd width corrected to work area " + px.Width);
    }

    private void CacheElement(FrameworkElement? fe)
    {
        if (fe == null) return;
        double scale = _dipScaleX >= 1 ? _dipScaleX : 1;
        if (fe.CacheMode is BitmapCache cache && Math.Abs(cache.RenderAtScale - scale) < 0.01) return;
        fe.CacheMode = new BitmapCache(scale);
    }

    private void RefreshPegCaches()
    {
        for (int i = 0; i < _pegs.Count; i++)
        {
            if (Pegs.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;
            if (VisualTreeHelper.GetChildrenCount(container) > 0 &&
                VisualTreeHelper.GetChild(container, 0) is FrameworkElement child)
                CacheElement(child);
        }
    }

    private void Peg_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) CacheElement(fe);
    }

    private double HiddenTop => _anchorTop - Height;

    internal void PrepareDemo()
    {
        _demo = true;
        _suspendPersist = true;
    }

    internal void SelftestPeg(string path) => AddCapture(path, fromClipboard: false);

    /// <summary>挂两张合成截图，渲染窗口内容为 PNG 后退出（公开仓库的演示图，不含桌面内容）。</summary>
    internal void StartDemo(string outPath)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            PegSynthetic(SyntheticCapture(Color.FromRgb(0x2E, 0x86, 0xAB), "Screenshot 01"));
            PegSynthetic(SyntheticCapture(Color.FromRgb(0xD3, 0x71, 0x2E), "Screenshot 02"));
            Top = 0;
            await Task.Delay(250);
            var rtb = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(Root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(outPath)) encoder.Save(fs);
            Logger.Log("demo saved: " + outPath);
            Application.Current.Shutdown();
        });
    }

    private void PegSynthetic(BitmapSource bmp)
    {
        var item = new PegItem("(demo)", bmp, _rng.NextDouble() * 10 - 5);
        _pegs.Add(item);
        TrimToCapacity();
    }

    private static BitmapSource SyntheticCapture(Color bg, string label)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, 800, 500));
            var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 44, Brushes.White, 1.25);
            dc.DrawText(text, new Point(60, 215));
        }
        var rtb = new RenderTargetBitmap(800, 500, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private void OnHwndReady()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _src = HwndSource.FromHwnd(hwnd);
        _src?.AddHook(WndProc);

        // 注意：不要在 SourceInitialized 里改 GWL_EXSTYLE——会把 AllowsTransparency
        // 窗口的 per-pixel alpha 弄坏（整窗变白底）。TOOLWINDOW/NOACTIVATE 由
        // ShowInTaskbar=False + ShowActivated=False 承担。

        _tray = new TrayIcon(hwnd, WM_TRAYICON, "Clothesline — 截图晾衣绳");
        FitToAnchorScreen();
        SyncClipboardListener();
        ReregisterHotkey();

        _watchers = new FolderWatchSet();
        _watchers.SetHandler(path => Dispatcher.BeginInvoke(() => AddCapture(path, fromClipboard: false)));
        _watchers.Replace(_settings.WatchFolders);

        CreateHotZone();
        if (!_demo) RestorePegs();
        Logger.Log("ready");
    }

    /// <summary>
    /// 独立热区窗口：屏幕顶边 3px 高的透明条。指针要在这里停约半秒才唤出晾衣绳，提前离开则取消。
    /// 主窗口隐藏时整个在屏幕外，得靠它把绳子拉回来。
    /// </summary>
    private void CreateHotZone()
    {
        _hotZone = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            ResizeMode = ResizeMode.NoResize,
            Left = Left,
            Top = _anchorTop,
            Width = Width,
            Height = 3,
            Content = HitTestBrush(),
        };
        _hotZone.MouseEnter += (_, _) =>
        {
            _hideTimer.Stop();
            Logger.Log("hotzone hover");
            if (_shown)
            {
                _mouseInside = true;
                return;
            }
            // 只延迟「鼠标停在顶边」。热键和 Peek()（新截图）不经过这里。
            _edgeHold.Enter();
            _edgeTimer.Stop();
            _edgeTimer.Start();
        };
        _hotZone.MouseLeave += (_, _) =>
        {
            _edgeHold.Leave();
            _edgeTimer.Stop();
            _mouseInside = false;
        };
        _hotZone.Show();
        FitToAnchorScreen();
        Logger.Log("hotzone ready");
    }

    /// <summary>alpha=1 的画刷：肉眼不可见，但操作系统的分层窗口命中测试需要 alpha&gt;0。</summary>
    private static Border HitTestBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        brush.Freeze();
        return new Border { Background = brush };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_CLIPBOARDUPDATE:
                if (_settings.ClipboardWatchEnabled && DateTime.UtcNow >= _suppressClipboardUntil)
                    Dispatcher.BeginInvoke(ReadClipboard);
                handled = true;
                break;
            case WM_HOTKEY:
                ToggleShow();
                handled = true;
                break;
            case WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(FitToAnchorScreen);
                break;
            case WM_TRAYICON:
                int m = unchecked((int)lParam.ToInt64());
                if (m == WM_LBUTTONDBLCLK) ToggleShow();
                else if (m == WM_RBUTTONUP) ShowTrayMenu();
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    // ---------- 截图来源 ----------

    private void ReadClipboard()
    {
        try
        {
            if (!_settings.ClipboardWatchEnabled) return;
            if (!Clipboard.ContainsImage()) return;
            var img = Clipboard.GetImage();
            if (img == null) return;
            if (!_settings.ClipboardAccepts(img.PixelWidth, img.PixelHeight))
            {
                Logger.Log($"clipboard filtered {img.PixelWidth}x{img.PixelHeight}");
                return;
            }

            // 微信等程序的 DIB 没有 alpha 字节（全 0），直接存会得到"全透明"的 PNG，
            // 卡片上图片就隐形了。把 alpha 抬满再存（截图本来就是不透明的）。
            var opaque = Opaque(img);

            string path = Path.Combine(App.CapturesDir, $"clip-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(opaque));
            using (var fs = File.Create(path)) encoder.Save(fs);

            AddCapture(path, fromClipboard: true);
        }
        catch (Exception ex)
        {
            Logger.Log("clipboard read: " + ex.Message);
        }
    }

    private void AddCapture(string path, bool fromClipboard, bool ignoreDedup = false, double? angle = null)
    {
        try
        {
            if (!File.Exists(path)) return;
            if (_pegs.Any(p => string.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                Logger.Log("already pegged: " + path);
                return;
            }

            var thumb = LoadThumbnail(path);
            if (thumb == null) { Logger.Log("unreadable: " + path); return; }

            string hash = Hash(thumb);
            if (!ignoreDedup && hash == _lastHash && DateTime.UtcNow - _lastHashAt < DedupWindow)
            {
                Logger.Log("dedup skip: " + path);
                return;
            }
            _lastHash = hash;
            _lastHashAt = DateTime.UtcNow;

            var item = new PegItem(path, thumb, angle ?? (_rng.NextDouble() * 10 - 5));
            _pegs.Add(item);
            TrimToCapacity();
            Logger.Log($"pegged ({(fromClipboard ? "clipboard" : "file")}): {path}");
            SnapToNewest();

            if (_shown) SwingOne(item);
            else Peek();
        }
        catch (Exception ex)
        {
            Logger.Log("addcapture: " + ex);
        }
    }

    private void RestorePegs()
    {
        _suspendPersist = true;
        var loaded = 0;
        var unreadable = 0;
        try
        {
            foreach (var row in PegStore.Load(App.DataDir))
            {
                var thumb = LoadThumbnail(row.FilePath);
                if (thumb == null)
                {
                    unreadable++;
                    Logger.Log("restore unreadable: " + row.FilePath);
                    continue;
                }
                _pegs.Add(new PegItem(row.FilePath, thumb, row.Angle));
                loaded++;
            }
            TrimToCapacity();
            Logger.Log("restored pegs: " + loaded);
        }
        catch (Exception ex)
        {
            Logger.Log("pegs.json unreadable: " + ex);
            QuarantinePegStore();
            _tray?.ShowBalloon("晾衣绳记录损坏", "pegs.json 无法读取，已改名为 pegs.json.bad，本次从空绳子开始。");
        }
        finally
        {
            _suspendPersist = false;
        }

        if (!_pegStoreBroken && unreadable == 0) RememberPegs();
        else if (unreadable > 0) Logger.Log("pegs.json kept because " + unreadable + " file(s) could not be read");
        SnapToNewest();
    }

    private void QuarantinePegStore()
    {
        try
        {
            var path = PegStore.FilePath(App.DataDir);
            if (!File.Exists(path)) return;
            var bad = path + ".bad";
            if (File.Exists(bad)) File.Delete(bad);
            File.Move(path, bad);
            Logger.Log("moved unreadable pegs.json to pegs.json.bad");
        }
        catch (Exception ex)
        {
            _pegStoreBroken = true;
            Logger.Log("could not quarantine pegs.json: " + ex.Message);
        }
    }

    private void Pegs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RememberPegs();
        FitLineHeight();
        ScheduleHang();
    }

    private void RememberPegs()
    {
        if (_suspendPersist || _pegStoreBroken || _demo) return;
        try
        {
            PegStore.Save(App.DataDir, _pegs.Select(p => new PegRecord { FilePath = p.FilePath, Angle = p.Angle }));
        }
        catch (Exception ex)
        {
            Logger.Log("pegs save: " + ex.Message);
        }
    }

    /// <summary>alpha 抬满。微信等 DIB 来源的图 alpha 全 0 但 RGB 有效；
    /// 不能用 FormatConvertedBitmap（会过预乘空间，A=0 的图直接变全黑），只能按字节改。</summary>
    private static BitmapSource Opaque(BitmapSource src)
    {
        if (src.Format == PixelFormats.Bgra32)
        {
            int stride = src.PixelWidth * 4;
            var buffer = new byte[stride * (long)src.PixelHeight];
            src.CopyPixels(buffer, stride, 0);
            for (int i = 3; i < buffer.Length; i += 4) buffer[i] = 255;
            var opaque = BitmapSource.Create(src.PixelWidth, src.PixelHeight, src.DpiX, src.DpiY,
                PixelFormats.Bgra32, null, buffer, stride);
            opaque.Freeze();
            return opaque;
        }
        if (src.Format == PixelFormats.Pbgra32)
        {
            var opaque = new FormatConvertedBitmap(src, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            return opaque;
        }
        return src;
    }

    private static BitmapSource? LoadThumbnail(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 800; // 覆盖基准宽 × 最大缩放（184×2）
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return Opaque(bmp);
        }
        catch (Exception ex)
        {
            Logger.Log("thumb " + path + ": " + ex.Message);
            return null;
        }
    }

    private static BitmapSource? LoadFullBitmap(string path)
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
            return Opaque(bmp);
        }
        catch (Exception ex)
        {
            Logger.Log("full " + path + ": " + ex.Message);
            return null;
        }
    }

    private static string Hash(BitmapSource src)
    {
        try
        {
            int stride = (src.PixelWidth * src.Format.BitsPerPixel + 7) / 8;
            long total = (long)stride * src.PixelHeight;
            int h = total <= 4_000_000 ? src.PixelHeight : Math.Max(1, (int)(4_000_000 / stride));
            var buffer = new byte[stride * h];
            src.CopyPixels(new Int32Rect(0, 0, src.PixelWidth, h), buffer, stride, 0);
            using var md5 = MD5.Create();
            return Convert.ToHexString(md5.ComputeHash(buffer));
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    // ---------- 显示 / 收起 ----------

    private void Peek()
    {
        if (_shown) return;
        if (Fullscreen.Suppressed())
        {
            Logger.Log("peek suppressed (fullscreen)");
            return;
        }
        SlideTo(true);
        _peekTimer.Stop();
        _peekTimer.Start();
    }

    private void ToggleShow()
    {
        _peekTimer.Stop();
        _hideTimer.Stop();
        SlideTo(!_shown);
    }

    private void SlideTo(bool show, bool instant = false)
    {
        _shown = show;
        double target = show ? _anchorTop : HiddenTop;
        // 先开晃，再开下落。延迟全部落在下落结束之前，照片是边下来边晃。
        if (show) SwingAll();
        if (instant)
        {
            BeginAnimation(Window.TopProperty, null);
            Top = target;
        }
        else
        {
            int ms = show ? SwingTiming.SlideDownMs : SwingTiming.SlideUpMs;
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn },
            };
            BeginAnimation(Window.TopProperty, anim);
        }
        // 绳子显示期间热区让位（不然顶边 3px 两窗争鼠标会来回闪）
        if (_hotZone != null) _hotZone.Visibility = show ? Visibility.Hidden : Visibility.Visible;
        Logger.Log(show ? "slide down" : "slide up");
    }

    private void SwingAll()
    {
        int n = _pegs.Count;
        for (int i = 0; i < n; i++)
            SwingOne(_pegs[i], SwingTiming.DelayMs(i, n));
    }

    private void SwingOne(PegItem item, int delayMs = 0)
    {
        double a = item.Angle;
        var anim = new DoubleAnimation(a - 13, a, TimeSpan.FromMilliseconds(SwingTiming.SwingMs))
        {
            EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 3, Springiness = 5 },
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
        };
        item.Rot.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void SnapToNewest()
    {
        Dispatcher.BeginInvoke(() =>
        {
            PegScroll.UpdateLayout();
            PegScroll.ScrollToRightEnd();
        }, DispatcherPriority.Background);
    }

    private void PegScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var peg = e.OriginalSource is DependencyObject node ? FindPeg(node) : null;
        if (PegZoom.Decide(peg != null) == PegZoom.Gesture.ZoomPeg)
        {
            double before = peg!.Zoom;
            peg.Zoom = PegZoom.Next(before, e.Delta);
            if (Math.Abs(peg.Zoom - before) > 1e-9)
            {
                FitLineHeight();
                ScheduleHang();
            }
            return;
        }

        if (PegScroll.ScrollableWidth <= 0) return;
        double ticks = e.Delta / 120.0;
        PegScroll.ScrollToHorizontalOffset(PegScroll.HorizontalOffset - ticks * 110);
    }

    private void FitLineHeight()
    {
        // 不低于默认高度。缩小单张时绳子不跟着塌下去。
        double max = PegZoom.Default;
        foreach (var peg in _pegs)
        {
            if (peg.Zoom > max) max = peg.Zoom;
        }

        double height = PegZoom.LineHeight(max);
        if (Math.Abs(Height - height) < 0.5) return;
        Height = height;
        if (_shown) return;
        BeginAnimation(Window.TopProperty, null);
        Top = HiddenTop;
    }

    // ---------- 照片交互 ----------

    private void Peg_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PegItem item)
        {
            if (e.ClickCount >= 2)
            {
                OpenFile(item); // Image 不是 Control，双击只能在这里用 ClickCount 判断
                _pressPoint = null;
                _pressItem = null;
                _dragging = false;
                return;
            }
            _pressPoint = e.GetPosition(Root);
            _pressItem = item;
            _dragging = false;
        }
    }

    private void Peg_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressPoint == null || _pressItem == null || _dragging) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed) { _pressPoint = null; _pressItem = null; return; }
        var p = e.GetPosition(Root);
        if (Math.Abs(p.X - _pressPoint.Value.X) + Math.Abs(p.Y - _pressPoint.Value.Y) < 8) return;

        _dragging = true;
        var item = _pressItem;
        var effect = DragDropEffects.None;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { item.FilePath });
            effect = DragDrop.DoDragDrop(Pegs, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Logger.Log("drag: " + ex.Message);
        }
        _pressPoint = null;
        _pressItem = null;
        _dragging = false;
        FinishDrag(item, effect);
    }

    /// <summary>
    /// 文件夹：取下，文件保留（资源管理器自己复制或移动）。
    /// 回收站：文件进回收站并取下。认不出的落点不动夹子；文件已被系统挪走时只取下。
    /// </summary>
    private void FinishDrag(PegItem item, DragDropEffects effect)
    {
        if (effect == DragDropEffects.None) return;
        var kind = ShellDrop.ClassifyAtCursor();
        Logger.Log($"drag effect={effect} kind={kind} {item.FilePath}");
        switch (kind)
        {
            case DropKind.RecycleBin:
                TakeDown(item, deleteFile: true);
                break;
            case DropKind.Folder:
                TakeDown(item, deleteFile: false);
                break;
            default:
                if (!File.Exists(item.FilePath)) TakeDown(item, deleteFile: false);
                break;
        }
    }

    private void Peg_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var item = _pressItem;
        var wasDragging = _dragging;
        _pressPoint = null;
        _pressItem = null;
        _dragging = false;
        if (item == null || wasDragging) return;
        if (e.ClickCount >= 2) return;
        CopyToClipboard(item);
    }

    private void OpenFile(PegItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log("open: " + ex.Message);
        }
    }

    private void Peg_RightDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PegItem item)
        {
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint, PlacementTarget = fe };

            var open = new MenuItem { Header = "打开图片" };
            open.Click += (_, _) => OpenFile(item);
            var folder = new MenuItem { Header = "打开所在文件夹" };
            folder.Click += (_, _) => RevealFile(item.FilePath);
            var pin = new MenuItem { Header = "贴图" };
            pin.Click += (_, _) => PinFile(item.FilePath);
            var ocr = new MenuItem { Header = "识别文字" };
            ocr.Click += (_, _) => _ = RecognizeAsync(item.FilePath);
            var remove = new MenuItem { Header = "取下" };
            remove.Click += (_, _) => TakeDown(item, deleteFile: false);
            // 标注（画笔/箭头/马赛克）以后从这里加，不要塞进拖拽路径。
            var delete = new MenuItem { Header = "取下并删除文件" };
            delete.Click += (_, _) => TakeDown(item, deleteFile: true);

            menu.Items.Add(open);
            menu.Items.Add(folder);
            menu.Items.Add(pin);
            menu.Items.Add(ocr);
            menu.Items.Add(new Separator());
            menu.Items.Add(remove);
            menu.Items.Add(delete);
            // 必须先占住，再打开。IsOpen 会同步让窗口收到 MouseLeave。
            HoldLineForMenu(menu);
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    /// <summary>
    /// 照片菜单打开期间不收回。弹层抢走鼠标后，窗口的 MouseLeave 是假离开。
    /// 关掉再看指针还在不在绳子上，离开才走原来的 350ms 收回。
    /// </summary>
    private void HoldLineForMenu(ContextMenu menu)
    {
        int generation = ++_menuHoldGeneration;
        _menuHold.Opened();
        _hideTimer.Stop();
        menu.Closed += (_, _) =>
            Dispatcher.BeginInvoke(() => ReleaseMenuHold(generation), DispatcherPriority.Background);
    }

    private void ReleaseMenuHold(int generation) => SettleMenuHold(generation, retried: false);

    private void SettleMenuHold(int generation, bool retried)
    {
        if (generation != _menuHoldGeneration) return;
        bool inside = PointerOverLine();
        // 弹层 HWND 可能还没拆掉。光标仍落在绳子矩形里时再等一轮输入，避免误收。
        if (!inside && !retried && CursorInsideWindow())
        {
            Dispatcher.BeginInvoke(() => SettleMenuHold(generation, retried: true), DispatcherPriority.ApplicationIdle);
            return;
        }
        _mouseInside = inside;
        if (_menuHold.Closed(inside))
        {
            if (_shown) _hideTimer.Start();
        }
        else
        {
            _hideTimer.Stop();
        }
    }

    private void NotePointerLeft()
    {
        _mouseInside = false;
        if (_shown && _menuHold.AllowHide(false))
            _hideTimer.Start();
        else
            _hideTimer.Stop();
    }

    /// <summary>菜单关掉后看光标下的窗口。IsMouseOver 在弹层拆掉前会一直是假的。</summary>
    private bool PointerOverLine()
    {
        if (IsMouseOver) return true;
        IntPtr hwnd = _src?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero || !GetCursorPos(out CursorPoint pt)) return false;
        IntPtr under = WindowFromPoint(pt);
        if (under == IntPtr.Zero) return false;
        return under == hwnd || GetAncestor(under, GaRoot) == hwnd;
    }

    private bool CursorInsideWindow()
    {
        IntPtr hwnd = _src?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero || !GetCursorPos(out CursorPoint pt) || !GetWindowRect(hwnd, out CursorRect rect))
            return false;
        return pt.X >= rect.Left && pt.X < rect.Right && pt.Y >= rect.Top && pt.Y < rect.Bottom;
    }

    /// <summary>取下夹子。deleteFile 为真时把文件移进回收站；删除失败则夹子留在绳子上。</summary>
    private void TakeDown(PegItem item, bool deleteFile)
    {
        if (deleteFile)
        {
            if (File.Exists(item.FilePath) && !ShellDrop.TryRecycle(item.FilePath))
            {
                _tray?.ShowBalloon("无法删除", "文件仍在原处，夹子保留。");
                return;
            }
            try { HistoryStore.Forget(App.DataDir, item.FilePath); }
            catch (Exception ex) { Logger.Log("history forget: " + ex.Message); }
        }
        else
        {
            RememberHistory(item);
        }
        _pegs.Remove(item);
        Logger.Log((deleteFile ? "taken down and deleted: " : "taken down: ") + item.FilePath);
    }

    /// <summary>超出容量时把最旧的夹子取下并记入历史（文件还在的才记）。</summary>
    private void TrimToCapacity()
    {
        int max = Math.Max(1, _settings.MaxPegs);
        while (_pegs.Count > max)
        {
            RememberHistory(_pegs[0]);
            _pegs.RemoveAt(0);
        }
    }

    private void RememberHistory(PegItem item)
    {
        if (_demo || item.FilePath == "(demo)") return;
        try
        {
            HistoryStore.Remember(App.DataDir, item.FilePath, item.Angle, _settings.HistoryLimit);
        }
        catch (Exception ex)
        {
            Logger.Log("history: " + ex.Message);
        }
    }

    /// <summary>贴图不取下夹子。关掉贴图窗口也不删文件。</summary>
    private void PinFile(string path)
    {
        if (!File.Exists(path))
        {
            _tray?.ShowBalloon("无法贴图", "文件已经不在了。");
            return;
        }
        var pin = new PinWindow(path, _pinOffset++);
        pin.Notice += (title, text) => _tray?.ShowBalloon(title, text);
        pin.Closed += (_, _) => _pins.Remove(pin);
        _pins.Add(pin);
        pin.Show();
        Logger.Log("pinned: " + path);
    }

    private void HangFromHistory(HistoryEntry entry)
    {
        if (!File.Exists(entry.FilePath))
        {
            try { HistoryStore.Forget(App.DataDir, entry.FilePath); }
            catch (Exception ex) { Logger.Log("history forget: " + ex.Message); }
            _tray?.ShowBalloon("无法挂回", "文件已经不在了。");
            return;
        }
        AddCapture(entry.FilePath, fromClipboard: false, ignoreDedup: true, angle: entry.Angle);
        if (_pegs.Any(p => string.Equals(p.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            try { HistoryStore.Forget(App.DataDir, entry.FilePath); }
            catch (Exception ex) { Logger.Log("history forget: " + ex.Message); }
        }
    }

    private async Task RecognizeAsync(string path)
    {
        int generation = OcrTextWindow.Claim();
        var outcome = await OcrText.ReadAsync(path);
        if (!OcrTextWindow.IsCurrent(generation)) return;
        if (!outcome.Available)
        {
            _tray?.ShowBalloon("无法识别", outcome.Error ?? "本机没有可用的 OCR。");
            return;
        }
        if (string.IsNullOrWhiteSpace(outcome.Text))
        {
            _tray?.ShowBalloon("识别文字", "没有识别到文字。");
            return;
        }
        OcrTextWindow.Present(outcome.Text, generation);
        Logger.Log("ocr shown: " + path);
    }

    private void ShowHistory()
    {
        if (_historyWindow != null)
        {
            _historyWindow.Reload();
            _historyWindow.Activate();
            return;
        }
        _historyWindow = new HistoryWindow();
        _historyWindow.Closed += (_, _) => _historyWindow = null;
        _historyWindow.RehangRequested += HangFromHistory;
        _historyWindow.PinRequested += entry => PinFile(entry.FilePath);
        _historyWindow.Show();
    }

    private void Peg_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PegItem item) AnimateScale(item, 1.06);
    }

    private void Peg_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PegItem item) AnimateScale(item, 1.0);
    }

    private static void AnimateScale(PegItem item, double to)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        item.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        item.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void CopyToClipboard(PegItem item)
    {
        try
        {
            var full = LoadFullBitmap(item.FilePath); // 复制原图，不用缩略图
            if (full == null) return;
            _suppressClipboardUntil = DateTime.UtcNow.AddMilliseconds(700); // 别监听自己
            Clipboard.SetImage(full);
            FlashCopied(item);
            Logger.Log("copied to clipboard: " + item.FilePath);
        }
        catch (Exception ex)
        {
            Logger.Log("copy: " + ex.Message);
        }
    }

    private void FlashCopied(PegItem item)
    {
        item.CopyFeedback = true;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
        t.Tick += (_, _) => { t.Stop(); item.CopyFeedback = false; };
        t.Start();
    }

    private void RevealFile(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"");

    /// <summary>点在照片上时不收起，点空白绳子/空白区才收。</summary>
    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindPeg(d) != null) return;
        _peekTimer.Stop();
        SlideTo(false);
    }

    private static PegItem? FindPeg(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is FrameworkElement fe && fe.DataContext is PegItem item) return item;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    // ---------- 设置 / 热键 / 剪贴板 ----------

    private void ShowSettings()
    {
        _peekTimer.Stop();
        _hideTimer.Stop();
        SlideTo(false, instant: true);
        if (_hotZone != null) _hotZone.Visibility = Visibility.Collapsed;
        try
        {
            var dlg = new SettingsWindow(_settings.Clone());
            if (dlg.ShowDialog() == true && dlg.Result != null)
                ApplySettings(dlg.Result);
        }
        catch (Exception ex)
        {
            Logger.Log("settings: " + ex);
            _tray?.ShowBalloon("设置未保存", "写入设置失败，原配置仍在使用。");
        }
        finally
        {
            if (_hotZone != null && !_shown) _hotZone.Visibility = Visibility.Visible;
        }
    }

    private void ApplySettings(AppSettings next)
    {
        next.Normalize();
        next.Save(App.DataDir);
        _settings = next;
        TrimToCapacity();
        try { HistoryStore.Trim(App.DataDir, _settings.HistoryLimit); }
        catch (Exception ex) { Logger.Log("history trim: " + ex.Message); }
        _watchers?.Replace(_settings.WatchFolders);
        SyncClipboardListener();
        ReregisterHotkey();
        SnapToNewest();
        Logger.Log($"settings applied max={_settings.MaxPegs} clipboard={_settings.ClipboardWatchEnabled} folders={_settings.WatchFolders.Count}");
    }

    private void SyncClipboardListener()
    {
        if (_src == null) return;
        var hwnd = _src.Handle;
        if (_settings.ClipboardWatchEnabled)
        {
            if (_clipboardListening) return;
            if (AddClipboardFormatListener(hwnd))
            {
                _clipboardListening = true;
                Logger.Log("clipboard listener on");
            }
            else Logger.Log("clipboard listener FAILED");
        }
        else if (_clipboardListening)
        {
            RemoveClipboardFormatListener(hwnd);
            _clipboardListening = false;
            Logger.Log("clipboard listener off");
        }
    }

    private void ReregisterHotkey()
    {
        if (_src == null) return;
        var hwnd = _src.Handle;
        UnregisterHotKey(hwnd, HOTKEY_ID);

        var preferred = HotkeyChord.FromSettings(_settings);
        HotkeyChord? used = null;
        foreach (var chord in HotkeyChord.RegistrationOrder(preferred))
        {
            if (!chord.IsRegisterable || chord.VirtualKey is not uint vk) continue;
            if (RegisterHotKey(hwnd, HOTKEY_ID, chord.ModifierFlags, vk))
            {
                used = chord;
                break;
            }
            Logger.Log("hotkey busy: " + chord.Label);
        }

        if (used == null)
        {
            _hotkeyLabel = "不可用";
            _hotkeyFellBack = false;
            Logger.Log("hotkey register FAILED");
            _tray?.ShowBalloon("热键注册失败", "首选和回退热键都被占用，请到设置里换一个。");
        }
        else
        {
            _hotkeyLabel = used.Label;
            _hotkeyFellBack = !used.SameBinding(preferred);
            Logger.Log(_hotkeyFellBack ? "hotkey fallback: " + used.Label : "hotkey: " + used.Label);
            if (_hotkeyFellBack)
                _tray?.ShowBalloon("热键被占用", $"{preferred.Label} 无法注册，已改用 {used.Label}。可在设置里更换。");
        }
        UpdateTrayTip();
    }

    private void UpdateTrayTip()
    {
        var tip = _hotkeyLabel == "不可用"
            ? "Clothesline — 截图晾衣绳"
            : $"Clothesline — 截图晾衣绳 ({_hotkeyLabel})";
        _tray?.SetTip(tip);
    }

    private string ToggleHeader()
    {
        if (_hotkeyLabel == "不可用") return "显示 / 收起（热键不可用）";
        return _hotkeyFellBack
            ? $"显示 / 收起（{_hotkeyLabel}，首选被占用）"
            : $"显示 / 收起（{_hotkeyLabel}）";
    }

    // ---------- 托盘 / 退出 ----------

    private void ShowTrayMenu()
    {
        try
        {
            SetForegroundWindow(new WindowInteropHelper(this).Handle);
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint, PlacementTarget = this };

            var toggle = new MenuItem { Header = ToggleHeader() };
            toggle.Click += (_, _) => ToggleShow();
            var clip = new MenuItem
            {
                Header = "监听剪贴板",
                IsCheckable = true,
                IsChecked = _settings.ClipboardWatchEnabled,
            };
            clip.Click += (_, _) =>
            {
                var previous = _settings.ClipboardWatchEnabled;
                _settings.ClipboardWatchEnabled = clip.IsChecked == true;
                try
                {
                    _settings.Save(App.DataDir);
                    SyncClipboardListener();
                }
                catch (Exception ex)
                {
                    _settings.ClipboardWatchEnabled = previous;
                    clip.IsChecked = previous;
                    Logger.Log("settings save: " + ex.Message);
                    _tray?.ShowBalloon("设置未保存", "剪贴板开关没有写入。");
                }
            };
            var settings = new MenuItem { Header = "设置…" };
            settings.Click += (_, _) => ShowSettings();
            var history = new MenuItem { Header = "历史…" };
            history.Click += (_, _) => ShowHistory();

            menu.Items.Add(toggle);
            menu.Items.Add(clip);
            menu.Items.Add(settings);
            menu.Items.Add(history);
            AddOpenPinItems(menu);
            menu.Items.Add(new Separator());
            AddWatchFolderItems(menu);
            var caps = new MenuItem { Header = "打开剪贴板目录" };
            caps.Click += (_, _) => OpenFolder(App.CapturesDir);
            var exit = new MenuItem { Header = "退出 Clothesline" };
            exit.Click += (_, _) => ExitApp();

            menu.Items.Add(caps);
            menu.Items.Add(new Separator());
            menu.Items.Add(exit);
            menu.IsOpen = true;
        }
        catch (Exception ex)
        {
            Logger.Log("traymenu: " + ex.Message);
        }
    }

    private void AddOpenPinItems(ContextMenu menu)
    {
        if (_pins.Count == 0) return;
        var parent = new MenuItem { Header = "已打开的贴图" };
        foreach (var pin in _pins.ToList())
        {
            var sub = new MenuItem { Header = pin.TitleText.Replace("_", "__") };
            var through = new MenuItem { Header = pin.ClickThrough ? "关闭穿透" : "鼠标穿透" };
            var target = pin;
            through.Click += (_, _) => target.SetClickThrough(!target.ClickThrough);
            var ocr = new MenuItem { Header = "识别文字" };
            ocr.Click += (_, _) => target.Recognize();
            var close = new MenuItem { Header = "关闭贴图" };
            close.Click += (_, _) => target.Close();
            sub.Items.Add(through);
            sub.Items.Add(ocr);
            sub.Items.Add(close);
            parent.Items.Add(sub);
        }
        menu.Items.Add(parent);
    }

    private void AddWatchFolderItems(ContextMenu menu)
    {
        if (_settings.WatchFolders.Count <= 1)
        {
            var one = new MenuItem { Header = "打开监视文件夹", IsEnabled = _settings.WatchFolders.Count == 1 };
            if (_settings.WatchFolders.Count == 1)
            {
                var path = _settings.WatchFolders[0];
                one.Click += (_, _) => OpenFolder(path);
            }
            menu.Items.Add(one);
            return;
        }

        var parent = new MenuItem { Header = "打开监视文件夹" };
        foreach (var dir in _settings.WatchFolders)
        {
            var path = dir;
            var item = new MenuItem { Header = path.Replace("_", "__") };
            item.Click += (_, _) => OpenFolder(path);
            parent.Items.Add(item);
        }
        menu.Items.Add(parent);
    }

    private void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\""));
    }

    private void ExitApp()
    {
        Logger.Log("exit requested");
        try
        {
            _watchers?.Dispose();
            foreach (var pin in _pins.ToList()) pin.Close();
            _historyWindow?.Close();
            _tray?.Dispose();
            if (_src != null)
            {
                UnregisterHotKey(_src.Handle, HOTKEY_ID);
                if (_clipboardListening) RemoveClipboardFormatListener(_src.Handle);
            }
        }
        catch { }
        Application.Current.Shutdown();
    }

    // ---------- Win32 ----------

    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(CursorPoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out CursorRect rect);
}
