using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class DisabledLocationsTests
{
    [Fact]
    public void GetDefaultDisabledPath_IsSiblingOfCommunity()
    {
        var community = Path.Combine(Path.GetTempPath(), "MSFS", "Community");
        var result = DisabledLocations.GetDefaultDisabledPath(community);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "MSFS", "Community_disabled_by_FlightOpsHub"), result);
    }

    [Fact]
    public void ResolveDisabledLocations_CreatesAndReturnsDefaultWhenNoCustomPath()
    {
        var community = Path.Combine(Path.GetTempPath(), $"flightops_test_resolve_{Guid.NewGuid():N}", "Community");
        Directory.CreateDirectory(community);
        var expectedDefault = DisabledLocations.GetDefaultDisabledPath(community);
        try
        {
            var locations = DisabledLocations.ResolveDisabledLocations(community, null);

            Assert.Single(locations);
            Assert.Equal(expectedDefault, locations[0]);
            Assert.True(Directory.Exists(expectedDefault));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(community)!, recursive: true);
        }
    }

    [Fact]
    public void ResolveDisabledLocations_CustomPathIsPrimaryDefaultAppendedIfItExists()
    {
        var root = Path.Combine(Path.GetTempPath(), $"flightops_test_resolve_custom_{Guid.NewGuid():N}");
        var community = Path.Combine(root, "Community");
        var custom = Path.Combine(root, "MyDisabledStuff");
        Directory.CreateDirectory(community);
        var defaultPath = DisabledLocations.GetDefaultDisabledPath(community);
        Directory.CreateDirectory(defaultPath); // pre-existing, from before the user switched to a custom path
        try
        {
            var locations = DisabledLocations.ResolveDisabledLocations(community, custom);

            Assert.Equal(2, locations.Count);
            Assert.Equal(Path.GetFullPath(custom), locations[0]);
            Assert.Equal(defaultPath, locations[1]);
            Assert.True(Directory.Exists(custom));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveDisabledLocations_DoesNotAppendDefaultWhenItDoesNotExist()
    {
        var root = Path.Combine(Path.GetTempPath(), $"flightops_test_resolve_nodup_{Guid.NewGuid():N}");
        var community = Path.Combine(root, "Community");
        var custom = Path.Combine(root, "MyDisabledStuff");
        Directory.CreateDirectory(community);
        try
        {
            var locations = DisabledLocations.ResolveDisabledLocations(community, custom);
            Assert.Single(locations);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
