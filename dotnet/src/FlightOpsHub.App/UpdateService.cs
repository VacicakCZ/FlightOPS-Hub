using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using FlightOpsHub.Core;

namespace FlightOpsHub.App;

/// <summary>
/// Port of backend/api.py's check_for_update/open_update_page/
/// open_release_page/download_update, extended beyond the Python original
/// with a true in-app self-update: once the download finishes, the
/// installer is launched silently and the app closes and relaunches
/// itself - the user never sees an installer window or the Downloads
/// folder.
/// </summary>
public class UpdateService
{
    private readonly EventBus _events;
    private readonly string _logPath;

    private volatile bool _downloadActive;

    public UpdateService(EventBus events, string logPath)
    {
        _events = events;
        _logPath = logPath;
    }

    public Task<JsonObject> CheckForUpdate() => UpdateCheck.CheckForUpdate();

    public JsonObject OpenUpdatePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(UpdateCheck.UpdatePageUrl) { UseShellExecute = true });
            return new JsonObject { ["ok"] = true };
        }
        catch
        {
            return new JsonObject { ["ok"] = false };
        }
    }

    /// <summary>Opens the GitHub release page for the exact version currently running, not just the generic releases list.</summary>
    public JsonObject OpenReleasePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"{UpdateCheck.RepoReleasesUrl}/tag/{AppVersion.Current}") { UseShellExecute = true });
            return new JsonObject { ["ok"] = true };
        }
        catch
        {
            return new JsonObject { ["ok"] = false };
        }
    }

    // Downloaded into the app's own private data directory, not the
    // user's visible Downloads folder - nothing in this flow asks the
    // user to find or double-click the file themselves, so there is no
    // reason to put it somewhere they'd see it (and no risk of colliding
    // with an unrelated file of theirs).
    private static string DestPath()
    {
        var dir = Path.Combine(AppPaths.UserDataDirectory, "update");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, UpdateCheck.ReleaseAssetName);
    }

    /// <summary>
    /// Kicks off a background download of the new release installer.
    /// Progress arrives via update_download_progress. On success, the
    /// installer is launched silently and the app exits to let it replace
    /// the running files (update_installing fires first so the UI can say
    /// so) - Inno Setup's own /RESTARTAPPLICATIONS relaunches the app once
    /// installation completes. On failure, update_download_done carries
    /// the error.
    /// </summary>
    public JsonObject DownloadUpdate(string? downloadUrl)
    {
        if (_downloadActive)
        {
            return new JsonObject { ["ok"] = false, ["error"] = "busy" };
        }
        if (string.IsNullOrEmpty(downloadUrl))
        {
            return new JsonObject { ["ok"] = false, ["error"] = "no_download_url" };
        }

        _downloadActive = true;
        var destPath = DestPath();

        void OnProgress(long downloaded, long? total) =>
            _events.Emit("update_download_progress", new JsonObject { ["downloaded"] = downloaded, ["total"] = total });

        Task.Run(async () =>
        {
            JsonObject result;
            try
            {
                result = await UpdateCheck.DownloadUpdate(downloadUrl, destPath, OnProgress);
            }
            finally
            {
                _downloadActive = false;
            }

            if (!result["ok"]!.GetValue<bool>())
            {
                AppLogging.Warning(_logPath, $"Update download failed: {result["error"]?.GetValue<string>()}");
                _events.Emit("update_download_done", result);
                return;
            }

            AppLogging.Info(_logPath, $"Update download finished: {result["path"]!.GetValue<string>()}");
            _events.Emit("update_installing", new JsonObject());
            InstallAndExit(result["path"]!.GetValue<string>());
        });

        return new JsonObject { ["ok"] = true };
    }

    /// <summary>
    /// Launches the downloaded installer completely silently
    /// (/VERYSILENT - no wizard window at all) and tells it to close this
    /// app's own running files itself and restart it afterward via
    /// Windows Restart Manager, then exits this process so the installer
    /// can actually overwrite the locked exe/dll files. Exiting proactively
    /// (rather than only relying on /CLOSEAPPLICATIONS to force-close us)
    /// keeps the handoff fast and deterministic.
    /// </summary>
    private void InstallAndExit(string installerPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(installerPath,
                "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLogging.Warning(_logPath, $"Failed to launch update installer: {ex.Message}");
            _events.Emit("update_install_failed", new JsonObject { ["ok"] = false, ["error"] = ex.Message });
            return;
        }

        AppLogging.Info(_logPath, "Launched silent update installer, exiting for update.");

        Task.Run(async () =>
        {
            await Task.Delay(600);
            System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current?.Shutdown());
        });
    }
}
