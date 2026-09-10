using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class UpdateCheckTests
{
    [Fact]
    public void IsNewer_DetectsARealBump()
    {
        Assert.True(UpdateCheck.IsNewer("v2.1", "v2.0"));
        Assert.True(UpdateCheck.IsNewer("2.1.0", "2.0.9"));
    }

    [Fact]
    public void IsNewer_FalseWhenSameOrOlder()
    {
        Assert.False(UpdateCheck.IsNewer("v2.0", "v2.0"));
        Assert.False(UpdateCheck.IsNewer("v1.9", "v2.0"));
    }

    [Fact]
    public void IsNewer_HandlesMissingPatchComponent()
    {
        // "v2.0" vs "v2.0.1" - the comparison must not blow up just
        // because one side has fewer dotted components than the other.
        Assert.True(UpdateCheck.IsNewer("v2.0.1", "v2.0"));
        Assert.False(UpdateCheck.IsNewer("v2.0", "v2.0.1"));
    }

    [Fact]
    public void IsNewer_GarbageTextNeverBeatsARealVersion()
    {
        Assert.False(UpdateCheck.IsNewer("not-a-version", "v2.0"));
        Assert.False(UpdateCheck.IsNewer("", "v2.0"));
    }

    [Fact]
    public void ParseVersion_IgnoresPrereleaseSuffix()
    {
        Assert.Equal(new[] { 2, 1 }, UpdateCheck.ParseVersion("v2.1-beta"));
        Assert.Equal(new[] { 2, 1, 3 }, UpdateCheck.ParseVersion("2.1.3-rc1"));
    }

    [Fact]
    public void FindAssetDownloadUrl_MatchesInstallerFilenamePattern()
    {
        // release-dotnet.yml's OutputBaseFilename bakes the version into
        // the actual uploaded asset name (FlightOpsHub-Setup-3.0.0.exe),
        // so this has to match by prefix/suffix, not one fixed name.
        var data = new JsonObject
        {
            ["assets"] = new JsonArray(
                new JsonObject { ["name"] = "source.zip", ["browser_download_url"] = "https://example.com/source.zip" },
                new JsonObject { ["name"] = "FlightOpsHub-Setup-3.0.0.exe", ["browser_download_url"] = "https://example.com/FlightOpsHub-Setup-3.0.0.exe" }),
        };
        Assert.Equal("https://example.com/FlightOpsHub-Setup-3.0.0.exe", UpdateCheck.FindAssetDownloadUrl(data));
    }

    [Fact]
    public void FindAssetDownloadUrl_NullWhenMissing()
    {
        var withOther = new JsonObject { ["assets"] = new JsonArray(new JsonObject { ["name"] = "other.zip" }) };
        Assert.Null(UpdateCheck.FindAssetDownloadUrl(withOther));
        Assert.Null(UpdateCheck.FindAssetDownloadUrl(new JsonObject()));
    }

    // CheckForUpdate itself isn't unit-tested here - it would mean a real
    // GitHub API call baked into the permanent suite (flaky under rate
    // limits/no network), same reasoning as AtcNetworkTests only covering
    // the pure-logic paths. Verified live instead (see the .NET rewrite
    // memory/plan notes for this session's live verification).

    [Fact]
    public async Task DownloadUpdate_BadUrlReturnsErrorWithoutThrowing()
    {
        var destPath = Path.Combine(Path.GetTempPath(), $"flightops_test_dl_{Guid.NewGuid():N}.exe");
        var result = await UpdateCheck.DownloadUpdate("https://this-domain-should-not-exist-flightopshub.invalid/file.exe", destPath);

        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.NotNull(result["error"]);
        Assert.False(File.Exists(destPath));
        Assert.False(File.Exists(destPath + ".part"));
    }
}
