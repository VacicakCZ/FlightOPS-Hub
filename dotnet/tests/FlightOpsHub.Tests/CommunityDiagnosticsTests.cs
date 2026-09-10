using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class CommunityDiagnosticsTests
{
    private static string TempDir(string label) =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_commdiag_{label}_{Guid.NewGuid():N}");

    private static void WriteManifest(string folderPath, string title, string contentType = "SCENERY")
    {
        Directory.CreateDirectory(folderPath);
        var manifest = new JsonObject { ["title"] = title, ["content_type"] = contentType };
        File.WriteAllText(Path.Combine(folderPath, "manifest.json"), manifest.ToJsonString());
    }

    // --- FindMisplacedPackages ---

    [Fact]
    public void FindMisplacedPackages_FindsManifestNestedOneLevelTooDeep()
    {
        var root = TempDir("nested");
        WriteManifest(Path.Combine(root, "some-airport", "some-airport"), "Some Airport");
        try
        {
            var result = CommunityDiagnostics.FindMisplacedPackages(root);

            Assert.Single(result);
            Assert.Equal("some-airport", result[0]["folder_name"]!.GetValue<string>());
            Assert.Equal("some-airport", result[0]["nested_folder"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindMisplacedPackages_CorrectlyPlacedPackageIsNotFlagged()
    {
        var root = TempDir("correct");
        WriteManifest(Path.Combine(root, "some-airport"), "Some Airport");
        try
        {
            Assert.Empty(CommunityDiagnostics.FindMisplacedPackages(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindMisplacedPackages_FolderWithNoManifestAnywhereIsNotFlagged()
    {
        var root = TempDir("nomanifest");
        Directory.CreateDirectory(Path.Combine(root, "not-a-package", "random-stuff"));
        try
        {
            Assert.Empty(CommunityDiagnostics.FindMisplacedPackages(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindMisplacedPackages_ManifestNestedTwoLevelsDeepIsNotFlagged()
    {
        // Only the specific "exactly one level too deep" mistake is
        // detected - anything deeper is a different (or non-) problem.
        var root = TempDir("twolevels");
        WriteManifest(Path.Combine(root, "wrapper", "inner", "actual-package"), "Deeply Nested");
        try
        {
            Assert.Empty(CommunityDiagnostics.FindMisplacedPackages(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindMisplacedPackages_MissingCommunityPathReturnsEmpty()
    {
        var root = TempDir("missing");
        Assert.Empty(CommunityDiagnostics.FindMisplacedPackages(Path.Combine(root, "does_not_exist")));
    }

    // --- FindDuplicatePackages ---

    [Fact]
    public void FindDuplicatePackages_SameTitleUnderTwoFolderNamesIsADuplicate()
    {
        var root = TempDir("dup");
        WriteManifest(Path.Combine(root, "aerosoft-ebbr-v1"), "Brussels Airport");
        WriteManifest(Path.Combine(root, "aerosoft-ebbr-v2"), "Brussels Airport");
        try
        {
            var result = CommunityDiagnostics.FindDuplicatePackages(new[] { root });

            Assert.Single(result);
            Assert.Equal("Brussels Airport", result[0]["title"]!.GetValue<string>());
            Assert.Equal(
                new[] { "aerosoft-ebbr-v1", "aerosoft-ebbr-v2" },
                result[0]["folders"]!.AsArray().Select(f => f!.GetValue<string>()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindDuplicatePackages_UniqueTitlesAreNotDuplicates()
    {
        var root = TempDir("unique");
        WriteManifest(Path.Combine(root, "pkg-a"), "Airport A");
        WriteManifest(Path.Combine(root, "pkg-b"), "Airport B");
        try
        {
            Assert.Empty(CommunityDiagnostics.FindDuplicatePackages(new[] { root }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindDuplicatePackages_DetectedAcrossMultipleLocations()
    {
        var root = TempDir("multiloc");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        WriteManifest(Path.Combine(community, "pkg-enabled"), "Some Scenery");
        WriteManifest(Path.Combine(disabled, "pkg-disabled-copy"), "Some Scenery");
        try
        {
            var result = CommunityDiagnostics.FindDuplicatePackages(new[] { community, disabled });

            Assert.Single(result);
            Assert.Equal(
                new[] { "pkg-disabled-copy", "pkg-enabled" },
                result[0]["folders"]!.AsArray().Select(f => f!.GetValue<string>()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindDuplicatePackages_SameFolderNameInTwoLocationsOnlyCountsOnce()
    {
        // Same folder name appearing in both a Community and a disabled
        // scan (e.g. a stale duplicate scan of the same physical folder)
        // should not be reported as "two different installs".
        var root = TempDir("samename");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        WriteManifest(Path.Combine(community, "same-name"), "A Package");
        WriteManifest(Path.Combine(disabled, "same-name"), "A Package");
        try
        {
            Assert.Empty(CommunityDiagnostics.FindDuplicatePackages(new[] { community, disabled }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindDuplicatePackages_FoldersWithoutManifestAreIgnored()
    {
        var root = TempDir("nomanifestdup");
        Directory.CreateDirectory(Path.Combine(root, "empty-folder"));
        try
        {
            Assert.Empty(CommunityDiagnostics.FindDuplicatePackages(new[] { root }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
