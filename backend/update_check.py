"""Checks GitHub Releases for a version newer than this build.

Unauthenticated GET against the public repo's releases/latest endpoint -
no token needed (and none could be safely embedded in a distributed .exe
anyway). Best-effort only: any failure (no internet, GitHub unreachable,
rate limited) is swallowed and treated as "no update info available",
never surfaced as an error - this is a purely optional nicety that must
never block or interrupt normal use of the app.
"""
import re

import requests

from .version import APP_VERSION

RELEASES_LATEST_URL = "https://api.github.com/repos/VacicakCZ/FlightOPS-Hub/releases/latest"
UPDATE_PAGE_URL = "https://github.com/VacicakCZ/FlightOPS-Hub/releases/latest"
_TIMEOUT_S = 6

_VERSION_RE = re.compile(r"v?(\d+(?:\.\d+)*)")


def _parse_version(text):
    """"v2.1" / "2.1.3" / "2.1-beta" -> (2, 1, 3) for tuple comparison.
    Anything that doesn't even start with a number sorts as (0,), i.e.
    never "newer" than a real version."""
    match = _VERSION_RE.match((text or "").strip())
    if not match:
        return (0,)
    return tuple(int(part) for part in match.group(1).split("."))


def is_newer(candidate_text, current_text):
    return _parse_version(candidate_text) > _parse_version(current_text)


def check_for_update():
    """Returns {"available": False} (no update, or the check failed/timed
    out/offline - all treated the same, silently) or {"available": True,
    "version": "v2.1", "notes": "<release body>"}."""
    try:
        response = requests.get(RELEASES_LATEST_URL, timeout=_TIMEOUT_S)
        if response.status_code != 200:
            return {"available": False}
        data = response.json()
    except Exception:
        return {"available": False}

    latest_version = data.get("tag_name", "")
    if not is_newer(latest_version, APP_VERSION):
        return {"available": False}

    return {
        "available": True,
        "version": latest_version,
        "notes": data.get("body") or "",
    }
