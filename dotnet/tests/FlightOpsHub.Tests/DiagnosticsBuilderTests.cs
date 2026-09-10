using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

// This is the file that decides what goes into a diagnostics export meant
// to be attached to a *public* GitHub issue - see
// [[flightops-privacy-discipline]] memory. Redaction correctness matters
// more here than almost anywhere else in the port.
public class DiagnosticsBuilderTests
{
    [Fact]
    public void BuildReport_RedactsSimBriefUsername()
    {
        var config = new JsonObject { ["_simbrief_username"] = "RealPersonName123" };
        var report = DiagnosticsBuilder.BuildReport(config, "Windows 11", "");

        Assert.DoesNotContain("RealPersonName123", report);
        Assert.Contains("<redacted>", report);
    }

    [Fact]
    public void BuildReport_RedactsWindowsUsernameInConfigPaths()
    {
        var config = new JsonObject { ["_community_path"] = @"C:\Users\RealWindowsName\AppData\Roaming\Microsoft Flight Simulator\Packages\Community" };
        var report = DiagnosticsBuilder.BuildReport(config, "Windows 11", "");

        Assert.DoesNotContain("RealWindowsName", report);
        Assert.Contains(@"Users\\<user>", report); // JSON-serialized path doubles backslashes
        Assert.Contains("AppData", report); // rest of the path stays intact - still useful for diagnosis
    }

    [Fact]
    public void BuildReport_RedactsWindowsUsernameInLogTail()
    {
        var config = new JsonObject();
        var logTail = @"2026-01-01 12:00:00 INFO flightops_hub: Update download finished: C:\Users\RealWindowsName\Downloads\flightops_hub.exe";
        var report = DiagnosticsBuilder.BuildReport(config, "Windows 11", logTail);

        Assert.DoesNotContain("RealWindowsName", report);
        Assert.Contains(@"Users\<user>", report); // raw log line, single backslashes
        Assert.Contains("Downloads", report);
    }

    [Fact]
    public void BuildReport_LeavesOtherConfigFieldsIntact()
    {
        var config = new JsonObject { ["_theme"] = "dark", ["_language"] = "EN" };
        var report = DiagnosticsBuilder.BuildReport(config, "Windows 11", "");

        Assert.Contains("dark", report);
        Assert.Contains("EN", report);
    }

    [Fact]
    public void BuildReport_IncludesAppVersionAndOsInfo()
    {
        var report = DiagnosticsBuilder.BuildReport(new JsonObject(), "Windows 11 (10.0.22631)", "");

        Assert.Contains(AppVersion.Current, report);
        Assert.Contains("Windows 11 (10.0.22631)", report);
    }

    [Fact]
    public void BuildReport_PlaceholderWhenNoLogYet()
    {
        var report = DiagnosticsBuilder.BuildReport(new JsonObject(), "Windows 11", "");
        Assert.Contains("(no log file yet)", report);
    }

    [Fact]
    public void DefaultFilename_HasTxtExtension()
    {
        Assert.EndsWith(".txt", DiagnosticsBuilder.DefaultFilename());
    }
}
