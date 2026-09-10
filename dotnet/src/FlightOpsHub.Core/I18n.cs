using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/i18n.py - translation lookup for the few strings the
/// backend itself needs (the native tray icon menu, which has no JS
/// runtime). Reads the same locale JSON files the frontend uses.
/// </summary>
public class I18n
{
    private readonly string _localesDir;
    private readonly Dictionary<string, JsonObject> _cache = new();

    public I18n(string localesDir)
    {
        _localesDir = localesDir;
    }

    public string Translate(string? lang, string key)
    {
        lang = (string.IsNullOrEmpty(lang) ? "EN" : lang).ToLowerInvariant();
        if (!_cache.TryGetValue(lang, out var strings))
        {
            try
            {
                var text = File.ReadAllText(Path.Combine(_localesDir, $"{lang}.json"));
                strings = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            }
            catch
            {
                strings = new JsonObject();
            }
            _cache[lang] = strings;
        }
        return strings[key] is JsonValue value && value.TryGetValue<string>(out var result) ? result : key;
    }
}
