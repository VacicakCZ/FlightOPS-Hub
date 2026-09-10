using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/api.py's settings_set_community_path/_switch_sim/
/// settings_set_sim_version/settings_set_sim_platform/
/// detect_community_path/settings_set_disabled_path/
/// settings_reset_disabled_path/disk_space_status/airac_status.
/// </summary>
public class SettingsService
{
    private readonly ConfigService _config;

    public SettingsService(ConfigService config)
    {
        _config = config;
    }

    public JsonObject SetCommunityPath(string path)
    {
        var normalized = Path.GetFullPath(path);
        return _config.MutateAndSave(config =>
        {
            if (config["_community_paths_by_sim"] is not JsonObject remembered)
            {
                remembered = new JsonObject();
                config["_community_paths_by_sim"] = remembered;
            }
            remembered[SimKeyFrom(config)] = normalized;
            config["_community_path"] = normalized;
        });
    }

    /// <summary>
    /// Remembers the Community path under the sim/platform combo being
    /// left, then restores whatever was last used for the combo being
    /// switched to (or clears it if that combo has never had one set) -
    /// so flipping between MSFS 2020/2024 (or Steam/MS Store) does not
    /// silently keep pointing at the wrong install's Community folder.
    /// </summary>
    private JsonObject SwitchSim(string key, string value)
    {
        return _config.MutateAndSave(config =>
        {
            if (config["_community_paths_by_sim"] is not JsonObject remembered)
            {
                remembered = new JsonObject();
                config["_community_paths_by_sim"] = remembered;
            }

            var oldPath = GetStringFrom(config, "_community_path") ?? "";
            if (!string.IsNullOrEmpty(oldPath))
            {
                remembered[SimKeyFrom(config)] = oldPath;
            }

            config[key] = value;

            config["_community_path"] = remembered[SimKeyFrom(config)] is JsonValue restored
                && restored.TryGetValue<string>(out var restoredPath)
                    ? restoredPath
                    : "";
        });
    }

    public JsonObject SetSimVersion(string version) => SwitchSim("_sim_version", version);

    public JsonObject SetSimPlatform(string platform) => SwitchSim("_sim_platform", platform);

    /// <summary>Best-effort suggestion only - see CommunityDetect.</summary>
    public JsonObject DetectCommunityPath()
    {
        var simVersion = GetString("_sim_version") ?? "MSFS 2024";
        var simPlatform = GetString("_sim_platform") ?? "Steam";
        return new JsonObject { ["found"] = CommunityDetect.DetectCommunityPath(simVersion, simPlatform) };
    }

    public JsonObject SetDisabledPath(string path)
    {
        var normalized = Path.GetFullPath(path);
        var result = CommunityPaths.ValidateDisabledPath(normalized, GetString("_community_path"));
        if (!result["ok"]!.GetValue<bool>())
        {
            return result;
        }
        result["config"] = _config.Update(new JsonObject { ["_disabled_holding_path"] = normalized });
        return result;
    }

    public JsonObject ResetDisabledPath()
    {
        return _config.MutateAndSave(config => config.Remove("_disabled_holding_path"));
    }

    /// <summary>
    /// Instant free-space check (not a folder walk) on the drive(s)
    /// backing Community and the disabled-holding folder.
    /// </summary>
    public JsonArray DiskSpaceStatus()
    {
        var communityPath = GetString("_community_path") ?? "";
        var disabledPath = "";
        if (!string.IsNullOrEmpty(communityPath) && Directory.Exists(communityPath))
        {
            disabledPath = DisabledLocations.ResolveDisabledLocations(communityPath, GetString("_disabled_holding_path"))[0];
        }
        var entries = DiskSpaceChecker.Check(communityPath, disabledPath);
        return new JsonArray(entries.Select(e => (JsonNode)e).ToArray());
    }

    public JsonObject AiracStatus()
    {
        return new JsonObject
        {
            ["current"] = AiracManager.GetCurrentAirac(),
            ["installed"] = AiracManager.GetInstalledAirac(GetString("_community_path")),
        };
    }

    /// <summary>
    /// Toggles the Startup-folder shortcut (see StartupShortcut.cs) - the
    /// shortcut's own presence is the persisted state, so there's no
    /// separate config flag to fall out of sync with it; exePath is the
    /// currently running exe's own path (Environment.ProcessPath), passed
    /// in rather than resolved here since Core has no opinion on how the
    /// App project determines its own executable location.
    /// </summary>
    public JsonObject SetLaunchAtStartup(bool enabled, string exePath)
    {
        if (enabled) StartupShortcut.Enable(exePath);
        else StartupShortcut.Disable();
        return _config.Snapshot();
    }

    private string? GetString(string key) => GetStringFrom(null, key);

    private string? GetStringFrom(JsonObject? config, string key)
    {
        var node = config?[key] ?? _config.Get(key);
        return node is JsonValue value && value.TryGetValue<string>(out var result) ? result : null;
    }

    private static string SimKeyFrom(JsonObject config)
    {
        var simVersion = (config["_sim_version"] as JsonValue)?.GetValue<string>() ?? "MSFS 2024";
        var simPlatform = (config["_sim_platform"] as JsonValue)?.GetValue<string>() ?? "Steam";
        return $"{simVersion}|{simPlatform}";
    }
}
