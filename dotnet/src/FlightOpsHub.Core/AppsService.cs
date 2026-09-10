using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Holds the in-memory apps list for the app's lifetime, mirroring
/// backend/api.py's self._apps + apps_list/apps_save/apps_remove/apps_reorder.
/// </summary>
public class AppsService
{
    private readonly string _path;
    private JsonObject _apps;

    public AppsService(string path)
    {
        _path = path;
        _apps = AppsManager.LoadApps(path);
    }

    public JsonObject List() => (JsonObject)_apps.DeepClone();

    /// <summary>app: {name, path, launch_mode, delay, admin, previous_name?}</summary>
    public JsonObject Save(JsonObject app)
    {
        var name = GetString(app, "name") ?? "";
        var appPath = GetString(app, "path") ?? "";
        var modeKey = GetString(app, "launch_mode");
        var delayText = GetDelayText(app);

        var error = AppsManager.ValidateAppInput(name, appPath, modeKey, delayText);
        if (error != null)
        {
            return new JsonObject { ["ok"] = false, ["error"] = error };
        }

        _apps = AppsManager.UpsertApp(
            _path, _apps, name, appPath, modeKey ?? "immediate", delayText,
            isAdmin: GetBool(app, "admin"), previousName: GetString(app, "previous_name"));

        return new JsonObject { ["ok"] = true, ["apps"] = _apps.DeepClone() };
    }

    public JsonObject Remove(string name)
    {
        _apps = AppsManager.RemoveApp(_path, _apps, name);
        return new JsonObject { ["ok"] = true, ["apps"] = _apps.DeepClone() };
    }

    public JsonObject Reorder(string name, string direction)
    {
        _apps = AppsManager.MoveApp(_path, _apps, name, direction);
        return new JsonObject { ["ok"] = true, ["apps"] = _apps.DeepClone() };
    }

    /// <summary>Wholesale replacement, for Import Config.</summary>
    public JsonObject ReplaceAll(JsonObject newApps)
    {
        _apps = (JsonObject)newApps.DeepClone();
        AppsManager.SaveApps(_path, _apps);
        return _apps.DeepClone().AsObject();
    }

    public bool AppExeExists(string name)
    {
        if (_apps[name] is not JsonObject data) return false;
        var exePath = GetString(data, "path");
        return !string.IsNullOrEmpty(exePath) && File.Exists(exePath);
    }

    private static string? GetString(JsonObject data, string key)
    {
        if (data.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var result))
        {
            return result;
        }
        return null;
    }

    private static bool GetBool(JsonObject data, string key)
    {
        if (data.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var result))
        {
            return result;
        }
        return false;
    }

    /// <summary>
    /// "delay" can arrive as a JSON number or a string depending on what the
    /// frontend control sent - normalize to text the same way Python's
    /// permissive int(delay_str) effectively does, since ValidateAppInput/
    /// UpsertApp both just need a parseable string.
    /// </summary>
    private static string? GetDelayText(JsonObject app)
    {
        if (!app.TryGetPropertyValue("delay", out var node) || node is not JsonValue value)
        {
            return null;
        }
        if (value.TryGetValue<string>(out var text)) return text;
        if (value.TryGetValue<double>(out var number)) return number.ToString("F0");
        return null;
    }
}
