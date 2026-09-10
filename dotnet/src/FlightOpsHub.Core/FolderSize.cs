using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/folder_size.py - computes disk usage for a folder of
/// add-on packages (Community, or the disabled-holding folder): total
/// size plus a per-package breakdown, for the Settings tab's storage-usage
/// panels. Deliberately on-demand only (not run automatically during
/// scenery/aircraft scans) - a full recursive walk of tens of thousands of
/// files can take a noticeable amount of time.
/// </summary>
public static class FolderSize
{
    private static long DirSize(string path)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        return total;
    }

    /// <summary>Returns {total_bytes, packages: [{folder_name, bytes}, ...]} sorted largest-first. Top-level entries only - each direct subfolder is one add-on package.</summary>
    public static JsonObject ScanFolderUsage(string folderPath)
    {
        var packages = new List<(string Name, long Bytes)>();
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateDirectories(folderPath).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new JsonObject { ["total_bytes"] = 0L, ["packages"] = new JsonArray() };
        }

        foreach (var dir in entries)
        {
            packages.Add((Path.GetFileName(dir), DirSize(dir)));
        }
        packages.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        var totalBytes = packages.Sum(p => p.Bytes);

        return new JsonObject
        {
            ["total_bytes"] = totalBytes,
            ["packages"] = new JsonArray(packages
                .Select(p => (JsonNode)new JsonObject { ["folder_name"] = p.Name, ["bytes"] = p.Bytes })
                .ToArray()),
        };
    }
}
