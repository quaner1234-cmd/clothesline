using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Clothesline;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<string> _folders = new();
    private HotkeyChord _chord;

    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings draft)
    {
        InitializeComponent();
        _settings = draft;
        _chord = HotkeyChord.FromSettings(draft);

        MaxPegsBox.Text = draft.MaxPegs.ToString();
        HistoryBox.Text = draft.HistoryLimit.ToString();
        ClipboardBox.IsChecked = draft.ClipboardWatchEnabled;
        MinWidthBox.Text = draft.ClipboardMinWidth.ToString();
        MinHeightBox.Text = draft.ClipboardMinHeight.ToString();
        HotkeyBox.Text = _chord.Label;
        foreach (var folder in draft.WatchFolders) _folders.Add(folder);
        FolderList.ItemsSource = _folders;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }
        if (key is Key.Escape or Key.Tab or Key.Enter or Key.Return) return;

        e.Handled = true;
        string? name = KeyName(key);
        if (name == null)
        {
            ErrorText.Text = "不支持这个按键。请用字母、数字、功能键 F1–F24、小键盘数字或空格。";
            return;
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool win = Keyboard.Modifiers.HasFlag(ModifierKeys.Windows);
        var chord = new HotkeyChord(ctrl, alt, shift, win, name);
        if (!chord.HasModifier)
        {
            ErrorText.Text = "至少需要一个修饰键（Ctrl、Alt、Shift 或 Win）。";
            return;
        }

        _chord = chord;
        HotkeyBox.Text = chord.Label;
        ErrorText.Text = "";
    }

    private static string? KeyName(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num" + (key - Key.NumPad0);
        if (key >= Key.F1 && key <= Key.F24) return key.ToString();
        if (key == Key.Space) return "Space";
        return null;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要监视的文件夹",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;
        var path = dialog.FolderName;
        if (_folders.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText.Text = "这个文件夹已经在列表里。";
            return;
        }
        _folders.Add(path);
        ErrorText.Text = "";
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is string path) _folders.Remove(path);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaxPegsBox.Text.Trim(), out int max) || max < 1 || max > AppSettings.MaxPegsLimit)
        {
            ErrorText.Text = $"最多挂件数须为 1 到 {AppSettings.MaxPegsLimit} 的整数。";
            return;
        }
        if (!int.TryParse(HistoryBox.Text.Trim(), out int history) || history < 1 || history > AppSettings.HistoryLimitMax)
        {
            ErrorText.Text = $"历史条数须为 1 到 {AppSettings.HistoryLimitMax} 的整数。";
            return;
        }
        if (!int.TryParse(MinWidthBox.Text.Trim(), out int minWidth) || minWidth < 0)
        {
            ErrorText.Text = "最小宽度须为大于等于 0 的整数，0 表示不限制。";
            return;
        }
        if (!int.TryParse(MinHeightBox.Text.Trim(), out int minHeight) || minHeight < 0)
        {
            ErrorText.Text = "最小高度须为大于等于 0 的整数，0 表示不限制。";
            return;
        }
        if (!_chord.IsRegisterable)
        {
            ErrorText.Text = "请设置带修饰键的热键。";
            return;
        }

        _settings.MaxPegs = max;
        _settings.HistoryLimit = history;
        _settings.ClipboardWatchEnabled = ClipboardBox.IsChecked == true;
        _settings.ClipboardMinWidth = minWidth;
        _settings.ClipboardMinHeight = minHeight;
        _settings.WatchFolders = _folders.ToList();
        _chord.WriteTo(_settings);
        _settings.Normalize();
        Result = _settings;
        DialogResult = true;
    }
}
