using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of scan_aircraft_and_liveries and its helpers from scenery_data.py -
/// the largest and most heuristic-heavy piece of the module. See the
/// original Python docstring (kept in comments below) for the reasoning
/// behind each heuristic; this is a close line-by-line port, not a
/// redesign.
/// </summary>
public static class AircraftScanner
{
    /// <summary>
    /// The first segment of a community package's folder name is
    /// consistently a developer/studio abbreviation (e.g. "fnx-", "bksq-",
    /// "flybywire-") - same principle as scenery, but here it's directly
    /// the grouping key rather than something to skip.
    /// </summary>
    public static string DeveloperKey(string folderName)
    {
        var parts = folderName.Split(new[] { '-', '_' }, 2);
        return parts[0].ToLowerInvariant();
    }

    public static string PrettifyDevKey(string key)
    {
        return string.IsNullOrEmpty(key) ? key : char.ToUpperInvariant(key[0]) + key[1..];
    }

    private static readonly Regex BaseContainerRegex = new(
        @"base_container\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);

    /// <summary>Names of folders directly under SimObjects/Airplanes/&lt;name&gt; - the internal identifier an aircraft and its liveries reference each other by.</summary>
    private static HashSet<string> FindSimObjectNames(string pkgPath)
    {
        var names = new HashSet<string>();
        var simObj = Path.Combine(pkgPath, "SimObjects", "Airplanes");
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(simObj))
            {
                names.Add(Path.GetFileName(dir));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return names;
    }

    /// <summary>
    /// Walks SimObjects/Airplanes/*/aircraft.cfg inside a package and
    /// returns the set of "base_container" values (just the target folder
    /// name, not the whole relative path) - MSFS's own official reference
    /// to a base aircraft, more reliable than guessing from title text.
    /// </summary>
    private static HashSet<string> FindBaseContainers(string pkgPath)
    {
        var bases = new HashSet<string>();
        var simObj = Path.Combine(pkgPath, "SimObjects", "Airplanes");
        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(simObj).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return bases;
        }

        foreach (var dir in dirs)
        {
            var cfgPath = Path.Combine(dir, "aircraft.cfg");
            string content;
            try
            {
                content = File.ReadAllText(cfgPath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var match = BaseContainerRegex.Match(content);
            if (match.Success)
            {
                var baseName = match.Groups[1].Value.Replace('\\', '/').TrimEnd('/').Split('/')[^1];
                if (!string.IsNullOrEmpty(baseName))
                {
                    bases.Add(baseName);
                }
            }
        }
        return bases;
    }

    /// <summary>
    /// True if this package's own aircraft.cfg base_container points
    /// outside its own SimObjects folder - i.e. it structurally depends on
    /// another package's aircraft to even render, which makes it a
    /// livery/repaint no matter what the package declares itself as.
    /// Needed because manifest.json content_type is self-reported and
    /// often wrong for registration/repaint packs (declared "AIRCRAFT" but
    /// really just a livery) - base_container is MSFS's own, non-optional
    /// wiring and can't lie the same way.
    /// </summary>
    private static bool IsActuallyALivery(string pkgPath)
    {
        var ownNames = FindSimObjectNames(pkgPath).Select(n => n.ToLowerInvariant()).ToHashSet();
        if (ownNames.Count == 0) return false;

        foreach (var baseName in FindBaseContainers(pkgPath))
        {
            if (!ownNames.Contains(baseName.ToLowerInvariant())) return true;
        }
        return false;
    }

    // Registration/tail-number-shaped token, e.g. "D-AIKL", "B-6113",
    // "N707RA" - common in repaint pack names, but this is just a text
    // pattern (unlike IsActuallyALivery's filesystem fact) and can be
    // wrong in either direction, so it's only ever used to *suggest* a fix
    // for the user to confirm, never to reclassify automatically.
    private static readonly Regex RegistrationRegex = new(@"\b([A-Z]{1,2}-[A-Z0-9]{3,5}|N\d{1,5}[A-Z]{0,2})\b");

    /// <summary>
    /// Weak, text-only signal for an AIRCRAFT-declared package that
    /// survived IsActuallyALivery (a genuinely self-contained package with
    /// no external base_container - common for payware repaints that
    /// bundle a full copy instead of a real base_container livery) but
    /// still looks like a registration/repaint pack by name. Only meant to
    /// flag a suggestion in the UI, never to auto-reclassify.
    /// </summary>
    public static bool LooksLikeALiveryByName(string folderName, string displayName)
    {
        var combined = $"{folderName} {displayName}";
        if (Regex.IsMatch(combined, @"\bliver(y|ies)\b", RegexOptions.IgnoreCase)) return true;
        return RegistrationRegex.IsMatch(combined);
    }

    /// <summary>
    /// Returns (aircraft, liveries) - see the Python original's long
    /// docstring for the full reasoning; summarized: "developer" comes
    /// from the folder-name prefix (reliable, same developer uses it
    /// across their whole lineup), display name resolution order is (1)
    /// the curated developers table, (2) manifest "creator" field but only
    /// when ALL aircraft (not liveries) sharing a prefix agree, (3) a
    /// neutral prettified prefix as last resort. content_type gets
    /// corrected AIRCRAFT-&gt;LIVERY via IsActuallyALivery (a filesystem
    /// fact); a surviving AIRCRAFT record additionally carries
    /// "suspected_livery" as a name-only suggestion, never auto-applied.
    /// Physical folder location = enabled/disabled state, same as scenery.
    /// </summary>
    public static (List<JsonObject> Aircraft, List<JsonObject> Liveries) ScanAircraftAndLiveries(
        string? communityPath, IReadOnlyList<string> disabledPaths, IReadOnlyDictionary<string, string> knownDevelopers)
    {
        if (string.IsNullOrEmpty(communityPath) || !Directory.Exists(communityPath))
        {
            return (new List<JsonObject>(), new List<JsonObject>());
        }

        var aircraftByName = new Dictionary<string, JsonObject>();
        var liveryByName = new Dictionary<string, JsonObject>();
        var creatorsByDevKey = new Dictionary<string, (HashSet<string> Aircraft, HashSet<string> All)>();
        var currentPathByFolder = new Dictionary<string, string>();

        var sources = disabledPaths.Select(p => (Path: p, Enabled: false)).Append((Path: communityPath, Enabled: true));

        foreach (var (basePath, enabled) in sources)
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
                var entryName = Path.GetFileName(entryPath);
                var manifest = ManifestReader.Read(entryPath);
                if (manifest is null) continue;

                var contentType = manifest["content_type"] is JsonValue ctv && ctv.TryGetValue<string>(out var ct) ? ct : null;
                if (contentType != "AIRCRAFT" && contentType != "LIVERY") continue;

                if (contentType == "AIRCRAFT" && IsActuallyALivery(entryPath))
                {
                    contentType = "LIVERY";
                }

                var title = ManifestReader.GetString(manifest, "title").Trim();
                var creator = ManifestReader.GetString(manifest, "creator").Trim();
                var devKey = DeveloperKey(entryName);

                if (!creatorsByDevKey.TryGetValue(devKey, out var bucket))
                {
                    bucket = (new HashSet<string>(), new HashSet<string>());
                    creatorsByDevKey[devKey] = bucket;
                }
                if (!string.IsNullOrEmpty(creator))
                {
                    bucket.All.Add(creator);
                    if (contentType == "AIRCRAFT") bucket.Aircraft.Add(creator);
                }

                var displayName = !string.IsNullOrEmpty(title) ? title : SceneryScanner.PrettifyFolderName(entryName);

                var record = new JsonObject
                {
                    ["folder_name"] = entryName,
                    ["display_name"] = displayName,
                    ["dev_key"] = devKey,
                    ["enabled"] = enabled,
                    ["parent_aircraft"] = null,
                    ["suspected_livery"] = contentType == "AIRCRAFT" && LooksLikeALiveryByName(entryName, displayName),
                };

                var target = contentType == "AIRCRAFT" ? aircraftByName : liveryByName;
                target[entryName] = record;
                currentPathByFolder[entryName] = entryPath;
            }
        }

        var devDisplay = new Dictionary<string, string>();
        foreach (var (devKey, bucket) in creatorsByDevKey)
        {
            if (knownDevelopers.TryGetValue(devKey, out var known))
            {
                devDisplay[devKey] = known;
            }
            else if (bucket.Aircraft.Count == 1)
            {
                devDisplay[devKey] = bucket.Aircraft.First();
            }
            else if (bucket.All.Count == 1)
            {
                devDisplay[devKey] = bucket.All.First();
            }
            else
            {
                devDisplay[devKey] = PrettifyDevKey(devKey);
            }
        }

        foreach (var records in new[] { aircraftByName, liveryByName })
        {
            foreach (var record in records.Values)
            {
                var devKey = record["dev_key"]!.GetValue<string>();
                record["developer"] = devDisplay.TryGetValue(devKey, out var d) ? d : PrettifyDevKey(devKey);
            }
        }

        // Map: internal SimObjects folder name of an aircraft -> its package's folder_name.
        var simObjectToAircraft = new Dictionary<string, string>();
        foreach (var (folderName, _) in aircraftByName)
        {
            foreach (var name in FindSimObjectNames(currentPathByFolder[folderName]))
            {
                simObjectToAircraft.TryAdd(name.ToLowerInvariant(), folderName);
            }
        }

        foreach (var (_, record) in liveryByName)
        {
            foreach (var baseName in FindBaseContainers(currentPathByFolder[record["folder_name"]!.GetValue<string>()]))
            {
                if (simObjectToAircraft.TryGetValue(baseName.ToLowerInvariant(), out var match))
                {
                    record["parent_aircraft"] = match;
                    break;
                }
            }
        }

        static int Compare(JsonObject a, JsonObject b)
        {
            var devA = a["developer"]?.GetValue<string>();
            var devB = b["developer"]?.GetValue<string>();
            var devCompare = string.CompareOrdinal(string.IsNullOrEmpty(devA) ? "￿" : devA, string.IsNullOrEmpty(devB) ? "￿" : devB);
            if (devCompare != 0) return devCompare;
            return string.CompareOrdinal(
                a["display_name"]!.GetValue<string>().ToLowerInvariant(),
                b["display_name"]!.GetValue<string>().ToLowerInvariant());
        }

        var aircraft = aircraftByName.Values.ToList();
        var liveries = liveryByName.Values.ToList();
        aircraft.Sort(Compare);
        liveries.Sort(Compare);
        return (aircraft, liveries);
    }
}
