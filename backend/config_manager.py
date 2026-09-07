"""Loading/saving/migrating msfs_launcher_config.json.

Kept CWD-relative (same as the old customtkinter app) so an existing
installation's config file is found without the user having to move anything.
"""
import json
import os
import re

from . import paths

CONFIG_FILE = "msfs_launcher_config.json"

CURRENT_CONFIG_VERSION = 1

_LEGACY_DARK_LABELS = {"Dark", "Tmavý", "Dunkel", "Oscuro", "暗色"}
_LEGACY_LIGHT_LABELS = {"Light", "Světlý", "Hell", "Claro", "亮色"}

_DEFAULT_WIDTH = 500
_DEFAULT_HEIGHT = 860

_GEOMETRY_RE = re.compile(r"^(\d+)x(\d+)(?:\+(-?\d+)\+(-?\d+))?$")

# Profile stores keyed by "kind" (flight-app profiles vs. exe.xml profiles) -
# single source of truth shared by the migration below and Api.profiles_*.
PROFILE_STORES = {"flight": "_profiles", "exe": "_exe_profiles"}
LAST_PROFILE_KEYS = {"flight": "_last_profile", "exe": "_last_exe_profile"}


def _canonical_theme(raw):
    if raw in _LEGACY_DARK_LABELS:
        return "dark"
    if raw in _LEGACY_LIGHT_LABELS:
        return "light"
    if raw in ("dark", "light", "system"):
        return raw
    return "system"


def _migrate_geometry(data):
    geometry = data.pop("_window_geometry", None)
    if geometry:
        match = _GEOMETRY_RE.match(geometry.strip())
        if match:
            width, height, x, y = match.groups()
            data.setdefault("_window_width", int(width))
            data.setdefault("_window_height", int(height))
            if x is not None and y is not None:
                data.setdefault("_window_x", int(x))
                data.setdefault("_window_y", int(y))
            return
    data.setdefault("_window_width", _DEFAULT_WIDTH)
    data.setdefault("_window_height", _DEFAULT_HEIGHT)


def _clamp_window_position(data):
    """Defensive: pywebview has no easy off-screen recovery gesture, so if a
    saved position is clearly bogus (e.g. from a monitor that's since been
    disconnected), drop it and let pywebview center the window instead."""
    x = data.get("_window_x")
    y = data.get("_window_y")
    if x is None or y is None:
        return
    if x < -10000 or y < -10000 or x > 20000 or y > 20000:
        data.pop("_window_x", None)
        data.pop("_window_y", None)


def _sanitize_last_profile(data, kind):
    """The old app stored the *translated* "Default" label as the sentinel
    for "no profile selected" (e.g. "_last_profile": "Vychozi"), which breaks
    the moment the UI language changes. Self-heal on every load: if the
    stored value isn't an actual profile name, treat it as "no profile"
    (None) instead of hardcoding any particular language's default label."""
    last_key = LAST_PROFILE_KEYS[kind]
    store_key = PROFILE_STORES[kind]
    last = data.get(last_key)
    if last is not None and last not in data.get(store_key, {}):
        data[last_key] = None


def migrate_config(data):
    version = data.get("_config_version", 0)
    if version < 1:
        _migrate_geometry(data)
        data["_theme"] = _canonical_theme(data.get("_theme", "system"))
        data["_config_version"] = 1
    _clamp_window_position(data)
    _sanitize_last_profile(data, "flight")
    _sanitize_last_profile(data, "exe")
    return data


def load_config():
    data = {}
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                data = json.load(f)
        except Exception:
            data = {}
    return migrate_config(data)


def save_config(data):
    with open(CONFIG_FILE, "w", encoding="utf-8") as f:
        json.dump(data, f)


_languages_cache = None


def available_languages():
    global _languages_cache
    if _languages_cache is None:
        with open(paths.resource_path("frontend", "locales", "languages.json"), "r", encoding="utf-8") as f:
            _languages_cache = json.load(f)
    return _languages_cache


def validate_language(code):
    codes = {entry["code"] for entry in available_languages()}
    return code if code in codes else "EN"
