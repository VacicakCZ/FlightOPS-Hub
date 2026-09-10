using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/disk_space.py - free disk space on the drives backing
/// the Community and disabled-holding folders. A cheap, instant check (one
/// DriveInfo lookup per path, not a full folder walk like folder_size.py).
///
/// Python dedupes by os.stat().st_dev; .NET has no direct equivalent, so
/// this dedupes by drive root (e.g. "C:\") instead - equivalent in practice
/// for the standard drive-letter setups this app runs on.
/// </summary>
public static class DiskSpaceChecker
{
    public const long LowSpaceThresholdBytes = 5L * 1024 * 1024 * 1024; // 5 GiB

    public static List<JsonObject> Check(string? communityPath, string? disabledPath)
    {
        var entries = new List<JsonObject>();
        var seenDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (label, path) in new[] { ("community", communityPath), ("disabled", disabledPath) })
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;

            long freeBytes;
            string drive;
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root)) continue;
                drive = root;
                freeBytes = new DriveInfo(root).AvailableFreeSpace;
            }
            catch
            {
                continue;
            }

            if (!seenDrives.Add(drive)) continue;

            entries.Add(new JsonObject
            {
                ["label"] = label,
                ["free_bytes"] = freeBytes,
                ["low"] = freeBytes < LowSpaceThresholdBytes,
            });
        }
        return entries;
    }
}
