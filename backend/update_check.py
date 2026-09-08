"""Checks GitHub Releases for a version newer than this build.

Unauthenticated GET against the public repo's releases/latest endpoint -
no token needed (and none could be safely embedded in a distributed .exe
anyway). Best-effort only: any failure (no internet, GitHub unreachable,
rate limited) is swallowed and treated as "no update info available",
never surfaced as an error - this is a purely optional nicety that must
never block or interrupt normal use of the app.
"""
import os
import re

import requests

from .version import APP_VERSION

RELEASES_LATEST_URL = "https://api.github.com/repos/VacicakCZ/FlightOPS-Hub/releases/latest"
UPDATE_PAGE_URL = "https://github.com/VacicakCZ/FlightOPS-Hub/releases/latest"
RELEASE_ASSET_NAME = "flightops_hub.exe"  # matches release-build.yml's upload step exactly
_TIMEOUT_S = 6
_DOWNLOAD_TIMEOUT_S = 30

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


def _find_asset_download_url(data):
    """Pure lookup, split out from check_for_update() for testing without a
    network call - same reasoning as simbrief_client.py's _parse_ofp split."""
    for asset in data.get("assets") or []:
        if asset.get("name") == RELEASE_ASSET_NAME:
            return asset.get("browser_download_url")
    return None


def check_for_update():
    """Returns {"available": False} (no update, or the check failed/timed
    out/offline - all treated the same, silently) or {"available": True,
    "version": "v2.1", "notes": "<release body>", "download_url": str|None}.
    download_url is None if the release has no flightops_hub.exe asset
    attached yet (e.g. the build workflow hasn't finished) - the caller
    falls back to just opening the release page in that case."""
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
        "download_url": _find_asset_download_url(data),
    }


def download_update(download_url, dest_path, on_progress=None):
    """Streams the release exe to dest_path (a temp ".part" file first, then
    an atomic os.replace into place, so a failed/cancelled download never
    leaves a half-written file at the final name). on_progress(downloaded,
    total), if given, is called after each chunk - total is None if the
    server didn't send a Content-Length.

    Returns {"ok": True, "path": dest_path} or {"ok": False, "error": str}.
    """
    tmp_path = dest_path + ".part"
    try:
        response = requests.get(download_url, stream=True, timeout=_DOWNLOAD_TIMEOUT_S)
        if response.status_code != 200:
            return {"ok": False, "error": f"HTTP {response.status_code}"}

        total = response.headers.get("content-length")
        total = int(total) if total and total.isdigit() else None
        downloaded = 0

        with open(tmp_path, "wb") as f:
            for chunk in response.iter_content(chunk_size=256 * 1024):
                if not chunk:
                    continue
                f.write(chunk)
                downloaded += len(chunk)
                if on_progress:
                    on_progress(downloaded, total)

        os.replace(tmp_path, dest_path)
        return {"ok": True, "path": dest_path}
    except Exception as e:
        try:
            os.remove(tmp_path)
        except OSError:
            pass
        return {"ok": False, "error": str(e)}
