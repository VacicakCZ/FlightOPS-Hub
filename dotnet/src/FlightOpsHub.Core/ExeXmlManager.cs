using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/exe_xml_manager.py - reads/writes MSFS's own AutoStart
/// config (exe.xml). Every write re-parses the file fresh and does a single
/// backup-then-rewrite rather than keeping a long-lived document in memory -
/// simpler, and self-healing if the file changes on disk between calls.
/// </summary>
public static class ExeXmlManager
{
    public static string GetExeXmlPath(string simVersion, string simPlatform)
    {
        var roaming = Environment.GetEnvironmentVariable("APPDATA") ?? "";
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";

        if (simVersion == "MSFS 2020")
        {
            return simPlatform == "Steam"
                ? Path.Combine(roaming, "Microsoft Flight Simulator", "exe.xml")
                : Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "exe.xml");
        }

        return simPlatform == "Steam"
            ? Path.Combine(roaming, "Microsoft Flight Simulator 2024", "exe.xml")
            : Path.Combine(local, "Packages", "Microsoft.FlightSimulator2024_8wekyb3d8bbwe", "LocalCache", "exe.xml");
    }

    /// <summary>
    /// Yields (uniqueKey, originalName, addonElement) for each Launch.Addon.
    /// The key is the addon's Path when it has one (stable across MSFS
    /// reordering exe.xml itself); only path-less entries fall back to an
    /// index-based key.
    /// </summary>
    private static IEnumerable<(string UniqueKey, string OriginalName, XElement Element)> IterAddons(XElement root)
    {
        var addons = root.Elements("Launch.Addon").ToList();
        for (var i = 0; i < addons.Count; i++)
        {
            var addon = addons[i];
            var addonPath = addon.Element("Path")?.Value ?? "";

            string originalName;
            var nameText = addon.Element("Name")?.Value;
            if (!string.IsNullOrEmpty(nameText))
            {
                originalName = nameText;
            }
            else if (!string.IsNullOrEmpty(addonPath))
            {
                originalName = Path.GetFileName(addonPath);
            }
            else
            {
                continue;
            }

            var uniqueKey = !string.IsNullOrEmpty(addonPath) ? addonPath : $"{originalName}_{i}";
            yield return (uniqueKey, originalName, addon);
        }
    }

    public static List<JsonObject> ListAddons(string xmlPath, JsonObject customNames)
    {
        var root = XDocument.Load(xmlPath).Root!;
        var records = new List<JsonObject>();
        foreach (var (uniqueKey, originalName, addonNode) in IterAddons(root))
        {
            var disabledText = addonNode.Element("Disabled")?.Value;
            var isDisabled = !string.IsNullOrEmpty(disabledText) && disabledText.Equals("true", StringComparison.OrdinalIgnoreCase);
            var displayName = customNames[uniqueKey] is JsonValue dn && dn.TryGetValue<string>(out var dnValue) ? dnValue : originalName;

            records.Add(new JsonObject
            {
                ["unique_key"] = uniqueKey,
                ["original_name"] = originalName,
                ["display_name"] = displayName,
                ["enabled"] = !isDisabled,
            });
        }
        return records;
    }

    public static string BackupPathFor(string xmlPath)
    {
        return Path.Combine(Path.GetDirectoryName(xmlPath) ?? "", "exe_FlightOpsHub_backup.xml");
    }

    public static bool HasBackup(string xmlPath) => File.Exists(BackupPathFor(xmlPath));

    /// <summary>
    /// Overwrites xmlPath with the pre-first-edit backup FlightOps Hub made
    /// (see BackupOnce), i.e. the state before this app ever touched the
    /// file. Returns true if a backup existed and was restored.
    /// </summary>
    public static bool RestoreFromBackup(string xmlPath)
    {
        var backupPath = BackupPathFor(xmlPath);
        if (!File.Exists(backupPath)) return false;
        File.Copy(backupPath, xmlPath, overwrite: true);
        return true;
    }

    private static void BackupOnce(string xmlPath)
    {
        var backupPath = BackupPathFor(xmlPath);
        if (!File.Exists(backupPath))
        {
            try { File.Copy(xmlPath, backupPath); } catch { /* best-effort, matches Python */ }
        }
    }

    /// <summary>
    /// desiredStates: {uniqueKey: enabled}. Addons not present in the dict
    /// are left untouched. Returns true if anything was written.
    /// </summary>
    public static bool SetAddonsEnabled(string xmlPath, IReadOnlyDictionary<string, bool> desiredStates)
    {
        var doc = XDocument.Load(xmlPath);
        var root = doc.Root!;

        var changed = false;
        foreach (var (uniqueKey, _, addonNode) in IterAddons(root))
        {
            if (!desiredStates.TryGetValue(uniqueKey, out var enabled)) continue;

            var disabledNode = addonNode.Element("Disabled");
            if (disabledNode is null)
            {
                disabledNode = new XElement("Disabled");
                addonNode.Add(disabledNode);
            }
            disabledNode.Value = enabled ? "False" : "True";
            changed = true;
        }

        if (changed)
        {
            BackupOnce(xmlPath);
            // Write to a temp file first, then an atomic move - this is
            // MSFS's own AutoStart config, not just this app's data, so
            // getting killed/crashing/losing power mid-write must never
            // leave a truncated exe.xml behind (same reasoning as
            // ConfigManager.SaveConfig).
            var tmpPath = xmlPath + ".tmp";
            try
            {
                doc.Save(tmpPath);
                File.Move(tmpPath, xmlPath, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmpPath); } catch { /* nothing to clean up */ }
                throw;
            }
        }
        return changed;
    }
}
