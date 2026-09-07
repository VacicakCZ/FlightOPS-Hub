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

    browseFolder: (initialDir) => call("dialogs_browse_folder", initialDir || ""),
    browseFile: (initialDir) => call("dialogs_browse_file", initialDir || ""),
  };
})();
