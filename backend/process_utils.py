"""Windows process helpers used by the launch pipeline - tasklist checks,
starting an app (with UAC elevation fallback), killing a PID, and building
the right Steam/MS Store launch command for MSFS.
"""
import os
import subprocess

from . import win_native

_MSFS_LAUNCH_COMMANDS = {
    ("MSFS 2020", "Steam"): "start steam://rungameid/1250410",
    ("MSFS 2020", "MS Store"): "start shell:AppsFolder\\Microsoft.FlightSimulator_8wekyb3d8bbwe!App",
    ("MSFS 2024", "Steam"): "start steam://rungameid/2537590",
    ("MSFS 2024", "MS Store"): "start shell:AppsFolder\\Microsoft.FlightSimulator2024_8wekyb3d8bbwe!App",
}
_DEFAULT_MSFS_LAUNCH_COMMAND = "start steam://rungameid/2537590"


def tasklist_lower():
    try:
        return os.popen("tasklist").read().lower()
    except Exception:
        return ""


def is_msfs_running(tasks):
    return "flightsimulator.exe" in tasks or "flightsimulator2024.exe" in tasks or "flightsimulator" in tasks


def is_exe_running(path, tasks):
    return os.path.basename(path).lower() in tasks


def run_app(path, as_admin=False):
    """Returns the launched PID, or None on failure - mirrors the old run_app/_run_as_admin."""
    if not os.path.exists(path):
        return None
    try:
        if as_admin:
            return win_native.run_as_admin(path, cwd=os.path.dirname(path) or None)
        proc = subprocess.Popen([path], cwd=os.path.dirname(path) or None)
        return proc.pid
    except OSError as e:
        if getattr(e, "winerror", None) == 740:
            # ERROR_ELEVATION_REQUIRED - the exe demands admin via its own manifest
            return win_native.run_as_admin(path, cwd=os.path.dirname(path) or None)
        return None
    except Exception:
        return None


def terminate_pid(pid):
    try:
        os.system(f"taskkill /PID {pid} /F /T >nul 2>&1")
    except Exception:
        pass


def launch_msfs(sim_version, sim_platform):
    command = _MSFS_LAUNCH_COMMANDS.get((sim_version, sim_platform), _DEFAULT_MSFS_LAUNCH_COMMAND)
    os.system(command)
