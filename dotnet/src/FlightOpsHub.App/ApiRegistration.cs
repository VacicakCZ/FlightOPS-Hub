using System.Text.Json.Nodes;
using FlightOpsHub.Core;

namespace FlightOpsHub.App;

/// <summary>
/// Wires the ~45 quick synchronous frontend/js/api.js methods (config,
/// apps, profiles, exe.xml, dialogs - per the .NET rewrite plan's phase 2)
/// to their ApiDispatcher handlers.
/// </summary>
public static class ApiRegistration
{
    public static void RegisterAll(
        ApiDispatcher dispatcher,
        ConfigService configService,
        AppsService appsService,
        ProfilesService profilesService,
        ExeXmlService exeXmlService,
        SettingsService settingsService,
        BackupService backupService,
        SceneryService sceneryService,
        LaunchService launchService,
        UpdateService updateService)
    {
        // --- config ---
        dispatcher.Register("config_get", _ => configService.Snapshot());
        dispatcher.Register("config_set", args => configService.Update(ArgObject(args, 0)));
        dispatcher.Register("export_config", _ => backupService.ExportConfig());
        dispatcher.Register("import_config", _ => backupService.ImportConfig());
        dispatcher.Register("export_diagnostics", _ => backupService.ExportDiagnostics());

        // --- app version / update check ---
        dispatcher.Register("app_version", _ => AppVersion.Current);
        dispatcher.RegisterAsync("check_for_update", async _ => await updateService.CheckForUpdate());
        dispatcher.Register("open_update_page", _ => updateService.OpenUpdatePage());
        dispatcher.Register("open_release_page", _ => updateService.OpenReleasePage());
        dispatcher.Register("download_update", args => updateService.DownloadUpdate(ArgStringOrNull(args, 0)));

        // --- settings: community / disabled-holding paths, AIRAC, disk space ---
        dispatcher.Register("settings_set_community_path", args => settingsService.SetCommunityPath(ArgString(args, 0)));
        dispatcher.Register("settings_set_sim_version", args => settingsService.SetSimVersion(ArgString(args, 0)));
        dispatcher.Register("settings_set_sim_platform", args => settingsService.SetSimPlatform(ArgString(args, 0)));
        dispatcher.Register("detect_community_path", _ => settingsService.DetectCommunityPath());
        dispatcher.Register("settings_set_disabled_path", args => settingsService.SetDisabledPath(ArgString(args, 0)));
        dispatcher.Register("settings_reset_disabled_path", _ => settingsService.ResetDisabledPath());
        dispatcher.Register("disk_space_status", _ => settingsService.DiskSpaceStatus());
        dispatcher.Register("airac_status", _ => settingsService.AiracStatus());
        dispatcher.Register("settings_set_launch_at_startup", args => settingsService.SetLaunchAtStartup(
            ArgBool(args, 0), Environment.ProcessPath ?? AppContext.BaseDirectory));

        // --- settings: GSX (virtuali) profile folder ---
        dispatcher.Register("settings_set_gsx_path", args => sceneryService.SetGsxPath(ArgString(args, 0)));
        dispatcher.Register("settings_reset_gsx_path", _ => sceneryService.ResetGsxPath());
        dispatcher.Register("open_gsx_search", args => sceneryService.OpenGsxSearch(ArgStringOrNull(args, 0)));
        dispatcher.Register("open_gsx_folder", _ => sceneryService.OpenGsxFolder());

        // --- apps (external addons) ---
        dispatcher.Register("apps_list", _ => appsService.List());
        dispatcher.Register("apps_save", args => appsService.Save(ArgObject(args, 0)));
        dispatcher.Register("apps_remove", args => appsService.Remove(ArgString(args, 0)));
        dispatcher.Register("apps_reorder", args => appsService.Reorder(ArgString(args, 0), ArgString(args, 1)));
        dispatcher.Register("apps_open_folder", args =>
        {
            AppsManager.OpenFolder(ArgString(args, 0));
            return new JsonObject { ["ok"] = true };
        });

        // --- profiles (kind: "flight" for the Flight tab, "exe" for exe.xml profiles) ---
        dispatcher.Register("profiles_list", args => profilesService.List(ArgString(args, 0)));
        dispatcher.Register("profiles_save", args => profilesService.Save(ArgString(args, 0), ArgString(args, 1), args.Count > 2 ? args[2] : null));
        dispatcher.Register("profiles_delete", args => profilesService.Delete(ArgString(args, 0), ArgString(args, 1)));

        // --- exe.xml (MSFS AutoStart) ---
        dispatcher.Register("settings_set_exe_xml_path", args => exeXmlService.SetExeXmlPath(ArgString(args, 0)));
        dispatcher.Register("settings_reset_exe_xml_path", _ => exeXmlService.ResetExeXmlPath());
        dispatcher.Register("exe_list", _ => exeXmlService.ListAddons());
        dispatcher.Register("exe_restore_backup", _ => exeXmlService.RestoreBackup());
        dispatcher.Register("exe_toggle", args => exeXmlService.Toggle(ArgString(args, 0), ArgBool(args, 1)));
        dispatcher.Register("exe_rename", args => exeXmlService.Rename(ArgString(args, 0), ArgString(args, 1), ArgStringOrNull(args, 2)));
        dispatcher.Register("exe_apply_profile", args => exeXmlService.ApplyProfile(ArgString(args, 0)));

        // --- scenery / aircraft scanning (scan only - apply is async, plan phase 5) ---
        dispatcher.Register("scenery_scan", _ => sceneryService.SceneryScan());
        dispatcher.Register("scenery_is_busy", _ => sceneryService.SceneryIsBusy());
        dispatcher.Register("aircraft_scan", _ => sceneryService.AircraftScan());
        dispatcher.Register("aircraft_is_busy", _ => sceneryService.AircraftIsBusy());
        dispatcher.Register("aircraft_set_type_override", args => sceneryService.SetAircraftTypeOverride(ArgString(args, 0), ArgStringOrNull(args, 1)));
        dispatcher.Register("aircraft_set_parent_override", args => sceneryService.SetAircraftParentOverride(ArgString(args, 0), ArgStringOrNull(args, 1)));
        dispatcher.Register("aircraft_dismiss_livery_suggestion", args => sceneryService.DismissLiverySuggestion(ArgString(args, 0)));
        dispatcher.Register("scenery_map_data", _ => sceneryService.SceneryMapData());
        dispatcher.Register("scan_community_diagnostics", _ => sceneryService.ScanCommunityDiagnostics());
        dispatcher.RegisterAsync("simbrief_check", async _ => await sceneryService.SimbriefCheck());
        dispatcher.Register("scan_community_usage", _ => sceneryService.ScanCommunityUsage());
        dispatcher.Register("scan_disabled_usage", _ => sceneryService.ScanDisabledUsage());
        dispatcher.Register("scenery_apply", args => sceneryService.SceneryApply(ArgBoolDict(args, 0)));
        dispatcher.Register("aircraft_apply", args => sceneryService.AircraftApply(ArgBoolDict(args, 0)));

        // --- launch ---
        dispatcher.Register("launch_precheck", args => launchService.LaunchPrecheck(ArgObject(args, 0)));
        dispatcher.Register("launch_all", args => launchService.LaunchAll(ArgObject(args, 0), ArgString(args, 1)));

        // --- dialogs ---
        dispatcher.Register("dialogs_browse_folder", args => DialogsService.BrowseFolder(ArgStringOrNull(args, 0)));
        dispatcher.Register("dialogs_browse_file", args =>
        {
            var fileTypes = args.Count > 1 && args[1] is JsonArray typesArray
                ? typesArray.Select(n => n!.GetValue<string>()).ToList()
                : null;
            return DialogsService.BrowseFile(ArgStringOrNull(args, 0), fileTypes);
        });
    }

    private static JsonObject ArgObject(JsonArray args, int index) =>
        args.Count > index ? args[index] as JsonObject ?? new JsonObject() : new JsonObject();

    private static string ArgString(JsonArray args, int index) =>
        args.Count > index ? args[index]?.GetValue<string>() ?? "" : "";

    private static string? ArgStringOrNull(JsonArray args, int index) =>
        args.Count > index ? args[index]?.GetValue<string>() : null;

    private static bool ArgBool(JsonArray args, int index) =>
        args.Count > index && (args[index]?.GetValue<bool>() ?? false);

    private static Dictionary<string, bool> ArgBoolDict(JsonArray args, int index)
    {
        var result = new Dictionary<string, bool>();
        if (args.Count <= index || args[index] is not JsonObject obj) return result;
        foreach (var (key, value) in obj)
        {
            if (value is JsonValue v && v.TryGetValue<bool>(out var b)) result[key] = b;
        }
        return result;
    }
}
