using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Shade.Models;

namespace Shade.Services;

/// Spawns and supervises the bundled `shade-core.exe` (PyInstaller-frozen
/// main.py via shade_core.py). The core reads its config from the path we
/// pass via `-c`. No admin rights required.
public sealed class CoreManager
{
    public Action<LogLine>? OnLog;
    public Action<AppState.StatusInfo>? OnStatus;

    private Process? _process;
    private Process? _scanProcess;
    private bool _userInitiatedStop;
    private readonly ConfigStore _store = new();

    /// Path to the bundled core binary, resolved next to the app's exe.
    /// Uses AppContext.BaseDirectory because Assembly.Location is empty in
    /// single-file (PublishSingleFile) builds.
    public static string CoreExePath =>
        Path.Combine(AppContext.BaseDirectory, "shade-core.exe");

    public async Task StartAsync(AppSettings settings)
    {
        await StopAsync();

        if (!File.Exists(CoreExePath))
            throw new FileNotFoundException(
                $"shade-core.exe missing at {CoreExePath}. Run scripts\\build-core.ps1 to build it.");

        var configPath = _store.WriteCoreConfig(settings);

        OnLog?.Invoke(LogLine.System($"[CoreManager] Launching {Path.GetFileName(CoreExePath)}\n"));

        var psi = new ProcessStartInfo
        {
            FileName = CoreExePath,
            Arguments = $"-c \"{configPath}\" --no-cert-check",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ConfigStore.AppSupportDir,
        };
        psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
        psi.EnvironmentVariables["DFT_SCRIPT_ID"] = settings.ScriptID;
        psi.EnvironmentVariables["DFT_AUTH_KEY"] = settings.AuthKey;

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            OnLog?.Invoke(new LogLine(DateTime.UtcNow, LogStream.Stdout, e.Data + "\n"));
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            OnLog?.Invoke(new LogLine(DateTime.UtcNow, LogStream.Stderr, e.Data + "\n"));
        };
        p.Exited += (_, _) =>
        {
            var initiated = _userInitiatedStop;
            _userInitiatedStop = false;
            var status = p.ExitCode;
            _process = null;

            if (initiated || status == 0)
            {
                OnStatus?.Invoke(AppState.StatusInfo.Stopped);
                return;
            }
            OnLog?.Invoke(LogLine.System($"[shade-core exited with status {status}]\n"));
            OnStatus?.Invoke(AppState.StatusInfo.Err($"Core exited ({status})"));
        };

        if (!p.Start())
            throw new InvalidOperationException("Failed to start shade-core.exe");
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _process = p;

        var probeHost = settings.ListenHost == "0.0.0.0" ? "127.0.0.1" : settings.ListenHost;
        var ready = await WaitForListenerAsync(probeHost, settings.SocksPort, TimeSpan.FromSeconds(30));
        if (!ready)
        {
            OnLog?.Invoke(LogLine.System($"[CoreManager] waitForListener timed out.\n"));
            if (_process is null) return;
            throw new InvalidOperationException(
                $"Core started but SOCKS5 listener on {probeHost}:{settings.SocksPort} didn't come up in time. " +
                "Check Logs: another process may be holding that port.");
        }
        OnLog?.Invoke(LogLine.System("[CoreManager] Listener ready.\n"));
        OnStatus?.Invoke(AppState.StatusInfo.Running);
    }

    public async Task StopAsync()
    {
        var p = _process;
        if (p is null || p.HasExited)
        {
            _process = null;
            return;
        }
        _userInitiatedStop = true;
        try { p.Kill(entireProcessTree: true); } catch { }
        try { await p.WaitForExitAsync(); } catch { }
        _process = null;
    }

    public void StopSync()
    {
        var p = _process;
        if (p is null || p.HasExited) { _process = null; return; }
        _userInitiatedStop = true;
        try { p.Kill(entireProcessTree: true); } catch { }
        try { p.WaitForExit(2000); } catch { }
        _process = null;
    }

    public async Task<string?> RunScanAsync(AppSettings settings, Action<string> onLog)
    {
        if (_scanProcess is { HasExited: false }) { try { _scanProcess.Kill(); } catch { } }
        _scanProcess = null;

        if (!File.Exists(CoreExePath))
        {
            onLog("[Scanner] shade-core.exe not bundled.\n");
            return null;
        }

        string configPath;
        try { configPath = _store.WriteCoreConfig(settings); }
        catch (Exception e) { onLog($"[Scanner] Could not write config: {e.Message}\n"); return null; }

        var psi = new ProcessStartInfo
        {
            FileName = CoreExePath,
            Arguments = $"-c \"{configPath}\" --scan",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ConfigStore.AppSupportDir,
        };
        psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
        psi.EnvironmentVariables["DFT_SCRIPT_ID"] = settings.ScriptID;
        psi.EnvironmentVariables["DFT_AUTH_KEY"] = settings.AuthKey;

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var fullOutput = new System.Text.StringBuilder();
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            fullOutput.AppendLine(e.Data);
            onLog(e.Data + "\n");
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            fullOutput.AppendLine(e.Data);
            onLog(e.Data + "\n");
        };

        try
        {
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _scanProcess = p;
            await p.WaitForExitAsync();
        }
        catch (Exception e)
        {
            onLog($"[Scanner] Launch failed: {e.Message}\n");
            return null;
        }
        finally
        {
            _scanProcess = null;
        }

        // Parse: Recommended: Set "google_ip": "1.2.3.4"
        foreach (var line in fullOutput.ToString().Split('\n'))
        {
            if (!line.Contains("Recommended:")) continue;
            var idx = line.LastIndexOf('"');
            if (idx <= 0) continue;
            var before = line.Substring(0, idx);
            var idx0 = before.LastIndexOf('"');
            if (idx0 < 0 || idx0 + 1 >= idx) continue;
            var ip = line.Substring(idx0 + 1, idx - idx0 - 1);
            if (!string.IsNullOrEmpty(ip)) return ip;
        }
        return null;
    }

    private static async Task<bool> WaitForListenerAsync(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CanConnectAsync(host, port)) return true;
            await Task.Delay(250);
        }
        return false;
    }

    private static async Task<bool> CanConnectAsync(string host, int port)
    {
        try
        {
            using var sock = new TcpClient();
            using var cts = new CancellationTokenSource(1000);
            await sock.ConnectAsync(host, port, cts.Token);
            return sock.Connected;
        }
        catch { return false; }
    }
}
