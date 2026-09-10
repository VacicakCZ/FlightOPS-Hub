using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class AircraftOverridesTests
{
    private static JsonObject Aircraft(string folderName, string developer = "Dev", bool suspectedLivery = false) => new()
    {
        ["folder_name"] = folderName,
        ["display_name"] = folderName,
        ["developer"] = developer,
        ["enabled"] = true,
        ["parent_aircraft"] = null,
        ["suspected_livery"] = suspectedLivery,
    };

    private static JsonObject Livery(string folderName, string developer = "Dev", string? parentAircraft = null) => new()
    {
        ["folder_name"] = folderName,
        ["display_name"] = folderName,
        ["developer"] = developer,
        ["enabled"] = true,
        ["parent_aircraft"] = parentAircraft,
        ["suspected_livery"] = false,
    };

    [Fact]
    public void TypeOverride_MovesAircraftToLiveries()
    {
        var (aircraft, liveries) = AircraftOverrides.ApplyOverrides(
            new[] { Aircraft("pkg-a") }, Array.Empty<JsonObject>(),
            new Dictionary<string, string> { ["pkg-a"] = "livery" }, new Dictionary<string, string>());

        Assert.Empty(aircraft);
        Assert.Equal(new[] { "pkg-a" }, liveries.Select(r => r["folder_name"]!.GetValue<string>()));
    }

    [Fact]
    public void TypeOverride_MovesLiveryToAircraft()
    {
        var (aircraft, liveries) = AircraftOverrides.ApplyOverrides(
            Array.Empty<JsonObject>(), new[] { Livery("pkg-b") },
            new Dictionary<string, string> { ["pkg-b"] = "aircraft" }, new Dictionary<string, string>());

        Assert.Equal(new[] { "pkg-b" }, aircraft.Select(r => r["folder_name"]!.GetValue<string>()));
        Assert.Empty(liveries);
        Assert.False(aircraft[0].ContainsKey("parent_aircraft"));
    }

    [Fact]
    public void ParentOverride_ForcesASpecificParent()
    {
        var (_, liveries) = AircraftOverrides.ApplyOverrides(
            new[] { Aircraft("plane-a"), Aircraft("plane-b") },
            new[] { Livery("liv-a") },
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["liv-a"] = "plane-b" });

        Assert.Equal("plane-b", liveries[0]["parent_aircraft"]!.GetValue<string>());
    }

    [Fact]
    public void ParentOverride_OfEmptyStringForcesUnassigned()
    {
        var (_, liveries) = AircraftOverrides.ApplyOverrides(
            new[] { Aircraft("plane-a") },
            new[] { Livery("liv-a", parentAircraft: "plane-a") },
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["liv-a"] = "" });

        Assert.Null(liveries[0]["parent_aircraft"]);
    }

    [Fact]
    public void ParentPointingAtFolderNoLongerAircraft_FallsBackToUnassigned()
    {
        // e.g. the parent got type-overridden to livery itself, or was
        // removed - never point at something that isn't in the final
        // aircraft list.
        var (_, liveries) = AircraftOverrides.ApplyOverrides(
            new[] { Aircraft("plane-a") },
            new[] { Livery("liv-a", parentAircraft: "plane-a") },
            new Dictionary<string, string> { ["plane-a"] = "livery" },
            new Dictionary<string, string>());

        Assert.Null(liveries[0]["parent_aircraft"]);
    }

    [Fact]
    public void SuspectedLiveryFlag_SurvivesUntouchedByDefault()
    {
        var (aircraft, _) = AircraftOverrides.ApplyOverrides(
            new[] { Aircraft("pkg-a", suspectedLivery: true) }, Array.Empty<JsonObject>(),
            new Dictionary<string, string>(), new Dictionary<string, string>());

        Assert.True(aircraft[0]["suspected_livery"]!.GetValue<bool>());
    }

    [Fact]
    public void SuspectedLiveryFlag_SuppressedOnceTypeOverridden()
    {
        var (aircraft, _) = AircraftOverrides.ApplyOverrides(
            new[] { Aircraft("pkg-a", suspectedLivery: true) }, Array.Empty<JsonObject>(),
            new Dictionary<string, string> { ["pkg-a"] = "aircraft" }, new Dictionary<string, string>());

        Assert.False(aircraft[0]["suspected_livery"]!.GetValue<bool>());
    }

    [Fact]
    public void SuspectedLiveryFlag_SuppressedOnceDismissed()
    {
        var (aircraft, _) = AircraftOverrides.ApplyOverrides(
            new[] { Aircraft("pkg-a", suspectedLivery: true) }, Array.Empty<JsonObject>(),
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            dismissedSuggestions: new HashSet<string> { "pkg-a" });

        Assert.False(aircraft[0]["suspected_livery"]!.GetValue<bool>());
    }

    [Fact]
    public void SuspectedLiveryFlag_OnlySuppressedForMatchingFolder()
    {
        var (aircraft, _) = AircraftOverrides.ApplyOverrides(
            new[] { Aircraft("pkg-a", suspectedLivery: true), Aircraft("pkg-b", suspectedLivery: true) },
            Array.Empty<JsonObject>(),
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            dismissedSuggestions: new HashSet<string> { "pkg-a" });

        var byFolder = aircraft.ToDictionary(r => r["folder_name"]!.GetValue<string>());
        Assert.False(byFolder["pkg-a"]["suspected_livery"]!.GetValue<bool>());
        Assert.True(byFolder["pkg-b"]["suspected_livery"]!.GetValue<bool>());
    }
}
