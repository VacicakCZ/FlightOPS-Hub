"""The js_api facade exposed to the frontend as `window.pywebview.api`.

Methods are grouped by domain (config, apps, dialogs, ...) rather than one
method per button. Each method validates its own input, delegates to a plain
Python module, and returns a JSON-serializable value - pywebview marshals the
return value into the resolved JS Promise automatically.
"""
import webview

from . import apps_manager, config_manager


class Api:
    def __init__(self):
        self._config = config_manager.load_config()
        self._apps = apps_manager.load_apps()

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
