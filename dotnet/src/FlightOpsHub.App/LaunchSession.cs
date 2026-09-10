using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FlightOpsHub.Core;

namespace FlightOpsHub.App;

/// <summary>
/// Port of backend/launch_orchestrator.py's LaunchSession - orchestrates
/// one "Launch selected + MSFS" run: buckets selected apps into
/// immediate/delayed/smart-launch groups, starts MSFS itself if needed,
/// hides the window while background tasks run, and (depending on the
/// post-launch setting) either exits once everything is done or switches
/// to a "stay and watch the sim" loop that force-closes everything it
/// started when MSFS quits.
///
/// One LaunchSession is created per launch. Python's threading.Timer/
/// threading.Event/threading.Thread map to Task.Delay/ManualResetEventSlim/
/// Task.Run here; every Window/TrayIcon touch from a background task goes
/// through Dispatcher.Invoke - both are WPF-UI-thread-affine.
/// </summary>
public class LaunchSession
{
    private const int SmartLaunchTimeoutS = 20 * 60;
    private const int SimWatchGracePeriodS = 5 * 60;
    private const int SimWatchPollIntervalS = 15;

    private readonly Window _window;
    private readonly Func<string, string> _tr;
    private readonly TrayIcon _tray;

    // ConcurrentBag, not List<int> - LaunchIfNotRunning can be invoked from
    // genuinely concurrent background tasks once smart-launch (currently a
    // stub, see SmartLaunchWorker) actually fires alongside the delayed-
    // launch chain; List<T> is not thread-safe against concurrent Add.
    private readonly ConcurrentBag<int> _launchedPids = new();
    private int _pendingTasks;
    private string _postLaunchBehavior = "exit";

    private ManualResetEventSlim? _smartCancelEvent;
    private ManualResetEventSlim? _watchStopEvent;
    private bool _simConfirmedRunning;
    private DateTime _watchConfirmDeadline;

    public LaunchSession(Window window, Func<string, string> translate, string trayIconPath)
    {
        _window = window;
        _tr = translate;
        _tray = new TrayIcon(trayIconPath);
    }

    private Dispatcher UiDispatcher => _window.Dispatcher;

    public void Start(
        IReadOnlyDictionary<string, (string Path, int Delay, bool Admin, string LaunchMode)> apps,
        IReadOnlyList<string> selectedNames,
        string simVersion, string simPlatform, string postLaunchBehavior)
    {
        _postLaunchBehavior = postLaunchBehavior;

        var immediateApps = new List<(string Path, bool Admin)>();
        var delayedApps = new List<(int Delay, string Path, bool Admin)>();
        var smartApps = new List<(string Path, bool Admin)>();

        foreach (var name in selectedNames)
        {
            if (!apps.TryGetValue(name, out var data)) continue;
            if (data.LaunchMode == "smart")
            {
                smartApps.Add((data.Path, data.Admin));
            }
            else if (data.LaunchMode == "timer" && data.Delay > 0)
            {
                delayedApps.Add((data.Delay, data.Path, data.Admin));
            }
            else
            {
                immediateApps.Add((data.Path, data.Admin));
            }
        }

        var currentTasks = ProcessUtils.TasklistLower();

        foreach (var (path, admin) in immediateApps)
        {
            LaunchIfNotRunning(path, admin, currentTasks);
        }

        if (!ProcessUtils.IsMsfsRunning(currentTasks))
        {
            ProcessUtils.LaunchMsfs(simVersion, simPlatform);
        }

        UiDispatcher.Invoke(_window.Hide);

        _pendingTasks = 0;
        if (delayedApps.Count > 0)
        {
            _pendingTasks++;
            delayedApps.Sort((a, b) => a.Delay.CompareTo(b.Delay));
            LaunchDelayedApps(delayedApps, 0, 0);
        }

        if (smartApps.Count > 0)
        {
            _pendingTasks++;
            StartSmartLaunch(smartApps);
        }

        if (_pendingTasks == 0)
        {
            // Matches the Python original exactly: an immediate-only
            // launch always exits right away, regardless of the
            // stay-and-watch setting.
            UiDispatcher.Invoke(_window.Close);
        }
    }

    private void LaunchIfNotRunning(string path, bool asAdmin, string tasksLower)
    {
        if (!File.Exists(path)) return;
        if (ProcessUtils.IsExeRunning(path, tasksLower)) return;
        var pid = ProcessUtils.RunApp(path, asAdmin);
        if (pid.HasValue) _launchedPids.Add(pid.Value);
    }

    // --- delayed (timer) launches ---
    private void LaunchDelayedApps(List<(int Delay, string Path, bool Admin)> delayedApps, int index, int elapsedTime)
    {
        if (index >= delayedApps.Count)
        {
            TaskFinished();
            return;
        }

        var (delay, path, asAdmin) = delayedApps[index];
        var waitS = Math.Max(0, delay - elapsedTime);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(waitS));
            LaunchIfNotRunning(path, asAdmin, ProcessUtils.TasklistLower());
            LaunchDelayedApps(delayedApps, index + 1, Math.Max(elapsedTime, delay));
        });
    }

    // --- smart launch (SimConnect) ---
    private void StartSmartLaunch(List<(string Path, bool Admin)> smartApps)
    {
        var deadline = DateTime.UtcNow.AddSeconds(SmartLaunchTimeoutS);
        _smartCancelEvent = new ManualResetEventSlim(false);

        UiDispatcher.Invoke(() =>
        {
            _tray.Ensure(_tr("tray_tooltip"));
            _tray.SetMenu(_tr("tray_status"), _tr("tray_cancel"), () => _smartCancelEvent.Set());
        });

        _ = Task.Run(() => SmartLaunchWorker(smartApps, deadline, _smartCancelEvent));
    }

    private void FireSmartApps(IReadOnlyList<(string Path, bool Admin)> smartApps)
    {
        var tasks = ProcessUtils.TasklistLower();
        foreach (var (path, admin) in smartApps)
        {
            LaunchIfNotRunning(path, admin, tasks);
        }
    }

    /// <summary>
    /// TODO: real SimConnect P/Invoke wiring against the native
    /// SimConnect.dll (not yet done - correctness here needs a live MSFS
    /// session with a loaded flight to verify the marshaling, which this
    /// port has not had the chance to test against; see the .NET rewrite
    /// plan/memory). Until then this degrades exactly the way the Python
    /// original does when the SimConnect package/native dependency is
    /// unavailable: the task completes without ever launching the smart
    /// apps, rather than hanging or guessing.
    /// </summary>
    private void SmartLaunchWorker(IReadOnlyList<(string Path, bool Admin)> smartApps, DateTime deadline, ManualResetEventSlim cancelEvent)
    {
        try
        {
            return;
        }
        finally
        {
            TaskFinished();
        }
    }

    // --- completion bookkeeping ---
    private void TaskFinished()
    {
        var remaining = Interlocked.Decrement(ref _pendingTasks);
        if (remaining <= 0)
        {
            AllLaunchesDone();
        }
    }

    private void AllLaunchesDone()
    {
        if (_postLaunchBehavior == "stay_and_watch" && _launchedPids.Count > 0)
        {
            StartSimWatch();
        }
        else
        {
            UiDispatcher.Invoke(() =>
            {
                _tray.Stop();
                _window.Close();
            });
        }
    }

    // --- stay-and-watch (waits for MSFS to close, then cleans up) ---
    private void StartSimWatch()
    {
        _simConfirmedRunning = false;
        _watchConfirmDeadline = DateTime.UtcNow.AddSeconds(SimWatchGracePeriodS);
        _watchStopEvent = new ManualResetEventSlim(false);

        UiDispatcher.Invoke(() =>
        {
            _tray.Ensure(_tr("tray_tooltip"));
            _tray.SetMenu(_tr("tray_watch_status"), _tr("tray_force_stop"), ForceStopWatch);
        });

        _ = Task.Run(WatchLoop);
    }

    private void WatchLoop()
    {
        while (true)
        {
            if (_watchStopEvent!.IsSet) break;

            var tasks = ProcessUtils.TasklistLower();
            if (ProcessUtils.IsMsfsRunning(tasks))
            {
                _simConfirmedRunning = true;
            }
            else if (_simConfirmedRunning || DateTime.UtcNow >= _watchConfirmDeadline)
            {
                break;
            }
            // else: MSFS hasn't shown up in tasklist yet - give it more time.

            if (_watchStopEvent.Wait(TimeSpan.FromSeconds(SimWatchPollIntervalS))) break;
        }

        TerminateLaunchedApps();
        UiDispatcher.Invoke(() =>
        {
            _tray.Stop();
            _window.Close();
        });
    }

    private void ForceStopWatch() => _watchStopEvent?.Set();

    private void TerminateLaunchedApps()
    {
        foreach (var pid in _launchedPids)
        {
            ProcessUtils.TerminatePid(pid);
        }
    }
}
