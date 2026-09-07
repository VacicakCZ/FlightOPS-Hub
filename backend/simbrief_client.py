"""Fetches a pilot's last-generated SimBrief flight plan (OFP) - the public,
key-less read endpoint (not the dispatch/generation API, which needs a
registered key). No auth beyond the user's own SimBrief/Navigraph username.
"""
import requests

SIMBRIEF_URL = "https://www.simbrief.com/api/xml.fetcher.php"
_TIMEOUT_S = 8


def fetch_latest_ofp(username):
    """Returns {"ok": True, "origin": "KJFK", "destination": "KLAX",
    "alternate": "KONT"|None} or {"ok": False, "error": <str>}. The error
    string is a translation key when recognized, else a raw message."""
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
        data = response.json()
        origin = data["origin"]["icao_code"]
        destination = data["destination"]["icao_code"]
        alternate = (data.get("alternate") or {}).get("icao_code") or None
    except Exception as e:
        return {"ok": False, "error": f"unexpected response ({e})"}

    return {"ok": True, "origin": origin, "destination": destination, "alternate": alternate}
