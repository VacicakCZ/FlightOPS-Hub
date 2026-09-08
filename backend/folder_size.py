"""Computes Community folder disk usage - total size plus a per-package
breakdown, for the Settings tab's storage-usage panel.

Deliberately its own on-demand action (not run automatically during scenery/
aircraft scans) - a full recursive walk of a Community folder with tens of
thousands of files can take a noticeable amount of time, and nothing else in
the app needs this data.
"""
import os


def _dir_size(path):
    total = 0
    for root, _dirs, files in os.walk(path):
        for name in files:
            try:
                total += os.path.getsize(os.path.join(root, name))
            except OSError:
                pass
    return total


def scan_community_usage(community_path):
    """Returns {"total_bytes": int, "packages": [{"folder_name": str,
    "bytes": int}, ...]} sorted largest-first. Top-level entries only -
    each direct Community subfolder is one add-on package."""
    packages = []
    try:
        entries = list(os.scandir(community_path))
    except OSError:
        return {"total_bytes": 0, "packages": []}

    for entry in entries:
        if not entry.is_dir():
            continue
        packages.append({"folder_name": entry.name, "bytes": _dir_size(entry.path)})

    packages.sort(key=lambda p: p["bytes"], reverse=True)
    total_bytes = sum(p["bytes"] for p in packages)
    return {"total_bytes": total_bytes, "packages": packages}
