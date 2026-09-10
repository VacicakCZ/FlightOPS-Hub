using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class AtcNetworkTests
{
    [Fact]
    public void IcaoFromCallsign_ExtractsPrefix()
    {
        Assert.Equal("VHHH", AtcNetwork.IcaoFromCallsign("VHHH_TWR"));
        Assert.Equal("KOGD", AtcNetwork.IcaoFromCallsign("KOGD_ATIS"));
    }

    [Fact]
    public void IcaoFromCallsign_RejectsShortShorthand()
    {
        // VATSIM occasionally uses a shortened local code instead of full
        // ICAO (e.g. "SY_TWR" for Sydney) - must not be mistaken for a
        // real 4-letter ICAO code.
        Assert.Null(AtcNetwork.IcaoFromCallsign("SY_TWR"));
    }

    [Fact]
    public void IcaoFromCallsign_RejectsPilotStyleCallsignWithNoUnderscore()
    {
        Assert.Null(AtcNetwork.IcaoFromCallsign("DLH456A"));
    }

    [Fact]
    public void IcaoFromCallsign_HandlesNullAndEmpty()
    {
        Assert.Null(AtcNetwork.IcaoFromCallsign(null));
        Assert.Null(AtcNetwork.IcaoFromCallsign(""));
    }

    [Fact]
    public void PositionFromCallsign_ExtractsSuffix()
    {
        Assert.Equal("TWR", AtcNetwork.PositionFromCallsign("VHHH_TWR"));
        Assert.Equal("ATIS", AtcNetwork.PositionFromCallsign("KOGD_ATIS"));
    }

    [Fact]
    public void PositionFromCallsign_FallsBackToAtcWhenNoSuffix()
    {
        Assert.Equal("TWR_APP", AtcNetwork.PositionFromCallsign("SY_TWR_APP"));
        Assert.Equal("ATC", AtcNetwork.PositionFromCallsign("NOFACILITY"));
    }

    [Fact]
    public void AddPosition_GroupsByAirport()
    {
        var byAirport = new Dictionary<string, List<JsonObject>>();
        AtcNetwork.AddPosition(byAirport, "VHHH_TWR", "118.200");
        AtcNetwork.AddPosition(byAirport, "VHHH_GND", "121.900");
        AtcNetwork.AddPosition(byAirport, "KOGD_ATIS", "125.550", "ATIS");

        Assert.Equal(2, byAirport["VHHH"].Count);
        Assert.Equal("TWR", byAirport["VHHH"][0]["position"]!.GetValue<string>());
        Assert.Equal("GND", byAirport["VHHH"][1]["position"]!.GetValue<string>());
        Assert.Single(byAirport["KOGD"]);
        Assert.Equal("ATIS", byAirport["KOGD"][0]["position"]!.GetValue<string>());
    }

    [Fact]
    public void AddPosition_IgnoresUnparseableCallsign()
    {
        var byAirport = new Dictionary<string, List<JsonObject>>();
        AtcNetwork.AddPosition(byAirport, "SY_TWR", "118.200");
        Assert.Empty(byAirport);
    }

    [Fact]
    public async Task FetchOnlineAtc_ReturnsEmptyDictForOff()
    {
        Assert.Empty(await AtcNetwork.FetchOnlineAtc("off"));
        Assert.Empty(await AtcNetwork.FetchOnlineAtc(null));
        Assert.Empty(await AtcNetwork.FetchOnlineAtc("something-else"));
    }
}
