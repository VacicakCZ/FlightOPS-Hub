namespace FlightOpsHub.Core;

/// <summary>
/// Port of the two path-resolution helpers from the top of the (much
/// larger, not yet ported) scenery_data.py -
/// get_default_disabled_path/resolve_disabled_locations - split out on
/// their own since disk_space_status/scan_community_diagnostics need them
/// without the rest of that module's scanning logic.
/// </summary>
public static class DisabledLocations
{
    public static string GetDefaultDisabledPath(string communityPath)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(communityPath)) ?? "";
        return Path.Combine(parent, "Community_disabled_by_FlightOpsHub");
    }

    /// <summary>
    /// Returns the known "disabled" folders, primary first (either
    /// customPath or the default sibling-of-Community folder) - created if
    /// missing. If the default folder differs from the primary and
    /// already exists, it's appended too, for visibility only (nothing
    /// new gets written there once the user has switched to a custom path).
    /// </summary>
    public static List<string> ResolveDisabledLocations(string communityPath, string? customPath)
    {
        var defaultPath = GetDefaultDisabledPath(communityPath);
        customPath = (customPath ?? "").Trim();
        var primary = !string.IsNullOrEmpty(customPath) ? Path.GetFullPath(customPath) : defaultPath;
        Directory.CreateDirectory(primary);

        var locations = new List<string> { primary };
        if (!string.Equals(Path.GetFullPath(defaultPath), primary, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(defaultPath))
        {
            locations.Add(defaultPath);
        }
        return locations;
    }
}
