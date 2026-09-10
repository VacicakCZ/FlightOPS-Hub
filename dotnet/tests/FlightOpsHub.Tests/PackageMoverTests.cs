using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class PackageMoverTests
{
    private static string TempDir(string label) =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_mover_{label}_{Guid.NewGuid():N}");

    private static void MakeSceneryPackage(string baseDir, string folderName, string title)
    {
        var pkgDir = Path.Combine(baseDir, folderName);
        Directory.CreateDirectory(pkgDir);
        var manifest = new JsonObject { ["content_type"] = "SCENERY", ["title"] = title };
        File.WriteAllText(Path.Combine(pkgDir, "manifest.json"), manifest.ToJsonString());
    }

    // --- ApplyPackageChanges ---

    [Fact]
    public void ApplyPackageChanges_MovesEnabledPackageToDisabled()
    {
        var root = TempDir("enable_to_disable");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            MakeSceneryPackage(community, "orbx-airport-eddf-frankfurt", "Frankfurt Airport");

            var results = PackageMover.ApplyPackageChanges(
                community, new[] { disabled }, new Dictionary<string, bool> { ["orbx-airport-eddf-frankfurt"] = false });

            Assert.Equal(new[] { new PackageMover.MoveResult("orbx-airport-eddf-frankfurt", true, null) }, results);
            Assert.False(Directory.Exists(Path.Combine(community, "orbx-airport-eddf-frankfurt")));
            Assert.True(Directory.Exists(Path.Combine(disabled, "orbx-airport-eddf-frankfurt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyPackageChanges_MovesDisabledPackageBackToCommunity()
    {
        var root = TempDir("disable_to_enable");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            MakeSceneryPackage(disabled, "orbx-airport-eddf-frankfurt", "Frankfurt Airport");

            var results = PackageMover.ApplyPackageChanges(
                community, new[] { disabled }, new Dictionary<string, bool> { ["orbx-airport-eddf-frankfurt"] = true });

            Assert.Equal(new[] { new PackageMover.MoveResult("orbx-airport-eddf-frankfurt", true, null) }, results);
            Assert.True(Directory.Exists(Path.Combine(community, "orbx-airport-eddf-frankfurt")));
            Assert.False(Directory.Exists(Path.Combine(disabled, "orbx-airport-eddf-frankfurt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyPackageChanges_IsNoOpWhenAlreadyInDesiredState()
    {
        var root = TempDir("noop");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            MakeSceneryPackage(community, "already-enabled", "Already Enabled");

            var results = PackageMover.ApplyPackageChanges(
                community, new[] { disabled }, new Dictionary<string, bool> { ["already-enabled"] = true });

            Assert.Empty(results);
            Assert.True(Directory.Exists(Path.Combine(community, "already-enabled")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyPackageChanges_ReportsMissingSource()
    {
        var root = TempDir("missingsrc");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            var results = PackageMover.ApplyPackageChanges(
                community, new[] { disabled }, new Dictionary<string, bool> { ["never-existed"] = false });

            Assert.Equal(new[] { new PackageMover.MoveResult("never-existed", false, "source not found") }, results);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyPackageChanges_DuplicateStateInBothLocationsIsANoOpAndTouchesNeitherCopy()
    {
        // The Python test for this ("never_overwrites_existing_destination")
        // fakes a scan-vs-move race via monkeypatching os.path.isdir, which
        // has no direct C# equivalent without adding a filesystem
        // abstraction purely for this one test. What IS directly
        // verifiable without that: if a folder somehow exists in BOTH
        // Community and the disabled location at once (should never
        // legitimately happen), ResolvePendingMoves's own currently-in-
        // community / current-disabled-path checks already make this a
        // no-op - neither copy gets touched or deleted, which is the
        // property that actually matters (nothing is silently lost).
        var root = TempDir("dupstate");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            MakeSceneryPackage(community, "dup-package", "Dup");
            MakeSceneryPackage(disabled, "dup-package", "Dup (stale copy)");

            var results = PackageMover.ApplyPackageChanges(
                community, new[] { disabled }, new Dictionary<string, bool> { ["dup-package"] = false });

            Assert.Empty(results);
            Assert.True(Directory.Exists(Path.Combine(community, "dup-package")));
            Assert.True(Directory.Exists(Path.Combine(disabled, "dup-package")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- EstimateApplySpace ---

    [Fact]
    public void EstimateApplySpace_IgnoresSameDriveMoves()
    {
        // A same-drive move is an instant rename and never needs extra
        // room - real temp subfolders are genuinely on the same drive
        // here, no override needed to prove the "no shortfall" case.
        var root = TempDir("samedrive");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            MakeSceneryPackage(community, "big-package", "Big");

            var shortfalls = PackageMover.EstimateApplySpace(
                community, new[] { disabled }, new Dictionary<string, bool> { ["big-package"] = false });

            Assert.Empty(shortfalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EstimateApplySpace_FlagsDriveWithoutEnoughRoom()
    {
        var root = TempDir("shortfall");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            MakeSceneryPackage(community, "big-package", "Big");

            var shortfalls = PackageMover.EstimateApplySpace(
                community, new[] { disabled }, new Dictionary<string, bool> { ["big-package"] = false },
                sameDriveOverride: (a, b) => false,
                directorySizeOverride: _ => 10L * 1024 * 1024 * 1024,
                freeSpaceOverride: _ => 1L * 1024 * 1024 * 1024);

            Assert.Single(shortfalls);
            Assert.Equal(10L * 1024 * 1024 * 1024, shortfalls[0].NeededBytes);
            Assert.Equal(1L * 1024 * 1024 * 1024, shortfalls[0].FreeBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EstimateApplySpace_PassesWhenDriveHasEnoughRoom()
    {
        var root = TempDir("enoughroom");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            MakeSceneryPackage(community, "small-package", "Small");

            var shortfalls = PackageMover.EstimateApplySpace(
                community, new[] { disabled }, new Dictionary<string, bool> { ["small-package"] = false },
                sameDriveOverride: (a, b) => false,
                directorySizeOverride: _ => 1024,
                freeSpaceOverride: _ => 50L * 1024 * 1024 * 1024);

            Assert.Empty(shortfalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EstimateApplySpace_SumsMultiplePackagesToTheSameDrive()
    {
        var root = TempDir("sums");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            MakeSceneryPackage(community, "pkg-a", "A");
            MakeSceneryPackage(community, "pkg-b", "B");

            var shortfalls = PackageMover.EstimateApplySpace(
                community, new[] { disabled }, new Dictionary<string, bool> { ["pkg-a"] = false, ["pkg-b"] = false },
                sameDriveOverride: (a, b) => false,
                directorySizeOverride: _ => 6L * 1024 * 1024 * 1024,
                freeSpaceOverride: _ => 10L * 1024 * 1024 * 1024);

            // 6 GiB + 6 GiB = 12 GiB needed, only 10 GiB free - neither
            // package alone would trip the check, only the combined total.
            Assert.Single(shortfalls);
            Assert.Equal(12L * 1024 * 1024 * 1024, shortfalls[0].NeededBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EstimateApplySpace_IgnoresMissingSource()
    {
        var root = TempDir("missingsrcspace");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "Disabled");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            var shortfalls = PackageMover.EstimateApplySpace(
                community, new[] { disabled }, new Dictionary<string, bool> { ["never-existed"] = false },
                sameDriveOverride: (a, b) => false);

            Assert.Empty(shortfalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
