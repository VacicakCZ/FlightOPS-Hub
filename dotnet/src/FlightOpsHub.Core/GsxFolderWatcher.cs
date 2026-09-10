namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/gsx_watcher.py - polls the GSX profiles folder for
/// changes on a background task and triggers a re-check when something
/// changes. Simpler and dependency-free compared to native filesystem
/// change notifications, and cheap enough (a plain directory listing) to
/// poll every few seconds without being felt.
/// </summary>
public class GsxFolderWatcher
{
    private readonly Func<string> _getPath;
    private readonly Action _onChange;
    private readonly TimeSpan _interval;
    private readonly ManualResetEventSlim _stop = new(false);

    private HashSet<(string Name, long MtimeTicks)>? _lastSignature;

    public GsxFolderWatcher(Func<string> getPath, Action onChange, double intervalSeconds = 3.0)
    {
        _getPath = getPath;
        _onChange = onChange;
        _interval = TimeSpan.FromSeconds(intervalSeconds);
    }

    private static HashSet<(string, long)>? Signature(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path)
                .Select(f => (Path.GetFileName(f), File.GetLastWriteTimeUtc(f).Ticks))
                .ToHashSet();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Loop()
    {
        while (!_stop.IsSet)
        {
            var signature = Signature(_getPath());
            if (signature != null && !(_lastSignature != null && signature.SetEquals(_lastSignature)))
            {
                // Skip the very first read (startup baseline) - only
                // changes seen after that should trigger a re-check.
                if (_lastSignature != null)
                {
                    _onChange();
                }
                _lastSignature = signature;
            }

            if (_stop.Wait(_interval)) break;
        }
    }

    public void Start() => Task.Run(Loop);

    public void Stop() => _stop.Set();
}
