using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>Shared manifest.json reader used by scenery/aircraft scanning.</summary>
public static class ManifestReader
{
    public static JsonObject? Read(string folderPath)
    {
        var manifestPath = Path.Combine(folderPath, "manifest.json");
        if (!File.Exists(manifestPath)) return null;
        try
        {
            // File.ReadAllText auto-strips a BOM, matching Python's utf-8-sig.
            var text = File.ReadAllText(manifestPath);
            return JsonNode.Parse(text) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    public static string GetString(JsonObject? manifest, string key)
    {
        if (manifest is null) return "";
        return manifest[key] is JsonValue value && value.TryGetValue<string>(out var result) ? result : "";
    }
}
