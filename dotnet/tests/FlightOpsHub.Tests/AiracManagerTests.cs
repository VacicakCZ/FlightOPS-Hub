using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

// No Python test file exists for backend/airac.py - this fills that gap,
// per the .NET rewrite plan's own note about the coverage hole.
public class AiracManagerTests
{
    [Fact]
    public void GetCurrentAirac_ReturnsFourDigitCycleCode()
    {
        var result = AiracManager.GetCurrentAirac();

        Assert.Equal(4, result.Length);
        Assert.True(int.TryParse(result, out _));
    }

    [Fact]
    public void GetInstalledAirac_EmptyPathReturnsEmptyString()
    {
        Assert.Equal("", AiracManager.GetInstalledAirac(""));
        Assert.Equal("", AiracManager.GetInstalledAirac(null));
    }

    [Fact]
    public void GetInstalledAirac_MissingDirectoryReturnsEmptyString()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_airac_missing_{Guid.NewGuid():N}");
        Assert.Equal("", AiracManager.GetInstalledAirac(path));
    }

    [Fact]
    public void GetInstalledAirac_ReadsCycleFromNavigraphManifestTitle()
    {
        var communityPath = Path.Combine(Path.GetTempPath(), $"flightops_test_airac_{Guid.NewGuid():N}");
        var navigraphFolder = Path.Combine(communityPath, "navigraph-navdata-warehouse");
        Directory.CreateDirectory(navigraphFolder);
        try
        {
            var manifest = new JsonObject { ["title"] = "Navigraph AIRAC Cycle 2509" };
            File.WriteAllText(Path.Combine(navigraphFolder, "manifest.json"), manifest.ToJsonString());

            var result = AiracManager.GetInstalledAirac(communityPath);

            Assert.Equal("2509", result);
        }
        finally
        {
            Directory.Delete(communityPath, recursive: true);
        }
    }

    [Fact]
    public void GetInstalledAirac_IgnoresNonNavigraphFoldersAndFoldersWithoutManifest()
    {
        var communityPath = Path.Combine(Path.GetTempPath(), $"flightops_test_airac_none_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(communityPath, "some-other-addon"));
        Directory.CreateDirectory(Path.Combine(communityPath, "navigraph-no-manifest"));
        try
        {
            var result = AiracManager.GetInstalledAirac(communityPath);
            Assert.Equal("", result);
        }
        finally
        {
            Directory.Delete(communityPath, recursive: true);
        }
    }

    [Fact]
    public void GetInstalledAirac_IgnoresManifestWithoutAiracOrNavdataInTitle()
    {
        var communityPath = Path.Combine(Path.GetTempPath(), $"flightops_test_airac_wrongtitle_{Guid.NewGuid():N}");
        var navigraphFolder = Path.Combine(communityPath, "navigraph-something");
        Directory.CreateDirectory(navigraphFolder);
        try
        {
            var manifest = new JsonObject { ["title"] = "Some Other Package 2509" };
            File.WriteAllText(Path.Combine(navigraphFolder, "manifest.json"), manifest.ToJsonString());

            var result = AiracManager.GetInstalledAirac(communityPath);

            Assert.Equal("", result);
        }
        finally
        {
            Directory.Delete(communityPath, recursive: true);
        }
    }
}
