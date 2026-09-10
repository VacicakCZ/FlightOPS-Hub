using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class SceneryMapBuilderTests
{
    private static AirportsData MakeAirports()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_scenerymap_{Guid.NewGuid():N}.json");
        var data = new JsonObject
        {
            ["LKPR"] = new JsonObject { ["lat"] = 50.1, ["lon"] = 14.26, ["name"] = "Prague/Vaclav Havel" },
        };
        File.WriteAllText(path, data.ToJsonString());
        return new AirportsData(path);
    }

    private static JsonObject Record(string folderName, string displayName, bool enabled) => new()
    {
        ["folder_name"] = folderName,
        ["display_name"] = displayName,
        ["enabled"] = enabled,
    };

    [Fact]
    public void BuildMarkers_OmitsRecordsWithoutResolvableAirport()
    {
        var builder = new SceneryMapBuilder(MakeAirports());
        var records = new List<JsonObject>
        {
            Record("fsdg-lkpr", "[LKPR] Prague", true),
            Record("some-poi-pack", "Landmark Pack With No ICAO", true),
        };

        var markers = builder.BuildMarkers(records);

        Assert.Single(markers);
        Assert.Equal("LKPR", markers[0]["icao"]!.GetValue<string>());
        Assert.Equal(50.1, markers[0]["lat"]!.GetValue<double>());
        Assert.Equal("Prague/Vaclav Havel", markers[0]["airport_name"]!.GetValue<string>());
    }

    [Fact]
    public void AttachAirportNames_SetsNullForUnresolvableRecords()
    {
        var builder = new SceneryMapBuilder(MakeAirports());
        var records = new List<JsonObject> { Record("some-poi-pack", "Landmark Pack", true) };

        builder.AttachAirportNames(records);

        Assert.True(records[0].ContainsKey("airport_name"));
        Assert.Null(records[0]["airport_name"]);
    }

    [Fact]
    public void AttachAirportNames_SetsNameForResolvableRecord()
    {
        var builder = new SceneryMapBuilder(MakeAirports());
        var records = new List<JsonObject> { Record("fsdg-lkpr", "[LKPR] Prague", true) };

        builder.AttachAirportNames(records);

        Assert.Equal("Prague/Vaclav Havel", records[0]["airport_name"]!.GetValue<string>());
    }
}
