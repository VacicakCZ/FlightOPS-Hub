using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>Port of backend/community_paths.py - validation for the "disabled addons holding path" setting.</summary>
public static class CommunityPaths
{
    /// <summary>
    /// "nested" is a hard error (same folder as Community, or one contains
    /// the other) - the caller should not persist the path. "cross_drive"
    /// is only a warning - the path is still valid and should be
    /// persisted, but moving packages there will copy instead of rename.
    /// </summary>
    public static JsonObject ValidateDisabledPath(string disabledPath, string? communityPath)
    {
        if (string.IsNullOrEmpty(communityPath))
        {
            return new JsonObject { ["ok"] = true, ["error"] = null, ["warning"] = null };
        }

        var a = Path.GetFullPath(disabledPath);
        var b = Path.GetFullPath(communityPath);
        var aNorm = a.ToUpperInvariant();
        var bNorm = b.ToUpperInvariant();

        var nested = aNorm == bNorm
            || aNorm.StartsWith(bNorm + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || bNorm.StartsWith(aNorm + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (nested)
        {
            return new JsonObject { ["ok"] = false, ["error"] = "nested", ["warning"] = null };
        }

        var crossDrive = !string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
        return new JsonObject { ["ok"] = true, ["error"] = null, ["warning"] = crossDrive ? "cross_drive" : null };
    }
}
