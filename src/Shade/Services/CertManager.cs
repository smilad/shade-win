using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Shade.Services;

/// Thin wrapper around `shade-core.exe --install-cert`.
///
/// The Python core's cert_installer already supports Windows: it generates
/// the CA on first run, then drops it into the per-user Trusted Root store
/// via `certutil -addstore -user Root` — no UAC required. Re-implementing
/// any of this in C# would be redundant.
public static class CertManager
{
    public enum Result { AlreadyTrusted, InstalledOK, Failed }

    private static string CoreExePath => CoreManager.CoreExePath;

    public static bool IsTrusted()
    {
        if (!File.Exists(ConfigStore.CaCertFile)) return false;
        // Cheapest path: see if certutil reports our cert in user Root.
        // (The core's own _is_trusted_windows uses the same approach.)
        try
        {
            var psi = new ProcessStartInfo("certutil", "-user -store Root")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output.IndexOf("MasterHttpRelayVPN", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<Result> InstallIfNeededAsync()
    {
        if (IsTrusted()) return Result.AlreadyTrusted;
        return await RunInstallAsync();
    }

    /// Removes the existing CA file so the core regenerates one, then
    /// re-runs install. Mirrors macOS `reinstallFreshCertificate`.
    public static async Task<Result> ReinstallFreshAsync()
    {
        try
        {
            var caFolder = Path.GetDirectoryName(ConfigStore.CaCertFile);
            if (caFolder is not null && Directory.Exists(caFolder))
                Directory.Delete(caFolder, recursive: true);
        }
        catch { /* best-effort */ }

        return await RunInstallAsync();
    }

    private static async Task<Result> RunInstallAsync()
    {
        if (!File.Exists(CoreExePath)) return Result.Failed;
        try
        {
            var psi = new ProcessStartInfo(CoreExePath, "--install-cert")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = ConfigStore.AppSupportDir,
            };
            psi.EnvironmentVariables["DFT_CONFIG"] = ConfigStore.CoreConfigFile;
            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

            using var p = Process.Start(psi);
            if (p is null) return Result.Failed;
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? Result.InstalledOK : Result.Failed;
        }
        catch
        {
            return Result.Failed;
        }
    }
}
