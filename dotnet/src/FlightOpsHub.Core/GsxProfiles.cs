using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/gsx_profiles.py - detects installed GSX (virtuali)
/// airport profiles by scanning their profile folder's filenames
/// ("{icao}-{author/variant}.ini"), and makes a best-effort guess at
/// whether an installed profile matches the scenery's own developer.
///
/// The Python original duplicates its own _developer_key/_prettify_dev_key
/// rather than importing scenery_data.py's, specifically to keep that
/// module untouched during the original migration. That constraint
/// doesn't apply to this from-scratch port, so this reuses
/// AircraftScanner.DeveloperKey/PrettifyDevKey directly - same logic, no
/// duplication.
/// </summary>
public static class GsxProfiles
{
    private static readonly Regex IcaoPrefixRegex = new(@"^([A-Za-z]{4})[-_ ]");

    public static string DefaultGsxPath()
    {
        var appdata = Environment.GetEnvironmentVariable("APPDATA");
        return !string.IsNullOrEmpty(appdata) ? Path.Combine(appdata, "virtuali", "GSX", "MSFS") : "";
    }

    /// <summary>Returns {icao: [filename, ...]} for every GSX profile file found.</summary>
    public static Dictionary<string, List<string>> ScanProfiles(string? gsxPath)
    {
        var profiles = new Dictionary<string, List<string>>();
        if (string.IsNullOrEmpty(gsxPath) || !Directory.Exists(gsxPath)) return profiles;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(gsxPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return profiles;
        }

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var match = IcaoPrefixRegex.Match(fileName);
            if (match.Success)
            {
                var icao = match.Groups[1].Value.ToUpperInvariant();
                if (!profiles.TryGetValue(icao, out var list))
                {
                    list = new List<string>();
                    profiles[icao] = list;
                }
                list.Add(fileName);
            }
        }
        return profiles;
    }

    public static string StatusFor(string developerKey, IReadOnlyList<string>? filenames)
    {
        if (filenames is null || filenames.Count == 0) return "missing";
        return filenames.Any(name => name.ToLowerInvariant().Contains(developerKey)) ? "installed_match" : "installed_unmatched";
    }

    /// <summary>
    /// Adds gsx_status to each record in place: "installed_match" (profile
    /// found, looks like the same developer), "installed_unmatched"
    /// (profile found, can't confirm the developer), "missing" (no
    /// profile at all), or null for records with no recognizable ICAO.
    /// Also attaches the ICAO itself and the scenery's own developer name.
    /// </summary>
    public static void AttachGsxStatus(IReadOnlyList<JsonObject> records, string? gsxPath)
    {
        var profiles = ScanProfiles(gsxPath);
        foreach (var record in records)
        {
            var icao = ScenerySimbriefMatch.IcaoFromDisplayName(record["display_name"]?.GetValue<string>());
            record["icao"] = icao;
            var devKey = AircraftScanner.DeveloperKey(record["folder_name"]!.GetValue<string>());
            record["developer"] = AircraftScanner.PrettifyDevKey(devKey);

            if (icao is null)
            {
                record["gsx_status"] = null;
                continue;
            }
            record["gsx_status"] = StatusFor(devKey, profiles.GetValueOrDefault(icao));
        }
    }
}
