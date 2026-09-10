using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/scenery_map.py - builds marker data for the Scenery
/// tab's map view, and attaches airport display names to scan records.
/// Records without a resolvable ICAO/coordinate are simply omitted from
/// markers - nothing to plot them at.
/// </summary>
public class SceneryMapBuilder
{
    private readonly AirportsData _airports;

    public SceneryMapBuilder(AirportsData airports)
    {
        _airports = airports;
    }

    public List<JsonObject> BuildMarkers(IReadOnlyList<JsonObject> records)
    {
        var markers = new List<JsonObject>();
        foreach (var record in records)
        {
            var icao = ScenerySimbriefMatch.IcaoFromDisplayName(record["display_name"]?.GetValue<string>());
            var airport = icao != null ? _airports.Lookup(icao) : null;
            if (airport is null) continue;

            markers.Add(new JsonObject
            {
                ["folder_name"] = record["folder_name"]?.DeepClone(),
                ["display_name"] = record["display_name"]?.DeepClone(),
                ["icao"] = icao,
                ["enabled"] = record["enabled"]?.DeepClone(),
                ["lat"] = airport["lat"]?.DeepClone(),
                ["lon"] = airport["lon"]?.DeepClone(),
                ["airport_name"] = airport["name"]?.DeepClone(),
                ["gsx_status"] = record["gsx_status"]?.DeepClone(),
                ["developer"] = record["developer"]?.DeepClone(),
            });
        }
        return markers;
    }

    /// <summary>Adds an airport_name field (null if unresolvable) to each record in place.</summary>
    public void AttachAirportNames(IReadOnlyList<JsonObject> records)
    {
        foreach (var record in records)
        {
            var icao = ScenerySimbriefMatch.IcaoFromDisplayName(record["display_name"]?.GetValue<string>());
            var airport = icao != null ? _airports.Lookup(icao) : null;
            record["airport_name"] = airport?["name"]?.DeepClone();
        }
    }
}
