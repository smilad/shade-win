using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shade.Models;

namespace Shade.Views;

public partial class TrayPopupView : UserControl
{
    private readonly DispatcherTimer _timer;

    public TrayPopupView()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Render();

        Loaded += (_, _) =>
        {
            App.State.PropertyChanged += OnAppStateChanged;
            App.State.Traffic.PropertyChanged += OnTrafficChanged;
            _timer.Start();
            Render();
        };
        Unloaded += (_, _) =>
        {
            App.State.PropertyChanged -= OnAppStateChanged;
            App.State.Traffic.PropertyChanged -= OnTrafficChanged;
            _timer.Stop();
        };
    }

    private void OnAppStateChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(Render);
    private void OnTrafficChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(RenderMeter);

    private void Render()
    {
        var st = App.State.StatusObj;
        HeaderSubtitle.Text = st.Kind switch
        {
            AppState.Status.Running                              => "Connected",
            AppState.Status.Starting                             => "Connecting…",
            AppState.Status.Stopping                             => "Disconnecting…",
            AppState.Status.Error                                => "Error",
            _                                                    => "Disconnected",
        };
        HeaderSubtitle.Foreground = st.Kind switch
        {
            AppState.Status.Running                              => (Brush)FindResource("AccentGreen"),
            AppState.Status.Starting or AppState.Status.Stopping => (Brush)FindResource("AccentYellow"),
            AppState.Status.Error                                => (Brush)FindResource("AccentRed"),
            _                                                    => (Brush)FindResource("Secondary"),
        };

        PowerLabel.Text = st.IsRunning ? "STOP" : "START";
        PowerIcon.Text = st.IsRunning ? "■" : "▶";
        PowerBtn.Background = (Brush)FindResource(st.IsRunning ? "StopButtonBrush" : "StartButtonBrush");
        PowerBtn.IsEnabled = !st.IsTransitioning;

        MeterPanel.Visibility = st.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        ListenerRow.Visibility = st.IsRunning ? Visibility.Visible : Visibility.Collapsed;

        ProfileNameText.Text = App.State.Settings.ActiveCredential?.Name ?? "No Profile";
        if (st.IsRunning && App.State.StartedAt is DateTime started)
        {
            var t = DateTime.UtcNow - started;
            UptimeText.Text = t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes:D2}m"
                : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:D2}s" : $"{t.Seconds}s";
        }
        else { UptimeText.Text = ""; }

        var port = App.State.ActiveSOCKSPort > 0 ? App.State.ActiveSOCKSPort : App.State.Settings.SocksPort;
        ListenerText.Text = $"SOCKS5 127.0.0.1:{port}";
        SystemProxyToggle.IsChecked = App.State.Settings.UseSystemProxy;

        RenderMeter();
    }

    private void RenderMeter()
    {
        var t = App.State.Traffic;
        SpeedDownText.Text = t.FormattedSpeedDown;
        SpeedUpText.Text = t.FormattedSpeedUp;

        const double maxBps = 2 * 1024 * 1024;
        var trackWidth = ((Border)SpeedDownBar.Parent).ActualWidth;
        if (trackWidth > 0)
        {
            SpeedDownBar.Width = Math.Min(trackWidth, trackWidth * Math.Min(t.SpeedDown / maxBps, 1));
            SpeedUpBar.Width = Math.Min(trackWidth, trackWidth * Math.Min(t.SpeedUp / maxBps, 1));
        }
    }

    private async void PowerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (App.State.IsRunning) await App.State.StopAsync();
        else await App.State.StartAsync();
    }

    private async void SystemProxyToggle_Click(object sender, RoutedEventArgs e) =>
        await App.State.SetSystemProxyAsync(SystemProxyToggle.IsChecked == true);

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mw)
        {
            mw.ShowMain();
            mw.SetTab(MainWindow.Tab.Dashboard);
        }
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mw) mw.RequestQuit();
        else Application.Current.Shutdown();
    }
}
