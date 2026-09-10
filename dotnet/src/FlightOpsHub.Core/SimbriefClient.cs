using System.Net.Http;
using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/simbrief_client.py - fetches a pilot's last-generated
/// SimBrief flight plan (OFP) via the public, key-less read endpoint.
/// </summary>
public static class SimbriefClient
{
    private const string SimbriefUrl = "https://www.simbrief.com/api/xml.fetcher.php";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient Http = new() { Timeout = Timeout };

    /// <summary>
    /// Pure parsing of an already-decoded SimBrief OFP JSON payload - kept
    /// separate from FetchLatestOfp so it's testable without a network
    /// call. origin/destination are load-bearing for the scenery-matching
    /// feature, so a missing one throws (caller turns that into an error).
    /// duration_minutes/aircraft_name/planned_at are a best-effort summary;
    /// any of those being absent/malformed just comes back null, never throws.
    /// </summary>
    public static JsonObject ParseOfp(JsonObject data)
    {
        var origin = GetRequiredIcao(data, "origin");
        var destination = GetRequiredIcao(data, "destination");
        var alternate = data["alternate"] is JsonObject altObj ? GetString(altObj, "icao_code") : null;

        var times = data["times"] as JsonObject ?? new JsonObject();
        var aircraft = data["aircraft"] as JsonObject ?? new JsonObject();
        var parameters = data["params"] as JsonObject ?? new JsonObject();

        long? durationMinutes = null;
        if (TryGetLong(times["est_time_enroute"], out var enrouteSeconds))
        {
            durationMinutes = (long)Math.Round(enrouteSeconds / 60.0);
        }

        long? plannedAt = TryGetLong(parameters["time_generated"], out var generated) ? generated : null;

        return new JsonObject
        {
            ["origin"] = origin,
            ["destination"] = destination,
            ["alternate"] = string.IsNullOrEmpty(alternate) ? null : alternate,
            ["duration_minutes"] = durationMinutes,
            ["aircraft_name"] = string.IsNullOrEmpty(GetString(aircraft, "name")) ? null : GetString(aircraft, "name"),
            ["planned_at"] = plannedAt,
            ["route_points"] = ParseRoutePoints(data),
            ["airac"] = string.IsNullOrEmpty(GetString(parameters, "airac")) ? null : GetString(parameters, "airac"),
        };
    }

    /// <summary>
    /// The actual planned route (origin -> destination only) as an
    /// ordered list of {lat, lon} - lets the map draw the real route
    /// instead of a straight line. Best-effort: a missing/malformed
    /// navlog just means an empty list, never fatal.
    /// </summary>
    private static JsonArray ParseRoutePoints(JsonObject data)
    {
        var points = new JsonArray();
        if (data["navlog"] is not JsonObject navlog || navlog["fix"] is not JsonArray fixes) return points;

        foreach (var fix in fixes)
        {
            if (fix is not JsonObject fixObj) continue;
            if (TryGetDouble(fixObj["pos_lat"], out var lat) && TryGetDouble(fixObj["pos_long"], out var lon))
            {
                points.Add(new JsonObject { ["lat"] = lat, ["lon"] = lon });
            }
        }
        return points;
    }

    /// <summary>
    /// Returns {ok:true, origin, destination, alternate, duration_minutes,
    /// aircraft_name, planned_at, route_points, airac} or
    /// {ok:false, error}. The error string is a translation key when
    /// recognized, else a raw message.
    /// </summary>
    public static async Task<JsonObject> FetchLatestOfp(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new JsonObject { ["ok"] = false, ["error"] = "simbrief_no_username" };
        }

        HttpResponseMessage response;
        try
        {
            var url = $"{SimbriefUrl}?username={Uri.EscapeDataString(username.Trim())}&json=1";
            response = await Http.GetAsync(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new JsonObject { ["ok"] = false, ["error"] = ex.Message };
        }

        if (!response.IsSuccessStatusCode)
        {
            return new JsonObject { ["ok"] = false, ["error"] = $"HTTP {(int)response.StatusCode}" };
        }

        JsonObject parsed;
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            var data = JsonNode.Parse(body) as JsonObject ?? throw new FormatException("empty response");
            parsed = ParseOfp(data);
        }
        catch (Exception ex)
        {
            return new JsonObject { ["ok"] = false, ["error"] = $"unexpected response ({ex.Message})" };
        }

        parsed["ok"] = true;
        return parsed;
    }

    private static string GetRequiredIcao(JsonObject data, string field)
    {
        if (data[field] is not JsonObject obj || GetString(obj, "icao_code") is not { } icao || icao.Length == 0)
        {
            throw new KeyNotFoundException(field);
        }
        return icao;
    }

    private static string? GetString(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static bool TryGetLong(JsonNode? node, out long result)
    {
        result = 0;
        if (node is not JsonValue value) return false;
        if (value.TryGetValue<long>(out result)) return true;
        if (value.TryGetValue<string>(out var s) && long.TryParse(s, out result)) return true;
        return false;
    }

    private static bool TryGetDouble(JsonNode? node, out double result)
    {
        result = 0;
        if (node is not JsonValue value) return false;
        if (value.TryGetValue<double>(out result)) return true;
        if (value.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out result)) return true;
        return false;
    }
}
