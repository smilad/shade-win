using System.Windows;
using Shade.Models;
using Shade.Services;

namespace Shade;

public partial class App : Application
{
    public static AppState State { get; } = new AppState();

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (State.Status.IsRunning && State.Settings.UseSystemProxy)
            {
                SystemProxy.DisableSync();
            }
            State.StopSync();
        }
        catch { /* shutdown is best-effort */ }
        base.OnExit(e);
    }
}
