using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shade.Models;

namespace Shade.Views;

public partial class DashboardView : UserControl
{
    private readonly DispatcherTimer _uptimeTimer;
    private bool _wired;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) => RenderHero();

        // Populate LB strategies once.
        foreach (LBStrategy s in Enum.GetValues(typeof(LBStrategy)))
        {
            LbStrategyCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{s.Label()} — {s.Detail()}",
                Tag = s,
            });
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_wired) return; _wired = true;
        App.State.PropertyChanged += OnAppStateChanged;
        App.State.Traffic.PropertyChanged += OnTrafficChanged;
        App.State.ScanLog.CollectionChanged += OnScanLogChanged;
        _uptimeTimer.Start();
        RenderAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.State.PropertyChanged -= OnAppStateChanged;
        App.State.Traffic.PropertyChanged -= OnTrafficChanged;
        App.State.ScanLog.CollectionChanged -= OnScanLogChanged;
        _uptimeTimer.Stop();
        _wired = false;
    }

    private void OnAppStateChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(RenderAll);
    private void OnTrafficChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(RenderTraffic);

    private void OnScanLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ScanList.Items.Clear();
            foreach (var s in App.State.ScanLog)
            {
                ScanList.Items.Add(new TextBlock
                {
                    Text = s,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    Foreground = (Brush)FindResource("Secondary"),
                });
            }
            ScanScroll.ScrollToBottom();
        });
    }

    private void RenderAll()
    {
        RenderHero();
        RenderProfile();
        RenderTraffic();
        RenderScan();
        RenderTest();
        SystemProxyToggle.IsChecked = App.State.Settings.UseSystemProxy;
    }

    private void RenderHero()
    {
        var st = App.State.StatusObj;
        HeroTitle.Text = st.Label;
        HeroSubtitle.Text = SecondaryLabel();

        var isRunning = st.IsRunning;
        PowerLabel.Text = isRunning ? "STOP" : "START";
        PowerIcon.Text = isRunning ? "■" : "▶";
        PowerBtn.Background = (Brush)FindResource(isRunning ? "StopButtonBrush" : "StartButtonBrush");

        var disabled = st.IsTransitioning || (!isRunning && !CanStart());
        PowerBtn.IsEnabled = !disabled;

        // Status orb color
        Brush coreBrush = st.Kind switch
        {
            AppState.Status.Running                              => (Brush)FindResource("AccentGreen"),
            AppState.Status.Starting or AppState.Status.Stopping => (Brush)FindResource("AccentYellow"),
            AppState.Status.Error                                => (Brush)FindResource("AccentRed"),
            _                                                    => (Brush)FindResource("AccentCyan"),
        };
        OrbCore.Fill = coreBrush;
        OrbHalo.Fill = new SolidColorBrush((coreBrush is SolidColorBrush sb) ? sb.Color : Colors.Cyan)
        { Opacity = 0.25 };

        // Listener line
        var s = App.State.Settings;
        var hp = App.State.ActiveHTTPPort > 0 ? App.State.ActiveHTTPPort : s.ListenPort;
        var sp = App.State.ActiveSOCKSPort > 0 ? App.State.ActiveSOCKSPort : s.SocksPort;
        ListenerLine.Text = $"HTTP {s.ListenHost}:{hp}  ·  SOCKS5 {s.ListenHost}:{sp}";

        SpeedCard.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        TestCard.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderProfile()
    {
        var s = App.State.Settings;
        LbToggle.IsChecked = s.EnableLoadBalancing;
        LbStrategyCombo.Visibility = s.EnableLoadBalancing ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < LbStrategyCombo.Items.Count; i++)
        {
            if (LbStrategyCombo.Items[i] is ComboBoxItem cbi
                && cbi.Tag is LBStrategy strat
                && strat == s.LbStrategy)
            {
                LbStrategyCombo.SelectedIndex = i;
                break;
            }
        }

        var any = s.Credentials.Count > 0;
        EmptyProfileBlock.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        ActiveProfileBtn.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EditLinkRow.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

        if (s.ActiveCredential is { } c)
        {
            ActiveProfileName.Text = c.Name;
            CFBadge.Visibility = c.UsesCloudflare ? Visibility.Visible : Visibility.Collapsed;
            ActiveProfileSID.Text = string.IsNullOrEmpty(c.ScriptID)
                ? "No Script ID set"
                : (c.ScriptID.Length > 28 ? c.ScriptID.Substring(0, 28) + "…" : c.ScriptID);
        }
    }

    private void RenderTraffic()
    {
        var t = App.State.Traffic;
        SpeedDownText.Text = t.FormattedSpeedDown;
        SpeedUpText.Text = t.FormattedSpeedUp;
        TotalDownText.Text = t.FormattedDown;
        TotalUpText.Text = t.FormattedUp;
        TotalSessionText.Text = t.FormattedTotal;

        // Bar widths: scale to a 2 MB/s ceiling, same as the macOS meter
        const double maxBps = 2 * 1024 * 1024;
        var trackWidth = ((Border)SpeedDownBar.Parent).ActualWidth;
        if (trackWidth > 0)
        {
            SpeedDownBar.Width = Math.Min(trackWidth, trackWidth * Math.Min(t.SpeedDown / maxBps, 1));
            SpeedUpBar.Width = Math.Min(trackWidth, trackWidth * Math.Min(t.SpeedUp / maxBps, 1));
        }
    }

    private void RenderTest()
    {
        TestBtn.IsEnabled = App.State.IsRunning && !App.State.TestRunning;
        TestBtnText.Text = App.State.TestRunning ? "Testing…" : "Run test";
        TestResultText.Text = App.State.TestResult;
    }

    private void RenderScan()
    {
        var st = App.State.ScanState;
        ScanBtn.IsEnabled = st != "scanning";
        ScanBtnText.Text = st switch
        {
            "scanning" => "Scanning…",
            _ => "Run scan",
        };
        ScanLogBox.Visibility = (st == "scanning" || App.State.ScanLog.Count > 0)
            ? Visibility.Visible : Visibility.Collapsed;

        if (st == "done" && !string.IsNullOrEmpty(App.State.ScanRecommendedIP))
        {
            ScanResultRow.Visibility = Visibility.Visible;
            ScanResultText.Text = $"Recommended: {App.State.ScanRecommendedIP}";
            ApplyScanText.Text = $"Apply {App.State.ScanRecommendedIP}";
        }
        else if (st == "failed")
        {
            ScanResultRow.Visibility = Visibility.Visible;
            ScanResultText.Text = "Scan failed.";
            ApplyScanBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            ScanResultRow.Visibility = Visibility.Collapsed;
        }
    }

    private bool CanStart()
    {
        var s = App.State.Settings;
        return !string.IsNullOrWhiteSpace(s.ScriptID) && !string.IsNullOrWhiteSpace(s.AuthKey);
    }

    private string SecondaryLabel()
    {
        var s = App.State.Settings;
        if (App.State.IsRunning && App.State.StartedAt is DateTime started)
        {
            if (!App.State.HasShownCertRestartSucceeded)
                return "Running! Restart your browser before testing.";
            var span = DateTime.UtcNow - started;
            return $"Up {FormatInterval(span)}";
        }
        if (!CanStart()) return "Add and select a profile to get started.";
        return $"Ready: SOCKS5/HTTP proxy on {s.ListenHost}:{s.ListenPort}.";
    }

    private static string FormatInterval(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds:D2}s";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds:D2}s";
        return $"{t.Seconds}s";
    }

    // ── Click handlers ───────────────────────────────────────────────────

    private async void PowerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (App.State.IsRunning) await App.State.StopAsync();
        else await App.State.StartAsync();
    }

    private void AddProfileBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CredentialEditDialog(null) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private void EditProfileBtn_Click(object sender, RoutedEventArgs e)
    {
        if (App.State.Settings.ActiveCredential is { } c)
        {
            var dlg = new CredentialEditDialog(c) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }
    }

    private void OpenProfilePicker_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CredentialPickerDialog { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private void LbToggle_Click(object sender, RoutedEventArgs e)
    {
        App.State.Settings.EnableLoadBalancing = LbToggle.IsChecked == true;
        App.State.SaveSettings();
        RenderProfile();
    }

    private void LbStrategyCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LbStrategyCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is LBStrategy s)
        {
            App.State.Settings.LbStrategy = s;
            App.State.SaveSettings();
        }
    }

    private async void TestBtn_Click(object sender, RoutedEventArgs e) => await App.State.TestYouTubeAsync();

    private async void ScanBtn_Click(object sender, RoutedEventArgs e)
    {
        if (App.State.ScanState == "scanning") return;
        await App.State.RunIPScanAsync();
        RenderScan();
    }

    private void ApplyScanBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(App.State.ScanRecommendedIP))
            App.State.ApplyScanResult(App.State.ScanRecommendedIP);
    }

    private async void SystemProxyToggle_Click(object sender, RoutedEventArgs e)
    {
        await App.State.SetSystemProxyAsync(SystemProxyToggle.IsChecked == true);
    }
}
