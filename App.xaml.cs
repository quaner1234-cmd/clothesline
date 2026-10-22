using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Clothesline;

public partial class App : Application
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clothesline");

    /// <summary>剪贴板截图落盘目录（拖拽/双击需要有真实文件）。</summary>
    public static string CapturesDir { get; } = Path.Combine(DataDir, "captures");

    /// <summary>Win+PrintScreen 的默认落点，跟随 OneDrive 重定向。</summary>
    public static string ScreenshotsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(CapturesDir);
        Directory.CreateDirectory(ScreenshotsDir);

        Logger.Init(Path.Combine(DataDir, "log.txt"));

        AppSettings settings;
        try
        {
            settings = AppSettings.Load(DataDir, ScreenshotsDir);
        }
        catch (Exception ex)
        {
            Logger.Log("settings.json: " + ex);
            MessageBox.Show(
                "无法读取 settings.json，Clothesline 已退出，以免用默认值覆盖现有设置。\n\n"
                + AppSettings.FilePath(DataDir) + "\n\n" + ex.Message,
                "Clothesline",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var hotkey = HotkeyChord.FromSettings(settings);
        Logger.Log($"startup, maxPegs={settings.MaxPegs}, clipboard={settings.ClipboardWatchEnabled}, folders={settings.WatchFolders.Count}, hotkey={hotkey.Label}");

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Log("dispatcher exception: " + args.Exception);
            args.Handled = true; // 常驻小工具，宁可吞掉单次异常也别弹窗卡死
        };

        var win = new LineWindow(settings);

        // --save-demo <路径>：挂两张合成图，把绳子窗口渲染成 PNG 后退出（生成 README 演示图用）
        bool demo = e.Args is { Length: 2 } demoArgs && demoArgs[0] == "--save-demo";
        if (demo) win.PrepareDemo();

        win.Show(); // ShowActivated=False + WS_EX_NOACTIVATE，不会抢焦点

        if (demo)
        {
            win.StartDemo(e.Args[1]);
            return;
        }

        // 自检：Clothesline.exe <图片路径> → 直接把该图挂上绳（诊断预览链路用）
        if (e.Args is { Length: 1 } args && File.Exists(args[0]))
        {
            var path = Path.GetFullPath(args[0]);
            win.Dispatcher.BeginInvoke(() => win.SelftestPeg(path));
        }
    }
}
