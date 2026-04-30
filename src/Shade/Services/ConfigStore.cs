using System;
using System.IO;
using Shade.Models;

namespace Shade.Services;

/// Reads/writes AppSettings under %APPDATA%\Shade\, plus produces config.json
/// for the bundled Python core.
public sealed class ConfigStore
{
    public static string AppSupportDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Shade");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsFile => Path.Combine(AppSupportDir, "settings.json");
    public static string CoreConfigFile => Path.Combine(AppSupportDir, "config.json");
    public static string CaCertFile => Path.Combine(AppSupportDir, "ca", "ca.crt");

    public AppSettings LoadSettings() => AppSettings.Load(SettingsFile);

    public void SaveSettings(AppSettings s) => s.Save(SettingsFile);

    /// Serializes settings.MakeCoreConfig() into the file the core reads with -c.
    public string WriteCoreConfig(AppSettings s)
    {
        var node = s.MakeCoreConfig();
        var json = node.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(CoreConfigFile, json);
        return CoreConfigFile;
    }
}
