using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/scenery_simbrief_match.py - matches a SimBrief flight
/// plan's airports against the already-scanned scenery list. Deliberately
/// does not touch SceneryScanner: a scenery record's ICAO is only ever
/// exposed embedded in its display_name ("[LKPR] Prague Airport"), so this
/// re-derives it via the same "[ICAO] " prefix instead of adding a field.
/// </summary>
public static class ScenerySimbriefMatch
{
    private static readonly Regex IcaoPrefixRegex = new(@"^\[([A-Z0-9]{4})\]");

    public static string? IcaoFromDisplayName(string? displayName)
    {
        var match = IcaoPrefixRegex.Match(displayName ?? "");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// legs: [(role, icaoOrNull), ...]. Returns a list of
    /// {role, icao, status, folder_name, display_name} - status is one of
    /// "enabled", "disabled", "not_installed". Legs with no ICAO (e.g. no
    /// alternate on this OFP) are skipped entirely.
    /// </summary>
    public static List<JsonObject> MatchFlightPlan(IReadOnlyList<JsonObject> records, IEnumerable<(string Role, string? Icao)> legs)
    {
        var byIcao = new Dictionary<string, JsonObject>();
        foreach (var record in records)
        {
            var icao = IcaoFromDisplayName(record["display_name"]?.GetValue<string>());
            if (icao != null) byIcao.TryAdd(icao, record);
        }

        var results = new List<JsonObject>();
        foreach (var (role, icao) in legs)
        {
            if (string.IsNullOrEmpty(icao)) continue;

            if (!byIcao.TryGetValue(icao, out var record))
            {
                results.Add(new JsonObject
                {
                    ["role"] = role, ["icao"] = icao, ["status"] = "not_installed",
                    ["folder_name"] = null, ["display_name"] = null,
                });
            }
            else
            {
                var status = record["enabled"]!.GetValue<bool>() ? "enabled" : "disabled";
                results.Add(new JsonObject
                {
                    ["role"] = role, ["icao"] = icao, ["status"] = status,
                    ["folder_name"] = record["folder_name"]!.GetValue<string>(),
                    ["display_name"] = record["display_name"]!.GetValue<string>(),
                });
            }
        }
        return results;
    }
}
