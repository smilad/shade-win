using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Shade.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
    }

    private void OpenTelegram_Click(object sender, RoutedEventArgs e) => OpenUrl("https://t.me/g3ntrix");
    private void OpenUpstream_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/masterking32/MasterHttpRelayVPN");

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s)
        {
            Clipboard.SetText(s);
            // Lightweight feedback
            var label = (TextBlock)b.Content;
            var prev = label.Text;
            label.Text = "Copied";
            var t = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(1.2) };
            t.Tick += (_, _) => { label.Text = prev; t.Stop(); };
            t.Start();
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }
}
