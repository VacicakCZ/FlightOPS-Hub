using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/config_manager.py - loading/saving/migrating
/// msfs_launcher_config.json. Kept close to the Python original method by
/// method so the two stay easy to diff during the rewrite.
/// </summary>
public static class ConfigManager
{
    public const string ConfigFileName = "msfs_launcher_config.json";
    public const int CurrentConfigVersion = 1;

    // The size the user landed on after trying the app at its old
    // portrait-ish 500x860 default and resizing it wider - now the
    // first-run default itself (still just a starting point: the window
    // remembers whatever size/position the user leaves it at afterward,
    // see MainWindow.RestoreWindowGeometry/SaveWindowGeometry).
    private const int DefaultWidth = 966;
    private const int DefaultHeight = 805;

    private static readonly HashSet<string> LegacyDarkLabels = new() { "Dark", "Tmavý", "Dunkel", "Oscuro", "暗色" };
    private static readonly HashSet<string> LegacyLightLabels = new() { "Light", "Světlý", "Hell", "Claro", "亮色" };

    private static readonly Regex GeometryRegex = new(@"^(\d+)x(\d+)(?:\+(-?\d+)\+(-?\d+))?$");

    public static readonly IReadOnlyDictionary<string, string> ProfileStores = new Dictionary<string, string>
    {
        ["flight"] = "_profiles",
        ["exe"] = "_exe_profiles",
    };

    public static readonly IReadOnlyDictionary<string, string> LastProfileKeys = new Dictionary<string, string>
    {
        ["flight"] = "_last_profile",
        ["exe"] = "_last_exe_profile",
    };

    public static string CanonicalTheme(string? raw)
    {
        if (raw != null && LegacyDarkLabels.Contains(raw)) return "dark";
        if (raw != null && LegacyLightLabels.Contains(raw)) return "light";
        if (raw is "dark" or "light" or "system") return raw;
        return "system";
    }

    public static void MigrateGeometry(JsonObject data)
    {
        var geometry = GetString(data, "_window_geometry");
        data.Remove("_window_geometry");

        if (!string.IsNullOrEmpty(geometry))
        {
            var match = GeometryRegex.Match(geometry.Trim());
            if (match.Success)
            {
                data.TryAdd("_window_width", int.Parse(match.Groups[1].Value));
                data.TryAdd("_window_height", int.Parse(match.Groups[2].Value));
                if (match.Groups[3].Success && match.Groups[4].Success)
                {
                    data.TryAdd("_window_x", int.Parse(match.Groups[3].Value));
                    data.TryAdd("_window_y", int.Parse(match.Groups[4].Value));
                }
                return;
            }
        }

        data.TryAdd("_window_width", DefaultWidth);
        data.TryAdd("_window_height", DefaultHeight);
    }

    /// <summary>
    /// Defensive: if a saved window position is clearly bogus (e.g. from a
    /// monitor that's since been disconnected), drop it and let WPF center
    /// the window instead.
    /// </summary>
    public static void ClampWindowPosition(JsonObject data)
    {
        var x = GetInt(data, "_window_x");
        var y = GetInt(data, "_window_y");
        if (x is null || y is null) return;

        if (x < -10000 || y < -10000 || x > 20000 || y > 20000)
        {
            data.Remove("_window_x");
            data.Remove("_window_y");
        }
    }

    /// <summary>
    /// The old app stored the *translated* "Default" label as the sentinel
    /// for "no profile selected", which breaks the moment the UI language
    /// changes. Self-heal on every load: if the stored value isn't an actual
    /// profile name, treat it as "no profile" (null) instead of hardcoding
    /// any particular language's default label.
    /// </summary>
    public static void SanitizeLastProfile(JsonObject data, string kind)
    {
        var lastKey = LastProfileKeys[kind];
        var storeKey = ProfileStores[kind];
        var last = GetString(data, lastKey);
        if (last is null) return;

        var store = data[storeKey] as JsonObject;
        if (store is null || !store.ContainsKey(last))
        {
            data[lastKey] = null;
        }
    }

    public static JsonObject MigrateConfig(JsonObject data)
    {
        var version = GetInt(data, "_config_version") ?? 0;
        if (version < 1)
        {
            MigrateGeometry(data);
            data["_theme"] = CanonicalTheme(GetString(data, "_theme") ?? "system");
            data["_config_version"] = CurrentConfigVersion;
        }
        ClampWindowPosition(data);
        SanitizeLastProfile(data, "flight");
        SanitizeLastProfile(data, "exe");
        return data;
    }

    public static JsonObject LoadConfig(string path)
    {
        var data = new JsonObject();
        if (File.Exists(path))
        {
            try
            {
                // File.ReadAllText auto-detects and strips a BOM, same as
                // Python's utf-8-sig - either way beats silently falling
                // back to an empty config over one stray BOM byte.
                var text = File.ReadAllText(path, Encoding.UTF8);
                data = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            }
            catch
            {
                data = new JsonObject();
            }
        }
        return MigrateConfig(data);
    }

    // .NET's default JSON encoder escapes '<', '>', '&', etc. to \uXXXX for
    // web-embedding safety - Python's json.dump never does that. This file
    // is read back only by JSON parsers (this app, or a human comparing it
    // to the Python build's output), so match Python's plain output.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void SaveConfig(string path, JsonObject data)
    {
        // Write to a temp file first, then an atomic move - a plain write
        // truncates the file immediately, so getting killed/crashing mid-
        // write would leave a half-written file that LoadConfig() can only
        // recover from by silently resetting to an empty config.
        var tmpPath = path + ".tmp";
        try
        {
            File.WriteAllText(tmpPath, data.ToJsonString(JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { /* nothing to clean up */ }
            throw;
        }
    }

    private static string? GetString(JsonObject data, string key)
    {
        if (data.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var result))
        {
            return result;
        }
        return null;
    }

    private static int? GetInt(JsonObject data, string key)
    {
        if (data.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<int>(out var result))
        {
            return result;
        }
        return null;
    }
}
