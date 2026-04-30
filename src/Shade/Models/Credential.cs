using System;
using System.Text.Json.Serialization;

namespace Shade.Models;

public sealed class Credential : IEquatable<Credential>
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Default";
    public string ScriptID { get; set; } = "";
    public string AuthKey { get; set; } = "";
    public bool IsEnabledForLB { get; set; } = true;
    public bool UsesCloudflare { get; set; } = false;

    public bool Equals(Credential? other)
    {
        if (other is null) return false;
        return Id == other.Id
            && Name == other.Name
            && ScriptID == other.ScriptID
            && AuthKey == other.AuthKey
            && IsEnabledForLB == other.IsEnabledForLB
            && UsesCloudflare == other.UsesCloudflare;
    }

    public override bool Equals(object? obj) => Equals(obj as Credential);
    public override int GetHashCode() => HashCode.Combine(Id, Name, ScriptID, AuthKey, IsEnabledForLB, UsesCloudflare);
}

public enum LBStrategy
{
    Balanced,
    CFPreferred,
    NormalPreferred,
    CFOnly,
    NormalOnly,
}

public static class LBStrategyExtensions
{
    public static string ToCode(this LBStrategy s) => s switch
    {
        LBStrategy.Balanced => "balanced",
        LBStrategy.CFPreferred => "cf_preferred",
        LBStrategy.NormalPreferred => "normal_preferred",
        LBStrategy.CFOnly => "cf_only",
        LBStrategy.NormalOnly => "normal_only",
        _ => "balanced",
    };

    public static LBStrategy FromCode(string s) => s switch
    {
        "cf_preferred" => LBStrategy.CFPreferred,
        "normal_preferred" => LBStrategy.NormalPreferred,
        "cf_only" => LBStrategy.CFOnly,
        "normal_only" => LBStrategy.NormalOnly,
        _ => LBStrategy.Balanced,
    };

    public static string Label(this LBStrategy s) => s switch
    {
        LBStrategy.Balanced => "Balanced",
        LBStrategy.CFPreferred => "Cloudflare First",
        LBStrategy.NormalPreferred => "Apps Script First",
        LBStrategy.CFOnly => "Cloudflare Only",
        LBStrategy.NormalOnly => "Apps Script Only",
        _ => "Balanced",
    };

    public static string Detail(this LBStrategy s) => s switch
    {
        LBStrategy.Balanced => "All selected profiles, spread evenly across requests.",
        LBStrategy.CFPreferred => "Use Cloudflare profiles first; fall back to Apps Script if all Cloudflare fail.",
        LBStrategy.NormalPreferred => "Use Apps Script profiles first; fall back to Cloudflare if all Apps Script fail.",
        LBStrategy.CFOnly => "Cloudflare profiles only.",
        LBStrategy.NormalOnly => "Apps Script profiles only.",
        _ => "",
    };

    public static bool HasFallback(this LBStrategy s) =>
        s == LBStrategy.CFPreferred || s == LBStrategy.NormalPreferred;
}

public enum LogLevel { Debug, Info, Warning, Error }

public static class LogLevelExtensions
{
    public static string ToCode(this LogLevel l) => l.ToString().ToUpperInvariant();

    public static LogLevel FromCode(string s) => s.ToUpperInvariant() switch
    {
        "DEBUG" => LogLevel.Debug,
        "WARNING" => LogLevel.Warning,
        "ERROR" => LogLevel.Error,
        _ => LogLevel.Info,
    };
}
