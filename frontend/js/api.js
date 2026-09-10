// Thin wrapper around whichever native bridge is hosting this page:
// - pywebview (Python build): window.pywebview.api.<method>(...args), ready
//   signaled by a `pywebviewready` document event once js_api is attached.
// - WebView2 (.NET build, see dotnet/): no per-method binding exists, so
//   calls go over window.chrome.webview.postMessage as a JSON-RPC-ish
//   envelope {id, method, args}, matched to a response {id, result|error}
//   posted back from C#.
// Every method below stays a plain function returning a promise either way,
// so frontend/js/tabs/*.js never needs to know which host it's running in.
//
// window.chrome.webview alone does NOT distinguish the two builds - on
// Windows, pywebview's own "edgechromium" backend is itself implemented on
// top of WebView2, so that object exists in BOTH builds. Checking for the
// ABSENCE of window.pywebview doesn't work either, even though pywebview
// does end up setting it: this file's very first Api call typically fires
// from index.html's x-init, at the earliest possible tick of page load -
// early enough that pywebview's own injection can still be in flight, so
// "window.pywebview isn't set yet" is not the same thing as "never will
// be" (this is exactly why the original pywebview-only version of this
// file waited for a pywebviewready event instead of ever assuming
// readiness from timing). Treating that race as "must be the .NET build"
// sent a stray postMessage into pywebview's own bridge and crashed its
// on_script_notify handler - caught live by running the Python build
// after this file was first written.
//
// The robust fix: the .NET host (see dotnet/.../MainWindow.xaml.cs)
// injects window.__flightOpsHubDotNetBridge = true via
// AddScriptToExecuteOnDocumentCreated, which WebView2 guarantees runs
// before ANY page script - including this one's first line. That flag is
// therefore synchronously readable from tick zero in the .NET build and
// never set at all in the Python build, with no timing window either way.
window.Api = (() => {
  let readyPromise = null;
  let nextRequestId = 1;
  const pendingWebView2Calls = new Map();

  function usesWebView2Bridge() {
    return !!window.__flightOpsHubDotNetBridge;
  }

  if (usesWebView2Bridge()) {
    window.chrome.webview.addEventListener("message", (event) => {
      // PostWebMessageAsJson delivers event.data already parsed; guard the
      // PostWebMessageAsString case too in case the C# side ever uses it.
      const message = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
      const pending = pendingWebView2Calls.get(message.id);
      if (!pending) return;
      pendingWebView2Calls.delete(message.id);
      if (message.error) {
        pending.reject(new Error(message.error));
      } else {
        pending.resolve(message.result);
      }
    });
  }

  function whenReady() {
    if (readyPromise) return readyPromise;
    readyPromise = new Promise((resolve) => {
      if (usesWebView2Bridge()) {
        resolve();
        return;
      }
      if (window.pywebview && window.pywebview.api) {
        resolve();
        return;
      }
      window.addEventListener("pywebviewready", () => resolve(), { once: true });
    });
    return readyPromise;
  }

  function callWebView2(method, args) {
    return new Promise((resolve, reject) => {
      const id = nextRequestId++;
      pendingWebView2Calls.set(id, { resolve, reject });
      window.chrome.webview.postMessage(JSON.stringify({ id, method, args }));
    });
  }

  async function call(method, ...args) {
    await whenReady();
    if (usesWebView2Bridge()) {
      return callWebView2(method, args);
    }
    return window.pywebview.api[method](...args);
  }

  return {
    appVersion: () => call("app_version"),
    checkForUpdate: () => call("check_for_update"),
    openUpdatePage: () => call("open_update_page"),
    openReleasePage: () => call("open_release_page"),
    downloadUpdate: (downloadUrl) => call("download_update", downloadUrl),

    configGet: () => call("config_get"),
    configSet: (patch) => call("config_set", patch),
    exportConfig: () => call("export_config"),
    importConfig: () => call("import_config"),
    exportDiagnostics: () => call("export_diagnostics"),

    appsList: () => call("apps_list"),
    appsSave: (app) => call("apps_save", app),
    appsRemove: (name) => call("apps_remove", name),
    appsReorder: (name, direction) => call("apps_reorder", name, direction),
    appsOpenFolder: (path) => call("apps_open_folder", path),

    setCommunityPath: (path) => call("settings_set_community_path", path),
    setSimVersion: (version) => call("settings_set_sim_version", version),
    setSimPlatform: (platform) => call("settings_set_sim_platform", platform),
    detectCommunityPath: () => call("detect_community_path"),
    setDisabledPath: (path) => call("settings_set_disabled_path", path),
    resetDisabledPath: () => call("settings_reset_disabled_path"),
    scanCommunityUsage: () => call("scan_community_usage"),
    scanDisabledUsage: () => call("scan_disabled_usage"),
    diskSpaceStatus: () => call("disk_space_status"),
    scanCommunityDiagnostics: () => call("scan_community_diagnostics"),
    setLaunchAtStartup: (enabled) => call("settings_set_launch_at_startup", enabled),
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
    exeRestoreBackup: () => call("exe_restore_backup"),
    setExeXmlPath: (path) => call("settings_set_exe_xml_path", path),
    resetExeXmlPath: () => call("settings_reset_exe_xml_path"),

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
    aircraftDismissLiverySuggestion: (folderName) => call("aircraft_dismiss_livery_suggestion", folderName),

    airacStatus: () => call("airac_status"),
    launchPrecheck: (appStates) => call("launch_precheck", appStates),
    launchAll: (appStates, profileName) => call("launch_all", appStates, profileName),

    browseFolder: (initialDir) => call("dialogs_browse_folder", initialDir || ""),
    // fileTypes lets a caller override the default "Executables (*.exe)"
    // filter (e.g. for picking exe.xml itself) - omitted entirely rather
    // than passed as null/undefined when not given, so the Python side's
    // own default still applies.
    browseFile: (initialDir, fileTypes) => (
      fileTypes ? call("dialogs_browse_file", initialDir || "", fileTypes) : call("dialogs_browse_file", initialDir || "")
    ),
  };
})();
