using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class SimbriefClientTests
{
    private static JsonObject Ofp(
        JsonNode? origin = null, JsonNode? destination = null, JsonNode? alternate = null,
        JsonNode? times = null, JsonNode? aircraft = null, JsonNode? parameters = null, JsonNode? navlog = null)
    {
        var obj = new JsonObject
        {
            ["origin"] = origin ?? new JsonObject { ["icao_code"] = "LKPR" },
            ["destination"] = destination ?? new JsonObject { ["icao_code"] = "LZIB" },
            ["alternate"] = alternate ?? new JsonObject { ["icao_code"] = "LZKZ" },
            ["times"] = times ?? new JsonObject { ["est_time_enroute"] = "2745" },
            ["aircraft"] = aircraft ?? new JsonObject { ["name"] = "Citation X" },
            ["params"] = parameters ?? new JsonObject { ["time_generated"] = "1788813731" },
        };
        if (navlog != null) obj["navlog"] = navlog;
        return obj;
    }

    [Fact]
    public void ParseOfp_ParsesLegsAndSummaryFields()
    {
        var result = SimbriefClient.ParseOfp(Ofp());

        Assert.Equal("LKPR", result["origin"]!.GetValue<string>());
        Assert.Equal("LZIB", result["destination"]!.GetValue<string>());
        Assert.Equal("LZKZ", result["alternate"]!.GetValue<string>());
        Assert.Equal(46, result["duration_minutes"]!.GetValue<long>()); // 2745s rounds to 46 min
        Assert.Equal("Citation X", result["aircraft_name"]!.GetValue<string>());
        Assert.Equal(1788813731, result["planned_at"]!.GetValue<long>());
    }

    [Fact]
    public void ParseOfp_MissingAlternateIsNull()
    {
        var result = SimbriefClient.ParseOfp(Ofp(alternate: new JsonObject()));
        Assert.Null(result["alternate"]);
    }

    [Fact]
    public void ParseOfp_MissingSummaryFieldsAreNullNotFatal()
    {
        var result = SimbriefClient.ParseOfp(Ofp(times: new JsonObject(), aircraft: new JsonObject(), parameters: new JsonObject()));
        Assert.Null(result["duration_minutes"]);
        Assert.Null(result["aircraft_name"]);
        Assert.Null(result["planned_at"]);
    }

    [Fact]
    public void ParseOfp_MissingOriginThrows()
    {
        // origin/destination are load-bearing for scenery matching - the
        // caller (FetchLatestOfp) turns this into an error response, not
        // a silently-missing field.
        Assert.Throws<KeyNotFoundException>(() => SimbriefClient.ParseOfp(Ofp(origin: new JsonObject())));
    }

    [Fact]
    public void ParseOfp_GarbageEstTimeEnrouteDoesNotCrash()
    {
        var result = SimbriefClient.ParseOfp(Ofp(times: new JsonObject { ["est_time_enroute"] = "not-a-number" }));
        Assert.Null(result["duration_minutes"]);
    }

    [Fact]
    public void ParseOfp_RoutePointsParsedInOrder()
    {
        var navlog = new JsonObject
        {
            ["fix"] = new JsonArray(
                new JsonObject { ["ident"] = "PR411", ["pos_lat"] = "49.975392", ["pos_long"] = "14.264369" },
                new JsonObject { ["ident"] = "VOZ", ["pos_lat"] = "49.532328", ["pos_long"] = "14.874664" }),
        };
        var result = SimbriefClient.ParseOfp(Ofp(navlog: navlog));
        var points = result["route_points"]!.AsArray();

        Assert.Equal(2, points.Count);
        Assert.Equal(49.975392, points[0]!["lat"]!.GetValue<double>());
        Assert.Equal(14.264369, points[0]!["lon"]!.GetValue<double>());
        Assert.Equal(49.532328, points[1]!["lat"]!.GetValue<double>());
    }

    [Fact]
    public void ParseOfp_RoutePointsEmptyWhenNavlogMissing()
    {
        var result = SimbriefClient.ParseOfp(Ofp());
        Assert.Empty(result["route_points"]!.AsArray());
    }

    [Fact]
    public void ParseOfp_RoutePointsSkipsMalformedFixesWithoutCrashing()
    {
        var navlog = new JsonObject
        {
            ["fix"] = new JsonArray(
                new JsonObject { ["ident"] = "OK", ["pos_lat"] = "49.5", ["pos_long"] = "14.5" },
                new JsonObject { ["ident"] = "BAD", ["pos_lat"] = "not-a-number", ["pos_long"] = "14.5" },
                new JsonObject { ["ident"] = "MISSING" }),
        };
        var result = SimbriefClient.ParseOfp(Ofp(navlog: navlog));
        var points = result["route_points"]!.AsArray();

        Assert.Single(points);
        Assert.Equal(49.5, points[0]!["lat"]!.GetValue<double>());
    }

    [Fact]
    public async Task FetchLatestOfp_EmptyUsernameReturnsError()
    {
        var result = await SimbriefClient.FetchLatestOfp("");
        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Equal("simbrief_no_username", result["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task FetchLatestOfp_NullUsernameReturnsError()
    {
        var result = await SimbriefClient.FetchLatestOfp(null);
        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Equal("simbrief_no_username", result["error"]!.GetValue<string>());
    }
}
