using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shade.Models;

public sealed class AppSettings
{
    public List<Credential> Credentials { get; set; } = new();
    public Guid? ActiveCredentialID { get; set; }

    public bool EnableLoadBalancing { get; set; } = false;
    public LBStrategy LbStrategy { get; set; } = LBStrategy.Balanced;

    /// Transient — not persisted. True while the core was auto-restarted on
    /// the fallback pool because the primary pool went fully unhealthy.
    public bool LbFallbackActive { get; set; } = false;

    public string ListenHost { get; set; } = "127.0.0.1";
    public int ListenPort { get; set; } = 1080;
    public int SocksPort { get; set; } = 8080;

    public string FrontDomain { get; set; } = "www.google.com";
    public string GoogleIP { get; set; } = "216.239.38.120";
    public bool VerifySSL { get; set; } = true;
    public LogLevel LogLevel { get; set; } = LogLevel.Info;

    public bool UseSystemProxy { get; set; } = false;

    public Credential? ActiveCredential
    {
        get
        {
            if (ActiveCredentialID is Guid id)
                return Credentials.FirstOrDefault(c => c.Id == id) ?? Credentials.FirstOrDefault();
            return Credentials.FirstOrDefault();
        }
    }

    public string ScriptID => ActiveCredential?.ScriptID ?? "";
    public string AuthKey => ActiveCredential?.AuthKey ?? "";

    /// Mirrors Swift `effectiveLBPool` exactly.
    public List<Credential> EffectiveLBPool()
    {
        if (!EnableLoadBalancing)
            return ActiveCredential is null ? new() : new() { ActiveCredential };

        var enabled = Credentials.Where(c => c.IsEnabledForLB).ToList();
        var cf = enabled.Where(c => c.UsesCloudflare).ToList();
        var normal = enabled.Where(c => !c.UsesCloudflare).ToList();

        return LbStrategy switch
        {
            LBStrategy.Balanced => enabled,
            LBStrategy.CFOnly => cf,
            LBStrategy.NormalOnly => normal,
            LBStrategy.CFPreferred => LbFallbackActive ? enabled : (cf.Count == 0 ? normal : cf),
            LBStrategy.NormalPreferred => LbFallbackActive ? enabled : (normal.Count == 0 ? cf : normal),
            _ => enabled,
        };
    }

    /// Build the JSON object the Python core consumes (via `-c <path>`).
    /// Mirrors Swift `makeCoreConfig()`.
    public JsonObject MakeCoreConfig()
    {
        var cred = ActiveCredential;
        var pool = EnableLoadBalancing ? EffectiveLBPool()
                                        : (cred is null ? new List<Credential>() : new() { cred });

        var scriptConfigs = new JsonArray();
        foreach (var c in pool)
        {
            if (string.IsNullOrEmpty(c.ScriptID)) continue;
            scriptConfigs.Add(new JsonObject
            {
                ["id"] = c.ScriptID,
                ["key"] = c.AuthKey,
            });
        }

        var dict = new JsonObject
        {
            ["mode"] = "apps_script",
            ["google_ip"] = GoogleIP,
            ["front_domain"] = FrontDomain,
            ["script_configs"] = scriptConfigs,
            ["parallel_relay"] = EnableLoadBalancing ? Math.Max(1, scriptConfigs.Count) : 1,
            ["listen_host"] = ListenHost,
            ["listen_port"] = ListenPort,
            ["socks5_host"] = ListenHost,
            ["socks5_port"] = SocksPort,
            ["socks5_enabled"] = true,
            ["log_level"] = LogLevel.ToCode(),
            ["verify_ssl"] = VerifySSL,
        };

        if (cred is not null)
        {
            if (!string.IsNullOrEmpty(cred.ScriptID)) dict["script_id"] = cred.ScriptID;
            if (!string.IsNullOrEmpty(cred.AuthKey)) dict["auth_key"] = cred.AuthKey;
        }
        return dict;
    }

    // ── Serialization ────────────────────────────────────────────────────
    // Custom because we need to (a) migrate legacy single-credential JSON
    // (keys `scriptID` / `authKey`) and (b) skip transient `lbFallbackActive`.

    public static AppSettings Load(string path)
    {
        if (!System.IO.File.Exists(path)) return new AppSettings();
        try
        {
            var text = System.IO.File.ReadAllText(path);
            var node = JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
            return Decode(node);
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        var node = Encode();
        var json = node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, json);
    }

    public JsonObject Encode()
    {
        var creds = new JsonArray();
        foreach (var c in Credentials)
        {
            creds.Add(new JsonObject
            {
                ["id"] = c.Id.ToString(),
                ["name"] = c.Name,
                ["scriptID"] = c.ScriptID,
                ["authKey"] = c.AuthKey,
                ["isEnabledForLB"] = c.IsEnabledForLB,
                ["usesCloudflare"] = c.UsesCloudflare,
            });
        }
        return new JsonObject
        {
            ["credentials"] = creds,
            ["activeCredentialID"] = ActiveCredentialID?.ToString(),
            ["enableLoadBalancing"] = EnableLoadBalancing,
            ["lbStrategy"] = LbStrategy.ToCode(),
            ["listenHost"] = ListenHost,
            ["listenPort"] = ListenPort,
            ["socksPort"] = SocksPort,
            ["frontDomain"] = FrontDomain,
            ["googleIP"] = GoogleIP,
            ["verifySSL"] = VerifySSL,
            ["logLevel"] = LogLevel.ToCode(),
            ["useSystemProxy"] = UseSystemProxy,
        };
    }

    public static AppSettings Decode(JsonObject c)
    {
        var s = new AppSettings();

        if (c["credentials"] is JsonArray arr)
        {
            foreach (var n in arr)
            {
                if (n is not JsonObject o) continue;
                var cred = new Credential
                {
                    Id = TryParseGuid(GetString(o, "id")) ?? Guid.NewGuid(),
                    Name = GetString(o, "name") ?? "Default",
                    ScriptID = GetString(o, "scriptID") ?? "",
                    AuthKey = GetString(o, "authKey") ?? "",
                    IsEnabledForLB = o["isEnabledForLB"]?.GetValue<bool>() ?? true,
                    UsesCloudflare = o["usesCloudflare"]?.GetValue<bool>() ?? false,
                };
                s.Credentials.Add(cred);
            }
        }

        s.ActiveCredentialID = TryParseGuid(GetString(c, "activeCredentialID"));

        // Legacy: single-credential layout — the macOS app migrates this exact way
        if (s.Credentials.Count == 0)
        {
            var sid = GetString(c, "scriptID") ?? "";
            var ak = GetString(c, "authKey") ?? "";
            if (!string.IsNullOrEmpty(sid) || !string.IsNullOrEmpty(ak))
            {
                var cred = new Credential { Name = "Default", ScriptID = sid, AuthKey = ak };
                s.Credentials.Add(cred);
                s.ActiveCredentialID = cred.Id;
            }
        }

        s.ListenHost = GetString(c, "listenHost") ?? "127.0.0.1";
        s.ListenPort = c["listenPort"]?.GetValue<int>() ?? 1080;
        s.SocksPort = c["socksPort"]?.GetValue<int>() ?? 8080;
        s.FrontDomain = GetString(c, "frontDomain") ?? "www.google.com";
        s.GoogleIP = GetString(c, "googleIP") ?? "216.239.38.120";
        s.VerifySSL = c["verifySSL"]?.GetValue<bool>() ?? true;
        s.LogLevel = LogLevelExtensions.FromCode(GetString(c, "logLevel") ?? "INFO");
        s.UseSystemProxy = c["useSystemProxy"]?.GetValue<bool>() ?? false;
        s.EnableLoadBalancing = c["enableLoadBalancing"]?.GetValue<bool>() ?? false;
        s.LbStrategy = LBStrategyExtensions.FromCode(GetString(c, "lbStrategy") ?? "balanced");
        return s;
    }

    /// JsonNode.ToString() on a string value can include surrounding quotes
    /// in some .NET builds. Using GetValue<string>() avoids the ambiguity.
    private static string? GetString(JsonObject o, string key)
    {
        try
        {
            return o[key]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static Guid? TryParseGuid(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return Guid.TryParse(s, out var g) ? g : null;
    }
}
