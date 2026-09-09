"""Free disk space on the drives backing the Community and disabled-holding
folders - a cheap, instant check (a single stat syscall per path, not a full
folder walk like folder_size.py) meant to catch a drive filling up before it
causes a partial/corrupted enable-disable move (that operation copies files
across drives when Community and the holding folder aren't on the same one).
"""
import os
import shutil

LOW_SPACE_THRESHOLD_BYTES = 5 * 1024 ** 3  # 5 GiB


def _drive_key(path):
    try:
        return os.stat(path).st_dev
    except OSError:
        return None


def check(community_path, disabled_path):
    """Returns [{"label": "community"|"disabled", "free_bytes": int,
    "low": bool}, ...] - one entry per distinct drive actually in use, so a
    setup where both paths live on the same drive (the common case) only
    reports once instead of showing the same number twice."""
    entries = []
    seen_drives = set()
    for label, path in (("community", community_path), ("disabled", disabled_path)):
        if not path or not os.path.isdir(path):
            continue
        drive = _drive_key(path)
        if drive is not None and drive in seen_drives:
            continue
        try:
            free_bytes = shutil.disk_usage(path).free
        except OSError:
            continue
        seen_drives.add(drive)
        entries.append({
            "label": label,
            "free_bytes": free_bytes,
            "low": free_bytes < LOW_SPACE_THRESHOLD_BYTES,
        })
    return entries
