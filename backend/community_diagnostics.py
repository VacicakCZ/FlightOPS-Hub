"""Structural diagnostics for the Community folder - catches two common,
hard-to-self-diagnose install mistakes that scenery_data.py's own scan
silently ignores (by design - it only cares about recognized packages):

1. Misplaced manifest: a folder with no manifest.json directly inside it,
   but one exactly one level deeper (Community/name/name/manifest.json
   instead of Community/name/manifest.json) - MSFS itself ignores these
   too, and it is a very common mistake when manually extracting a zip
   that already contains a wrapping folder.
2. Duplicate installs: the same package (by manifest title) present under
   two or more different folder names, anywhere across Community and the
   known disabled-holding locations - wastes disk space and can conflict.

Pure read-only diagnostics - never moves, renames, or deletes anything.
Kept separate from scenery_data.py (untouched) since this is a different
concern (structural sanity, not content_type-aware enable/disable) and not
limited to SCENERY packages the way that module is used elsewhere.
"""
import json
import os


def _read_manifest_title(folder_path):
    manifest_path = os.path.join(folder_path, "manifest.json")
    if not os.path.exists(manifest_path):
        return None
    try:
        with open(manifest_path, "r", encoding="utf-8-sig") as f:
            data = json.load(f)
    except Exception:
        return None
    return (data.get("title") or "").strip() or None


def find_misplaced_packages(community_path):
    """Returns [{"folder_name": str, "nested_folder": str}, ...] for
    top-level Community folders with no manifest.json of their own, but a
    valid one exactly one level deeper."""
    misplaced = []
    try:
        entries = list(os.scandir(community_path))
    except OSError:
        return misplaced

    for entry in entries:
        if not entry.is_dir():
            continue
        if os.path.exists(os.path.join(entry.path, "manifest.json")):
            continue  # a normal, correctly-placed package - nothing wrong here

        try:
            subentries = list(os.scandir(entry.path))
        except OSError:
            continue
        for sub in subentries:
            if sub.is_dir() and os.path.exists(os.path.join(sub.path, "manifest.json")):
                misplaced.append({"folder_name": entry.name, "nested_folder": sub.name})
                break

    return misplaced


def find_duplicate_packages(locations):
    """locations: folder paths to scan (Community + all known disabled-
    holding locations). Flags any manifest title found under 2+ distinct
    folder names anywhere across them. Returns [{"title": str,
    "folders": [folder_name, ...]}, ...]."""
    by_title = {}
    for base_path in locations:
        try:
            entries = list(os.scandir(base_path))
        except OSError:
            continue
        for entry in entries:
            if not entry.is_dir():
                continue
            title = _read_manifest_title(entry.path)
            if title:
                by_title.setdefault(title, set()).add(entry.name)

    return [
        {"title": title, "folders": sorted(folders)}
        for title, folders in by_title.items()
        if len(folders) > 1
    ]
