"""Lazy-loaded ICAO -> coordinates lookup, backed by data/airports.json
(generated offline by scripts/build_airports_json.py from the OurAirports
dataset - see that script's docstring)."""
import json

from . import paths

_cache = None


def _load():
    global _cache
    if _cache is None:
        try:
            with open(paths.resource_path("data", "airports.json"), "r", encoding="utf-8") as f:
                _cache = json.load(f)
        except (OSError, json.JSONDecodeError):
            _cache = {}
    return _cache


def lookup(icao_code):
    if not icao_code:
        return None
    return _load().get(icao_code.upper())
