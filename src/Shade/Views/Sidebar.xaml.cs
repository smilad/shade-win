using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shade.Models;

namespace Shade.Views;

public partial class Sidebar : System.Windows.Controls.UserControl
{
    public Action<MainWindow.Tab>? OnTabSelected;

    public Sidebar()
    {
        InitializeComponent();
        TabDashboard.IsChecked = true;

        App.State.PropertyChanged += OnAppStateChanged;
        UpdateStatusChip();
    }

    public void SetActive(MainWindow.Tab t)
    {
        TabDashboard.IsChecked = t == MainWindow.Tab.Dashboard;
        TabSetup.IsChecked     = t == MainWindow.Tab.Setup;
        TabSettings.IsChecked  = t == MainWindow.Tab.Settings;
        TabLogs.IsChecked      = t == MainWindow.Tab.Logs;
        TabAbout.IsChecked     = t == MainWindow.Tab.About;
    }

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn) return;
        // Re-toggle so it stays checked even if user clicks again
        btn.IsChecked = true;

        if (btn.Tag is not string s) return;
        if (Enum.TryParse<MainWindow.Tab>(s, out var t))
            OnTabSelected?.Invoke(t);
    }

    private void OnAppStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.StatusObj) || e.PropertyName == nameof(AppState.IsRunning))
            Dispatcher.BeginInvoke(UpdateStatusChip);
    }

    private void UpdateStatusChip()
    {
        var s = App.State.StatusObj;
        StatusLabel.Text = s.Label;
        StatusDot.Fill = s.Kind switch
        {
            AppState.Status.Running                          => (Brush)FindResource("AccentGreen"),
            AppState.Status.Starting or AppState.Status.Stopping => (Brush)FindResource("AccentYellow"),
            AppState.Status.Error                            => (Brush)FindResource("AccentRed"),
            _                                                => (Brush)new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
        };
    }
}
