using System.ComponentModel;
using System.Diagnostics;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/process_utils.py - Windows process helpers used by the
/// launch pipeline. Simpler than the Python original in one respect: .NET's
/// ProcessStartInfo(UseShellExecute=true, Verb="runas") triggers the UAC
/// elevation prompt natively, so no win32/ctypes ShellExecuteEx P/Invoke
/// (win_native.py's run_as_admin) is needed here at all.
/// </summary>
public static class ProcessUtils
{
    private static readonly Dictionary<(string SimVersion, string SimPlatform), string> MsfsLaunchCommands = new()
    {
        [("MSFS 2020", "Steam")] = "steam://rungameid/1250410",
        [("MSFS 2020", "MS Store")] = @"shell:AppsFolder\Microsoft.FlightSimulator_8wekyb3d8bbwe!App",
        [("MSFS 2024", "Steam")] = "steam://rungameid/2537590",
        [("MSFS 2024", "MS Store")] = @"shell:AppsFolder\Microsoft.FlightSimulator2024_8wekyb3d8bbwe!App",
    };
    private const string DefaultMsfsLaunchCommand = "steam://rungameid/2537590";

    public static string TasklistLower()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("tasklist")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            var output = proc!.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return output.ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    public static bool IsMsfsRunning(string tasksLower) =>
        tasksLower.Contains("flightsimulator.exe") || tasksLower.Contains("flightsimulator2024.exe") || tasksLower.Contains("flightsimulator");

    public static bool IsExeRunning(string path, string tasksLower) =>
        tasksLower.Contains(Path.GetFileName(path).ToLowerInvariant());

    /// <summary>Returns the launched PID, or null on failure.</summary>
    public static int? RunApp(string path, bool asAdmin = false)
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (asAdmin) return RunAsAdmin(path);
            using var proc = Process.Start(new ProcessStartInfo(path)
            {
                WorkingDirectory = Path.GetDirectoryName(path) is { Length: > 0 } dir ? dir : null,
                UseShellExecute = false,
            });
            return proc?.Id;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740) // ERROR_ELEVATION_REQUIRED - the exe demands admin via its own manifest
        {
            return RunAsAdmin(path);
        }
        catch
        {
            return null;
        }
    }

    private static int? RunAsAdmin(string path)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(path)
            {
                WorkingDirectory = Path.GetDirectoryName(path) is { Length: > 0 } dir ? dir : null,
                UseShellExecute = true,
                Verb = "runas",
            });
            return proc?.Id;
        }
        catch
        {
            // User declined the UAC prompt, or the launch failed outright.
            return null;
        }
    }

    public static void TerminatePid(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo("taskkill")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("/PID");
            psi.ArgumentList.Add(pid.ToString());
            psi.ArgumentList.Add("/F");
            psi.ArgumentList.Add("/T");
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        catch
        {
            // Best-effort, matches Python's bare except here.
        }
    }

    public static void LaunchMsfs(string simVersion, string simPlatform)
    {
        var command = MsfsLaunchCommands.GetValueOrDefault((simVersion, simPlatform), DefaultMsfsLaunchCommand);
        try
        {
            Process.Start(new ProcessStartInfo(command) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort - matches Python's os.system() call, which also never raises.
        }
    }
}
