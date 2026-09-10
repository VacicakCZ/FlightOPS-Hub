using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/community_diagnostics.py - structural diagnostics for
/// the Community folder, catching two common install mistakes that
/// SceneryScanner silently ignores by design (it only cares about
/// recognized packages):
///
/// 1. Misplaced manifest: a folder with no manifest.json directly inside
///    it, but one exactly one level deeper (a very common mistake when
///    manually extracting a zip that already contains a wrapping folder).
/// 2. Duplicate installs: the same package (by manifest title) present
///    under two or more different folder names, anywhere across Community
///    and the known disabled-holding locations.
///
/// Pure read-only diagnostics - never moves, renames, or deletes anything.
/// </summary>
public static class CommunityDiagnostics
{
    private static string? ReadManifestTitle(string folderPath)
    {
        var manifest = ManifestReader.Read(folderPath);
        var title = ManifestReader.GetString(manifest, "title").Trim();
        return title.Length > 0 ? title : null;
    }

    /// <summary>Top-level Community folders with no manifest.json of their own, but a valid one exactly one level deeper.</summary>
    public static List<JsonObject> FindMisplacedPackages(string communityPath)
    {
        var misplaced = new List<JsonObject>();
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateDirectories(communityPath).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return misplaced;
        }

        foreach (var entryPath in entries)
        {
            if (File.Exists(Path.Combine(entryPath, "manifest.json")))
            {
                continue; // a normal, correctly-placed package - nothing wrong here
            }

            IEnumerable<string> subentries;
            try
            {
                subentries = Directory.EnumerateDirectories(entryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var subPath in subentries)
            {
                if (File.Exists(Path.Combine(subPath, "manifest.json")))
                {
                    misplaced.Add(new JsonObject
                    {
                        ["folder_name"] = Path.GetFileName(entryPath),
                        ["nested_folder"] = Path.GetFileName(subPath),
                    });
                    break;
                }
            }
        }
        return misplaced;
    }

    /// <summary>
    /// locations: folder paths to scan (Community + all known disabled-
    /// holding locations). Flags any manifest title found under 2+
    /// distinct folder names anywhere across them.
    /// </summary>
    public static List<JsonObject> FindDuplicatePackages(IEnumerable<string> locations)
    {
        var byTitle = new Dictionary<string, SortedSet<string>>();
        foreach (var basePath in locations)
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateDirectories(basePath).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entryPath in entries)
            {
                var title = ReadManifestTitle(entryPath);
                if (title is null) continue;
                if (!byTitle.TryGetValue(title, out var folders))
                {
                    folders = new SortedSet<string>(StringComparer.Ordinal);
                    byTitle[title] = folders;
                }
                folders.Add(Path.GetFileName(entryPath));
            }
        }

        return byTitle
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => new JsonObject
            {
                ["title"] = kv.Key,
                ["folders"] = new JsonArray(kv.Value.Select(f => (JsonNode)f).ToArray()),
            })
            .ToList();
    }
}
