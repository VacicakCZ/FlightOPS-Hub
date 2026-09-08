document.addEventListener("alpine:init", () => {
  Alpine.data("flightTab", () => ({
    checked: {},
    profiles: {},
    profileName: null,
    airac: { current: "", installed: "" },
    updateJustSaved: false,
    launching: false,

    async init() {
      await window.AppReady;
      const cfg = this.$store.app.config;
      for (const name of Object.keys(this.$store.app.apps)) {
        this.checked[name] = cfg[name] === "on";
      }
      this.profiles = await Api.profilesList("flight");
      this.airac = await Api.airacStatus();

      const lastProfile = cfg._last_profile;
      if (lastProfile && this.profiles[lastProfile]) {
        this.applyProfileSelection(lastProfile);
      }

      // AIRAC status depends on the Community folder - re-check if it gets
      // set/changed in Settings after this tab already mounted (every tab
      // mounts at startup regardless of which one is visible).
      FlightOpsEvents.on("config_changed", async () => {
        this.airac = await Api.airacStatus();
      });
    },

    get appNames() {
      return Object.keys(this.$store.app.apps);
    },

    get profileNames() {
      return Object.keys(this.profiles);
    },

    get airacStatusClass() {
      if (!this.airac.installed) return "airac-unknown";
      return this.airac.installed === this.airac.current ? "airac-ok" : "airac-old";
    },

    get airacStatusText() {
      const app = this.$store.app;
      if (!this.airac.installed) return app.t("nav_not_found", this.airac.current);
      if (this.airac.installed === this.airac.current) return app.t("nav_ok", this.airac.installed);
      return app.t("nav_old", this.airac.installed, this.airac.current);
    },

    applyProfileSelection(name) {
      const members = this.profiles[name] || [];
      for (const appName of this.appNames) {
        this.checked[appName] = members.includes(appName);
      }
    },

    onProfileChange(name) {
      this.profileName = name || null;
      if (this.profileName) this.applyProfileSelection(this.profileName);
    },

    activeAppNames() {
      return this.appNames.filter((name) => this.checked[name]);
    },

    async updateProfile() {
      if (!this.profileName) return;
      const result = await Api.profilesSave("flight", this.profileName, this.activeAppNames());
      this.profiles = result.profiles;
      this.updateJustSaved = true;
      setTimeout(() => (this.updateJustSaved = false), 1000);
    },

    async saveProfileAs() {
      const name = await Modal.promptText(this.$store.app.t("profile_prompt_title"), this.$store.app.t("profile_prompt_text"));
      if (!name || !name.trim()) return;
      const result = await Api.profilesSave("flight", name.trim(), this.activeAppNames());
      this.profiles = result.profiles;
      this.profileName = name.trim();
    },

    async deleteProfile() {
      if (!this.profileName) return;
      const result = await Api.profilesDelete("flight", this.profileName);
      this.profiles = result.profiles;
      this.profileName = null;
    },

    async launchAll() {
      if (this.launching) return;

      const precheck = await Api.launchPrecheck(this.checked);
      if (precheck.missing.length) {
        const proceed = await Modal.confirmDialog(
          this.$store.app.t("launch_missing_title"),
          this.$store.app.t("launch_missing_message", precheck.missing.join(", "))
        );
        if (!proceed) return;
      }

      this.launching = true;
      // For an all-immediate-apps launch, the backend hides (often
      // destroys) the native window almost instantly once launch_all()
      // starts running - previously that could happen before the browser
      // ever got a chance to paint the "launching" button state, so
      // clicking Launch looked like it did nothing. This deliberate pause
      // guarantees the user actually sees the button change first.
      await new Promise((resolve) => setTimeout(resolve, 400));
      await Api.launchAll(this.checked, this.profileName);
    },
  }));
});
