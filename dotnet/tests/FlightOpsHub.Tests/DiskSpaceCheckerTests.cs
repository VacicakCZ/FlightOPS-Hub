using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class DiskSpaceCheckerTests
{
    [Fact]
    public void Check_MissingPathsReturnsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"flightops_test_disk_missing_{Guid.NewGuid():N}");
        var result = DiskSpaceChecker.Check(missing, "");
        Assert.Empty(result);
    }

    [Fact]
    public void Check_IgnoresEmptyDisabledPath()
    {
        var community = Path.Combine(Path.GetTempPath(), $"flightops_test_disk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(community);
        try
        {
            var result = DiskSpaceChecker.Check(community, "");
            Assert.Single(result);
            Assert.Equal("community", result[0]["label"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(community, recursive: true);
        }
    }

    [Fact]
    public void Check_DedupesPathsOnTheSameDrive()
    {
        var root = Path.Combine(Path.GetTempPath(), $"flightops_test_disk_dedupe_{Guid.NewGuid():N}");
        var community = Path.Combine(root, "Community");
        var disabled = Path.Combine(root, "disabled-addons");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(disabled);
        try
        {
            // Both real subfolders of the same temp root, so they genuinely
            // share a drive - the disabled-holding folder should not be
            // reported a second time.
            var result = DiskSpaceChecker.Check(community, disabled);
            Assert.Single(result);
            Assert.Equal("community", result[0]["label"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Check_FlagsLowSpaceThreshold()
    {
        Assert.True(DiskSpaceChecker.LowSpaceThresholdBytes == 5L * 1024 * 1024 * 1024);
    }
}
