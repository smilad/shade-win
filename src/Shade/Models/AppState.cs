using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Shade.Services;

namespace Shade.Models;

public sealed class AppState : INotifyPropertyChanged
{
    public enum Status { Stopped, Starting, Running, Stopping, Error }

    public sealed class StatusInfo
    {
        public Status Kind { get; init; }
        public string? Message { get; init; }
        public bool IsRunning => Kind == Status.Running;
        public bool IsTransitioning => Kind == Status.Starting || Kind == Status.Stopping;
        public string Label => Kind switch
        {
            Status.Stopped => "Ready to connect",
            Status.Starting => "Starting…",
            Status.Running => "Running",
            Status.Stopping => "Stopping…",
            Status.Error => $"Error: {Message ?? "unknown"}",
            _ => "",
        };
        public static StatusInfo Stopped => new() { Kind = Status.Stopped };
        public static StatusInfo Starting => new() { Kind = Status.Starting };
        public static StatusInfo Running => new() { Kind = Status.Running };
        public static StatusInfo Stopping => new() { Kind = Status.Stopping };
        public static StatusInfo Err(string msg) => new() { Kind = Status.Error, Message = msg };
    }

    public sealed class TrafficStats : INotifyPropertyChanged
    {
        public long TotalDown { get; set; }
        public long TotalUp { get; set; }
        public long SpeedDown { get; set; }
        public long SpeedUp { get; set; }

        public string FormattedDown => FormatBytes(TotalDown);
        public string FormattedUp => FormatBytes(TotalUp);
        public string FormattedTotal => FormatBytes(TotalDown + TotalUp);
        public string FormattedSpeedDown => FormatSpeed(SpeedDown);
        public string FormattedSpeedUp => FormatSpeed(SpeedUp);

        public event PropertyChangedEventHandler? PropertyChanged;
        public void Refresh()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        }

        private static string FormatBytes(long b)
        {
            double kb = b / 1024.0, mb = kb / 1024.0, gb = mb / 1024.0;
            if (gb >= 1) return $"{gb:F2} GB";
            if (mb >= 0.1) return $"{mb:F1} MB";
            if (kb >= 0.1) return $"{kb:F1} KB";
            return $"{b} B";
        }

        private static string FormatSpeed(long bps)
        {
            double kbps = bps / 1024.0, mbps = kbps / 1024.0;
            if (mbps >= 1) return $"{mbps:F1} MB/s";
            if (kbps >= 0.1) return $"{kbps:F0} KB/s";
            return "0 KB/s";
        }
    }

    private readonly ConfigStore _store = new();
    private readonly CoreManager _core = new();
    private readonly DispatcherTimer _speedTimer;
    private readonly DispatcherTimer _decayTimer;
    private long _currentSecDown;
    private long _currentSecUp;
    private CancellationTokenSource? _healthCts;

    public AppSettings Settings { get; private set; }
    public StatusInfo StatusObj { get; private set; } = StatusInfo.Stopped;
    public bool IsRunning => StatusObj.IsRunning;
    public bool IsTransitioning => StatusObj.IsTransitioning;

    public ObservableCollection<LogLine> Logs { get; } = new();
    public DateTime? StartedAt { get; private set; }
    public TrafficStats Traffic { get; } = new();

    public ObservableCollection<string> ActiveSIDs { get; } = new();
    public ObservableCollection<string> UnhealthySIDs { get; } = new();
    public string? LbFallbackMessage { get; private set; }
    private readonly Dictionary<string, DateTime> _lastHitAt = new();

    public int ActiveHTTPPort { get; private set; }
    public int ActiveSOCKSPort { get; private set; }

    public string TestResult { get; private set; } = "";
    public bool TestRunning { get; private set; }

    public string ScanState { get; private set; } = "idle";  // idle, scanning, done, failed
    public string? ScanRecommendedIP { get; private set; }
    public ObservableCollection<string> ScanLog { get; } = new();

    public bool HasShownCertRestartSucceeded { get; set; }

    public AppState()
    {
        Settings = _store.LoadSettings();

        _core.OnLog = line => Dispatcher().BeginInvoke(() =>
        {
            Append(line);
            TrackHits(line.Text);
        });
        _core.OnStatus = newStatus => Dispatcher().BeginInvoke(() =>
        {
            StatusObj = newStatus;
            OnChanged(nameof(StatusObj));
            OnChanged(nameof(IsRunning));
            OnChanged(nameof(IsTransitioning));
        });

        _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _speedTimer.Tick += (_, _) =>
        {
            Traffic.SpeedDown = _currentSecDown;
            Traffic.SpeedUp = _currentSecUp;
            _currentSecDown = 0;
            _currentSecUp = 0;
            Traffic.Refresh();
            // refresh uptime label
            OnChanged(nameof(StartedAt));
        };
        _speedTimer.Start();

        _decayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
        _decayTimer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var stale = _lastHitAt.Where(kv => (now - kv.Value).TotalSeconds > 2.0).Select(kv => kv.Key).ToList();
            foreach (var sid in stale)
            {
                _lastHitAt.Remove(sid);
                ActiveSIDs.Remove(sid);
            }
        };
        _decayTimer.Start();
    }

    private static Dispatcher Dispatcher() => Application.Current.Dispatcher;

    public void SaveSettings()
    {
        _store.SaveSettings(Settings);
        OnChanged(nameof(Settings));
    }

    // ── Start / Stop ─────────────────────────────────────────────────────

    public async Task StartAsync()
    {
        SaveSettings();

        var sid = (Settings.ScriptID ?? "").Trim();
        var key = (Settings.AuthKey ?? "").Trim();
        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(key))
        {
            StatusObj = StatusInfo.Err("Add and select a profile on the Dashboard first.");
            OnChanged(nameof(StatusObj));
            OnChanged(nameof(IsRunning));
            return;
        }

        var (httpPort, socksPort) = PortAvailability.FindAvailablePair(
            Settings.ListenPort, Settings.SocksPort, Settings.ListenHost);

        var effective = CloneSettings(Settings);
        effective.ListenPort = httpPort;
        effective.SocksPort = socksPort;
        ActiveHTTPPort = httpPort;
        ActiveSOCKSPort = socksPort;

        if (httpPort != Settings.ListenPort || socksPort != Settings.SocksPort)
        {
            Append(LogLine.System($"⚠︎ Preferred ports busy: using HTTP:{httpPort} SOCKS5:{socksPort}\n"));
        }

        StatusObj = StatusInfo.Starting;
        StartedAt = DateTime.UtcNow;
        UnhealthySIDs.Clear();
        OnChanged(nameof(StatusObj));
        OnChanged(nameof(IsRunning));
        OnChanged(nameof(IsTransitioning));
        OnChanged(nameof(StartedAt));

        try
        {
            // 1. Cert install
            var certResult = await CertManager.InstallIfNeededAsync();
            switch (certResult)
            {
                case CertManager.Result.InstalledOK:
                    Append(LogLine.System("✓ Certificate installed: restart your browser to apply.\n"));
                    HasShownCertRestartSucceeded = false;
                    break;
                case CertManager.Result.AlreadyTrusted:
                    HasShownCertRestartSucceeded = true;
                    break;
                case CertManager.Result.Failed:
                    Append(LogLine.System($"⚠ Certificate install failed.\n"));
                    break;
            }

            // 2. Spawn core
            await _core.StartAsync(effective);

            // 3. System proxy
            if (Settings.UseSystemProxy)
            {
                var host = effective.ListenHost == "0.0.0.0" ? "127.0.0.1" : effective.ListenHost;
                var ok = SystemProxy.Enable(host, effective.ListenPort);
                Append(LogLine.System(ok
                    ? $"✓ System HTTP proxy set to {host}:{effective.ListenPort}\n"
                    : "⚠ System proxy enable failed.\n"));
            }

            if (!HasShownCertRestartSucceeded)
            {
                _ = Task.Delay(12_000).ContinueWith(_ =>
                    Dispatcher().BeginInvoke(() => { HasShownCertRestartSucceeded = true; OnChanged(nameof(HasShownCertRestartSucceeded)); }));
            }

            StartHealthMonitor();
        }
        catch (Exception ex)
        {
            StatusObj = StatusInfo.Err(ex.Message);
            StartedAt = null;
            await _core.StopAsync();
            OnChanged(nameof(StatusObj));
            OnChanged(nameof(IsRunning));
            OnChanged(nameof(StartedAt));
        }
    }

    public async Task StopAsync()
    {
        _healthCts?.Cancel();
        _healthCts = null;

        StatusObj = StatusInfo.Stopping;
        OnChanged(nameof(StatusObj));
        OnChanged(nameof(IsTransitioning));

        if (Settings.UseSystemProxy) SystemProxy.Disable();

        await _core.StopAsync();
        StartedAt = null;
        ActiveHTTPPort = 0;
        ActiveSOCKSPort = 0;
        Traffic.TotalDown = 0;
        Traffic.TotalUp = 0;
        Traffic.SpeedDown = 0;
        Traffic.SpeedUp = 0;
        Traffic.Refresh();
        _currentSecDown = 0;
        _currentSecUp = 0;

        Settings.LbFallbackActive = false;
        LbFallbackMessage = null;
        OnChanged(nameof(LbFallbackMessage));
        OnChanged(nameof(StartedAt));
    }

    public void StopSync()
    {
        try
        {
            _healthCts?.Cancel();
            if (Settings.UseSystemProxy) SystemProxy.DisableSync();
            _core.StopSync();
        }
        catch { }
    }

    public async Task SetSystemProxyAsync(bool on)
    {
        Settings.UseSystemProxy = on;
        SaveSettings();

        if (!IsRunning) return;

        if (on)
        {
            var host = (Settings.ListenHost == "0.0.0.0" ? "127.0.0.1" : Settings.ListenHost);
            var port = ActiveHTTPPort > 0 ? ActiveHTTPPort : Settings.ListenPort;
            var ok = SystemProxy.Enable(host, port);
            if (!ok) Append(LogLine.System("⚠ System proxy enable failed.\n"));
        }
        else
        {
            SystemProxy.Disable();
        }
        await Task.CompletedTask;
    }

    // ── LB health monitor ────────────────────────────────────────────────

    private void StartHealthMonitor()
    {
        _healthCts?.Cancel();
        _healthCts = null;
        if (!Settings.EnableLoadBalancing || !Settings.LbStrategy.HasFallback()) return;
        var cts = new CancellationTokenSource();
        _healthCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(15_000, cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    Dispatcher().Invoke(EvaluateFallback);
                    await Task.Delay(8_000, cts.Token);
                }
            }
            catch (TaskCanceledException) { }
        }, cts.Token);
    }

    private void EvaluateFallback()
    {
        if (!IsRunning || Settings.LbFallbackActive) return;

        var enabled = Settings.Credentials.Where(c => c.IsEnabledForLB).ToList();
        var cf = enabled.Where(c => c.UsesCloudflare).ToList();
        var normal = enabled.Where(c => !c.UsesCloudflare).ToList();
        bool shouldFallback;
        string message;

        if (Settings.LbStrategy == LBStrategy.CFPreferred)
        {
            var allCFDead = cf.Count > 0 && cf.All(c => UnhealthySIDs.Contains(c.ScriptID));
            shouldFallback = allCFDead && normal.Count > 0;
            message = "All Cloudflare profiles failed. Falling back to Apps Script profiles. Restart to try Cloudflare again.";
        }
        else if (Settings.LbStrategy == LBStrategy.NormalPreferred)
        {
            var allNormalDead = normal.Count > 0 && normal.All(c => UnhealthySIDs.Contains(c.ScriptID));
            shouldFallback = allNormalDead && cf.Count > 0;
            message = "All Apps Script profiles failed. Falling back to Cloudflare profiles. Restart to try Apps Script again.";
        }
        else { return; }

        if (!shouldFallback) return;

        Settings.LbFallbackActive = true;
        LbFallbackMessage = message;
        OnChanged(nameof(LbFallbackMessage));
        Append(LogLine.System($"⚠ {message}\n"));
        Append(LogLine.System("↻ Restarting core on fallback pool…\n"));

        _ = StopAsync().ContinueWith(_ => Dispatcher().BeginInvoke(async () => await StartAsync()));
    }

    // ── Logs / traffic parsing ───────────────────────────────────────────

    public void Append(LogLine line)
    {
        if (Logs.Count > 5000)
        {
            for (int i = 0; i < 1000; i++) Logs.RemoveAt(0);
        }
        Logs.Add(line);
        CountTrafficBytes(line.Text);
    }

    public void ClearLogs() => Logs.Clear();

    private static readonly Regex AnsiRx = new(@"\x1b\[[0-9;]*[mK]", RegexOptions.Compiled);
    private static readonly Regex TrafficRx = new(@"\[TRAFFIC\][^\n]*?rx=(\d+)[^\n]*?tx=(\d+)", RegexOptions.Compiled);
    private static readonly Regex HitRx = new(@"\[HIT\] ([^\s\]]+)", RegexOptions.Compiled);
    private static readonly Regex HealthRx = new(@"\[HEALTH\] sid=(\S+) ok=(true|false)", RegexOptions.Compiled);

    private void CountTrafficBytes(string text)
    {
        if (!text.Contains("[TRAFFIC]")) return;
        foreach (Match m in TrafficRx.Matches(text))
        {
            if (long.TryParse(m.Groups[1].Value, out var rx))
            {
                Traffic.TotalDown += rx;
                Interlocked.Add(ref _currentSecDown, rx);
            }
            if (long.TryParse(m.Groups[2].Value, out var tx))
            {
                Traffic.TotalUp += tx;
                Interlocked.Add(ref _currentSecUp, tx);
            }
        }
    }

    private void TrackHits(string text)
    {
        var clean = AnsiRx.Replace(text, "");
        foreach (Match m in HealthRx.Matches(clean))
        {
            var sid = m.Groups[1].Value;
            var ok = m.Groups[2].Value == "true";
            foreach (var c in Settings.Credentials)
            {
                if (c.ScriptID.EndsWith(sid) || sid.EndsWith(c.ScriptID))
                {
                    if (ok) UnhealthySIDs.Remove(c.ScriptID);
                    else if (!UnhealthySIDs.Contains(c.ScriptID)) UnhealthySIDs.Add(c.ScriptID);
                }
            }
        }

        foreach (Match m in HitRx.Matches(clean))
        {
            var hitSid = m.Groups[1].Value;
            foreach (var c in Settings.Credentials)
            {
                if (c.ScriptID.EndsWith(hitSid) || hitSid.EndsWith(c.ScriptID))
                {
                    if (!ActiveSIDs.Contains(c.ScriptID)) ActiveSIDs.Add(c.ScriptID);
                    _lastHitAt[c.ScriptID] = DateTime.UtcNow;
                }
            }
        }
    }

    // ── Cert repair ──────────────────────────────────────────────────────

    public async Task<string> RepairCertificateNowAsync()
    {
        Append(LogLine.System("↻ Manual certificate repair requested.\n"));
        var result = await CertManager.ReinstallFreshAsync();
        return result switch
        {
            CertManager.Result.InstalledOK => "Certificate refreshed. Restart your browser and retry.",
            CertManager.Result.AlreadyTrusted => "Certificate is already trusted.",
            CertManager.Result.Failed => "Certificate repair failed.",
            _ => "",
        };
    }

    // ── YouTube probe ────────────────────────────────────────────────────

    public async Task TestYouTubeAsync()
    {
        TestRunning = true;
        TestResult = "Testing…";
        OnChanged(nameof(TestRunning));
        OnChanged(nameof(TestResult));

        var port = ActiveHTTPPort > 0 ? ActiveHTTPPort : Settings.ListenPort;
        var host = Settings.ListenHost == "0.0.0.0" ? "127.0.0.1" : Settings.ListenHost;

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var handler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy($"http://{host}:{port}"),
                UseProxy = true,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            var req = new HttpRequestMessage(HttpMethod.Head, "https://www.youtube.com/generate_204");
            using var resp = await client.SendAsync(req);
            sw.Stop();
            TestResult = $"{sw.ElapsedMilliseconds} ms";
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Length > 60) msg = msg.Substring(0, 60) + "…";
            TestResult = msg;
        }
        finally
        {
            TestRunning = false;
            OnChanged(nameof(TestRunning));
            OnChanged(nameof(TestResult));
            _ = Task.Delay(8000).ContinueWith(_ => Dispatcher().BeginInvoke(() =>
            {
                if (!TestRunning) { TestResult = ""; OnChanged(nameof(TestResult)); }
            }));
        }
    }

    // ── IP scan ──────────────────────────────────────────────────────────

    public async Task RunIPScanAsync()
    {
        if (ScanState == "scanning") return;
        ScanState = "scanning";
        ScanRecommendedIP = null;
        ScanLog.Clear();
        OnChanged(nameof(ScanState));

        var ip = await _core.RunScanAsync(Settings, chunk =>
        {
            Dispatcher().BeginInvoke(() =>
            {
                foreach (var line in chunk.Split('\n'))
                {
                    var t = line.Trim();
                    if (!string.IsNullOrEmpty(t)) ScanLog.Add(t);
                }
            });
        });

        ScanRecommendedIP = ip;
        if (ip != null) ScanState = "done";
        else
        {
            var got = ScanLog.Any(l => l.Contains("/") || l.Contains("ms") || l.Contains("timeout"));
            ScanState = got ? "done" : "failed";
        }
        OnChanged(nameof(ScanState));
        OnChanged(nameof(ScanRecommendedIP));
    }

    public void CancelScan()
    {
        ScanState = "idle";
        ScanLog.Clear();
        OnChanged(nameof(ScanState));
    }

    public void ApplyScanResult(string ip)
    {
        Settings.GoogleIP = ip;
        SaveSettings();
        Append(LogLine.System($"✓ google_ip updated to {ip} from scanner: restart to apply.\n"));
        ScanState = "idle";
        ScanLog.Clear();
        OnChanged(nameof(ScanState));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static AppSettings CloneSettings(AppSettings src) => AppSettings.Decode(src.Encode());

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

