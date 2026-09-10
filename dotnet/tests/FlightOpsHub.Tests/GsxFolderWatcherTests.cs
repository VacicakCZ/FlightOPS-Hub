using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class GsxFolderWatcherTests
{
    private static string TempDir(string label) =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_gsxwatch_{label}_{Guid.NewGuid():N}");

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    [Fact]
    public async Task DoesNotFireOnChangeForTheStartupBaselineRead()
    {
        var dir = TempDir("baseline");
        Directory.CreateDirectory(dir);
        try
        {
            var changeCount = 0;
            var watcher = new GsxFolderWatcher(() => dir, () => Interlocked.Increment(ref changeCount), intervalSeconds: 0.1);
            watcher.Start();

            // Give it a few polling cycles to establish the baseline - no
            // file ever changes, so onChange must never fire.
            await Task.Delay(400);
            watcher.Stop();

            Assert.Equal(0, changeCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task FiresOnChangeWhenAFileIsAddedAfterTheBaseline()
    {
        var dir = TempDir("addfile");
        Directory.CreateDirectory(dir);
        try
        {
            var changeCount = 0;
            var watcher = new GsxFolderWatcher(() => dir, () => Interlocked.Increment(ref changeCount), intervalSeconds: 0.1);
            watcher.Start();

            // Let the baseline (empty folder) get established first.
            await Task.Delay(250);
            File.WriteAllText(Path.Combine(dir, "LKPR-test.ini"), "");

            var fired = await WaitUntil(() => changeCount > 0, TimeSpan.FromSeconds(2));
            watcher.Stop();

            Assert.True(fired, "onChange should have fired after a file was added");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task StopHaltsFurtherPolling()
    {
        var dir = TempDir("stop");
        Directory.CreateDirectory(dir);
        try
        {
            var changeCount = 0;
            var watcher = new GsxFolderWatcher(() => dir, () => Interlocked.Increment(ref changeCount), intervalSeconds: 0.1);
            watcher.Start();
            await Task.Delay(150); // let the baseline establish
            watcher.Stop();

            // Changing the folder after Stop() must not trigger onChange -
            // wait long enough that a still-running poller would have caught it.
            await Task.Delay(300);
            File.WriteAllText(Path.Combine(dir, "LKPR-test.ini"), "");
            await Task.Delay(300);

            Assert.Equal(0, changeCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
