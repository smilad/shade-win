using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Shade.Models;

namespace Shade.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        foreach (LogLevel l in Enum.GetValues(typeof(LogLevel)))
        {
            LogLevelCombo.Items.Add(new ComboBoxItem { Content = l.ToCode(), Tag = l });
        }
        Loaded += (_, _) => LoadFromState();
        App.State.PropertyChanged += OnAppStateChanged;
    }

    private void OnAppStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.StatusObj) || e.PropertyName == nameof(AppState.IsRunning))
            Dispatcher.BeginInvoke(UpdateRestartHint);
    }

    private void LoadFromState()
    {
        var s = App.State.Settings;
        ListenHostBox.Text = s.ListenHost;
        HttpPortBox.Text = s.ListenPort.ToString();
        SocksPortBox.Text = s.SocksPort.ToString();
        FrontDomainBox.Text = s.FrontDomain;
        GoogleIPBox.Text = s.GoogleIP;
        VerifySSLToggle.IsChecked = s.VerifySSL;

        for (int i = 0; i < LogLevelCombo.Items.Count; i++)
        {
            if (LogLevelCombo.Items[i] is ComboBoxItem c && c.Tag is LogLevel l && l == s.LogLevel)
            {
                LogLevelCombo.SelectedIndex = i; break;
            }
        }
        UpdateRestartHint();
    }

    private void UpdateRestartHint()
    {
        RestartHint.Visibility = App.State.IsRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var s = App.State.Settings;
        s.ListenHost = ListenHostBox.Text.Trim();
        s.FrontDomain = FrontDomainBox.Text.Trim();
        s.GoogleIP = GoogleIPBox.Text.Trim();
        s.VerifySSL = VerifySSLToggle.IsChecked == true;
        if (int.TryParse(HttpPortBox.Text, out var hp)) s.ListenPort = hp;
        if (int.TryParse(SocksPortBox.Text, out var sp)) s.SocksPort = sp;
        if (LogLevelCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is LogLevel l)
            s.LogLevel = l;

        App.State.SaveSettings();
        SavedBadge.Visibility = Visibility.Visible;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        t.Tick += (_, _) => { SavedBadge.Visibility = Visibility.Collapsed; t.Stop(); };
        t.Start();
    }

    private void RevertBtn_Click(object sender, RoutedEventArgs e) => LoadFromState();

    private async void RepairBtn_Click(object sender, RoutedEventArgs e)
    {
        RepairBtn.IsEnabled = false;
        RepairStatusText.Text = "Repairing…";
        RepairStatusText.Text = await App.State.RepairCertificateNowAsync();
        RepairBtn.IsEnabled = true;
    }
}
