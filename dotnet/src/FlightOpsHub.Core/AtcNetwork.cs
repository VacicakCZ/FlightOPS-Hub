using System.Net.Http;
using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/atc_network.py - best-effort "is there ATC online at
/// this airport right now" check against VATSIM/IVAO's public live-data
/// feeds. Controller callsigns follow an "&lt;ICAO&gt;_&lt;POSITION&gt;"
/// convention; the airport is the prefix before the first underscore.
/// </summary>
public static class AtcNetwork
{
    private const string VatsimDataUrl = "https://data.vatsim.net/v3/vatsim-data.json";
    private const string IvaoWhazzupUrl = "https://api.ivao.aero/v2/tracker/whazzup";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient Http = new() { Timeout = Timeout };

    public static string? IcaoFromCallsign(string? callsign)
    {
        var icao = (callsign ?? "").Split('_', 2)[0].Trim().ToUpperInvariant();
        return icao.Length == 4 && icao.All(char.IsLetter) ? icao : null;
    }

    public static string PositionFromCallsign(string? callsign)
    {
        var parts = (callsign ?? "").Split('_', 2);
        return parts.Length == 2 && parts[1].Trim().Length > 0 ? parts[1].Trim().ToUpperInvariant() : "ATC";
    }

    public static void AddPosition(Dictionary<string, List<JsonObject>> byAirport, string? callsign, JsonNode? frequency, string? positionOverride = null)
    {
        var icao = IcaoFromCallsign(callsign);
        if (icao is null) return;
        var position = positionOverride ?? PositionFromCallsign(callsign);
        if (!byAirport.TryGetValue(icao, out var list))
        {
            list = new List<JsonObject>();
            byAirport[icao] = list;
        }
        list.Add(new JsonObject { ["position"] = position, ["frequency"] = frequency?.DeepClone() });
    }

    private static async Task<Dictionary<string, List<JsonObject>>> FetchVatsimAtc()
    {
        var response = await Http.GetAsync(VatsimDataUrl);
        response.EnsureSuccessStatusCode();
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject ?? new JsonObject();

        var byAirport = new Dictionary<string, List<JsonObject>>();
        if (data["controllers"] is JsonArray controllers)
        {
            foreach (var c in controllers)
            {
                if (c is not JsonObject obj) continue;
                var facility = obj["facility"] is JsonValue fv && fv.TryGetValue<int>(out var f) ? f : 0;
                // facility 0 is an observer connection, not a staffed ATC position.
                if (facility > 0)
                {
                    AddPosition(byAirport, obj["callsign"]?.GetValue<string>(), obj["frequency"]);
                }
            }
        }
        if (data["atis"] is JsonArray atisList)
        {
            foreach (var a in atisList)
            {
                if (a is not JsonObject obj) continue;
                AddPosition(byAirport, obj["callsign"]?.GetValue<string>(), obj["frequency"], "ATIS");
            }
        }
        return byAirport;
    }

    private static async Task<Dictionary<string, List<JsonObject>>> FetchIvaoAtc()
    {
        var response = await Http.GetAsync(IvaoWhazzupUrl);
        response.EnsureSuccessStatusCode();
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject ?? new JsonObject();

        var byAirport = new Dictionary<string, List<JsonObject>>();
        if (data["clients"] is JsonObject clients && clients["atcs"] is JsonArray atcs)
        {
            foreach (var c in atcs)
            {
                if (c is not JsonObject obj) continue;
                var session = obj["atcSession"] as JsonObject ?? new JsonObject();
                JsonNode? frequency = session["frequency"];
                if (frequency is JsonValue fv && fv.TryGetValue<double>(out var freqNum))
                {
                    frequency = freqNum.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                }
                var positionOverride = session["position"] is JsonValue pv && pv.TryGetValue<string>(out var pos) ? pos : null;
                AddPosition(byAirport, obj["callsign"]?.GetValue<string>(), frequency, positionOverride);
            }
        }
        return byAirport;
    }

    /// <summary>
    /// network: "vatsim" | "ivao" | anything else (including "off") -> no
    /// fetch, empty dict. Best-effort: any failure (offline, unreachable,
    /// response shape changed) returns an empty dict silently - an ATC
    /// overlay is never allowed to block or error out the SimBrief check
    /// it's attached to.
    ///
    /// Returns {icao: [{position, frequency}, ...]}.
    /// </summary>
    public static async Task<Dictionary<string, List<JsonObject>>> FetchOnlineAtc(string? network)
    {
        try
        {
            if (network == "vatsim") return await FetchVatsimAtc();
            if (network == "ivao") return await FetchIvaoAtc();
        }
        catch
        {
            // Best-effort, matches Python's bare except here.
        }
        return new Dictionary<string, List<JsonObject>>();
    }
}
