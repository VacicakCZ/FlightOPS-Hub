using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class ScenerySimbriefMatchTests
{
    [Fact]
    public void IcaoFromDisplayName_ExtractsBracketedPrefix()
    {
        Assert.Equal("LKPR", ScenerySimbriefMatch.IcaoFromDisplayName("[LKPR] Prague Airport"));
    }

    [Fact]
    public void IcaoFromDisplayName_NullWhenNoPrefix()
    {
        Assert.Null(ScenerySimbriefMatch.IcaoFromDisplayName("Prague Airport"));
        Assert.Null(ScenerySimbriefMatch.IcaoFromDisplayName(null));
    }

    private static JsonObject Record(string folderName, string displayName, bool enabled) => new()
    {
        ["folder_name"] = folderName,
        ["display_name"] = displayName,
        ["enabled"] = enabled,
    };

    [Fact]
    public void MatchFlightPlan_ReportsEnabledDisabledAndNotInstalled()
    {
        var records = new List<JsonObject>
        {
            Record("fsdg-lkpr", "[LKPR] Prague", enabled: true),
            Record("someone-lzib", "[LZIB] Bratislava", enabled: false),
        };
        var legs = new (string, string?)[] { ("origin", "LKPR"), ("destination", "LZIB"), ("alternate", "LZKZ") };

        var results = ScenerySimbriefMatch.MatchFlightPlan(records, legs);

        Assert.Equal(3, results.Count);
        Assert.Equal("enabled", results[0]["status"]!.GetValue<string>());
        Assert.Equal("disabled", results[1]["status"]!.GetValue<string>());
        Assert.Equal("not_installed", results[2]["status"]!.GetValue<string>());
        Assert.Null(results[2]["folder_name"]);
    }

    [Fact]
    public void MatchFlightPlan_SkipsLegsWithNoIcao()
    {
        var legs = new (string, string?)[] { ("alternate", null) };
        var results = ScenerySimbriefMatch.MatchFlightPlan(new List<JsonObject>(), legs);
        Assert.Empty(results);
    }
}
