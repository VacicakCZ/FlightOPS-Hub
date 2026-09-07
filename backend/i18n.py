"""Python-side translation lookup for the few strings the backend itself
needs to display (the native tray icon menu, which has no JS runtime).
Reads the same locale JSON files the frontend uses, so there's still only
one set of translation strings in the whole app.
"""
import json

from . import paths

_cache = {}


def translate(lang, key):
    lang = (lang or "EN").lower()
    if lang not in _cache:
        try:
            with open(paths.resource_path("frontend", "locales", f"{lang}.json"), "r", encoding="utf-8") as f:
                _cache[lang] = json.load(f)
        except Exception:
            _cache[lang] = {}
    return _cache[lang].get(key, key)
