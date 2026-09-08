document.addEventListener("alpine:init", () => {
  Alpine.data("settingsTab", () => ({
    form: {
      name: "",
      path: "",
      launchMode: "immediate",
      delay: 0,
      admin: false,
      previousName: null,
    },
    errorKey: "",
    // Keyed by "community"/"disabled" - same shape, same on-demand-scan
    // pattern, just a different backend folder + event per kind.
    folderUsage: { community: null, disabled: null },
    folderUsageScanning: { community: false, disabled: false },
    detectedCommunityPath: null,

    async init() {
      await window.AppReady;
      FlightOpsEvents.on("community_usage_done", (payload) => {
        this.folderUsageScanning.community = false;
        this.folderUsage.community = payload;
      });
      FlightOpsEvents.on("disabled_usage_done", (payload) => {
        this.folderUsageScanning.disabled = false;
        this.folderUsage.disabled = payload;
      });
      await this.refreshDetectedCommunityPath();
    },

    async refreshDetectedCommunityPath() {
      const result = await Api.detectCommunityPath();
      this.detectedCommunityPath = result.found;
    },

    // Only worth showing as a suggestion when it actually differs from
    // what is already set - otherwise it is just noise confirming what
    // the user already has.
    get communityPathSuggestion() {
      if (!this.detectedCommunityPath) return null;
      return this.detectedCommunityPath !== this.communityPath ? this.detectedCommunityPath : null;
    },

    async useDetectedCommunityPath() {
      if (!this.detectedCommunityPath) return;
      this.$store.app.config = await Api.setCommunityPath(this.detectedCommunityPath);
      FlightOpsEvents.dispatch({ type: "config_changed" });
    },

    get languages() {
      return this.$store.app.languages;
    },

    get communityPath() {
      return this.$store.app.config._community_path || "";
    },

    get disabledPath() {
      return this.$store.app.config._disabled_holding_path || "";
    },

    get gsxPath() {
      return this.$store.app.config._gsx_profiles_path_effective || "";
    },

    get exeXmlPath() {
      return this.$store.app.config._exe_xml_path_effective || "";
    },

    get simVersion() {
      return this.$store.app.config._sim_version || "MSFS 2024";
    },

    get simPlatform() {
      return this.$store.app.config._sim_platform || "Steam";
    },

    get postLaunch() {
      return this.$store.app.config._post_launch_behavior || "exit";
    },

    get simbriefUsername() {
      return this.$store.app.config._simbrief_username || "";
    },

    async saveSimbriefUsername(value) {
      this.$store.app.config = await Api.configSet({ _simbrief_username: value.trim() });
    },

    get appEntries() {
      return Object.entries(this.$store.app.apps);
    },

    async browseCommunity() {
      const path = await Api.browseFolder(this.communityPath);
      if (!path) return;
      this.$store.app.config = await Api.setCommunityPath(path);
      FlightOpsEvents.dispatch({ type: "config_changed" });
    },

    async browseDisabledPath() {
      const path = await Api.browseFolder(this.disabledPath);
      if (!path) return;
      const result = await Api.setDisabledPath(path);
      if (!result.ok) {
        await Modal.alertMsg(this.$store.app.t("disabled_path_title"), this.$store.app.t("disabled_path_invalid_nested"));
        return;
      }
      if (result.warning === "cross_drive") {
        await Modal.alertMsg(this.$store.app.t("disabled_path_title"), this.$store.app.t("disabled_path_cross_drive_warning"));
      }
      this.$store.app.config = result.config;
      FlightOpsEvents.dispatch({ type: "config_changed" });
    },

    async resetDisabledPath() {
      this.$store.app.config = await Api.resetDisabledPath();
      FlightOpsEvents.dispatch({ type: "config_changed" });
    },

    async browseGsxPath() {
      const path = await Api.browseFolder(this.gsxPath);
      if (!path) return;
      this.$store.app.config = await Api.setGsxPath(path);
      FlightOpsEvents.dispatch({ type: "config_changed" });
    },

    async resetGsxPath() {
      this.$store.app.config = await Api.resetGsxPath();
      FlightOpsEvents.dispatch({ type: "config_changed" });
    },

    async scanCommunityUsage() {
      this.folderUsage.community = null;
      this.folderUsageScanning.community = true;
      const result = await Api.scanCommunityUsage();
      if (!result.ok) this.folderUsageScanning.community = false;
      // On success the community_usage_done event (registered in init())
      // fills folderUsage.community in / clears the scanning flag once the
      // background scan finishes - a full walk of a large folder can take
      // a while.
    },

    async scanDisabledUsage() {
      this.folderUsage.disabled = null;
      this.folderUsageScanning.disabled = true;
      const result = await Api.scanDisabledUsage();
      if (!result.ok) this.folderUsageScanning.disabled = false;
    },

    closeUsage(kind) {
      this.folderUsage[kind] = null;
    },

    topPackages(kind) {
      return this.folderUsage[kind] ? this.folderUsage[kind].packages.slice(0, 20) : [];
    },

    formatBytes(bytes) {
      const units = ["B", "KB", "MB", "GB", "TB"];
      let value = bytes || 0;
      let unitIndex = 0;
      while (value >= 1024 && unitIndex < units.length - 1) {
        value /= 1024;
        unitIndex++;
      }
      return `${value.toFixed(unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
    },

    // exe.xml path is picked directly (not a folder) - initial_dir needs
    // its containing folder, or the native dialog would be pointed at a
    // nonexistent "folder" (the full file path).
    _dirname(path) {
      const idx = path.lastIndexOf("\\");
      return idx >= 0 ? path.slice(0, idx) : "";
    },

    async browseExeXmlPath() {
      const path = await Api.browseFile(this._dirname(this.exeXmlPath), ["XML files (*.xml)"]);
      if (!path) return;
      this.$store.app.config = await Api.setExeXmlPath(path);
      FlightOpsEvents.dispatch({ type: "config_changed" });
    },

    async resetExeXmlPath() {
      this.$store.app.config = await Api.resetExeXmlPath();
      FlightOpsEvents.dispatch({ type: "config_changed" });
    },

    // Dedicated Api calls (not generic configSet) - the backend remembers
    // the Community path per sim_version/platform combo and swaps it back
    // in when switching, instead of silently pointing at the wrong
    // install's folder. See Api._switch_sim.
    async saveSimVersion(version) {
      this.$store.app.config = await Api.setSimVersion(version);
      FlightOpsEvents.dispatch({ type: "config_changed" });
      await this.refreshDetectedCommunityPath();
    },

    async saveSimPlatform(platform) {
      this.$store.app.config = await Api.setSimPlatform(platform);
      FlightOpsEvents.dispatch({ type: "config_changed" });
      await this.refreshDetectedCommunityPath();
    },

    async savePostLaunch(key) {
      this.$store.app.config = await Api.configSet({ _post_launch_behavior: key });
    },

    async browseAppPath() {
      const path = await Api.browseFile();
      if (path) this.form.path = path;
    },

    resetForm() {
      this.form = { name: "", path: "", launchMode: "immediate", delay: 0, admin: false, previousName: null };
      this.errorKey = "";
    },

    startEdit(name) {
      const data = this.$store.app.apps[name];
      this.form = {
        name,
        path: data.path,
        launchMode: data.launch_mode || (data.delay > 0 ? "timer" : "immediate"),
        delay: data.delay || 0,
        admin: !!data.admin,
        previousName: name,
      };
      this.errorKey = "";
    },

    async submitApp() {
      const result = await Api.appsSave({
        name: this.form.name,
        path: this.form.path,
        launch_mode: this.form.launchMode,
        delay: this.form.delay,
        admin: this.form.admin,
        previous_name: this.form.previousName,
      });
      if (!result.ok) {
        this.errorKey = result.error;
        return;
      }
      this.$store.app.apps = result.apps;
      this.resetForm();
    },

    async removeApp(name) {
      this.$store.app.apps = (await Api.appsRemove(name)).apps;
    },

    async moveApp(name, direction) {
      this.$store.app.apps = (await Api.appsReorder(name, direction)).apps;
    },

    openFolder(path) {
      Api.appsOpenFolder(path);
    },

    appRowLabel(name, data) {
      const adminIcon = data.admin ? "🛡️ " : "";
      let suffix = "";
      if (data.launch_mode === "timer") {
        suffix = ` (${data.delay}s)`;
      } else if (data.launch_mode === "smart") {
        suffix = ` [🎯 ${this.$store.app.t("mode_smart_short")}]`;
      }
      return `${adminIcon}${name}${suffix}`;
    },
  }));
});
