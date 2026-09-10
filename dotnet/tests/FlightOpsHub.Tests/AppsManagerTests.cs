using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class AppsManagerTests
{
    private static string TempPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_apps_{label}_{Guid.NewGuid():N}.json");

    [Fact]
    public void ValidateAppInput_RejectsEmptyNameOrPath()
    {
        Assert.Equal("err_empty", AppsManager.ValidateAppInput("", "C:\\a.exe", "immediate", null));
        Assert.Equal("err_empty", AppsManager.ValidateAppInput("Name", "  ", "immediate", null));
    }

    [Fact]
    public void ValidateAppInput_RequiresPositiveDelayForTimerMode()
    {
        Assert.Equal("err_delay", AppsManager.ValidateAppInput("Name", "C:\\a.exe", "timer", "0"));
        Assert.Equal("err_delay", AppsManager.ValidateAppInput("Name", "C:\\a.exe", "timer", "not a number"));
        Assert.Null(AppsManager.ValidateAppInput("Name", "C:\\a.exe", "timer", "150"));
    }

    [Fact]
    public void ValidateAppInput_ImmediateModeIgnoresDelay()
    {
        Assert.Null(AppsManager.ValidateAppInput("Name", "C:\\a.exe", "immediate", null));
    }

    [Fact]
    public void LoadApps_MigratesLegacyStringEntries()
    {
        var path = TempPath("legacy");
        try
        {
            File.WriteAllText(path, """{"REX Atmos": "C:\\rex.exe", "Other": "C:\\other.exe"}""");
            var apps = AppsManager.LoadApps(path);

            var rex = (JsonObject)apps["REX Atmos"]!;
            Assert.Equal(150, rex["delay"]!.GetValue<int>());
            Assert.Equal("timer", rex["launch_mode"]!.GetValue<string>());

            var other = (JsonObject)apps["Other"]!;
            Assert.Equal(0, other["delay"]!.GetValue<int>());
            Assert.Equal("immediate", other["launch_mode"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadApps_MissingFileCreatesEmptyOne()
    {
        var path = TempPath("missing");
        try
        {
            var apps = AppsManager.LoadApps(path);
            Assert.Empty(apps);
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UpsertApp_AddsNewApp()
    {
        var path = TempPath("upsert_add");
        try
        {
            var apps = new JsonObject();
            var result = AppsManager.UpsertApp(path, apps, "New App", "C:\\new.exe", "immediate", null, false, null);

            var entry = (JsonObject)result["New App"]!;
            Assert.Equal("C:\\new.exe", entry["path"]!.GetValue<string>());
            Assert.Equal(0, entry["delay"]!.GetValue<int>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UpsertApp_RenamePreservesPositionAndData()
    {
        var path = TempPath("upsert_rename");
        try
        {
            var apps = new JsonObject
            {
                ["First"] = new JsonObject { ["path"] = "C:\\1.exe", ["delay"] = 0, ["admin"] = false, ["launch_mode"] = "immediate" },
                ["Second"] = new JsonObject { ["path"] = "C:\\2.exe", ["delay"] = 0, ["admin"] = false, ["launch_mode"] = "immediate" },
            };

            var result = AppsManager.UpsertApp(path, apps, "Renamed", "C:\\1-new.exe", "immediate", null, false, previousName: "First");

            var keys = result.Select(kv => kv.Key).ToList();
            Assert.Equal(new[] { "Renamed", "Second" }, keys);
            Assert.Equal("C:\\1-new.exe", ((JsonObject)result["Renamed"]!)["path"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RemoveApp_RemovesByName()
    {
        var path = TempPath("remove");
        try
        {
            var apps = new JsonObject { ["A"] = new JsonObject(), ["B"] = new JsonObject() };
            var result = AppsManager.RemoveApp(path, apps, "A");

            Assert.False(result.ContainsKey("A"));
            Assert.True(result.ContainsKey("B"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MoveApp_SwapsWithNeighborInDirection()
    {
        var path = TempPath("move");
        try
        {
            var apps = new JsonObject { ["A"] = new JsonObject(), ["B"] = new JsonObject(), ["C"] = new JsonObject() };

            var movedDown = AppsManager.MoveApp(path, apps, "A", "down");
            Assert.Equal(new[] { "B", "A", "C" }, movedDown.Select(kv => kv.Key));

            var movedUp = AppsManager.MoveApp(path, movedDown, "A", "up");
            Assert.Equal(new[] { "A", "B", "C" }, movedUp.Select(kv => kv.Key));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MoveApp_AtBoundaryIsNoOp()
    {
        var path = TempPath("move_boundary");
        try
        {
            var apps = new JsonObject { ["A"] = new JsonObject(), ["B"] = new JsonObject() };
            var result = AppsManager.MoveApp(path, apps, "A", "up");
            Assert.Equal(new[] { "A", "B" }, result.Select(kv => kv.Key));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveApps_LeavesNoTempFileBehind()
    {
        var path = TempPath("notemp");
        try
        {
            AppsManager.SaveApps(path, new JsonObject { ["A"] = new JsonObject() });
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
