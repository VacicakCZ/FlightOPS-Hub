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
    community_detect,
    community_diagnostics,
    community_paths,
    config_manager,
    exe_xml_manager,
    folder_size,
    gsx_profiles,
    gsx_watcher,
    launch_orchestrator,
    scenery_map,
    scenery_simbrief_match,
    simbrief_client,
    update_check,
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

    def check_for_update(self):
        return update_check.check_for_update()

    def open_update_page(self):
        try:
            os.startfile(update_check.UPDATE_PAGE_URL)
            return {"ok": True}
        except OSError:
            return {"ok": False}

    # --- config ---
    def _save_config(self):
        # The ONLY place that should call config_manager.save_config(). Every
        # config-mutating method below goes through this (directly, or via
        # _update_config) rather than calling config_manager.save_config()
        # itself, so it's structurally impossible to accidentally save some
        # other dict (e.g. a stale captured snapshot) over the real config -
        # exactly the bug that once leaked the computed-only
        # _gsx_profiles_path_effective field into the config file on every
        # window close (see save_window_geometry's docstring below).
        config_manager.save_config(self._config)

    def _update_config(self, patch):
        self._config.update(patch)
        self._save_config()

    def _config_snapshot(self):
        # _gsx_profiles_path_effective is computed fresh each call (never
        # persisted under that key) so Settings can always show the actual
        # path being scanned - the default virtuali install location unless
        # the user overrode it. Every method that hands the config back to
        # the frontend should return this, not self._config directly, or
        # that field would vanish after any unrelated config_set() call.
        result = dict(self._config)
        result["_gsx_profiles_path_effective"] = self._gsx_path()
        result["_exe_xml_path_effective"] = self._current_exe_xml_path()
        return result

    def config_get(self):
        return self._config_snapshot()

    def config_set(self, patch):
        self._update_config(patch)
        return self._config_snapshot()

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
    def _sim_key(self):
        return f"{self._config.get('_sim_version', 'MSFS 2024')}|{self._config.get('_sim_platform', 'Steam')}"

    def settings_set_community_path(self, path):
        path = os.path.normpath(path)
        remembered = self._config.setdefault("_community_paths_by_sim", {})
        remembered[self._sim_key()] = path
        self._update_config({"_community_path": path, "_community_paths_by_sim": remembered})
        return self._config_snapshot()

    def _switch_sim(self, key, value):
        """Shared by settings_set_sim_version/settings_set_sim_platform:
        remembers the Community path under the sim/platform combo being left,
        then restores whatever was last used for the combo being switched to
        (or clears it if that combo has never had one set) - so flipping
        between MSFS 2020/2024 (or Steam/MS Store) does not silently keep
        pointing at the wrong install's Community folder."""
        remembered = self._config.setdefault("_community_paths_by_sim", {})
        old_path = self._config.get("_community_path", "")
        if old_path:
            remembered[self._sim_key()] = old_path

        self._config[key] = value
        self._config["_community_path"] = remembered.get(self._sim_key(), "")
        self._save_config()
        return self._config_snapshot()

    def settings_set_sim_version(self, version):
        return self._switch_sim("_sim_version", version)

    def settings_set_sim_platform(self, platform):
        return self._switch_sim("_sim_platform", platform)

    def detect_community_path(self):
        """Best-effort suggestion only - see community_detect.py. Returns
        {"found": path} or {"found": None}."""
        sim_version = self._config.get("_sim_version", "MSFS 2024")
        sim_platform = self._config.get("_sim_platform", "Steam")
        return {"found": community_detect.detect_community_path(sim_version, sim_platform)}

    def settings_set_disabled_path(self, path):
        path = os.path.normpath(path)
        result = community_paths.validate_disabled_path(path, self._config.get("_community_path", ""))
        if not result["ok"]:
            return result
        self._update_config({"_disabled_holding_path": path})
        return {**result, "config": self._config_snapshot()}

    def settings_reset_disabled_path(self):
        self._config.pop("_disabled_holding_path", None)
        self._save_config()
        return self._config_snapshot()

    def _start_folder_usage_scan(self, folder_path, event_name):
        """Shared by scan_community_usage/scan_disabled_usage below - both
        just walk a different folder and report to a different event, so the
        threading/emit machinery is identical."""
        if not folder_path or not os.path.isdir(folder_path):
            return {"ok": False}

        def worker():
            result = folder_size.scan_folder_usage(folder_path)
            bus.emit(event_name, result)

        threading.Thread(target=worker, daemon=True).start()
        return {"ok": True}

    def scan_community_usage(self):
        """Kicks off a background disk-usage scan of the Community folder;
        result arrives via the community_usage_done event. A full recursive
        walk can take a noticeable while on a large folder, so this is only
        ever run on explicit user request (Settings tab button), never as
        part of the regular scenery/aircraft scan."""
        community_path = self._config.get("_community_path", "")
        return self._start_folder_usage_scan(community_path, "community_usage_done")

    def scan_disabled_usage(self):
        """Same as scan_community_usage, but for the disabled-holding folder
        (where toggled-off scenery/aircraft packages live) - result arrives
        via disabled_usage_done. Resolves the actual effective location
        (custom override, or the default sibling-of-Community folder) the
        same way scenery_scan/aircraft_scan already do, not just whatever
        (possibly empty) override the user typed in Settings."""
        community_path = self._config.get("_community_path", "")
        if not community_path or not os.path.isdir(community_path):
            return {"ok": False}
        primary_disabled = self._resolve_disabled_locations(community_path)[0]
        return self._start_folder_usage_scan(primary_disabled, "disabled_usage_done")

    def scan_community_diagnostics(self):
        """Structural sanity check of the Community folder - misplaced
        (one-level-too-deep) manifests and duplicate installs across
        Community and all known disabled-holding locations. Just manifest.json
        reads (no byte counting), so unlike the disk-usage scans above this
        runs synchronously on explicit user request."""
        community_path = self._config.get("_community_path", "")
        if not community_path or not os.path.isdir(community_path):
            return {"no_community": True, "misplaced": [], "duplicates": []}

        disabled_locations = self._resolve_disabled_locations(community_path)
        misplaced = community_diagnostics.find_misplaced_packages(community_path)
        duplicates = community_diagnostics.find_duplicate_packages([community_path] + disabled_locations)
        return {"no_community": False, "misplaced": misplaced, "duplicates": duplicates}

    # --- settings: GSX (virtuali) profile folder, for the scenery-tab GSX badge ---
    def _gsx_path(self):
        return self._config.get("_gsx_profiles_path") or gsx_profiles.default_gsx_path()

    def settings_set_gsx_path(self, path):
        self._update_config({"_gsx_profiles_path": os.path.normpath(path)})
        return self._config_snapshot()

    def settings_reset_gsx_path(self):
        self._config.pop("_gsx_profiles_path", None)
        self._save_config()
        return self._config_snapshot()

    def open_gsx_search(self, icao):
        # Builds the URL itself from a validated ICAO rather than accepting
        # an arbitrary URL from the frontend, so this can't be used to open
        # anything other than a flightsim.to GSX Pro search.
        icao = (icao or "").strip().upper()
        if len(icao) != 4 or not icao.isalpha():
            return {"ok": False}
        try:
            os.startfile(f"https://flightsim.to/miscellaneous/gsx-pro?q={icao}")
            return {"ok": True}
        except OSError:
            return {"ok": False}

    def save_window_geometry(self, width, height, x, y):
        # Called directly with the live window's own dimensions (not a
        # config_get()/config_set() round trip) from flightops_hub.pyw on
        # close - that snapshot dict includes the computed-not-persisted
        # _gsx_profiles_path_effective field (see _config_snapshot), and an
        # earlier version of this that saved a captured snapshot dict
        # instead of self._config baked that field permanently into the
        # real config file on every close.
        self._update_config({"_window_width": width, "_window_height": height, "_window_x": x, "_window_y": y})

    def open_gsx_folder(self):
        path = self._gsx_path()
        if not path:
            return {"ok": False}
        try:
            # Create it if this is a first-time user who's never had GSX
            # write anything there yet - opening a folder that doesn't
            # exist would otherwise just fail.
            os.makedirs(path, exist_ok=True)
            os.startfile(path)
            return {"ok": True}
        except OSError:
            return {"ok": False}

    def start_gsx_watcher(self):
        # Called once from flightops_hub.pyw after the window/EventBus are
        # ready. Reuses config_changed (already listened for by the Scenery
        # tab) rather than a dedicated event - a GSX folder change should
        # trigger exactly the same full reload/re-check a Community-path
        # change does.
        self._gsx_watcher = gsx_watcher.GsxFolderWatcher(self._gsx_path, lambda: bus.emit("config_changed"))
        self._gsx_watcher.start()

    # --- profiles (kind: "flight" for the Flight tab, "exe" for exe.xml profiles in M3) ---
    def profiles_list(self, kind):
        return self._config.get(config_manager.PROFILE_STORES[kind], {})

    def profiles_save(self, kind, name, members):
        store_key = config_manager.PROFILE_STORES[kind]
        self._config.setdefault(store_key, {})[name] = members
        self._config[config_manager.LAST_PROFILE_KEYS[kind]] = name
        self._save_config()
        return {"profiles": self._config[store_key], "last": name}

    def profiles_delete(self, kind, name):
        store_key = config_manager.PROFILE_STORES[kind]
        self._config.get(store_key, {}).pop(name, None)
        self._config[config_manager.LAST_PROFILE_KEYS[kind]] = None
        self._save_config()
        return {"profiles": self._config.get(store_key, {}), "last": None}

    # --- exe.xml (MSFS AutoStart) ---
    def _current_exe_xml_path(self):
        override = self._config.get("_exe_xml_path_override")
        if override:
            return override
        return exe_xml_manager.get_exe_xml_path(
            self._config.get("_sim_version", "MSFS 2024"),
            self._config.get("_sim_platform", "Steam"),
        )

    def settings_set_exe_xml_path(self, path):
        self._update_config({"_exe_xml_path_override": os.path.normpath(path)})
        return self._config_snapshot()

    def settings_reset_exe_xml_path(self):
        self._config.pop("_exe_xml_path_override", None)
        self._save_config()
        return self._config_snapshot()

    def exe_list(self):
        xml_path = self._current_exe_xml_path()
        has_backup = exe_xml_manager.has_backup(xml_path)
        if not os.path.exists(xml_path):
            return {"path": xml_path, "exists": False, "addons": [], "error": None, "has_backup": has_backup}
        try:
            addons = exe_xml_manager.list_addons(xml_path, self._config.get("_exe_custom_names", {}))
            return {"path": xml_path, "exists": True, "addons": addons, "error": None, "has_backup": has_backup}
        except Exception as e:
            return {"path": xml_path, "exists": True, "addons": [], "error": str(e), "has_backup": has_backup}

    def exe_restore_backup(self):
        restored = exe_xml_manager.restore_from_backup(self._current_exe_xml_path())
        return {"ok": restored}

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
        self._save_config()
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
        self._save_config()
        return {"ok": True}

    # --- Navigraph AIRAC status ---
    def airac_status(self):
        return {
            "current": airac.get_current_airac(),
            "installed": airac.get_installed_airac(self._config.get("_community_path", "")),
        }

    # --- launch pipeline ---
    def launch_precheck(self, app_states):
        """Called before the real launch so the frontend can warn about (and
        let the user confirm past) any checked app whose .exe path no longer
        exists - launch_orchestrator itself silently skips those with no
        user-visible signal at all, so without this a moved/uninstalled
        add-on just looks like a launch that quietly did nothing for it."""
        selected = [name for name, checked in app_states.items() if checked]
        missing = [name for name in selected if not self._app_exe_exists(name)]
        return {"missing": missing}

    def _app_exe_exists(self, name):
        data = self._apps.get(name)
        return bool(data and os.path.exists(data.get("path", "")))

    def launch_all(self, app_states, profile_name):
        """app_states: {app_name: bool} for every configured app (mirrors the
        old per-checkbox on/off persistence, including explicitly-unchecked apps)."""
        for name, checked in app_states.items():
            self._config[name] = "on" if checked else "off"
        self._config["_last_profile"] = profile_name
        self._save_config()

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
        scenery_map.attach_airport_names(records)
        gsx_profiles.attach_gsx_status(records, self._gsx_path())
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

        # scan["records"] already carries gsx_status (from scenery_scan's
        # gsx_profiles.attach_gsx_status) for every installed scenery - reuse
        # it here instead of re-deriving, so the SimBrief panel shows the
        # same GSX info as the scenery list without a separate lookup.
        records_by_icao = {r["icao"]: r for r in scan["records"] if r.get("icao")}
        gsx_profiles_by_icao = gsx_profiles.scan_profiles(self._gsx_path())

        for leg in matched_legs:
            # Coordinates for the map's route line - looked up independently
            # of the scenery match, since a leg (e.g. an alternate with no
            # scenery installed) should still be plottable on the route.
            airport = airports_data.lookup(leg["icao"])
            leg["lat"] = airport["lat"] if airport else None
            leg["lon"] = airport["lon"] if airport else None

            matched_record = records_by_icao.get(leg["icao"])
            if matched_record:
                leg["gsx_status"] = matched_record.get("gsx_status")
            else:
                # No scenery installed for this leg at all, so there's no
                # developer to compare a profile against - the best we can
                # say is whether any profile exists for the ICAO.
                leg["gsx_status"] = "installed_unmatched" if gsx_profiles_by_icao.get(leg["icao"]) else "missing"
        return {
            "ok": True,
            "legs": matched_legs,
            "duration_minutes": ofp.get("duration_minutes"),
            "aircraft_name": ofp.get("aircraft_name"),
            "planned_at": ofp.get("planned_at"),
        }

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
        dismissed = set(self._config.get("_aircraft_livery_suggestions_dismissed", []))
        aircraft, liveries = aircraft_overrides.apply_overrides(
            aircraft, liveries, type_overrides, parent_overrides, dismissed
        )

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
        self._save_config()
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
        self._save_config()
        return self.aircraft_scan()

    def aircraft_dismiss_livery_suggestion(self, folder_name):
        """User clicked 'ignore' on the suspected_livery hint (see
        scenery_data.py) - remembers not to show it again for this folder,
        without touching its actual classification."""
        dismissed = self._config.setdefault("_aircraft_livery_suggestions_dismissed", [])
        if folder_name not in dismissed:
            dismissed.append(folder_name)
        self._save_config()
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
