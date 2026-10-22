using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Clothesline;

/// <summary>识别出的文字。用户自己选择片段；复制和翻译都不在识别完成时自动发生。</summary>
public partial class OcrTextWindow : Window
{
    private static OcrTextWindow? _open;
    private static int _generation;

    /// <summary>在开始识别前调用。后发起的识别会作废更早、更慢的结果。</summary>
    public static int Claim() => Interlocked.Increment(ref _generation);

    public static bool IsCurrent(int generation) => generation == Volatile.Read(ref _generation);

    public static void Present(string text, int generation)
    {
        var app = Application.Current;
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => Present(text, generation));
            return;
        }

        if (!IsCurrent(generation)) return;

        if (_open == null)
        {
            _open = new OcrTextWindow();
            _open.Closed += (_, _) => _open = null;
            _open.Show();
        }

        _open.Body.Text = text ?? "";
        _open.Body.Select(0, 0);
        _open.Status.Text = "";
        _open.Activate();
    }

    public OcrTextWindow()
    {
        InitializeComponent();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        string portion = OcrActions.Portion(Body.Text, Body.SelectionStart, Body.SelectionLength);
        if (portion.Length == 0)
        {
            Status.Text = "没有可复制的文字。";
            return;
        }

        try
        {
            Clipboard.SetText(portion);
            Status.Text = Body.SelectionLength > 0 ? "已复制选中的文字。" : "已复制全部文字。";
        }
        catch (Exception ex)
        {
            Status.Text = "复制失败：" + ex.Message;
        }
    }

    private void Translate_Click(object sender, RoutedEventArgs e)
    {
        string portion = OcrActions.Portion(Body.Text, Body.SelectionStart, Body.SelectionLength);
        if (string.IsNullOrWhiteSpace(portion))
        {
            Status.Text = "没有可翻译的文字。";
            return;
        }

        string url = OcrActions.BingSimplifiedChinese(portion, out bool truncated);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            Status.Text = truncated
                ? "已用浏览器打开必应翻译，只发送了前 " + OcrActions.TranslateLimit + " 个字符。"
                : "已用浏览器打开必应翻译。";
        }
        catch (Exception ex)
        {
            Status.Text = "无法打开翻译：" + ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private void Sizer_Drag(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }
}
