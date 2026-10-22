using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace Clothesline;

public partial class HistoryWindow : Window
{
    private readonly ObservableCollection<HistoryEntry> _rows = new();

    public event Action<HistoryEntry>? RehangRequested;
    public event Action<HistoryEntry>? PinRequested;

    public HistoryWindow()
    {
        InitializeComponent();
        Items.ItemsSource = _rows;
        Reload();
    }

    public void Reload()
    {
        _rows.Clear();
        Status.Text = "";
        try
        {
            foreach (var row in HistoryStore.Load(App.DataDir)) _rows.Add(row);
        }
        catch (InvalidDataException ex)
        {
            Logger.Log("history.json: " + ex.Message);
            try { HistoryStore.Quarantine(App.DataDir); }
            catch (Exception moveEx) { Logger.Log("history quarantine: " + moveEx.Message); }
            Status.Text = "history.json 无法读取，已改名为 history.json.bad。";
        }
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private HistoryEntry? Selected()
    {
        if (Items.SelectedItem is HistoryEntry entry) return entry;
        Status.Text = "先选一条历史。";
        return null;
    }

    private void Rehang_Click(object sender, RoutedEventArgs e)
    {
        var entry = Selected();
        if (entry == null) return;
        RehangRequested?.Invoke(entry);
        Reload();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        var entry = Selected();
        if (entry == null) return;
        PinRequested?.Invoke(entry);
    }

    private void Forget_Click(object sender, RoutedEventArgs e)
    {
        var entry = Selected();
        if (entry == null) return;
        try { HistoryStore.Forget(App.DataDir, entry.FilePath); }
        catch (Exception ex)
        {
            Logger.Log("history forget: " + ex.Message);
            Status.Text = "没有从历史里去掉。";
            return;
        }
        Reload();
    }
}
