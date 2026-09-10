using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class ProcessUtilsTests
{
    [Theory]
    [InlineData("...flightsimulator.exe...", true)]
    [InlineData("...flightsimulator2024.exe...", true)]
    [InlineData("...flightsimulator...", true)]
    [InlineData("...notepad.exe...", false)]
    [InlineData("", false)]
    public void IsMsfsRunning_DetectsKnownProcessNames(string tasksLower, bool expected)
    {
        Assert.Equal(expected, ProcessUtils.IsMsfsRunning(tasksLower));
    }

    [Fact]
    public void IsExeRunning_MatchesByFileNameOnly()
    {
        var tasks = "image name   pid\nvpilot.exe   1234\n";
        Assert.True(ProcessUtils.IsExeRunning(@"C:\Program Files\vPilot\vPilot.exe", tasks));
    }

    [Fact]
    public void IsExeRunning_FalseWhenNotInTaskList()
    {
        var tasks = "image name   pid\nexplorer.exe 100\n";
        Assert.False(ProcessUtils.IsExeRunning(@"C:\Apps\vPilot.exe", tasks));
    }

    [Fact]
    public void RunApp_MissingFileReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_missing_{Guid.NewGuid():N}.exe");
        Assert.Null(ProcessUtils.RunApp(path));
    }

    [Fact]
    public void TasklistLower_ReturnsNonEmptyLowercaseOutput()
    {
        // Genuinely shells out to the real `tasklist` command - this machine
        // always has one, and the result must be all-lowercase (matches
        // Python's tasklist_lower()).
        var result = ProcessUtils.TasklistLower();
        Assert.NotEmpty(result);
        Assert.Equal(result, result.ToLowerInvariant());
    }
}
