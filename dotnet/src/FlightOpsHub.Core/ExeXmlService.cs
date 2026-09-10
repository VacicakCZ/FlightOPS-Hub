using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/api.py's exe_* / settings_*_exe_xml_path methods -
/// stateful wrapper around ExeXmlManager that resolves the effective
/// exe.xml path from config (override, else sim version/platform) the same
/// way _current_exe_xml_path() does on the Python side.
/// </summary>
public class ExeXmlService
{
    private readonly ConfigService _config;

    public ExeXmlService(ConfigService config)
    {
        _config = config;
    }

    /// <summary>The effective exe.xml path in use right now (override, else resolved from sim version/platform) - also exposed as config's _exe_xml_path_effective, see ConfigService.RegisterEffectiveField.</summary>
    public string CurrentPath()
    {
        var overridePath = GetString("_exe_xml_path_override");
        if (!string.IsNullOrEmpty(overridePath)) return overridePath;

        var simVersion = GetString("_sim_version") ?? "MSFS 2024";
        var simPlatform = GetString("_sim_platform") ?? "Steam";
        return ExeXmlManager.GetExeXmlPath(simVersion, simPlatform);
    }

    public JsonObject SetExeXmlPath(string path)
    {
        return _config.Update(new JsonObject { ["_exe_xml_path_override"] = Path.GetFullPath(path) });
    }

    public JsonObject ResetExeXmlPath()
    {
        return _config.MutateAndSave(config => config.Remove("_exe_xml_path_override"));
    }

    public JsonObject ListAddons()
    {
        var xmlPath = CurrentPath();
        var hasBackup = ExeXmlManager.HasBackup(xmlPath);

        if (!File.Exists(xmlPath))
        {
            return new JsonObject
            {
                ["path"] = xmlPath,
                ["exists"] = false,
                ["addons"] = new JsonArray(),
                ["error"] = null,
                ["has_backup"] = hasBackup,
            };
        }

        try
        {
            var customNames = _config.Get("_exe_custom_names") as JsonObject ?? new JsonObject();
            var addons = ExeXmlManager.ListAddons(xmlPath, customNames);
            return new JsonObject
            {
                ["path"] = xmlPath,
                ["exists"] = true,
                ["addons"] = new JsonArray(addons.Select(a => (JsonNode)a).ToArray()),
                ["error"] = null,
                ["has_backup"] = hasBackup,
            };
        }
        catch (Exception ex)
        {
            return new JsonObject
            {
                ["path"] = xmlPath,
                ["exists"] = true,
                ["addons"] = new JsonArray(),
                ["error"] = ex.Message,
                ["has_backup"] = hasBackup,
            };
        }
    }

    public JsonObject RestoreBackup()
    {
        var restored = ExeXmlManager.RestoreFromBackup(CurrentPath());
        return new JsonObject { ["ok"] = restored };
    }

    public JsonObject Toggle(string uniqueKey, bool enabled)
    {
        ExeXmlManager.SetAddonsEnabled(CurrentPath(), new Dictionary<string, bool> { [uniqueKey] = enabled });
        return new JsonObject { ["ok"] = true };
    }

    public JsonObject Rename(string uniqueKey, string originalName, string? newName)
    {
        newName = (newName ?? "").Trim();
        _config.MutateAndSave(config =>
        {
            if (config["_exe_custom_names"] is not JsonObject customNames)
            {
                customNames = new JsonObject();
                config["_exe_custom_names"] = customNames;
            }
            if (string.IsNullOrEmpty(newName) || newName == originalName)
            {
                customNames.Remove(uniqueKey);
            }
            else
            {
                customNames[uniqueKey] = newName;
            }
        });
        return new JsonObject { ["ok"] = true };
    }

    public JsonObject ApplyProfile(string profileName)
    {
        var exeProfiles = _config.Get("_exe_profiles") as JsonObject;
        var membersNode = exeProfiles?[profileName] as JsonArray;
        var members = new HashSet<string>(
            (membersNode ?? new JsonArray())
                .Select(n => n?.GetValue<string>())
                .Where(s => s is not null)!);

        var xmlPath = CurrentPath();
        List<JsonObject> addons;
        try
        {
            addons = ExeXmlManager.ListAddons(xmlPath, new JsonObject());
        }
        catch
        {
            return new JsonObject { ["ok"] = false };
        }

        var desired = addons.ToDictionary(
            a => a["unique_key"]!.GetValue<string>(),
            a => members.Contains(a["unique_key"]!.GetValue<string>()));

        ExeXmlManager.SetAddonsEnabled(xmlPath, desired);
        _config.MutateAndSave(config => config["_last_exe_profile"] = profileName);
        return new JsonObject { ["ok"] = true };
    }

    private string? GetString(string key)
    {
        return _config.Get(key) is JsonValue value && value.TryGetValue<string>(out var result) ? result : null;
    }
}
