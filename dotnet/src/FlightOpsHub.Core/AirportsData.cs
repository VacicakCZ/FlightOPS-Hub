using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/airports_data.py - lazy-loaded ICAO -> coordinates
/// lookup, backed by data/airports.json.
/// </summary>
public class AirportsData
{
    private readonly string _jsonPath;
    private JsonObject? _cache;

    public AirportsData(string jsonPath)
    {
        _jsonPath = jsonPath;
    }

    private JsonObject Load()
    {
        if (_cache != null) return _cache;
        try
        {
            var text = File.ReadAllText(_jsonPath);
            _cache = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        }
        catch
        {
            _cache = new JsonObject();
        }
        return _cache;
    }

    public JsonObject? Lookup(string? icaoCode)
    {
        if (string.IsNullOrEmpty(icaoCode)) return null;
        return Load()[icaoCode.ToUpperInvariant()] as JsonObject;
    }
}
