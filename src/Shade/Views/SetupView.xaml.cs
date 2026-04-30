using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Shade.Services;

namespace Shade.Views;

public partial class SetupView : UserControl
{
    public SetupView() => InitializeComponent();

    private void OpenAppsScript_Click(object sender, RoutedEventArgs e) => OpenUrl("https://script.google.com");
    private void OpenCode_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://github.com/g3ntrix/Shade/blob/main/apps_script/Code.gs");

    private async void InstallCert_Click(object sender, RoutedEventArgs e)
    {
        InstallCertBtn.IsEnabled = false;
        InstallCertText.Text = "Installing…";
        var result = await CertManager.InstallIfNeededAsync();
        InstallCertText.Text = result switch
        {
            CertManager.Result.AlreadyTrusted => "Already trusted.",
            CertManager.Result.InstalledOK    => "Installed. Restart your browser.",
            _                                 => "Failed — see Logs.",
        };
        InstallCertBtn.IsEnabled = true;
    }

    private void GoToDashboard_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mw)
            mw.SetTab(MainWindow.Tab.Dashboard);
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }
}
