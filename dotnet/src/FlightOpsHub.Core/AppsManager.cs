using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/apps_manager.py - loading/saving/CRUD for
/// msfs_apps.json (the external addon apps list). Pure logic + file IO,
/// no UI - error results are i18n *keys* (e.g. "err_empty"), translated by
/// whatever calls this.
/// </summary>
public static class AppsManager
{
    public const string AppsFileName = "msfs_apps.json";

    public static JsonObject LoadApps(string path)
    {
        if (!File.Exists(path))
        {
            var empty = new JsonObject();
            SaveApps(path, empty);
            return empty;
        }

        JsonObject loaded;
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            loaded = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        }
        catch
        {
            loaded = new JsonObject();
        }

        var apps = new JsonObject();
        foreach (var (name, data) in loaded)
        {
            if (data is JsonValue stringValue && stringValue.TryGetValue<string>(out var pathString))
            {
                var delay = name == "REX Atmos" ? 150 : 0;
                apps[name] = new JsonObject
                {
                    ["path"] = pathString,
                    ["delay"] = delay,
                    ["admin"] = false,
                    ["launch_mode"] = delay > 0 ? "timer" : "immediate",
                };
            }
            else if (data is JsonObject obj)
            {
                var clone = (JsonObject)obj.DeepClone();
                clone.TryAdd("admin", false);
                var existingDelay = GetInt(clone, "delay") ?? 0;
                clone.TryAdd("launch_mode", existingDelay > 0 ? "timer" : "immediate");
                apps[name] = clone;
            }
        }
        SaveApps(path, apps);
        return apps;
    }

    public static void SaveApps(string path, JsonObject apps)
    {
        // Temp file + atomic move, same reasoning as ConfigManager.SaveConfig.
        var tmpPath = path + ".tmp";
        try
        {
            // Matches Python's json.dump(apps, f, indent=4); the relaxed
            // encoder avoids .NET's default \uXXXX-escaping of '<'/'>'/'&'
            // etc., which Python's json.dump never does (see ConfigManager
            // and DiagnosticsBuilder for the same fix).
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            File.WriteAllText(tmpPath, apps.ToJsonString(options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { /* nothing to clean up */ }
            throw;
        }
    }

    /// <summary>direction: "up" or "down". Persists and returns the (possibly reordered) apps.</summary>
    public static JsonObject MoveApp(string path, JsonObject apps, string name, string direction)
    {
        var keys = apps.Select(kv => kv.Key).ToList();
        var idx = keys.IndexOf(name);
        if (idx < 0) return apps;

        var target = direction == "up" ? idx - 1 : idx + 1;
        if (target < 0 || target >= keys.Count) return apps;

        (keys[idx], keys[target]) = (keys[target], keys[idx]);
        var reordered = new JsonObject();
        foreach (var key in keys)
        {
            reordered[key] = apps[key]?.DeepClone();
        }
        SaveApps(path, reordered);
        return reordered;
    }

    /// <summary>Returns an i18n error key on failure, or null if valid.</summary>
    public static string? ValidateAppInput(string name, string path, string? modeKey, string? delayText)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
        {
            return "err_empty";
        }
        if (modeKey == "timer")
        {
            if (!int.TryParse(delayText, out var delay) || delay <= 0)
            {
                return "err_delay";
            }
        }
        return null;
    }

    /// <summary>Adds a new app or edits an existing one in place (preserving list position on rename).</summary>
    public static JsonObject UpsertApp(
        string path, JsonObject apps, string name, string appPath, string modeKey,
        string? delayText, bool isAdmin, string? previousName)
    {
        var delay = modeKey == "timer" ? int.Parse(delayText!) : 0;
        var newData = new JsonObject
        {
            ["path"] = appPath.Trim(),
            ["delay"] = delay,
            ["admin"] = isAdmin,
            ["launch_mode"] = modeKey,
        };
        name = name.Trim();

        JsonObject result;
        if (!string.IsNullOrEmpty(previousName) && previousName != name && apps.ContainsKey(previousName))
        {
            result = new JsonObject();
            foreach (var (key, value) in apps)
            {
                result[key == previousName ? name : key] = key == previousName ? newData.DeepClone() : value?.DeepClone();
            }
        }
        else
        {
            result = (JsonObject)apps.DeepClone();
            result[name] = newData;
        }

        SaveApps(path, result);
        return result;
    }

    public static JsonObject RemoveApp(string path, JsonObject apps, string name)
    {
        var result = (JsonObject)apps.DeepClone();
        result.Remove(name);
        SaveApps(path, result);
        return result;
    }

    public static void OpenFolder(string appPath)
    {
        var folder = Path.GetDirectoryName(appPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort, matches Python's bare except here.
        }
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
