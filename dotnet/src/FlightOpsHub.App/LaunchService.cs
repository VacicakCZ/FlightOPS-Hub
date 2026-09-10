using System.Text.Json.Nodes;
using System.Windows;
using FlightOpsHub.Core;

namespace FlightOpsHub.App;

/// <summary>
/// Port of backend/api.py's launch_precheck/launch_all - ties AppsService/
/// ConfigService/I18n to a fresh LaunchSession per launch.
/// </summary>
public class LaunchService
{
    private readonly Window _window;
    private readonly ConfigService _config;
    private readonly AppsService _apps;
    private readonly I18n _i18n;
    private readonly string _trayIconPath;

    public LaunchService(Window window, ConfigService config, AppsService apps, I18n i18n, string trayIconPath)
    {
        _window = window;
        _config = config;
        _apps = apps;
        _i18n = i18n;
        _trayIconPath = trayIconPath;
    }

    /// <summary>
    /// Called before the real launch so the frontend can warn about (and
    /// let the user confirm past) any checked app whose .exe path no
    /// longer exists - LaunchSession itself silently skips those with no
    /// user-visible signal at all.
    /// </summary>
    public JsonObject LaunchPrecheck(JsonObject appStates)
    {
        var selected = SelectedNames(appStates);
        var missing = selected.Where(name => !_apps.AppExeExists(name)).ToList();
        return new JsonObject { ["missing"] = new JsonArray(missing.Select(n => (JsonNode)n).ToArray()) };
    }

    /// <summary>appStates: {appName: bool} for every configured app (mirrors the old per-checkbox on/off persistence).</summary>
    public JsonObject LaunchAll(JsonObject appStates, string profileName)
    {
        _config.MutateAndSave(config =>
        {
            foreach (var (name, value) in appStates)
            {
                config[name] = IsChecked(value) ? "on" : "off";
            }
            config["_last_profile"] = profileName;
        });

        var selected = SelectedNames(appStates);
        var lang = GetConfigString("_language") ?? "EN";
        var simVersion = GetConfigString("_sim_version") ?? "MSFS 2024";
        var simPlatform = GetConfigString("_sim_platform") ?? "Steam";
        var postLaunchBehavior = GetConfigString("_post_launch_behavior") ?? "exit";
        var appsDict = BuildAppsDict(_apps.List());

        var session = new LaunchSession(_window, key => _i18n.Translate(lang, key), _trayIconPath);

        // Start() makes blocking tasklist/process calls and must never run
        // on the WPF UI thread that dispatched this API call.
        Task.Run(() => session.Start(appsDict, selected, simVersion, simPlatform, postLaunchBehavior));

        return new JsonObject { ["ok"] = true };
    }

    private static List<string> SelectedNames(JsonObject appStates) =>
        appStates.Where(kv => IsChecked(kv.Value)).Select(kv => kv.Key).ToList();

    private static bool IsChecked(JsonNode? value) => value is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private string? GetConfigString(string key) =>
        _config.Get(key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static Dictionary<string, (string Path, int Delay, bool Admin, string LaunchMode)> BuildAppsDict(JsonObject apps)
    {
        var result = new Dictionary<string, (string, int, bool, string)>();
        foreach (var (name, value) in apps)
        {
            if (value is not JsonObject data) continue;
            var path = data["path"] is JsonValue pv && pv.TryGetValue<string>(out var p) ? p : "";
            var delay = data["delay"] is JsonValue dv && dv.TryGetValue<int>(out var d) ? d : 0;
            var admin = data["admin"] is JsonValue av && av.TryGetValue<bool>(out var a) && a;
            var launchMode = data["launch_mode"] is JsonValue lv && lv.TryGetValue<string>(out var lm)
                ? lm
                : (delay > 0 ? "timer" : "immediate");
            result[name] = (path, delay, admin, launchMode);
        }
        return result;
    }
}
