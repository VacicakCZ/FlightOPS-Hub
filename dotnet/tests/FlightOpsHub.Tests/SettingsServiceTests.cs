using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class SettingsServiceTests
{
    private static string TempConfigPath() =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_settingssvc_{Guid.NewGuid():N}.json");

    [Fact]
    public void SetCommunityPath_PersistsAndRemembersUnderCurrentSimKey()
    {
        var path = TempConfigPath();
        try
        {
            var config = new ConfigService(path);
            var settings = new SettingsService(config);
            var community = Path.Combine(Path.GetTempPath(), "Community");

            var snapshot = settings.SetCommunityPath(community);

            Assert.Equal(Path.GetFullPath(community), snapshot["_community_path"]!.GetValue<string>());
            var remembered = (JsonObject)snapshot["_community_paths_by_sim"]!;
            Assert.Equal(Path.GetFullPath(community), remembered["MSFS 2024|Steam"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SwitchingSimVersion_RestoresPreviouslyRememberedPathForThatCombo()
    {
        var path = TempConfigPath();
        try
        {
            var config = new ConfigService(path);
            var settings = new SettingsService(config);

            // Default combo is "MSFS 2024|Steam" - set its path, then switch
            // to 2020 (no remembered path yet -> should clear), then switch
            // back to 2024 (should restore what was set for it).
            var community2024 = Path.Combine(Path.GetTempPath(), "Community2024");
            settings.SetCommunityPath(community2024);

            var after2020 = settings.SetSimVersion("MSFS 2020");
            Assert.Equal("", after2020["_community_path"]!.GetValue<string>());

            var back2024 = settings.SetSimVersion("MSFS 2024");
            Assert.Equal(Path.GetFullPath(community2024), back2024["_community_path"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SetDisabledPath_RejectsPathNestedInsideCommunity()
    {
        var path = TempConfigPath();
        try
        {
            var config = new ConfigService(path);
            var settings = new SettingsService(config);
            var community = Path.Combine(Path.GetTempPath(), "Community");
            settings.SetCommunityPath(community);

            var result = settings.SetDisabledPath(Path.Combine(community, "disabled"));

            Assert.False(result["ok"]!.GetValue<bool>());
            Assert.Equal("nested", result["error"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SetDisabledPath_AcceptsAndPersistsValidPath()
    {
        var path = TempConfigPath();
        try
        {
            var config = new ConfigService(path);
            var settings = new SettingsService(config);
            settings.SetCommunityPath(Path.Combine(Path.GetTempPath(), "Community"));

            var disabled = Path.Combine(Path.GetTempPath(), "disabled-holding");
            var result = settings.SetDisabledPath(disabled);

            Assert.True(result["ok"]!.GetValue<bool>());
            Assert.Equal(Path.GetFullPath(disabled), config.Snapshot()["_disabled_holding_path"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResetDisabledPath_RemovesTheOverride()
    {
        var path = TempConfigPath();
        try
        {
            var config = new ConfigService(path);
            var settings = new SettingsService(config);
            settings.SetCommunityPath(Path.Combine(Path.GetTempPath(), "Community"));
            settings.SetDisabledPath(Path.Combine(Path.GetTempPath(), "disabled-holding"));

            settings.ResetDisabledPath();

            Assert.False(config.Snapshot().ContainsKey("_disabled_holding_path"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AiracStatus_ReturnsCurrentAndInstalledFields()
    {
        var path = TempConfigPath();
        try
        {
            var settings = new SettingsService(new ConfigService(path));
            var result = settings.AiracStatus();

            Assert.NotNull(result["current"]);
            Assert.NotNull(result["installed"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DiskSpaceStatus_EmptyWhenNoCommunityPathSet()
    {
        var path = TempConfigPath();
        try
        {
            var settings = new SettingsService(new ConfigService(path));
            var result = settings.DiskSpaceStatus();
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
