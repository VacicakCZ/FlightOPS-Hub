// Global Alpine store: persisted config mirror + ephemeral UI state
// (active tab) + the current language's translation strings. Translation
// strings live here (not in a plain JS module) so that x-text="$store.app.t(...)"
// bindings are tracked by Alpine's reactivity and re-render on language change.
document.addEventListener("alpine:init", () => {
  Alpine.store("app", {
    activeTab: "flight",
    theme: "system",
    language: "EN",
    config: {},
    strings: {},
    languages: [],
    apps: {},
    uiScale: 100,
    version: "",
    updateInfo: null,
    updateNotesOpen: false,
    updateDownloading: false,
    updateDownloadProgress: null,
    updateDownloadError: null,

    async init() {
      this.version = await Api.appVersion();
      this.config = await Api.configGet();
      this.theme = this.config._theme || "system";
      this.applyTheme(this.theme);
      this.uiScale = this.config._ui_scale || 100;
      this.applyUiScale(this.uiScale);
      this.apps = await Api.appsList();

      // Stored on the reactive store (not read from I18n.getLanguages()
      // directly in the template) for the same reason `strings` lives
      // here: I18n.loadLanguages() populates a plain module-level array,
      // which Alpine's x-for has no way to know changed once the fetch
      // resolves - it would render zero <option>s forever, since the
      // first (synchronous) mount pass runs before this await settles.
      this.languages = await I18n.loadLanguages();
      const requestedLang = this.config._language || "EN";
      const lang = this.languages.some((l) => l.code === requestedLang) ? requestedLang : "EN";
      await this.setLanguage(lang, /* persist */ false);

      FlightOpsEvents.on("update_download_progress", (payload) => {
        this.updateDownloadProgress = payload;
      });
      FlightOpsEvents.on("update_download_done", (result) => {
        this.updateDownloading = false;
        this.updateDownloadProgress = null;
        this.updateDownloadError = result.ok ? null : result.error;
      });

      // Fire-and-forget: a slow/failed network call must never delay the
      // app becoming interactive, and a missing update is not worth
      // surfacing as an error - see backend/update_check.py.
      this.checkForUpdate();
    },

    async checkForUpdate() {
      try {
        const result = await Api.checkForUpdate();
        if (result.available) {
          this.updateInfo = result;
        }
      } catch (e) {
        // silent - purely a nicety, never worth bothering the user about
      }
    },

    openUpdatePage() {
      Api.openUpdatePage();
    },

    openReleasePage() {
      Api.openReleasePage();
    },

    // The update notice lives at the bottom of the Settings tab, below the
    // whole config form - just switching tabs left the user to scroll down
    // and find it themselves. This expands it and scrolls it into view in
    // one click, same as the banner already promises.
    goToUpdate() {
      this.setTab("settings");
      this.updateNotesOpen = true;
      setTimeout(() => {
        document.querySelector(".update-notice-wrap")?.scrollIntoView({ behavior: "smooth", block: "center" });
      }, 50);
    },

    async downloadUpdate() {
      if (!this.updateInfo || !this.updateInfo.download_url || this.updateDownloading) return;

      if (await Api.updateDownloadTargetExists()) {
        const proceed = await Modal.confirmDialog(
          this.t("update_overwrite_confirm_title"),
          this.t("update_overwrite_confirm_message")
        );
        if (!proceed) return;
      }

      this.updateDownloading = true;
      this.updateDownloadError = null;
      this.updateDownloadProgress = null;
      const result = await Api.downloadUpdate(this.updateInfo.download_url);
      if (!result.ok) {
        this.updateDownloading = false;
        this.updateDownloadError = result.error;
      }
      // On success, updateDownloading/Progress are cleared by the
      // update_download_done event once the background download actually
      // finishes - this initial result only confirms the download started.
    },

    get updateDownloadPercent() {
      const p = this.updateDownloadProgress;
      if (!p || !p.total) return null;
      return Math.min(100, Math.round((p.downloaded / p.total) * 100));
    },

    async refreshApps() {
      this.apps = await Api.appsList();
    },

    t(key, ...args) {
      let template = this.strings[key] ?? key;
      args.forEach((arg, i) => {
        template = template.replaceAll(`{${i}}`, arg);
      });
      let idx = 0;
      template = template.replaceAll("{}", () => (args[idx] !== undefined ? args[idx++] : "{}"));
      return template;
    },

    // Splits "Main text (parenthetical)" into its two parts, so tab labels
    // can render the parenthetical on its own (smaller) second line instead
    // of being squeezed/truncated on one line.
    splitParen(text) {
      const match = text.trim().match(/^(.*?)\s*(\(.*\))\s*$/);
      return match ? { main: match[1], paren: match[2] } : { main: text.trim(), paren: "" };
    },

    tMain(key, ...args) {
      return this.splitParen(this.t(key, ...args)).main;
    },

    tParen(key, ...args) {
      return this.splitParen(this.t(key, ...args)).paren;
    },

    setTab(tab) {
      this.activeTab = tab;
    },

    applyTheme(theme) {
      const root = document.documentElement;
      if (theme === "dark" || theme === "light") {
        root.setAttribute("data-theme", theme);
      } else {
        root.removeAttribute("data-theme");
      }
    },

    async setTheme(theme) {
      this.theme = theme;
      this.applyTheme(theme);
      this.config = await Api.configSet({ _theme: theme });
    },

    applyUiScale(pct) {
      document.documentElement.style.zoom = pct / 100;
    },

    previewUiScale(pct) {
      this.uiScale = pct;
      this.applyUiScale(pct);
    },

    async setUiScale(pct) {
      this.previewUiScale(pct);
      this.config = await Api.configSet({ _ui_scale: pct });
    },

    async setLanguage(lang, persist = true) {
      this.strings = await I18n.loadStrings(lang);
      this.language = lang;
      if (persist) {
        this.config = await Api.configSet({ _language: lang });
      }
    },
  });
});
