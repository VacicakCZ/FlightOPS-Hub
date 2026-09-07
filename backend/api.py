"""The js_api facade exposed to the frontend as `window.pywebview.api`.

Methods are grouped by domain (config, apps, dialogs, ...) rather than one
method per button. Each method validates its own input, delegates to a plain
Python module, and returns a JSON-serializable value - pywebview marshals the
return value into the resolved JS Promise automatically.
"""
import os
import threading

import webview

import scenery_data
from . import (
    aircraft_overrides,
    aircraft_type_hint,
    airac,
    apps_manager,
    airports_data,
    community_paths,
    config_manager,
    exe_xml_manager,
    launch_orchestrator,
    scenery_map,
    scenery_simbrief_match,
    simbrief_client,
)
from .events import bus
from .i18n import translate
from .version import APP_VERSION


class Api:
    def __init__(self):
        self._config = config_manager.load_config()
        self._apps = apps_manager.load_apps()
        self._session = None  # the in-flight LaunchSession, if any
        self._addon_apply_active = False  # shared busy flag: scenery (M4) and aircraft (M5) share the Community folder

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

    # --- exe.xml (MSFS AutoStart) ---
    def _current_exe_xml_path(self):
        return exe_xml_manager.get_exe_xml_path(
            self._config.get("_sim_version", "MSFS 2024"),
            self._config.get("_sim_platform", "Steam"),
        )

    def exe_list(self):
        xml_path = self._current_exe_xml_path()
        if not os.path.exists(xml_path):
            return {"path": xml_path, "exists": False, "addons": [], "error": None}
        try:
            addons = exe_xml_manager.list_addons(xml_path, self._config.get("_exe_custom_names", {}))
            return {"path": xml_path, "exists": True, "addons": addons, "error": None}
        except Exception as e:
            return {"path": xml_path, "exists": True, "addons": [], "error": str(e)}

    def exe_toggle(self, unique_key, enabled):
        exe_xml_manager.set_addons_enabled(self._current_exe_xml_path(), {unique_key: enabled})
        return {"ok": True}

    def exe_rename(self, unique_key, original_name, new_name):
        new_name = (new_name or "").strip()
        custom_names = self._config.setdefault("_exe_custom_names", {})
        if not new_name or new_name == original_name:
            custom_names.pop(unique_key, None)
        else:
            custom_names[unique_key] = new_name
        config_manager.save_config(self._config)
        return {"ok": True}

    def exe_apply_profile(self, profile_name):
        members = set(self._config.get("_exe_profiles", {}).get(profile_name, []))
        xml_path = self._current_exe_xml_path()
        try:
            addons = exe_xml_manager.list_addons(xml_path, {})
        except Exception:
            return {"ok": False}

        desired = {a["unique_key"]: (a["unique_key"] in members) for a in addons}
        exe_xml_manager.set_addons_enabled(xml_path, desired)
        self._config["_last_exe_profile"] = profile_name
        config_manager.save_config(self._config)
        return {"ok": True}

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

    # --- scenery (Community folder addon enable/disable) ---
    def _resolve_disabled_locations(self, community_path):
        return scenery_data.resolve_disabled_locations(
            community_path, self._config.get("_disabled_holding_path", "")
        )

    def scenery_scan(self):
        community_path = self._config.get("_community_path", "")
        if not community_path or not os.path.isdir(community_path):
            return {"records": [], "no_community": True}
        disabled_locations = self._resolve_disabled_locations(community_path)
        records = scenery_data.scan_scenery_packages(community_path, disabled_locations)
        return {"records": records, "no_community": False}

    def scenery_is_busy(self):
        return self._addon_apply_active

    def simbrief_check(self):
        username = self._config.get("_simbrief_username", "")
        ofp = simbrief_client.fetch_latest_ofp(username)
        if not ofp["ok"]:
            return ofp

        scan = self.scenery_scan()
        if scan["no_community"]:
            return {"ok": False, "error": "scenery_no_community"}

        legs = [("origin", ofp["origin"]), ("destination", ofp["destination"]), ("alternate", ofp["alternate"])]
        matched_legs = scenery_simbrief_match.match_flight_plan(scan["records"], legs)
        # Coordinates for the map's route line - looked up independently of
        # the scenery match, since a leg (e.g. an alternate with no scenery
        # installed) should still be plottable on the route.
        for leg in matched_legs:
            airport = airports_data.lookup(leg["icao"])
            leg["lat"] = airport["lat"] if airport else None
            leg["lon"] = airport["lon"] if airport else None
        return {"ok": True, "legs": matched_legs}

    def scenery_apply(self, desired_states):
        return self._run_addon_apply("scenery", desired_states)

    def scenery_map_data(self):
        scan = self.scenery_scan()
        if scan["no_community"]:
            return {"markers": [], "no_community": True}
        return {"markers": scenery_map.build_markers(scan["records"]), "no_community": False}

    # --- aircraft/liveries (shares scenery's Community folder + busy flag) ---
    def aircraft_scan(self):
        community_path = self._config.get("_community_path", "")
        if not community_path or not os.path.isdir(community_path):
            return {"aircraft": [], "liveries": [], "no_community": True}
        disabled_locations = self._resolve_disabled_locations(community_path)
        aircraft, liveries = scenery_data.scan_aircraft_and_liveries(community_path, disabled_locations)

        type_overrides = self._config.get("_aircraft_type_overrides", {})
        parent_overrides = self._config.get("_aircraft_parent_overrides", {})
        aircraft, liveries = aircraft_overrides.apply_overrides(aircraft, liveries, type_overrides, parent_overrides)

        for record in aircraft:
            record["type_hint"] = aircraft_type_hint.extract_type_hint(record["display_name"])

        return {
            "aircraft": aircraft,
            "liveries": liveries,
            "no_community": False,
            "type_overrides": type_overrides,
            "parent_overrides": parent_overrides,
        }

    def aircraft_is_busy(self):
        return self._addon_apply_active

    def aircraft_set_type_override(self, folder_name, type_value):
        """type_value: "aircraft" | "livery" | None (None clears the
        override, reverting to whatever the package's own manifest says)."""
        overrides = self._config.setdefault("_aircraft_type_overrides", {})
        if type_value is None:
            overrides.pop(folder_name, None)
        else:
            overrides[folder_name] = type_value
        config_manager.save_config(self._config)
        return self.aircraft_scan()

    def aircraft_set_parent_override(self, folder_name, parent_folder_name):
        """parent_folder_name: a folder_name to force that parent, "" to force
        "unassigned", or None to clear the override (revert to the
        base_container heuristic)."""
        overrides = self._config.setdefault("_aircraft_parent_overrides", {})
        if parent_folder_name is None:
            overrides.pop(folder_name, None)
        else:
            overrides[folder_name] = parent_folder_name
        config_manager.save_config(self._config)
        return self.aircraft_scan()

    def aircraft_apply(self, desired_states):
        return self._run_addon_apply("aircraft", desired_states)

    def _run_addon_apply(self, event_prefix, desired_states):
        """Shared by scenery_apply/aircraft_apply - both just move folders
        between Community and a disabled-holding folder by name, so the
        move/progress/busy-flag machinery is identical; only the event names
        (so each tab's own frontend listener gets its update) differ."""
        if self._addon_apply_active:
            return {"ok": False, "error": "busy"}

        community_path = self._config.get("_community_path", "")
        if not community_path or not os.path.isdir(community_path):
            return {"ok": False, "error": "no_community"}

        self._addon_apply_active = True
        disabled_locations = self._resolve_disabled_locations(community_path)

        def on_progress(idx, total, name, copied, total_bytes):
            bus.emit(f"{event_prefix}_apply_progress", {
                "idx": idx, "total": total, "name": name, "copied": copied, "total_bytes": total_bytes,
            })

        def worker():
            try:
                raw_results = scenery_data.apply_package_changes(
                    community_path, disabled_locations, desired_states, progress_callback=on_progress
                )
            except Exception as e:
                raw_results = [("?", False, str(e))]
            finally:
                self._addon_apply_active = False
            results = [{"folder_name": n, "ok": ok, "error": err} for n, ok, err in raw_results]
            bus.emit(f"{event_prefix}_apply_done", {"results": results})

        threading.Thread(target=worker, daemon=True).start()
        return {"ok": True}

    # --- native dialogs ---
    def dialogs_browse_folder(self, initial_dir=""):
        window = webview.windows[0]
        result = window.create_file_dialog(webview.FileDialog.FOLDER, directory=initial_dir or "")
        return result[0] if result else None

    def dialogs_browse_file(self, initial_dir="", file_types=("Executables (*.exe)",)):
        window = webview.windows[0]
        result = window.create_file_dialog(
            webview.FileDialog.OPEN, directory=initial_dir or "", file_types=file_types
        )
        return result[0] if result else None
