using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of scan_scenery_packages/_build_record/_prettify_folder_name from
/// scenery_data.py. Physical folder location IS the enabled/disabled
/// state - no separate state file to drift from reality.
/// </summary>
public static class SceneryScanner
{
    public static string PrettifyFolderName(string folderName)
    {
        var cleaned = folderName.Replace("-", " ").Replace("_", " ").Trim();
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    private static JsonObject? BuildRecord(string folderName, string folderPath, bool enabled)
    {
        var manifest = ManifestReader.Read(folderPath);
        if (manifest is null || ManifestReader.GetString(manifest, "content_type") != "SCENERY")
        {
            return null;
        }

        var title = ManifestReader.GetString(manifest, "title").Trim();
        var (country, continent) = SceneryCategorization.CategorizeScenery(folderName, title);
        var icao = SceneryCategorization.FindIcaoCode(folderName, title);

        var baseName = !string.IsNullOrEmpty(title) ? title : PrettifyFolderName(folderName);
        var displayName = icao != null ? $"[{icao}] {baseName}" : baseName;

        return new JsonObject
        {
            ["folder_name"] = folderName,
            ["display_name"] = displayName,
            ["country"] = country,
            ["continent"] = continent,
            ["enabled"] = enabled,
        };
    }

    /// <summary>
    /// Scenery (content_type == SCENERY only) from Community and every
    /// known disabled location (see DisabledLocations.ResolveDisabledLocations).
    /// Community is scanned last - on a name conflict (should not happen in
    /// practice), "enabled" wins as the safer default.
    /// </summary>
    public static List<JsonObject> ScanSceneryPackages(string? communityPath, IReadOnlyList<string> disabledPaths)
    {
        if (string.IsNullOrEmpty(communityPath) || !Directory.Exists(communityPath))
        {
            return new List<JsonObject>();
        }

        var recordsByName = new Dictionary<string, JsonObject>();
        var sources = disabledPaths.Select(p => (Path: p, Enabled: false))
            .Append((Path: communityPath, Enabled: true));

        foreach (var (basePath, enabled) in sources)
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateDirectories(basePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entryPath in entries)
            {
                var name = Path.GetFileName(entryPath);
                var record = BuildRecord(name, entryPath, enabled);
                if (record != null)
                {
                    recordsByName[name] = record;
                }
            }
        }

        var records = recordsByName.Values.ToList();
        records.Sort((a, b) =>
        {
            var continentCompare = SceneryCategorization.ContinentSortKey(a["continent"]!.GetValue<string>())
                .CompareTo(SceneryCategorization.ContinentSortKey(b["continent"]!.GetValue<string>()));
            if (continentCompare != 0) return continentCompare;

            var countryA = a["country"]?.GetValue<string>() ?? "￿";
            var countryB = b["country"]?.GetValue<string>() ?? "￿";
            var countryCompare = string.CompareOrdinal(countryA, countryB);
            if (countryCompare != 0) return countryCompare;

            return string.CompareOrdinal(
                a["display_name"]!.GetValue<string>().ToLowerInvariant(),
                b["display_name"]!.GetValue<string>().ToLowerInvariant());
        });
        return records;
    }
}
