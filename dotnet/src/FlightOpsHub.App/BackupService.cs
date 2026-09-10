using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlightOpsHub.Core;

namespace FlightOpsHub.App;

/// <summary>
/// Port of backend/api.py's export_config/import_config/export_diagnostics -
/// wires BackupManager/DiagnosticsBuilder to real dialogs and disk IO
/// (App-project-only since it needs DialogsService).
/// </summary>
public class BackupService
{
    private readonly ConfigService _configService;
    private readonly AppsService _appsService;
    private readonly string _logPath;

    public BackupService(ConfigService configService, AppsService appsService, string logPath)
    {
        _configService = configService;
        _appsService = appsService;
        _logPath = logPath;
    }

    public JsonObject ExportConfig()
    {
        var path = DialogsService.SaveFile(BackupManager.DefaultFilename(), "JSON files (*.json)");
        if (path is null) return new JsonObject { ["ok"] = false };

        var bundle = BackupManager.BuildBundle(_configService.Snapshot(), _appsService.List());
        try
        {
            // Matches Python's json.dump(bundle, f, indent=2, ensure_ascii=False);
            // the relaxed encoder avoids .NET's default \uXXXX-escaping of
            // '<'/'>'/'&' etc. (see ConfigManager/DiagnosticsBuilder).
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(path, bundle.ToJsonString(options));
        }
        catch (Exception ex)
        {
            AppLogging.Error(_logPath, $"Config export failed: {ex}");
            return new JsonObject { ["ok"] = false, ["error"] = ex.Message };
        }
        AppLogging.Info(_logPath, $"Config exported to {path}");
        return new JsonObject { ["ok"] = true, ["path"] = path };
    }

    public JsonObject ImportConfig()
    {
        var path = DialogsService.BrowseFile(null, new[] { "JSON files (*.json)" });
        if (path is null) return new JsonObject { ["ok"] = false };

        JsonObject newConfig;
        JsonObject newApps;
        try
        {
            var data = JsonNode.Parse(File.ReadAllText(path));
            (newConfig, newApps) = BackupManager.ParseBundle(data);
        }
        catch (Exception ex)
        {
            AppLogging.Warning(_logPath, $"Config import failed ({path}): {ex.Message}");
            return new JsonObject { ["ok"] = false, ["error"] = ex.Message };
        }

        var configSnapshot = _configService.ReplaceAll(newConfig);
        var appsSnapshot = _appsService.ReplaceAll(newApps);
        AppLogging.Info(_logPath, $"Config imported from {path}");
        return new JsonObject { ["ok"] = true, ["config"] = configSnapshot, ["apps"] = appsSnapshot };
    }

    public JsonObject ExportDiagnostics()
    {
        var path = DialogsService.SaveFile(DiagnosticsBuilder.DefaultFilename(), "Text files (*.txt)");
        if (path is null) return new JsonObject { ["ok"] = false };

        var osInfo = $"{Environment.OSVersion.VersionString} ({Environment.OSVersion.Platform})";
        var report = DiagnosticsBuilder.BuildReport(_configService.Snapshot(), osInfo, AppLogging.ReadRecentLines(_logPath));
        try
        {
            File.WriteAllText(path, report);
        }
        catch (Exception ex)
        {
            AppLogging.Error(_logPath, $"Diagnostics export failed: {ex}");
            return new JsonObject { ["ok"] = false, ["error"] = ex.Message };
        }
        AppLogging.Info(_logPath, $"Diagnostics exported to {path}");
        return new JsonObject { ["ok"] = true, ["path"] = path };
    }
}
