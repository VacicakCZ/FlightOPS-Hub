using System;
using System.Threading;

namespace FlightOpsHub.Core;

/// <summary>
/// Named-mutex single-instance guard, mirroring backend/win_native.py's
/// acquire_single_instance_lock(). Uses a distinct mutex name from the
/// Python build's "Global\FlightOpsHub_SingleInstance_Mutex" so the two
/// can run side by side during the rewrite; unify the name at cutover.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Global\FlightOpsHub_DotNet_SingleInstance_Mutex";

    private readonly Mutex _mutex;

    public bool AlreadyRunning { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out var createdNew);
        AlreadyRunning = !createdNew;
    }

    public void Dispose()
    {
        if (!AlreadyRunning)
        {
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
    }
}
