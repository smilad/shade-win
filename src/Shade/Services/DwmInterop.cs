using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Shade.Services;

/// Wraps the dwmapi.dll calls that turn a vanilla WPF window into a
/// macOS-style "frosted glass" surface on Windows 11:
///   - Mica backdrop      (the same translucent material Settings/Notepad use)
///   - Rounded corners
///   - Dark-mode title bar (so the system caption text turns white)
///
/// All three calls are no-ops on Windows 10 — they just return error codes
/// we ignore — so the app degrades gracefully on older releases.
public static class DwmInterop
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // ── Attribute IDs (see microsoft/microsoft-ui-xaml DWM enums) ────────
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE   = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE  = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE       = 38;

    // Corner preference values
    private const int DWMWCP_DEFAULT      = 0;
    private const int DWMWCP_DONOTROUND   = 1;
    private const int DWMWCP_ROUND        = 2;
    private const int DWMWCP_ROUNDSMALL   = 3;

    // Backdrop values
    private const int DWMSBT_AUTO         = 0;
    private const int DWMSBT_NONE         = 1;
    private const int DWMSBT_MAINWINDOW   = 2;  // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4;  // Mica Alt

    public static void ApplyMica(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        int dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int backdrop = DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
    }
}
