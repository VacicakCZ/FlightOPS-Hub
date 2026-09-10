using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class AppLoggingTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_log_{Guid.NewGuid():N}.log");

    [Fact]
    public void Info_AppendsALineWithLevelAndMessage()
    {
        var path = TempPath();
        try
        {
            AppLogging.Info(path, "hello world");
            var content = File.ReadAllText(path);

            Assert.Contains("INFO", content);
            Assert.Contains("hello world", content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MultipleWrites_Accumulate()
    {
        var path = TempPath();
        try
        {
            AppLogging.Info(path, "first");
            AppLogging.Warning(path, "second");
            AppLogging.Error(path, "third");

            var lines = File.ReadAllLines(path);
            Assert.Equal(3, lines.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadRecentLines_ReturnsEmptyStringWhenFileMissing()
    {
        var path = TempPath();
        Assert.Equal("", AppLogging.ReadRecentLines(path));
    }

    [Fact]
    public void ReadRecentLines_TailsToMaxLines()
    {
        var path = TempPath();
        try
        {
            for (var i = 0; i < 10; i++)
            {
                File.AppendAllText(path, $"line {i}\n");
            }

            var tail = AppLogging.ReadRecentLines(path, maxLines: 3);
            var lines = tail.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(3, lines.Length);
            Assert.Equal("line 7", lines[0]);
            Assert.Equal("line 9", lines[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Write_NeverThrowsEvenIfDirectoryDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_log_missing_dir_{Guid.NewGuid():N}", "log.log");
        // Should not throw - logging must never be the reason the app crashes.
        AppLogging.Info(path, "this directory does not exist");
    }
}
