"""Matches a SimBrief flight plan's airports against the already-scanned
scenery list, so the Scenery tab can suggest enabling installed-but-disabled
sceneries for the departure/destination/alternate.

Deliberately does not touch scenery_data.py: a scenery record's ICAO is only
ever exposed embedded in its display_name ("[LKPR] Prague Airport", built by
_build_record), so this re-derives it via the same "[ICAO] " prefix instead
of adding a new field to that module - keeps scenery_data.py exactly as
untouched/pure as it's been through the rest of this migration.
"""
import re

_ICAO_PREFIX_RE = re.compile(r"^\[([A-Z0-9]{4})\]")


def icao_from_display_name(display_name):
    match = _ICAO_PREFIX_RE.match(display_name or "")
    return match.group(1) if match else None


def match_flight_plan(records, legs):
    """legs: [(role, icao_or_None), ...] e.g. [("origin", "KJFK"), ...].
    Returns a list of {role, icao, status, folder_name, display_name} - status
    is one of "enabled", "disabled", "not_installed". Legs with no ICAO
    (e.g. no alternate on this OFP) are skipped entirely."""
    by_icao = {}
    for record in records:
        icao = icao_from_display_name(record["display_name"])
        if icao and icao not in by_icao:
            by_icao[icao] = record

    results = []
    for role, icao in legs:
        if not icao:
            continue
        record = by_icao.get(icao)
        if record is None:
            results.append({"role": role, "icao": icao, "status": "not_installed", "folder_name": None, "display_name": None})
        else:
            status = "enabled" if record["enabled"] else "disabled"
            results.append({
                "role": role, "icao": icao, "status": status,
                "folder_name": record["folder_name"], "display_name": record["display_name"],
            })
    return results
