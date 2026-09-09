"""Best-effort "is there ATC online at this airport right now, and what
exactly" check against VATSIM/IVAO's public live-data feeds - opt-in via
a Settings dropdown (off by default), used to add an ATC indicator (and,
on click, a position/frequency breakdown) to the SimBrief panel's legs.

Both feeds are unauthenticated and public - no API key/login needed for
this kind of read-only lookup. Controller callsigns on both networks
follow an "<ICAO>_<POSITION>" convention (e.g. "VHHH_TWR", "KOGD_ATIS"),
so the airport is just the prefix before the first underscore and the
position is the suffix - IVAO's callsigns are consistently full ICAO in
practice; VATSIM's occasionally use a shortened local code instead (e.g.
"SY_TWR" for Sydney) which this simply will not match - a false negative
there, never a false positive, which is the safe direction to be wrong
in for a heads-up indicator. VATSIM's ATIS entries are included too
(surfaced as a distinct "ATIS" position, not lumped in with real
controllers) - it is not a live human, but it is genuinely useful to
know it is there.
"""
import requests

VATSIM_DATA_URL = "https://data.vatsim.net/v3/vatsim-data.json"
IVAO_WHAZZUP_URL = "https://api.ivao.aero/v2/tracker/whazzup"
_TIMEOUT_S = 8


def _icao_from_callsign(callsign):
    icao = (callsign or "").split("_", 1)[0].strip().upper()
    return icao if len(icao) == 4 and icao.isalpha() else None


def _position_from_callsign(callsign):
    parts = (callsign or "").split("_", 1)
    return parts[1].strip().upper() if len(parts) == 2 and parts[1].strip() else "ATC"


def _add_position(by_airport, callsign, frequency, position_override=None):
    icao = _icao_from_callsign(callsign)
    if not icao:
        return
    position = position_override or _position_from_callsign(callsign)
    by_airport.setdefault(icao, []).append({"position": position, "frequency": frequency})


def _fetch_vatsim_atc():
    response = requests.get(VATSIM_DATA_URL, timeout=_TIMEOUT_S)
    response.raise_for_status()
    data = response.json()
    by_airport = {}
    # facility 0 is an observer connection, not a staffed ATC position -
    # only a real facility (ground/tower/approach/center/...) counts.
    for c in data.get("controllers", []):
        if c.get("facility", 0) > 0:
            _add_position(by_airport, c.get("callsign"), c.get("frequency"))
    for a in data.get("atis", []):
        _add_position(by_airport, a.get("callsign"), a.get("frequency"), position_override="ATIS")
    return by_airport


def _fetch_ivao_atc():
    response = requests.get(IVAO_WHAZZUP_URL, timeout=_TIMEOUT_S)
    response.raise_for_status()
    data = response.json()
    by_airport = {}
    for c in (data.get("clients") or {}).get("atcs", []):
        session = c.get("atcSession") or {}
        frequency = session.get("frequency")
        frequency = f"{frequency:.3f}" if isinstance(frequency, (int, float)) else frequency
        # atcSession.position is usually already a clean "TWR"/"GND"/etc,
        # but fall back to parsing the callsign the same way as VATSIM if
        # it's ever missing, for consistency between the two networks.
        _add_position(by_airport, c.get("callsign"), frequency, position_override=session.get("position"))
    return by_airport


def fetch_online_atc(network):
    """network: "vatsim" | "ivao" | anything else (including "off") -> no
    fetch, empty dict. Best-effort like update_check.py - any failure
    (offline, API unreachable, response shape changed) returns an empty
    dict silently; ATC-online is a nice-to-have overlay, never allowed to
    block or error out the SimBrief check it's attached to.

    Returns {icao: [{"position": "TWR", "frequency": "118.100"}, ...]}."""
    try:
        if network == "vatsim":
            return _fetch_vatsim_atc()
        if network == "ivao":
            return _fetch_ivao_atc()
    except Exception:
        pass
    return {}
