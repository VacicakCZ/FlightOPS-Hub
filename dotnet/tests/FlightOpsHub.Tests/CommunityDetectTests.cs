using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

// DetectCommunityPath's exists-vs-missing branches would need mocking
// APPDATA/LOCALAPPDATA, but those are process-wide env vars and xUnit runs
// test classes in parallel by default - mutating them here would race with
// ExeXmlManagerTests.GetExeXmlPath_UsesCorrectFolderPerVersionAndPlatform,
// which reads the same two variables concurrently. Sticking to the
// structural DefaultCommunityPath checks (same approach as that test)
// keeps this parallel-safe.
public class CommunityDetectTests
{
    [Fact]
    public void DefaultCommunityPath_Msfs2020Steam()
    {
        var path = CommunityDetect.DefaultCommunityPath("MSFS 2020", "Steam");
        Assert.EndsWith(Path.Combine("Microsoft Flight Simulator", "Packages", "Community"), path);
    }

    [Fact]
    public void DefaultCommunityPath_Msfs2020MsStore()
    {
        var path = CommunityDetect.DefaultCommunityPath("MSFS 2020", "MS Store");
        Assert.Contains("Microsoft.FlightSimulator_8wekyb3d8bbwe", path);
        Assert.EndsWith(Path.Combine("LocalCache", "Packages", "Community"), path);
    }

    [Fact]
    public void DefaultCommunityPath_Msfs2024Steam()
    {
        var path = CommunityDetect.DefaultCommunityPath("MSFS 2024", "Steam");
        Assert.EndsWith(Path.Combine("Microsoft Flight Simulator 2024", "Packages", "Community"), path);
    }

    [Fact]
    public void DefaultCommunityPath_Msfs2024MsStore()
    {
        var path = CommunityDetect.DefaultCommunityPath("MSFS 2024", "MS Store");
        Assert.Contains("Microsoft.FlightSimulator2024_8wekyb3d8bbwe", path);
        Assert.EndsWith(Path.Combine("LocalCache", "Packages", "Community"), path);
    }

    [Fact]
    public void DetectCommunityPath_ReturnsNullOrTheDefaultPathButNeverAnythingElse()
    {
        var result = CommunityDetect.DetectCommunityPath("MSFS 2024", "Steam");
        var expected = CommunityDetect.DefaultCommunityPath("MSFS 2024", "Steam");
        Assert.True(result is null || result == expected);
    }
}
