using System.Text.Json.Nodes;
using System.Xml.Linq;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class ExeXmlManagerTests
{
    private const string SampleXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<SimBase.Document>" +
        "<Launch.Addon><Name>Test Addon</Name><Path>C:\\test.exe</Path><Disabled>False</Disabled></Launch.Addon>" +
        "</SimBase.Document>";

    // A dedicated directory per test, not just a uniquely-named file in the
    // shared temp root - BackupPathFor() derives the backup's name from the
    // DIRECTORY only ("exe_FlightOpsHub_backup.xml", always that one name),
    // so tests sharing a directory race on the same backup file under
    // xUnit's default parallel test execution.
    private static string TempPath(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"flightops_test_exexml_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "exe.xml");
    }

    private static void CleanUp(string xmlPath) =>
        Directory.Delete(Path.GetDirectoryName(xmlPath)!, recursive: true);

    [Fact]
    public void SetAddonsEnabled_TogglesDisabledNode()
    {
        var path = TempPath("toggle");
        try
        {
            File.WriteAllText(path, SampleXml);
            var changed = ExeXmlManager.SetAddonsEnabled(path, new Dictionary<string, bool> { ["C:\\test.exe"] = false });

            Assert.True(changed);
            var root = XDocument.Load(path).Root!;
            Assert.Equal("True", root.Element("Launch.Addon")!.Element("Disabled")!.Value);
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void SetAddonsEnabled_LeavesNoTempFileBehind()
    {
        var path = TempPath("notemp");
        try
        {
            File.WriteAllText(path, SampleXml);
            ExeXmlManager.SetAddonsEnabled(path, new Dictionary<string, bool> { ["C:\\test.exe"] = false });

            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void BackupPathFor_SitsNextToTheXmlFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "somewhere", "exe.xml");
        Assert.Equal(Path.Combine(Path.GetTempPath(), "somewhere", "exe_FlightOpsHub_backup.xml"), ExeXmlManager.BackupPathFor(path));
    }

    [Fact]
    public void HasBackup_FalseWhenMissing()
    {
        var path = TempPath("nobackup");
        try
        {
            Assert.False(ExeXmlManager.HasBackup(path));
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void SetAddonsEnabled_CreatesBackupOnlyOnFirstEdit()
    {
        var path = TempPath("backuponce");
        try
        {
            var backupPath = ExeXmlManager.BackupPathFor(path);
            File.WriteAllText(path, SampleXml);

            ExeXmlManager.SetAddonsEnabled(path, new Dictionary<string, bool> { ["C:\\test.exe"] = false });
            Assert.True(ExeXmlManager.HasBackup(path));
            var firstBackupBytes = File.ReadAllBytes(backupPath);

            // A second edit must NOT overwrite the backup with the by-then-modified file.
            ExeXmlManager.SetAddonsEnabled(path, new Dictionary<string, bool> { ["C:\\test.exe"] = true });
            Assert.Equal(firstBackupBytes, File.ReadAllBytes(backupPath));
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void RestoreFromBackup_ReturnsFalseWhenNoBackupExists()
    {
        var path = TempPath("norestore");
        try
        {
            Assert.False(ExeXmlManager.RestoreFromBackup(path));
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void RestoreFromBackup_OverwritesCurrentFileWithBackup()
    {
        var path = TempPath("restore");
        try
        {
            File.WriteAllText(path, SampleXml);
            ExeXmlManager.SetAddonsEnabled(path, new Dictionary<string, bool> { ["C:\\test.exe"] = false }); // creates the backup
            File.WriteAllText(path, "<modified-by-user-or-app/>");

            var restored = ExeXmlManager.RestoreFromBackup(path);

            Assert.True(restored);
            Assert.Contains("Test Addon", File.ReadAllText(path));
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void ListAddons_ReadsDisplayNameFromCustomNamesOverride()
    {
        var path = TempPath("listaddons");
        try
        {
            File.WriteAllText(path, SampleXml);
            var customNames = new JsonObject { ["C:\\test.exe"] = "My Custom Name" };

            var addons = ExeXmlManager.ListAddons(path, customNames);

            Assert.Single(addons);
            Assert.Equal("Test Addon", addons[0]["original_name"]!.GetValue<string>());
            Assert.Equal("My Custom Name", addons[0]["display_name"]!.GetValue<string>());
            Assert.True(addons[0]["enabled"]!.GetValue<bool>());
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void GetExeXmlPath_UsesCorrectFolderPerVersionAndPlatform()
    {
        var steam2024 = ExeXmlManager.GetExeXmlPath("MSFS 2024", "Steam");
        Assert.Contains("Microsoft Flight Simulator 2024", steam2024);

        var store2020 = ExeXmlManager.GetExeXmlPath("MSFS 2020", "MS Store");
        Assert.Contains("Microsoft.FlightSimulator_8wekyb3d8bbwe", store2020);
    }
}
