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

    async saveSimVersion(version) {
      this.$store.app.config = await Api.configSet({ _sim_version: version });
      FlightOpsEvents.dispatch({ type: "config_changed" });
    },

    async saveSimPlatform(platform) {
      this.$store.app.config = await Api.configSet({ _sim_platform: platform });
      FlightOpsEvents.dispatch({ type: "config_changed" });
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
