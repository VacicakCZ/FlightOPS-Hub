using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class ExeXmlServiceTests
{
    private const string SampleXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<SimBase.Document>" +
        "<Launch.Addon><Name>Test Addon</Name><Path>C:\\test.exe</Path><Disabled>False</Disabled></Launch.Addon>" +
        "<Launch.Addon><Name>Second Addon</Name><Path>C:\\second.exe</Path><Disabled>True</Disabled></Launch.Addon>" +
        "</SimBase.Document>";

    private static (string ConfigPath, string XmlPath) SetUpFixture(string label)
    {
        // xmlPath gets its own unique directory, not just a unique filename
        // in the shared temp root - BackupPathFor() names the backup from
        // the DIRECTORY only ("exe_FlightOpsHub_backup.xml", always that
        // one name), so tests sharing a directory race on the same backup
        // file under xUnit's default parallel test execution.
        var dir = Path.Combine(Path.GetTempPath(), $"flightops_test_exesvc_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var configPath = Path.Combine(dir, "msfs_launcher_config.json");
        var xmlPath = Path.Combine(dir, "exe.xml");
        File.WriteAllText(xmlPath, SampleXml);
        return (configPath, xmlPath);
    }

    private static void CleanUp(string xmlPath) =>
        Directory.Delete(Path.GetDirectoryName(xmlPath)!, recursive: true);

    [Fact]
    public void ListAddons_UsesOverridePathAndReturnsAddons()
    {
        var (configPath, xmlPath) = SetUpFixture("list");
        try
        {
            var config = new ConfigService(configPath);
            var exeXml = new ExeXmlService(config);
            exeXml.SetExeXmlPath(xmlPath);

            var result = exeXml.ListAddons();

            Assert.True(result["exists"]!.GetValue<bool>());
            Assert.Equal(2, ((JsonArray)result["addons"]!).Count);
            Assert.Equal(xmlPath, result["path"]!.GetValue<string>());
        }
        finally
        {
            CleanUp(xmlPath);
        }
    }

    [Fact]
    public void ListAddons_MissingFileReportsNotExists()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"flightops_test_exesvc_missing_{Guid.NewGuid():N}.json");
        var missingXmlPath = Path.Combine(Path.GetTempPath(), $"flightops_test_exesvc_missing_{Guid.NewGuid():N}.xml");
        try
        {
            var exeXml = new ExeXmlService(new ConfigService(configPath));
            exeXml.SetExeXmlPath(missingXmlPath);

            var result = exeXml.ListAddons();

            Assert.False(result["exists"]!.GetValue<bool>());
            Assert.Empty((JsonArray)result["addons"]!);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Toggle_DisablesAddonInXml()
    {
        var (configPath, xmlPath) = SetUpFixture("toggle");
        try
        {
            var exeXml = new ExeXmlService(new ConfigService(configPath));
            exeXml.SetExeXmlPath(xmlPath);

            exeXml.Toggle("C:\\test.exe", false);

            var after = exeXml.ListAddons();
            var addons = (JsonArray)after["addons"]!;
            var toggled = addons.Cast<JsonObject>().Single(a => a["unique_key"]!.GetValue<string>() == "C:\\test.exe");
            Assert.False(toggled["enabled"]!.GetValue<bool>());
        }
        finally
        {
            CleanUp(xmlPath);
        }
    }

    [Fact]
    public void Rename_SetsAndClearsCustomDisplayName()
    {
        var (configPath, xmlPath) = SetUpFixture("rename");
        try
        {
            var exeXml = new ExeXmlService(new ConfigService(configPath));
            exeXml.SetExeXmlPath(xmlPath);

            exeXml.Rename("C:\\test.exe", "Test Addon", "Renamed");
            var renamed = (JsonArray)exeXml.ListAddons()["addons"]!;
            Assert.Equal("Renamed", renamed.Cast<JsonObject>().Single(a => a["unique_key"]!.GetValue<string>() == "C:\\test.exe")["display_name"]!.GetValue<string>());

            // Renaming back to the original name clears the override.
            exeXml.Rename("C:\\test.exe", "Test Addon", "Test Addon");
            var reverted = (JsonArray)exeXml.ListAddons()["addons"]!;
            Assert.Equal("Test Addon", reverted.Cast<JsonObject>().Single(a => a["unique_key"]!.GetValue<string>() == "C:\\test.exe")["display_name"]!.GetValue<string>());
        }
        finally
        {
            CleanUp(xmlPath);
        }
    }

    [Fact]
    public void ApplyProfile_EnablesOnlyMembers()
    {
        var (configPath, xmlPath) = SetUpFixture("applyprofile");
        try
        {
            var config = new ConfigService(configPath);
            var exeXml = new ExeXmlService(config);
            exeXml.SetExeXmlPath(xmlPath);

            config.Update(new JsonObject
            {
                ["_exe_profiles"] = new JsonObject { ["OnlyFirst"] = new JsonArray("C:\\test.exe") },
            });

            var result = exeXml.ApplyProfile("OnlyFirst");
            Assert.True(result["ok"]!.GetValue<bool>());

            var addons = (JsonArray)exeXml.ListAddons()["addons"]!;
            var first = addons.Cast<JsonObject>().Single(a => a["unique_key"]!.GetValue<string>() == "C:\\test.exe");
            var second = addons.Cast<JsonObject>().Single(a => a["unique_key"]!.GetValue<string>() == "C:\\second.exe");
            Assert.True(first["enabled"]!.GetValue<bool>());
            Assert.False(second["enabled"]!.GetValue<bool>());
        }
        finally
        {
            CleanUp(xmlPath);
        }
    }

    [Fact]
    public void RestoreBackup_ReturnsFalseWhenNoEditHappenedYet()
    {
        var (configPath, xmlPath) = SetUpFixture("restore");
        try
        {
            var exeXml = new ExeXmlService(new ConfigService(configPath));
            exeXml.SetExeXmlPath(xmlPath);

            var result = exeXml.RestoreBackup();
            Assert.False(result["ok"]!.GetValue<bool>());
        }
        finally
        {
            CleanUp(xmlPath);
        }
    }
}
