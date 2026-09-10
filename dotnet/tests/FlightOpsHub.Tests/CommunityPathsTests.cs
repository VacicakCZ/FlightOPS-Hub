using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class CommunityPathsTests
{
    [Fact]
    public void ValidateDisabledPath_NoCommunityPathIsAlwaysOk()
    {
        var result = CommunityPaths.ValidateDisabledPath(@"C:\Somewhere", null);
        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.Null(result["error"]);
        Assert.Null(result["warning"]);
    }

    [Fact]
    public void ValidateDisabledPath_SamePathIsNested()
    {
        var result = CommunityPaths.ValidateDisabledPath(@"C:\MSFS\Community", @"C:\MSFS\Community");
        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Equal("nested", result["error"]!.GetValue<string>());
    }

    [Fact]
    public void ValidateDisabledPath_SubfolderOfCommunityIsNested()
    {
        var result = CommunityPaths.ValidateDisabledPath(@"C:\MSFS\Community\disabled", @"C:\MSFS\Community");
        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Equal("nested", result["error"]!.GetValue<string>());
    }

    [Fact]
    public void ValidateDisabledPath_CommunityInsideDisabledIsAlsoNested()
    {
        var result = CommunityPaths.ValidateDisabledPath(@"C:\MSFS", @"C:\MSFS\Community");
        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Equal("nested", result["error"]!.GetValue<string>());
    }

    [Fact]
    public void ValidateDisabledPath_SameDriveSiblingIsOkWithNoWarning()
    {
        var result = CommunityPaths.ValidateDisabledPath(@"C:\MSFS\disabled", @"C:\MSFS\Community");
        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.Null(result["warning"]);
    }

    [Fact]
    public void ValidateDisabledPath_DifferentDriveWarnsCrossDrive()
    {
        var result = CommunityPaths.ValidateDisabledPath(@"D:\disabled", @"C:\MSFS\Community");
        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.Equal("cross_drive", result["warning"]!.GetValue<string>());
    }
}
