using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/aircraft_overrides.py - applies user-supplied
/// corrections on top of AircraftScanner's best-effort classification
/// (manifest content_type for aircraft-vs-livery, base_container matching
/// for a livery's parent aircraft) - both are heuristics and can be wrong,
/// so the user can override either per package.
/// </summary>
public static class AircraftOverrides
{
    /// <summary>
    /// typeOverrides: {folderName: "aircraft"|"livery"} - reclassifies a
    /// package regardless of what its own manifest said.
    /// parentOverrides: {folderName: parentFolderName} where "" means
    /// "explicitly unassigned" and an absent key means "use the heuristic
    /// (base_container) result".
    /// dismissedSuggestions: folder names the user has dismissed the
    /// "suspected_livery" hint for - stops the hint from showing again
    /// without forcing any actual reclassification.
    ///
    /// Returns new lists; inputs are not mutated.
    /// </summary>
    public static (List<JsonObject> Aircraft, List<JsonObject> Liveries) ApplyOverrides(
        IReadOnlyList<JsonObject> aircraft,
        IReadOnlyList<JsonObject> liveries,
        IReadOnlyDictionary<string, string> typeOverrides,
        IReadOnlyDictionary<string, string> parentOverrides,
        IReadOnlySet<string>? dismissedSuggestions = null)
    {
        dismissedSuggestions ??= new HashSet<string>();

        var combined = new Dictionary<string, JsonObject>();
        foreach (var r in aircraft.Concat(liveries))
        {
            combined[r["folder_name"]!.GetValue<string>()] = r;
        }
        var originalAircraftFolders = aircraft.Select(r => r["folder_name"]!.GetValue<string>()).ToHashSet();

        bool IsAircraft(string folderName)
        {
            if (typeOverrides.TryGetValue(folderName, out var over))
            {
                if (over == "aircraft") return true;
                if (over == "livery") return false;
            }
            return originalAircraftFolders.Contains(folderName);
        }

        var finalAircraftFolders = combined.Keys.Where(IsAircraft).ToHashSet();

        var resultAircraft = new List<JsonObject>();
        var resultLiveries = new List<JsonObject>();

        foreach (var (folderName, original) in combined)
        {
            var record = (JsonObject)original.DeepClone();
            if (IsAircraft(folderName))
            {
                record.Remove("parent_aircraft");
                // Once the user has made any explicit decision about this
                // package (an override either way, or an explicit
                // dismissal), the suggestion has done its job.
                if (typeOverrides.ContainsKey(folderName) || dismissedSuggestions.Contains(folderName))
                {
                    record["suspected_livery"] = false;
                }
                resultAircraft.Add(record);
            }
            else
            {
                string? parent;
                if (parentOverrides.TryGetValue(folderName, out var overriddenParent))
                {
                    parent = string.IsNullOrEmpty(overriddenParent) ? null : overriddenParent;
                }
                else
                {
                    parent = record["parent_aircraft"]?.GetValue<string>();
                }
                if (parent is null || !finalAircraftFolders.Contains(parent))
                {
                    parent = null;
                }
                record["parent_aircraft"] = parent;
                resultLiveries.Add(record);
            }
        }

        static int SortCompare(JsonObject a, JsonObject b)
        {
            var devA = a["developer"]?.GetValue<string>();
            var devB = b["developer"]?.GetValue<string>();
            var devCompare = string.CompareOrdinal(string.IsNullOrEmpty(devA) ? "￿" : devA, string.IsNullOrEmpty(devB) ? "￿" : devB);
            if (devCompare != 0) return devCompare;
            return string.CompareOrdinal(
                a["display_name"]!.GetValue<string>().ToLowerInvariant(),
                b["display_name"]!.GetValue<string>().ToLowerInvariant());
        }

        resultAircraft.Sort(SortCompare);
        resultLiveries.Sort(SortCompare);
        return (resultAircraft, resultLiveries);
    }
}
