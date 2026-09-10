using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/api.py's profiles_list/profiles_save/profiles_delete.
/// Profiles live inside the main config (ConfigManager.ProfileStores/
/// LastProfileKeys), keyed by "kind" - "flight" for the Flight tab,
/// "exe" for exe.xml profiles.
/// </summary>
public class ProfilesService
{
    private readonly ConfigService _config;

    public ProfilesService(ConfigService config)
    {
        _config = config;
    }

    public JsonObject List(string kind)
    {
        var storeKey = ConfigManager.ProfileStores[kind];
        return _config.Get(storeKey) is JsonObject store ? (JsonObject)store.DeepClone() : new JsonObject();
    }

    public JsonObject Save(string kind, string name, JsonNode? members)
    {
        var storeKey = ConfigManager.ProfileStores[kind];
        var lastKey = ConfigManager.LastProfileKeys[kind];

        var snapshot = _config.MutateAndSave(config =>
        {
            if (config[storeKey] is not JsonObject store)
            {
                store = new JsonObject();
                config[storeKey] = store;
            }
            store[name] = members?.DeepClone();
            config[lastKey] = name;
        });

        return new JsonObject
        {
            ["profiles"] = snapshot[storeKey]?.DeepClone() ?? new JsonObject(),
            ["last"] = name,
        };
    }

    public JsonObject Delete(string kind, string name)
    {
        var storeKey = ConfigManager.ProfileStores[kind];
        var lastKey = ConfigManager.LastProfileKeys[kind];

        var snapshot = _config.MutateAndSave(config =>
        {
            if (config[storeKey] is JsonObject store)
            {
                store.Remove(name);
            }
            config[lastKey] = null;
        });

        return new JsonObject
        {
            ["profiles"] = snapshot[storeKey]?.DeepClone() ?? new JsonObject(),
            ["last"] = null,
        };
    }
}
