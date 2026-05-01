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
            try { Tray.IconSource = LoadTrayIcon(); }
            catch (Exception ex) { App.LogCrash("LoadTrayIcon", ex); /* tray works without icon */ }

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

    /// Tray icon loading. H.NotifyIcon's IconSource accepts BitmapFrame
    /// (from a real .ico/.png stream) but NOT RenderTargetBitmap — assigning
    /// an RTB throws "ImageSource type ... not supported" at runtime. So we
    /// build a tiny PNG in memory and load it as a BitmapFrame, which the
    /// tray library converts to an HICON internally.
    private static ImageSource LoadTrayIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Shade.ico");
        if (File.Exists(icoPath))
        {
            try
            {
                using var fs = File.OpenRead(icoPath);
                return BitmapFrame.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch (Exception ex) { App.LogCrash("LoadTrayIcon.IcoFile", ex); }
        }

        try
        {
            var packed = Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/Shade.ico"));
            if (packed != null)
                return BitmapFrame.Create(packed.Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) { App.LogCrash("LoadTrayIcon.Pack", ex); }

        // Fallback: render a 16x16 purple square, encode as PNG, hand back a BitmapFrame.
        var rtb = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xB1, 0x91, 0xFF)),
                null, new Rect(0, 0, 16, 16));
        rtb.Render(visual);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        return BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }
}
