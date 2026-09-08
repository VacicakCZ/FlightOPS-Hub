"""Best-effort detection of the default Community folder location for the
selected MSFS version/platform - mirrors the well-known root folders
exe_xml_manager.py already uses for exe.xml (same install, a sibling
"Packages\\Community" folder under that same root).

Only ever offered as a suggestion: the candidate is checked to actually
exist on disk before being returned, never assumed - plenty of real
installs (custom Steam library layouts, a Community folder relocated via
UserCfg.opt, ...) will not match this guess at all, and that is expected,
not a bug. Browse always remains the real fallback.
"""
import os


def default_community_path(sim_version, sim_platform):
    roaming = os.environ.get("APPDATA", "")
    local = os.environ.get("LOCALAPPDATA", "")

    if sim_version == "MSFS 2020":
        if sim_platform == "Steam":
            return os.path.join(roaming, "Microsoft Flight Simulator", "Packages", "Community")
        return os.path.join(
            local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages", "Community"
        )
    else:
        if sim_platform == "Steam":
            return os.path.join(roaming, "Microsoft Flight Simulator 2024", "Packages", "Community")
        return os.path.join(
            local, "Packages", "Microsoft.FlightSimulator2024_8wekyb3d8bbwe", "LocalCache", "Packages", "Community"
        )


def detect_community_path(sim_version, sim_platform):
    """Returns the default path for this sim/platform combo if it actually
    exists on disk, else None."""
    candidate = default_community_path(sim_version, sim_platform)
    return candidate if os.path.isdir(candidate) else None
