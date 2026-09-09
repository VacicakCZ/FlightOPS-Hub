"""Loading/saving/CRUD for msfs_apps.json (the external addon apps list).

Pure logic, no UI - the Api layer translates results into what the frontend
needs (including translated error messages, since this module only returns
error *codes* matching i18n keys like "err_empty"/"err_delay").
"""
import json
import os

APPS_FILE = "msfs_apps.json"


def load_apps():
    if not os.path.exists(APPS_FILE):
        save_apps({})
        return {}

    with open(APPS_FILE, "r", encoding="utf-8") as f:
        loaded = json.load(f)

    apps = {}
    for name, data in loaded.items():
        if isinstance(data, str):
            delay = 150 if name == "REX Atmos" else 0
            launch_mode = "timer" if delay > 0 else "immediate"
            apps[name] = {"path": data, "delay": delay, "admin": False, "launch_mode": launch_mode}
        else:
            data.setdefault("admin", False)
            data.setdefault("launch_mode", "timer" if data.get("delay", 0) > 0 else "immediate")
            apps[name] = data
    save_apps(apps)
    return apps


def save_apps(apps):
    # Temp file + atomic os.replace, same reasoning as config_manager.py's
    # save_config - a plain "w" open truncates the file immediately, so a
    # crash/kill/power loss mid-write would leave the addon list corrupted.
    tmp_path = APPS_FILE + ".tmp"
    with open(tmp_path, "w", encoding="utf-8") as f:
        json.dump(apps, f, indent=4)
    os.replace(tmp_path, APPS_FILE)


def move_app(apps, name, direction):
    """direction: 'up' or 'down'. Returns the (possibly reordered) dict."""
    keys = list(apps.keys())
    if name not in keys:
        return apps
    idx = keys.index(name)
    target = idx - 1 if direction == "up" else idx + 1
    if 0 <= target < len(keys):
        keys[idx], keys[target] = keys[target], keys[idx]
        apps = {k: apps[k] for k in keys}
        save_apps(apps)
    return apps


def validate_app_input(name, path, mode_key, delay_str):
    """Returns an i18n error key (str) on failure, or None if valid."""
    if not name.strip() or not path.strip():
        return "err_empty"
    if mode_key == "timer":
        try:
            delay = int(delay_str)
            if delay <= 0:
                return "err_delay"
        except (TypeError, ValueError):
            return "err_delay"
    return None


def upsert_app(apps, name, path, mode_key, delay_str, is_admin, previous_name=None):
    """Adds a new app or edits an existing one in place (preserving list position on rename)."""
    delay = int(delay_str) if mode_key == "timer" else 0
    new_data = {"path": path.strip(), "delay": delay, "admin": is_admin, "launch_mode": mode_key}
    name = name.strip()

    if previous_name and previous_name != name and previous_name in apps:
        apps = {
            (name if key == previous_name else key): (new_data if key == previous_name else value)
            for key, value in apps.items()
        }
    else:
        apps[name] = new_data

    save_apps(apps)
    return apps


def remove_app(apps, name):
    apps.pop(name, None)
    save_apps(apps)
    return apps


def open_folder(path):
    folder = os.path.dirname(path)
    if os.path.exists(folder):
        try:
            os.startfile(folder)
        except Exception:
            pass
