using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/backup.py - export/import bundle for the app's own
/// settings (config + apps combined into one portable JSON file). Pure
/// data shaping only; the file dialog and disk IO live in the service that
/// calls this.
/// </summary>
public static class BackupManager
{
    public const int BundleVersion = 1;

    public static JsonObject BuildBundle(JsonObject config, JsonObject apps)
    {
        return new JsonObject
        {
            ["bundle_version"] = BundleVersion,
            ["app_version"] = AppVersion.Current,
            ["exported_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["config"] = config.DeepClone(),
            ["apps"] = apps.DeepClone(),
        };
    }

    public static string DefaultFilename() => $"flightops_hub_backup_{DateTime.Now:yyyy-MM-dd}.json";

    /// <summary>
    /// Validates the loaded JSON has the expected shape. Throws
    /// FormatException with a short message on anything unrecognized - the
    /// caller turns that into a user-facing error rather than silently
    /// importing garbage into the running config.
    /// </summary>
    public static (JsonObject Config, JsonObject Apps) ParseBundle(JsonNode? data)
    {
        if (data is not JsonObject obj)
        {
            throw new FormatException("not a FlightOps Hub backup file");
        }
        if (obj["config"] is not JsonObject config || obj["apps"] is not JsonObject apps)
        {
            throw new FormatException("missing config/apps in backup file");
        }
        return (config, apps);
    }
}
