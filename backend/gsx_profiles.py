"""Detects installed GSX (virtuali) airport profiles by scanning their
profile folder's filenames. GSX profile files are conventionally named
"{icao}-{author/variant}.ini" (or with "_"/" " as the separator, case
doesn't matter) - e.g. "LKPR-darriancze-gsxvdgs.ini",
"ebbr_aerosoft_v2.ini", "DTMB - Nizar.ini" - so a plain regex on the
leading token is enough; no need to parse file contents.

Also makes a best-effort guess at whether an installed profile was made
for the same developer as the installed scenery, by checking whether the
scenery's developer slug (the folder-name-prefix heuristic already used
by scenery_data.py's own _developer_key, duplicated here rather than
imported to keep scenery_data.py untouched) shows up in the profile's
filename. Confirmed via real data: "aerosoft-airport-ebbr-..." scenery
matches "ebbr_aerosoft_v2.ini", while a payware scenery with a
community-made GSX profile by an unrelated author (e.g. "lfbo-snekye.ini"
for a Flightbeam airport) correctly comes back unmatched."""
import os
import re

from .scenery_simbrief_match import icao_from_display_name

_ICAO_PREFIX_RE = re.compile(r"^([A-Za-z]{4})[-_ ]")


def default_gsx_path():
    appdata = os.environ.get("APPDATA", "")
    return os.path.join(appdata, "virtuali", "GSX", "MSFS") if appdata else ""


def _developer_key(folder_name):
    return re.split(r"[-_]", folder_name, maxsplit=1)[0].lower()


def scan_profiles(gsx_path):
    """Returns {icao: [filename, ...]} for every GSX profile file found."""
    if not gsx_path or not os.path.isdir(gsx_path):
        return {}
    try:
        entries = list(os.scandir(gsx_path))
    except OSError:
        return {}

    profiles = {}
    for entry in entries:
        if not entry.is_file():
            continue
        match = _ICAO_PREFIX_RE.match(entry.name)
        if match:
            profiles.setdefault(match.group(1).upper(), []).append(entry.name)
    return profiles


def _status_for(developer_key, filenames):
    if not filenames:
        return "missing"
    if any(developer_key in name.lower() for name in filenames):
        return "installed_match"
    return "installed_unmatched"


def attach_gsx_status(records, gsx_path):
    """Adds gsx_status to each record in place: "installed_match" (profile
    found, looks like the same developer), "installed_unmatched" (profile
    found, can't confirm the developer), "missing" (no profile at all), or
    None for records with no recognizable ICAO (nothing to check). Also
    attaches the ICAO itself (already embedded in display_name, but not
    otherwise exposed as its own field) - the frontend needs it to build
    the flightsim.to search link for a missing profile."""
    profiles = scan_profiles(gsx_path)
    for record in records:
        icao = icao_from_display_name(record["display_name"])
        record["icao"] = icao
        if not icao:
            record["gsx_status"] = None
            continue
        record["gsx_status"] = _status_for(_developer_key(record["folder_name"]), profiles.get(icao, []))
    return records
