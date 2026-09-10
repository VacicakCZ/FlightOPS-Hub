namespace FlightOpsHub.Core;

/// <summary>
/// Manages a shortcut in the current user's Startup folder
/// (%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup) so FlightOps
/// Hub can launch itself automatically when Windows starts - the other
/// half of the "launch at startup" + "minimize to tray" feature that
/// shipped once after v2.2.0 and was fully reverted after a reproducible
/// crash (see the [[flightops-tray-feature-reverted]] memory; the tray
/// half was rebuilt in MainWindowTrayIcon.cs). The shortcut's own presence
/// IS the enabled/disabled state - no separate config flag to keep in
/// sync, matching how Windows itself tracks every other Startup app.
///
/// Uses the WScript.Shell COM object (late-bound via ProgID, no extra
/// package/reference needed) to write a real .lnk file - .NET has no
/// built-in shortcut-file API, and this is the standard, well-established
/// way to create one from managed code.
/// </summary>
public static class StartupShortcut
{
    // Matches the name the installer's [UninstallDelete] entry cleans up
    // on uninstall (belt-and-suspenders: this shortcut is created/removed
    // by the app itself at runtime, not by Inno's own [Icons] tracking, so
    // it wouldn't otherwise be known to the uninstaller at all).
    private const string ShortcutFileName = "FlightOps Hub.lnk";

    // Matches the flag flightops_hub.pyw's own MainWindow checks for to
    // start minimized straight to tray, rather than flashing a full
    // window open right after Windows boots.
    private const string MinimizedArg = "--minimized";

    public static string ShortcutPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            ShortcutFileName);

    public static bool IsEnabled() => File.Exists(ShortcutPath());

    public static void Enable(string exePath)
    {
        // This whole project targets plain net10.0 rather than
        // net10.0-windows (so it stays unit-testable without a WPF/
        // WinForms dependency) even though it's Windows-only in practice
        // throughout (P/Invoke, MSFS/Windows-specific paths) - the
        // analyzer only actually flags this one call, the sole direct use
        // of a [SupportedOSPlatform("windows")]-annotated BCL API.
#pragma warning disable CA1416
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell COM object is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(ShortcutPath());
            try
            {
                shortcut.TargetPath = exePath;
                shortcut.Arguments = MinimizedArg;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                shortcut.IconLocation = exePath;
                shortcut.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
#pragma warning restore CA1416
    }

    public static void Disable()
    {
        var path = ShortcutPath();
        if (File.Exists(path)) File.Delete(path);
    }
}
