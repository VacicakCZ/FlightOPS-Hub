using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void FirstInstance_AcquiresLock()
    {
        using var instance = new SingleInstance();

        Assert.False(instance.AlreadyRunning);
    }

    [Fact]
    public void SecondInstance_DetectsAlreadyRunning()
    {
        using var first = new SingleInstance();
        using var second = new SingleInstance();

        Assert.False(first.AlreadyRunning);
        Assert.True(second.AlreadyRunning);
    }
}
