"""Export/import bundle for the app's own settings - msfs_launcher_config.json
+ msfs_apps.json combined into one portable JSON file, so profiles,
aircraft overrides, and the addon list survive a reinstall or a move to a
new PC instead of starting from scratch. Pure data shaping only; the Api
layer handles the actual file dialog and disk IO.

Window geometry (_window_x/_window_y/_window_width/_window_height) is
included as-is rather than stripped out - harmless even on a different
monitor setup, since config_manager already clamps it to the visible
screen on load.
"""
import time

from .version import APP_VERSION

BUNDLE_VERSION = 1


def build_bundle(config, apps):
    return {
        "bundle_version": BUNDLE_VERSION,
        "app_version": APP_VERSION,
        "exported_at": int(time.time()),
        "config": config,
        "apps": apps,
    }


def default_filename():
    return f"flightops_hub_backup_{time.strftime('%Y-%m-%d')}.json"


def parse_bundle(data):
    """Validates the loaded JSON has the expected shape. Returns
    (config, apps) on success, raises ValueError with a short message on
    anything unrecognized - the caller turns that into a user-facing error
    rather than silently importing garbage into the running config."""
    if not isinstance(data, dict):
        raise ValueError("not a FlightOps Hub backup file")
    config = data.get("config")
    apps = data.get("apps")
    if not isinstance(config, dict) or not isinstance(apps, dict):
        raise ValueError("missing config/apps in backup file")
    return config, apps
