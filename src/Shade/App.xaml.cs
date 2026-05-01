using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Shade.Models;
using Shade.Services;

namespace Shade;

public partial class App : Application
{
    public static AppState State { get; } = new AppState();

    public static string CrashLogPath =>
        Path.Combine(Path.GetTempPath(), "shade-crash.log");

    public App()
    {
        // Global exception handlers. Without these, a startup exception kills
        // the WPF process silently — the user just sees "it didn't open" and
        // we have no info. Now every crash dumps to %TEMP%\shade-crash.log
        // AND pops a message box with the error.
        DispatcherUnhandledException += (_, e) =>
        {
            LogCrash("DispatcherUnhandled", e.Exception);
            MessageBox.Show(
                $"Shade hit an error.\n\n" +
                $"{e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
                $"Full stack written to:\n{CrashLogPath}",
                "Shade — error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomainUnhandled", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("TaskScheduler", e.Exception);
            e.SetObserved();
        };
    }

    public static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var line = $"[{DateTime.Now:O}] {source}: {ex}\n\n";
            File.AppendAllText(CrashLogPath, line);
        }
        catch { /* nothing we can do */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (State.StatusObj.IsRunning && State.Settings.UseSystemProxy)
            {
                SystemProxy.DisableSync();
            }
            State.StopSync();
        }
        catch { /* shutdown is best-effort */ }
        base.OnExit(e);
    }
}
