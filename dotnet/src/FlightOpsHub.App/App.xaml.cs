using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using FlightOpsHub.Core;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace FlightOpsHub.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstance();
        if (_singleInstance.AlreadyRunning)
        {
            ShowAlreadyRunningMessage();
            Shutdown();
            return;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Port of flightops_hub.pyw's _show_already_running_message/_get_saved_language.</summary>
    private static void ShowAlreadyRunningMessage()
    {
        var lang = GetSavedLanguageWithoutTouchingConfig();

        var messagesPath = Path.Combine(AppContext.BaseDirectory, "frontend", "locales", "already_running.json");
        var text = "FlightOps Hub is already running.\nCheck your system tray or the taskbar.";
        try
        {
            if (JsonNode.Parse(File.ReadAllText(messagesPath)) is JsonObject messages)
            {
                var localized = messages[lang]?.GetValue<string>() ?? messages["EN"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(localized)) text = localized;
            }
        }
        catch
        {
            // Fall back to the English default above.
        }

        MessageBox.Show(text, "FlightOps Hub", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    }

    /// <summary>
    /// Reads _language directly from the raw config file, without going
    /// through ConfigManager.LoadConfig()'s migration/save path - a second
    /// instance must not write to the config file the running instance
    /// might be using at the same time.
    /// </summary>
    private static string GetSavedLanguageWithoutTouchingConfig()
    {
        try
        {
            var configPath = Path.Combine(AppPaths.UserDataDirectory, ConfigManager.ConfigFileName);
            if (JsonNode.Parse(File.ReadAllText(configPath)) is JsonObject config &&
                config["_language"]?.GetValue<string>() is { Length: > 0 } lang)
            {
                return lang;
            }
        }
        catch
        {
            // Missing/corrupt config - fall back to EN.
        }
        return "EN";
    }
}
