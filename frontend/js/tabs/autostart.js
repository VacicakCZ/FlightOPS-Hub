document.addEventListener("alpine:init", () => {
  Alpine.data("autostartTab", () => ({
    path: "",
    exists: true,
    error: null,
    addons: [],
    profiles: {},
    profileName: null,
    updateJustSaved: false,
    searchQuery: "",
    hasBackup: false,

    async init() {
      await window.AppReady;
      this.profiles = await Api.profilesList("exe");
      await this.loadAddons();
      // The exe.xml path depends on _sim_version/_sim_platform - if those
      // are set/changed in Settings after this tab already mounted (every
      // tab mounts at startup), re-resolve and reload without needing a
      // manual switch away and back.
      FlightOpsEvents.on("config_changed", () => this.loadAddons());
    },

    async loadAddons() {
      const result = await Api.exeList();
      this.path = result.path;
      this.exists = result.exists;
      this.error = result.error;
      this.addons = result.addons;
      this.hasBackup = result.has_backup;
    },

    get profileNames() {
      return Object.keys(this.profiles);
    },

    // Flat list (unlike Scenery/Aircraft's continent/developer trees), so
    // filtering is just this - no expand/collapse state needed.
    get filteredAddons() {
      const q = this.searchQuery.trim().toLowerCase();
      if (!q) return this.addons;
      return this.addons.filter((a) => (a.display_name || "").toLowerCase().includes(q));
    },

    async restoreBackup() {
      const proceed = await Modal.confirmDialog(
        this.$store.app.t("exe_restore_confirm_title"),
        this.$store.app.t("exe_restore_confirm_message")
      );
      if (!proceed) return;
      await Api.exeRestoreBackup();
      await this.loadAddons();
    },

    activeKeys() {
      return this.addons.filter((a) => a.enabled).map((a) => a.unique_key);
    },

    async toggle(addon) {
      addon.enabled = !addon.enabled;
      await Api.exeToggle(addon.unique_key, addon.enabled);
    },

    async rename(addon) {
      const name = await Modal.promptText(this.$store.app.t("exe_rename_title"), this.$store.app.t("exe_rename_prompt"));
      if (name === null) return;
      await Api.exeRename(addon.unique_key, addon.original_name, name);
      await this.loadAddons();
    },

    async onProfileChange(name) {
      this.profileName = name || null;
      if (this.profileName) {
        await Api.exeApplyProfile(this.profileName);
        await this.loadAddons();
      }
    },

    async updateProfile() {
      if (!this.profileName) return;
      const result = await Api.profilesSave("exe", this.profileName, this.activeKeys());
      this.profiles = result.profiles;
      this.updateJustSaved = true;
      setTimeout(() => (this.updateJustSaved = false), 1000);
    },

    async saveProfileAs() {
      const name = await Modal.promptText(this.$store.app.t("profile_prompt_title"), this.$store.app.t("profile_prompt_text"));
      if (!name || !name.trim()) return;
      const result = await Api.profilesSave("exe", name.trim(), this.activeKeys());
      this.profiles = result.profiles;
      this.profileName = name.trim();
    },

    async deleteProfile() {
      if (!this.profileName) return;
      const result = await Api.profilesDelete("exe", this.profileName);
      this.profiles = result.profiles;
      this.profileName = null;
    },
  }));
});
