using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shade.Models;
using Shade.Views;

namespace Shade;

public partial class MainWindow : Window
{
    public enum Tab { Dashboard, Setup, Settings, Logs, About }

    private Tab _current = Tab.Dashboard;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.State;
        Tray.IconSource = LoadTrayIcon();

        SidebarHost.OnTabSelected = SetTab;
        SetTab(Tab.Dashboard);

        // Hide instead of quitting when the user closes the window — match
        // macOS `applicationShouldTerminateAfterLastWindowClosed = false`.
        Closing += (s, e) =>
        {
            if (!_isQuitting)
            {
                e.Cancel = true;
                Hide();
            }
        };

        // Update tray icon tooltip live with status
        App.State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.StatusObj))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    Tray.ToolTipText = $"Shade — {App.State.StatusObj.Label}";
                });
            }
        };
    }

    private bool _isQuitting;

    public void SetTab(Tab t)
    {
        _current = t;
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

    /// Single shutdown path. Both the tray popup's Quit and the tray
    /// context-menu's Quit funnel through here so MainWindow.Closing's
    /// cancel-and-hide guard knows we really are quitting.
    public void RequestQuit()
    {
        _isQuitting = true;
        try { Tray.Dispose(); } catch { }
        Application.Current.Shutdown();
    }

    /// Tries to load the bundled Shade.ico, otherwise renders a small
    /// purple-square placeholder so the tray slot is always populated.
    private static ImageSource LoadTrayIcon()
    {
        try
        {
            // AppContext.BaseDirectory works in both single-file and normal
            // builds; Assembly.Location is empty under PublishSingleFile.
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Shade.ico");
            if (File.Exists(icoPath))
                return new BitmapImage(new Uri(icoPath, UriKind.Absolute));

            // Pack URI fallback (works when icon is embedded as a resource)
            return new BitmapImage(new Uri("pack://application:,,,/Resources/Shade.ico"));
        }
        catch
        {
            // Generate a 16x16 purple square as last resort
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                ctx.DrawRectangle(
                    new SolidColorBrush(Color.FromRgb(0xB1, 0x91, 0xFF)),
                    null, new Rect(0, 0, 16, 16));
            }
            bmp.Render(visual);
            return bmp;
        }
    }
}

