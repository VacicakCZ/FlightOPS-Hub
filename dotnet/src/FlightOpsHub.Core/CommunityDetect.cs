namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/community_detect.py - best-effort detection of the
/// default Community folder location for the selected MSFS version/
/// platform. Only ever offered as a suggestion: the candidate is checked
/// to actually exist on disk before being returned, never assumed.
/// </summary>
public static class CommunityDetect
{
    public static string DefaultCommunityPath(string simVersion, string simPlatform)
    {
        var roaming = Environment.GetEnvironmentVariable("APPDATA") ?? "";
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";

        if (simVersion == "MSFS 2020")
        {
            return simPlatform == "Steam"
                ? Path.Combine(roaming, "Microsoft Flight Simulator", "Packages", "Community")
                : Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages", "Community");
        }

        return simPlatform == "Steam"
            ? Path.Combine(roaming, "Microsoft Flight Simulator 2024", "Packages", "Community")
            : Path.Combine(local, "Packages", "Microsoft.FlightSimulator2024_8wekyb3d8bbwe", "LocalCache", "Packages", "Community");
    }

    /// <summary>Returns the default path for this sim/platform combo if it actually exists on disk, else null.</summary>
    public static string? DetectCommunityPath(string simVersion, string simPlatform)
    {
        var candidate = DefaultCommunityPath(simVersion, simPlatform);
        return Directory.Exists(candidate) ? candidate : null;
    }
}
