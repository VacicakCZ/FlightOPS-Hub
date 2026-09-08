"""Fetches a pilot's last-generated SimBrief flight plan (OFP) - the public,
key-less read endpoint (not the dispatch/generation API, which needs a
registered key). No auth beyond the user's own SimBrief/Navigraph username.
"""
import requests

SIMBRIEF_URL = "https://www.simbrief.com/api/xml.fetcher.php"
_TIMEOUT_S = 8


def _parse_ofp(data):
    """Pure parsing of an already-decoded SimBrief OFP JSON payload - kept
    separate from fetch_latest_ofp below so it's testable without a network
    call (same split as update_check.py's _parse_version/check_for_update).

    origin/destination/alternate are load-bearing for the scenery-matching
    feature, so a missing one raises (caller turns that into an error).
    duration_minutes/aircraft_name/planned_at are a best-effort summary so
    the UI can show the user at a glance what plan they're looking at and
    when it was generated (SimBrief has no concept of "current" flight -
    it's just whatever was last generated, could be stale) - any of those
    being absent/malformed just comes back None, never raises."""
    origin = data["origin"]["icao_code"]
    destination = data["destination"]["icao_code"]
    alternate = (data.get("alternate") or {}).get("icao_code") or None

    times = data.get("times") or {}
    aircraft = data.get("aircraft") or {}
    params = data.get("params") or {}

    duration_minutes = None
    try:
        duration_minutes = round(int(times.get("est_time_enroute")) / 60)
    except (TypeError, ValueError):
        pass

    planned_at = None
    try:
        planned_at = int(params.get("time_generated"))
    except (TypeError, ValueError):
        pass

    return {
        "origin": origin,
        "destination": destination,
        "alternate": alternate,
        "duration_minutes": duration_minutes,
        "aircraft_name": aircraft.get("name") or None,
        "planned_at": planned_at,
    }


def fetch_latest_ofp(username):
    """Returns {"ok": True, "origin": "KJFK", "destination": "KLAX",
    "alternate": "KONT"|None, "duration_minutes": int|None,
    "aircraft_name": str|None, "planned_at": int|None (unix seconds)} or
    {"ok": False, "error": <str>}. The error string is a translation key
    when recognized, else a raw message."""
    if not username or not username.strip():
        return {"ok": False, "error": "simbrief_no_username"}

    try:
        response = requests.get(
            SIMBRIEF_URL, params={"username": username.strip(), "json": 1}, timeout=_TIMEOUT_S
        )
    except requests.RequestException as e:
        return {"ok": False, "error": str(e)}

    if response.status_code != 200:
        return {"ok": False, "error": f"HTTP {response.status_code}"}

    try:
        parsed = _parse_ofp(response.json())
    except Exception as e:
        return {"ok": False, "error": f"unexpected response ({e})"}

    return {"ok": True, **parsed}
