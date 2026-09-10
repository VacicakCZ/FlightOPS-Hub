namespace FlightOpsHub.Core;

/// <summary>
/// Resolves where this app's own writable data (config.json, apps.json,
/// the log file) lives. The portable Python app and the .NET app's early
/// phases both wrote these next to the executable - fine for a portable
/// build, but installed builds can't assume their own install folder is
/// writable (a per-user Inno Setup install under %LocalAppData%\Programs
/// actually is, but relying on that is fragile, and mixing user data into
/// the program folder makes a clean uninstall/upgrade ambiguous about what
/// to keep). Since v3.0.0 has no prior installed user base to migrate,
/// this switches straight to %LocalAppData%\FlightOpsHub with no
/// migration step - see the .NET rewrite plan's installer phase.
/// </summary>
public static class AppPaths
{
    public static string UserDataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlightOpsHub");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
