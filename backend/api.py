"""The js_api facade exposed to the frontend as `window.pywebview.api`.

Methods are grouped by domain (config, apps, dialogs, ...) rather than one
method per button. Each method validates its own input, delegates to a plain
Python module, and returns a JSON-serializable value - pywebview marshals the
return value into the resolved JS Promise automatically.
"""
import os

import webview

from . import airac, apps_manager, community_paths, config_manager, launch_orchestrator
from .i18n import translate
from .version import APP_VERSION


class Api:
    def __init__(self):
        self._config = config_manager.load_config()
        self._apps = apps_manager.load_apps()
        self._session = None  # the in-flight LaunchSession, if any

    def app_version(self):
        return APP_VERSION

    # --- config ---
    def config_get(self):
        return self._config

    def config_set(self, patch):
        self._config.update(patch)
        config_manager.save_config(self._config)
        return self._config

    # --- apps (external addons) ---
    def apps_list(self):
        return self._apps

    def apps_save(self, app):
        """app: {name, path, launch_mode, delay, admin, previous_name?}"""
        error = apps_manager.validate_app_input(
            app.get("name", ""), app.get("path", ""), app.get("launch_mode"), app.get("delay")
        )
        if error:
            return {"ok": False, "error": error}

        self._apps = apps_manager.upsert_app(
            self._apps,
            name=app["name"],
            path=app["path"],
            mode_key=app.get("launch_mode", "immediate"),
            delay_str=app.get("delay", 0),
            is_admin=bool(app.get("admin", False)),
            previous_name=app.get("previous_name"),
        )
        return {"ok": True, "apps": self._apps}

    def apps_remove(self, name):
        self._apps = apps_manager.remove_app(self._apps, name)
        return {"ok": True, "apps": self._apps}

    def apps_reorder(self, name, direction):
        self._apps = apps_manager.move_app(self._apps, name, direction)
        return {"ok": True, "apps": self._apps}

    def apps_open_folder(self, path):
        apps_manager.open_folder(path)
        return {"ok": True}

    # --- settings: community / disabled-holding paths ---
    def settings_set_community_path(self, path):
        self._config["_community_path"] = os.path.normpath(path)
        config_manager.save_config(self._config)
        return self._config

    def settings_set_disabled_path(self, path):
        path = os.path.normpath(path)
        result = community_paths.validate_disabled_path(path, self._config.get("_community_path", ""))
        if not result["ok"]:
            return result
        self._config["_disabled_holding_path"] = path
        config_manager.save_config(self._config)
        return {**result, "config": self._config}

    def settings_reset_disabled_path(self):
        self._config.pop("_disabled_holding_path", None)
        config_manager.save_config(self._config)
        return self._config

    # --- profiles (kind: "flight" for the Flight tab, "exe" for exe.xml profiles in M3) ---
    def profiles_list(self, kind):
        return self._config.get(config_manager.PROFILE_STORES[kind], {})

    def profiles_save(self, kind, name, members):
        store_key = config_manager.PROFILE_STORES[kind]
        self._config.setdefault(store_key, {})[name] = members
        self._config[config_manager.LAST_PROFILE_KEYS[kind]] = name
        config_manager.save_config(self._config)
        return {"profiles": self._config[store_key], "last": name}

    def profiles_delete(self, kind, name):
        store_key = config_manager.PROFILE_STORES[kind]
        self._config.get(store_key, {}).pop(name, None)
        self._config[config_manager.LAST_PROFILE_KEYS[kind]] = None
        config_manager.save_config(self._config)
        return {"profiles": self._config.get(store_key, {}), "last": None}

    # --- Navigraph AIRAC status ---
    def airac_status(self):
        return {
            "current": airac.get_current_airac(),
            "installed": airac.get_installed_airac(self._config.get("_community_path", "")),
        }

    # --- launch pipeline ---
    def launch_all(self, app_states, profile_name):
        """app_states: {app_name: bool} for every configured app (mirrors the
        old per-checkbox on/off persistence, including explicitly-unchecked apps)."""
        for name, checked in app_states.items():
            self._config[name] = "on" if checked else "off"
        self._config["_last_profile"] = profile_name
        config_manager.save_config(self._config)

        selected = [name for name, checked in app_states.items() if checked]
        lang = self._config.get("_language", "EN")

        self._session = launch_orchestrator.LaunchSession(
            webview.windows[0], lambda key: translate(lang, key)
        )
        self._session.start(
            apps=self._apps,
            selected_names=selected,
            sim_version=self._config.get("_sim_version", "MSFS 2024"),
            sim_platform=self._config.get("_sim_platform", "Steam"),
            post_launch_behavior=self._config.get("_post_launch_behavior", "exit"),
        )
        return {"ok": True}

    # --- native dialogs ---
    def dialogs_browse_folder(self, initial_dir=""):
        window = webview.windows[0]
        result = window.create_file_dialog(webview.FileDialog.FOLDER, directory=initial_dir or "")
        return result[0] if result else None

    def dialogs_browse_file(self, initial_dir="", file_types=("Executables (*.exe)", "*.exe")):
        window = webview.windows[0]
        result = window.create_file_dialog(
            webview.FileDialog.OPEN, directory=initial_dir or "", file_types=file_types
        )
        return result[0] if result else None
