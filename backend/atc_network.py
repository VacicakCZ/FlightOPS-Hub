"""Best-effort "is there ATC online at this airport right now" check
against VATSIM/IVAO's public live-data feeds - opt-in via a Settings
dropdown (off by default), used to add an ATC indicator to the SimBrief
panel's legs (same "is this leg actually ready" spirit as the GSX badge
and the scenery-installed check already there).

Both feeds are unauthenticated and public - no API key/login needed for
this kind of read-only lookup. Controller callsigns on both networks
follow an "<ICAO>_<POSITION>" convention (e.g. "VHHH_TWR", "KOGD_ATIS"),
so the airport is just the prefix before the first underscore - IVAO's
callsigns are consistently full ICAO in practice; VATSIM's occasionally
use a shortened local code instead (e.g. "SY_TWR" for Sydney) which this
simply will not match - a false negative there, never a false positive,
which is the safe direction to be wrong in for a "heads up" indicator.
"""
import requests

VATSIM_DATA_URL = "https://data.vatsim.net/v3/vatsim-data.json"
IVAO_WHAZZUP_URL = "https://api.ivao.aero/v2/tracker/whazzup"
_TIMEOUT_S = 8


def _icao_from_callsign(callsign):
    icao = (callsign or "").split("_", 1)[0].strip().upper()
    return icao if len(icao) == 4 and icao.isalpha() else None


def _extract_airports(callsigns):
    airports = set()
    for callsign in callsigns:
        icao = _icao_from_callsign(callsign)
        if icao:
            airports.add(icao)
    return airports


def _fetch_vatsim_online_airports():
    response = requests.get(VATSIM_DATA_URL, timeout=_TIMEOUT_S)
    response.raise_for_status()
    data = response.json()
    # facility 0 is an observer connection, not a staffed ATC position -
    # only a real facility (ground/tower/approach/center/...) counts.
    controller_callsigns = [
        c.get("callsign") for c in data.get("controllers", []) if c.get("facility", 0) > 0
    ]
    atis_callsigns = [a.get("callsign") for a in data.get("atis", [])]
    return _extract_airports(controller_callsigns + atis_callsigns)


def _fetch_ivao_online_airports():
    response = requests.get(IVAO_WHAZZUP_URL, timeout=_TIMEOUT_S)
    response.raise_for_status()
    data = response.json()
    atcs = (data.get("clients") or {}).get("atcs", [])
    return _extract_airports(a.get("callsign") for a in atcs)


def fetch_online_atc_airports(network):
    """network: "vatsim" | "ivao" | anything else (including "off") -> no
    fetch, empty set. Best-effort like update_check.py - any failure
    (offline, API unreachable, response shape changed) returns an empty
    set silently; ATC-online is a nice-to-have overlay, never allowed to
    block or error out the SimBrief check it's attached to."""
    try:
        if network == "vatsim":
            return _fetch_vatsim_online_airports()
        if network == "ivao":
            return _fetch_ivao_online_airports()
    except Exception:
        pass
    return set()
