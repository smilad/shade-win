using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Shade.Services;

/// Sets / clears the user's WinINet proxy via the registry, then notifies
/// listening apps so the change takes effect immediately.
///
/// IMPORTANT: WinINet's "ProxyServer" controls system-wide HTTP/HTTPS routing
/// only — apps that respect it (Edge, IE, Office, .NET WebRequest, many
/// installers) will tunnel through Shade's HTTP listener. Chrome on Windows
/// reads the same setting; Firefox does not (it has its own proxy config).
/// SOCKS5 routing is *not* portable through this knob — to push every app's
/// SOCKS5 traffic through the system you'd need a TUN device. That's a
/// future enhancement. For now this gives users the same one-click behavior
/// as the macOS app for HTTP-bound traffic.
public static class SystemProxy
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    // WinINet API
    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    public static bool Enable(string host, int port)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (key is null) return false;
            // Same proxy for all schemes (HTTP, HTTPS, FTP).
            var proxyValue = $"{host}:{port}";
            key.SetValue("ProxyServer", proxyValue, RegistryValueKind.String);
            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
            Notify();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null) return true;  // nothing to disable
            key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            Notify();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void DisableSync() => Disable();

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            if (key is null) return false;
            return (key.GetValue("ProxyEnable") as int? ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    private static void Notify()
    {
        // Tell listening apps (and the WinINet caches) that proxy settings have changed.
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
    }
}
