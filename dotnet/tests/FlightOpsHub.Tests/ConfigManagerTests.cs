using System.Text;
using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class ConfigManagerTests
{
    // --- theme migration ---

    [Fact]
    public void CanonicalTheme_PassesThroughKnownValues()
    {
        Assert.Equal("dark", ConfigManager.CanonicalTheme("dark"));
        Assert.Equal("light", ConfigManager.CanonicalTheme("light"));
        Assert.Equal("system", ConfigManager.CanonicalTheme("system"));
    }

    [Fact]
    public void CanonicalTheme_MigratesLegacyLocalizedLabels()
    {
        Assert.Equal("dark", ConfigManager.CanonicalTheme("Tmavý"));
        Assert.Equal("dark", ConfigManager.CanonicalTheme("Dunkel"));
        Assert.Equal("light", ConfigManager.CanonicalTheme("Světlý"));
        Assert.Equal("light", ConfigManager.CanonicalTheme("Claro"));
    }

    [Fact]
    public void CanonicalTheme_FallsBackToSystemForUnknownValues()
    {
        Assert.Equal("system", ConfigManager.CanonicalTheme("garbage"));
        Assert.Equal("system", ConfigManager.CanonicalTheme(null));
    }

    // --- window geometry migration ---

    [Fact]
    public void MigrateGeometry_ParsesLegacyTkGeometryString()
    {
        var data = new JsonObject { ["_window_geometry"] = "500x860+1174+220" };
        ConfigManager.MigrateGeometry(data);

        Assert.Equal(500, data["_window_width"]!.GetValue<int>());
        Assert.Equal(860, data["_window_height"]!.GetValue<int>());
        Assert.Equal(1174, data["_window_x"]!.GetValue<int>());
        Assert.Equal(220, data["_window_y"]!.GetValue<int>());
        Assert.False(data.ContainsKey("_window_geometry"));
    }

    [Fact]
    public void MigrateGeometry_HandlesSizeOnlyString()
    {
        var data = new JsonObject { ["_window_geometry"] = "500x860" };
        ConfigManager.MigrateGeometry(data);

        Assert.Equal(500, data["_window_width"]!.GetValue<int>());
        Assert.Equal(860, data["_window_height"]!.GetValue<int>());
        Assert.False(data.ContainsKey("_window_x"));
    }

    [Fact]
    public void MigrateGeometry_DefaultsWhenMissing()
    {
        var data = new JsonObject();
        ConfigManager.MigrateGeometry(data);

        Assert.Equal(966, data["_window_width"]!.GetValue<int>());
        Assert.Equal(805, data["_window_height"]!.GetValue<int>());
    }

    [Fact]
    public void ClampWindowPosition_DropsBogusCoordinates()
    {
        var data = new JsonObject { ["_window_x"] = -50000, ["_window_y"] = 200 };
        ConfigManager.ClampWindowPosition(data);

        Assert.False(data.ContainsKey("_window_x"));
        Assert.False(data.ContainsKey("_window_y"));
    }

    [Fact]
    public void ClampWindowPosition_KeepsSaneCoordinates()
    {
        var data = new JsonObject { ["_window_x"] = 100, ["_window_y"] = 200 };
        ConfigManager.ClampWindowPosition(data);

        Assert.Equal(100, data["_window_x"]!.GetValue<int>());
        Assert.Equal(200, data["_window_y"]!.GetValue<int>());
    }

    // --- stale "translated default" last-profile sentinel self-heal ---

    [Fact]
    public void SanitizeLastProfile_ClearsUnknownValue()
    {
        var data = new JsonObject
        {
            ["_profiles"] = new JsonObject { ["Real Profile"] = new JsonArray() },
            ["_last_profile"] = "Vychozi",
        };
        ConfigManager.SanitizeLastProfile(data, "flight");

        Assert.True(data.ContainsKey("_last_profile"));
        Assert.Null(data["_last_profile"]);
    }

    [Fact]
    public void SanitizeLastProfile_KeepsKnownValue()
    {
        var data = new JsonObject
        {
            ["_profiles"] = new JsonObject { ["Real Profile"] = new JsonArray() },
            ["_last_profile"] = "Real Profile",
        };
        ConfigManager.SanitizeLastProfile(data, "flight");

        Assert.Equal("Real Profile", data["_last_profile"]!.GetValue<string>());
    }

    // --- full migrate pipeline ---

    [Fact]
    public void MigrateConfig_SetsVersionAndIsIdempotent()
    {
        var data = new JsonObject { ["_window_geometry"] = "500x860+10+20", ["_theme"] = "Tmavý" };
        var once = ConfigManager.MigrateConfig(data);

        Assert.Equal(ConfigManager.CurrentConfigVersion, once["_config_version"]!.GetValue<int>());
        Assert.Equal("dark", once["_theme"]!.GetValue<string>());

        var twice = ConfigManager.MigrateConfig((JsonObject)once.DeepClone());
        Assert.Equal(once.ToJsonString(), twice.ToJsonString());
    }

    // --- load/save round trip (always a tmp_path-style temp file, never the real config) ---

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_{Guid.NewGuid():N}.json");
        try
        {
            var toSave = new JsonObject { ["_language"] = "CZ", ["_theme"] = "dark" };
            ConfigManager.SaveConfig(path, toSave);
            var loaded = ConfigManager.LoadConfig(path);

            Assert.Equal("CZ", loaded["_language"]!.GetValue<string>());
            Assert.Equal("dark", loaded["_theme"]!.GetValue<string>());
            Assert.Equal(ConfigManager.CurrentConfigVersion, loaded["_config_version"]!.GetValue<int>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadConfig_MissingFileReturnsMigratedDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_missing_{Guid.NewGuid():N}.json");
        var loaded = ConfigManager.LoadConfig(path);

        Assert.Equal(ConfigManager.CurrentConfigVersion, loaded["_config_version"]!.GetValue<int>());
    }

    [Fact]
    public void LoadConfig_CorruptJsonFallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_corrupt_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{not valid json");
        try
        {
            var loaded = ConfigManager.LoadConfig(path);
            Assert.Equal(ConfigManager.CurrentConfigVersion, loaded["_config_version"]!.GetValue<int>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveConfig_LeavesNoTempFileBehind()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_notemp_{Guid.NewGuid():N}.json");
        try
        {
            ConfigManager.SaveConfig(path, new JsonObject { ["_language"] = "CZ" });

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadConfig_StripsBom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_bom_{Guid.NewGuid():N}.json");
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var json = Encoding.UTF8.GetBytes("{\"_language\":\"EN\"}");
        File.WriteAllBytes(path, bom.Concat(json).ToArray());
        try
        {
            var loaded = ConfigManager.LoadConfig(path);
            Assert.Equal("EN", loaded["_language"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
