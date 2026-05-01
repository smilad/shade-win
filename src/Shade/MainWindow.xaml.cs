using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shade.Models;
using Shade.Services;
using Shade.Views;

namespace Shade;

public partial class MainWindow : Window
{
    public enum Tab { Dashboard, Setup, Settings, Logs, About }

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            DataContext = App.State;
            Tray.IconSource = LoadTrayIcon();

            SidebarHost.OnTabSelected = SetTab;
            SetTab(Tab.Dashboard);

            Closing += (s, e) =>
            {
                if (!_isQuitting) { e.Cancel = true; Hide(); }
            };

            App.State.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(AppState.StatusObj))
                    Dispatcher.BeginInvoke(() =>
                        Tray.ToolTipText = $"Shade — {App.State.StatusObj.Label}");
            };
        }
        catch (Exception ex)
        {
            App.LogCrash("MainWindow.ctor", ex);
            throw;
        }
    }

    private bool _isQuitting;

    /// SourceInitialized is the earliest point a window has a HWND.
    /// All DWM calls (Mica backdrop, rounded corners, dark title bar)
    /// require a HWND, so they go here.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try { DwmInterop.ApplyMica(this); }
        catch (Exception ex) { App.LogCrash("ApplyMica", ex); }
    }

    public void SetTab(Tab t)
    {
        SidebarHost.SetActive(t);
        FrameworkElement view = t switch
        {
            Tab.Dashboard => new DashboardView(),
            Tab.Setup     => new SetupView(),
            Tab.Settings  => new SettingsView(),
            Tab.Logs      => new LogsView(),
            Tab.About     => new AboutView(),
            _             => new DashboardView(),
        };
        DetailContent.Content = view;
    }

    // ── macOS-style traffic-light buttons ────────────────────────────────

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();   // mirror macOS: red dot hides, doesn't quit
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    // ── Tray ──────────────────────────────────────────────────────────────

    private void Tray_OnDoubleClick(object sender, RoutedEventArgs e) => ShowMain();
    private void ShowFromTray_Click(object sender, RoutedEventArgs e) => ShowMain();

    public void ShowMain()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => RequestQuit();

    public void RequestQuit()
    {
        _isQuitting = true;
        try { Tray.Dispose(); } catch { }
        Application.Current.Shutdown();
    }

    private static ImageSource LoadTrayIcon()
    {
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Shade.ico");
            if (File.Exists(icoPath))
                return new BitmapImage(new Uri(icoPath, UriKind.Absolute));
            return new BitmapImage(new Uri("pack://application:,,,/Resources/Shade.ico"));
        }
        catch
        {
            var bmp = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
                ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xB1, 0x91, 0xFF)),
                    null, new Rect(0, 0, 16, 16));
            bmp.Render(visual);
            return bmp;
        }
    }
}
