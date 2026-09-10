using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Holds the in-memory config for the app's lifetime and persists it on
/// every change, mirroring backend/api.py's self._config +
/// _update_config()/_config_snapshot().
///
/// _config_snapshot() on the Python side also injects fields computed
/// fresh on every call rather than ever persisted (_gsx_profiles_path_effective,
/// _exe_xml_path_effective) - those depend on SceneryService/ExeXmlService,
/// which this Core-project class must not reference directly (App wires
/// everything together, Core stays UI/composition-agnostic). RegisterEffectiveField
/// lets MainWindow plug those resolvers in after constructing the services
/// that own them, so every snapshot - config_get, config_set, and every
/// settings_*/exe_*/import_config method that returns one - stays enriched
/// from one place, the same way Python's single _config_snapshot() does.
/// </summary>
public class ConfigService
{
    private readonly string _path;
    private readonly List<(string Key, Func<JsonNode?> Resolve)> _effectiveFields = new();
    private JsonObject _config;

    public ConfigService(string path)
    {
        _path = path;
        _config = ConfigManager.LoadConfig(path);
    }

    public void RegisterEffectiveField(string key, Func<string> resolve) =>
        RegisterEffectiveField(key, () => JsonValue.Create(resolve()));

    public void RegisterEffectiveField(string key, Func<bool> resolve) =>
        RegisterEffectiveField(key, () => JsonValue.Create(resolve()));

    private void RegisterEffectiveField(string key, Func<JsonNode?> resolve)
    {
        _effectiveFields.Add((key, resolve));
    }

    public JsonObject Snapshot()
    {
        var snapshot = (JsonObject)_config.DeepClone();
        foreach (var (key, resolve) in _effectiveFields)
        {
            snapshot[key] = resolve();
        }
        return snapshot;
    }

    public JsonObject Update(JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            _config[key] = value?.DeepClone();
        }
        ConfigManager.SaveConfig(_path, _config);
        return Snapshot();
    }

    /// <summary>Read a single top-level field without a full snapshot clone.</summary>
    public JsonNode? Get(string key) => _config[key];

    /// <summary>
    /// For the mutations that don't fit the flat-patch shape Update()
    /// expects - setdefault-ing a nested dict, popping a key, direct
    /// assignment - mirroring how backend/api.py's exe_*/profiles_* methods
    /// poke self._config directly rather than always going through
    /// _update_config(patch). This stays the only other mutation path so
    /// every write still funnels through one save call, same invariant the
    /// Python side documents on _save_config.
    /// </summary>
    public JsonObject MutateAndSave(Action<JsonObject> mutate)
    {
        mutate(_config);
        ConfigManager.SaveConfig(_path, _config);
        return Snapshot();
    }

    /// <summary>
    /// Wholesale replacement, for Import Config (backend/api.py's
    /// import_config does self._config = new_config, not a patch merge).
    /// </summary>
    public JsonObject ReplaceAll(JsonObject newConfig)
    {
        _config = (JsonObject)newConfig.DeepClone();
        ConfigManager.SaveConfig(_path, _config);
        return Snapshot();
    }
}
