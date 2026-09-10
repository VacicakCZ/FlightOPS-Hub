using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class AirportsDataTests
{
    private static string TempFile(JsonObject content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_airports_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content.ToJsonString());
        return path;
    }

    [Fact]
    public void Lookup_ReturnsAirportByIcao()
    {
        var path = TempFile(new JsonObject
        {
            ["LKPR"] = new JsonObject { ["lat"] = 50.1008, ["lon"] = 14.26, ["name"] = "Prague/Vaclav Havel" },
        });
        try
        {
            var airports = new AirportsData(path);
            var result = airports.Lookup("lkpr"); // lowercase input, matches Python's .upper() normalization
            Assert.NotNull(result);
            Assert.Equal("Prague/Vaclav Havel", result!["name"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Lookup_ReturnsNullForUnknownIcao()
    {
        var path = TempFile(new JsonObject());
        try
        {
            var airports = new AirportsData(path);
            Assert.Null(airports.Lookup("ZZZZ"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Lookup_ReturnsNullForEmptyOrNullIcao()
    {
        var path = TempFile(new JsonObject());
        try
        {
            var airports = new AirportsData(path);
            Assert.Null(airports.Lookup(""));
            Assert.Null(airports.Lookup(null));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Lookup_MissingFileReturnsNullGracefully()
    {
        var airports = new AirportsData(Path.Combine(Path.GetTempPath(), $"flightops_test_airports_missing_{Guid.NewGuid():N}.json"));
        Assert.Null(airports.Lookup("LKPR"));
    }
}
