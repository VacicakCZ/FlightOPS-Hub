"""Detects installed GSX (virtuali) airport profiles by scanning their
profile folder's filenames. GSX profile files are conventionally named
"{icao}-{author/variant}.ini" (or with "_"/" " as the separator, case
doesn't matter) - e.g. "LKPR-darriancze-gsxvdgs.ini",
"ebbr_aerosoft_v2.ini", "DTMB - Nizar.ini" - so a plain regex on the
leading token is enough; no need to parse file contents."""
import os
import re

from .scenery_simbrief_match import icao_from_display_name

_ICAO_PREFIX_RE = re.compile(r"^([A-Za-z]{4})[-_ ]")


def default_gsx_path():
    appdata = os.environ.get("APPDATA", "")
    return os.path.join(appdata, "virtuali", "GSX", "MSFS") if appdata else ""


def scan_installed_icaos(gsx_path):
    """Returns the set of ICAO codes (uppercase) that have at least one GSX
    profile file in gsx_path. Empty set if the path doesn't exist."""
    if not gsx_path or not os.path.isdir(gsx_path):
        return set()
    try:
        entries = list(os.scandir(gsx_path))
    except OSError:
        return set()

    icaos = set()
    for entry in entries:
        if not entry.is_file():
            continue
        match = _ICAO_PREFIX_RE.match(entry.name)
        if match:
            icaos.add(match.group(1).upper())
    return icaos


def attach_gsx_status(records, gsx_path):
    """Adds a gsx_installed bool to each record in place, so the Scenery
    tab can flag which installed sceneries already have a GSX profile."""
    installed_icaos = scan_installed_icaos(gsx_path)
    for record in records:
        icao = icao_from_display_name(record["display_name"])
        record["gsx_installed"] = bool(icao and icao in installed_icaos)
    return records
