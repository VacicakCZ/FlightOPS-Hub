using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

/// <summary>
/// Covers SceneryService.SetGsxPath/ResetGsxPath/GsxPath - found missing
/// entirely from the API bridge during the .NET rewrite's Phase 7 parity
/// pass (frontend/js/api.js called settings_set_gsx_path/
/// settings_reset_gsx_path, but nothing implemented or registered them).
/// OpenGsxSearch/OpenGsxFolder aren't covered here since they call
/// Process.Start for real - same as Python's own api.py, which has no
/// test_api.py either; live-verified instead.
/// </summary>
public class SceneryServiceGsxSettingsTests
{
    private static SceneryService MakeService(string configPath)
    {
        var config = new ConfigService(configPath);
        return new SceneryService(
            config,
            Path.Combine(Path.GetTempPath(), "flightops_test_nonexistent_developers.json"),
            Path.Combine(Path.GetTempPath(), "flightops_test_nonexistent_airports.json"),
            (_, _) => { });
    }

    [Fact]
    public void GsxPath_DefaultsWhenNoOverrideSet()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"flightops_test_gsxsettings_default_{Guid.NewGuid():N}.json");
        try
        {
            var service = MakeService(configPath);
            Assert.Equal(GsxProfiles.DefaultGsxPath(), service.GsxPath());
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void SetGsxPath_OverridesGsxPathUntilReset()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"flightops_test_gsxsettings_override_{Guid.NewGuid():N}.json");
        var customDir = Path.Combine(Path.GetTempPath(), $"flightops_test_gsxsettings_custom_{Guid.NewGuid():N}");
        try
        {
            var service = MakeService(configPath);

            var result = service.SetGsxPath(customDir);
            Assert.Equal(Path.GetFullPath(customDir), result["_gsx_profiles_path"]!.GetValue<string>());
            Assert.Equal(Path.GetFullPath(customDir), service.GsxPath());

            var afterReset = service.ResetGsxPath();
            Assert.False(afterReset.ContainsKey("_gsx_profiles_path"));
            Assert.Equal(GsxProfiles.DefaultGsxPath(), service.GsxPath());
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void SetGsxPath_IsReflectedInConfigSnapshotEffectiveField()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"flightops_test_gsxsettings_effective_{Guid.NewGuid():N}.json");
        var customDir = Path.Combine(Path.GetTempPath(), $"flightops_test_gsxsettings_effdir_{Guid.NewGuid():N}");
        try
        {
            var config = new ConfigService(configPath);
            var service = new SceneryService(
                config,
                Path.Combine(Path.GetTempPath(), "flightops_test_nonexistent_developers.json"),
                Path.Combine(Path.GetTempPath(), "flightops_test_nonexistent_airports.json"),
                (_, _) => { });
            config.RegisterEffectiveField("_gsx_profiles_path_effective", service.GsxPath);

            var beforeSnapshot = config.Snapshot();
            Assert.Equal(GsxProfiles.DefaultGsxPath(), beforeSnapshot["_gsx_profiles_path_effective"]!.GetValue<string>());

            service.SetGsxPath(customDir);
            var afterSnapshot = config.Snapshot();
            Assert.Equal(Path.GetFullPath(customDir), afterSnapshot["_gsx_profiles_path_effective"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(configPath);
        }
    }
}
