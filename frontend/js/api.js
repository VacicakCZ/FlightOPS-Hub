// Thin wrapper around window.pywebview.api.* - waits for pywebview to be
// ready (it fires a `pywebviewready` document event once js_api is attached)
// so calls made during initial page load don't race the bridge setup.
window.Api = (() => {
  let readyPromise = null;

  function whenReady() {
    if (readyPromise) return readyPromise;
    readyPromise = new Promise((resolve) => {
      if (window.pywebview && window.pywebview.api) {
        resolve();
        return;
      }
      window.addEventListener("pywebviewready", () => resolve(), { once: true });
    });
    return readyPromise;
  }

  async function call(method, ...args) {
    await whenReady();
    return window.pywebview.api[method](...args);
  }

  return {
    appVersion: () => call("app_version"),

    configGet: () => call("config_get"),
    configSet: (patch) => call("config_set", patch),

    appsList: () => call("apps_list"),
    appsSave: (app) => call("apps_save", app),
    appsRemove: (name) => call("apps_remove", name),
    appsReorder: (name, direction) => call("apps_reorder", name, direction),
    appsOpenFolder: (path) => call("apps_open_folder", path),

    setCommunityPath: (path) => call("settings_set_community_path", path),
    setDisabledPath: (path) => call("settings_set_disabled_path", path),
    resetDisabledPath: () => call("settings_reset_disabled_path"),
    setGsxPath: (path) => call("settings_set_gsx_path", path),
    resetGsxPath: () => call("settings_reset_gsx_path"),
    openGsxSearch: (icao) => call("open_gsx_search", icao),
    openGsxFolder: () => call("open_gsx_folder"),

    profilesList: (kind) => call("profiles_list", kind),
    profilesSave: (kind, name, members) => call("profiles_save", kind, name, members),
    profilesDelete: (kind, name) => call("profiles_delete", kind, name),

    exeList: () => call("exe_list"),
    exeToggle: (uniqueKey, enabled) => call("exe_toggle", uniqueKey, enabled),
    exeRename: (uniqueKey, originalName, newName) => call("exe_rename", uniqueKey, originalName, newName),
    exeApplyProfile: (name) => call("exe_apply_profile", name),

    sceneryScan: () => call("scenery_scan"),
    sceneryIsBusy: () => call("scenery_is_busy"),
    sceneryApply: (desiredStates) => call("scenery_apply", desiredStates),
    sceneryMapData: () => call("scenery_map_data"),

    simbriefCheck: () => call("simbrief_check"),

    aircraftScan: () => call("aircraft_scan"),
    aircraftIsBusy: () => call("aircraft_is_busy"),
    aircraftApply: (desiredStates) => call("aircraft_apply", desiredStates),
    aircraftSetTypeOverride: (folderName, typeValue) => call("aircraft_set_type_override", folderName, typeValue),
    aircraftSetParentOverride: (folderName, parentFolderName) => call("aircraft_set_parent_override", folderName, parentFolderName),

    airacStatus: () => call("airac_status"),
    launchAll: (appStates, profileName) => call("launch_all", appStates, profileName),

    browseFolder: (initialDir) => call("dialogs_browse_folder", initialDir || ""),
    browseFile: (initialDir) => call("dialogs_browse_file", initialDir || ""),
  };
})();
