using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Shade.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            App.State.Logs.CollectionChanged += OnLogsChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            App.State.Logs.CollectionChanged -= OnLogsChanged;
        };
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var sb = new StringBuilder(App.State.Logs.Count * 80);
        foreach (var l in App.State.Logs) sb.Append(l.Text);
        LogText.Text = sb.ToString();
        LogScroll.ScrollToEnd();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        App.State.ClearLogs();
        Refresh();
    }
}
