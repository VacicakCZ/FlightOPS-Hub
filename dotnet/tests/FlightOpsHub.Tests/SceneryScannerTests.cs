using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class SceneryScannerTests
{
    private static string TempDir(string label) =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_sceneryscan_{label}_{Guid.NewGuid():N}");

    private static void MakeSceneryPackage(string baseDir, string folderName, string title, string contentType = "SCENERY")
    {
        var pkgDir = Path.Combine(baseDir, folderName);
        Directory.CreateDirectory(pkgDir);
        var manifest = new JsonObject { ["content_type"] = contentType, ["title"] = title };
        File.WriteAllText(Path.Combine(pkgDir, "manifest.json"), manifest.ToJsonString());
    }

    [Fact]
    public void ScanSceneryPackages_ReadsEnabledAndDisabledLocations()
    {
        var root = TempDir("enabled_disabled");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            MakeSceneryPackage(community, "orbx-airport-eddf-frankfurt", "Frankfurt Airport");
            MakeSceneryPackage(disabled, "fsdg-airport-lkpr-prague", "Prague Airport");

            var records = SceneryScanner.ScanSceneryPackages(community, new[] { disabled });
            var byFolder = records.ToDictionary(r => r["folder_name"]!.GetValue<string>());

            Assert.True(byFolder["orbx-airport-eddf-frankfurt"]["enabled"]!.GetValue<bool>());
            Assert.False(byFolder["fsdg-airport-lkpr-prague"]["enabled"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanSceneryPackages_IgnoresNonSceneryContentType()
    {
        var root = TempDir("nonscenery");
        var community = Path.Combine(root, "Community");
        Directory.CreateDirectory(community);
        try
        {
            MakeSceneryPackage(community, "some-aircraft", "Some Aircraft", "SIMOBJECT");
            var records = SceneryScanner.ScanSceneryPackages(community, Array.Empty<string>());
            Assert.Empty(records);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanSceneryPackages_IgnoresFoldersWithoutManifest()
    {
        var root = TempDir("nomanifest");
        var community = Path.Combine(root, "Community");
        Directory.CreateDirectory(Path.Combine(community, "not-a-package"));
        try
        {
            var records = SceneryScanner.ScanSceneryPackages(community, Array.Empty<string>());
            Assert.Empty(records);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanSceneryPackages_MissingCommunityPathReturnsEmpty()
    {
        var root = TempDir("missing");
        var records = SceneryScanner.ScanSceneryPackages(Path.Combine(root, "nope"), Array.Empty<string>());
        Assert.Empty(records);
    }

    [Fact]
    public void ScanSceneryPackages_CommunityWinsOnNameConflict()
    {
        // Should never legitimately happen (a folder can't be enabled and
        // disabled at once), but the documented tie-break is "enabled"
        // wins, so verify that stays true rather than accidentally regressing.
        var root = TempDir("conflict");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            MakeSceneryPackage(community, "same-name-airport", "Same Name");
            MakeSceneryPackage(disabled, "same-name-airport", "Same Name");

            var records = SceneryScanner.ScanSceneryPackages(community, new[] { disabled });

            Assert.Single(records);
            Assert.True(records[0]["enabled"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
