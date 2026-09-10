using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Loads data/aircraft_developers.json - a curated folder-prefix -> studio
/// display name table. Deliberately small; individual community livery
/// painters intentionally fall through to the creator-consensus/
/// prettified-prefix heuristic in AircraftScanner instead of a guess.
/// </summary>
public static class AircraftDeveloperData
{
    public static Dictionary<string, string> Load(string jsonPath)
    {
        var result = new Dictionary<string, string>();
        if (!File.Exists(jsonPath)) return result;
        try
        {
            var text = File.ReadAllText(jsonPath);
            if (JsonNode.Parse(text) is not JsonObject obj) return result;
            foreach (var (key, value) in obj)
            {
                if (value is JsonValue v && v.TryGetValue<string>(out var s))
                {
                    result[key] = s;
                }
            }
        }
        catch
        {
            // Missing/corrupt data file - fall back to the empty table,
            // matching Python's bare except here.
        }
        return result;
    }
}
