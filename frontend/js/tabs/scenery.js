document.addEventListener("alpine:init", () => {
  Alpine.data("sceneryTab", () => ({
    continentOrder: ["europe", "north_america", "south_america", "asia", "africa", "oceania", "other"],

    allRecords: [],
    noCommunity: false,
    dataLoaded: false,
    pending: {},
    expandedContinents: {},
    expandedCountries: {},
    applying: false,
    statusText: "",
    progressFraction: 0,
    _lastDesired: {},

    async init() {
      await window.AppReady;
      await this.load();

      FlightOpsEvents.on("scenery_apply_progress", (payload) => this.onProgress(payload));
      FlightOpsEvents.on("scenery_apply_done", (payload) => this.onDone(payload));
    },

    async load() {
      // If a move is running (started from here or from the Aircraft tab -
      // they share the same Community folder and busy flag), skip scanning
      // entirely rather than reading a filesystem mid-move.
      if (await Api.sceneryIsBusy()) {
        this.applying = true;
        this.statusText = this.$store.app.t("addon_apply_busy");
        return;
      }
      const result = await Api.sceneryScan();
      this.allRecords = result.records;
      this.noCommunity = result.no_community;
      this.dataLoaded = true;
    },

    get byContinent() {
      const map = {};
      for (const r of this.allRecords) {
        (map[r.continent] ??= []).push(r);
      }
      return map;
    },

    get visibleContinents() {
      return this.continentOrder.filter((c) => this.byContinent[c] && this.byContinent[c].length);
    },

    countriesFor(items) {
      const map = {};
      const otherLabel = this.$store.app.t("scenery_other_country");
      for (const r of items) {
        const country = r.country || otherLabel;
        (map[country] ??= []).push(r);
      }
      return Object.keys(map)
        .sort()
        .map((name) => ({
          name,
          items: map[name].slice().sort((a, b) => a.display_name.toLowerCase().localeCompare(b.display_name.toLowerCase())),
        }));
    },

    isEnabled(record) {
      const override = this.pending[record.folder_name];
      return override === undefined ? record.enabled : override;
    },

    groupState(items) {
      const total = items.length;
      const enabled = items.filter((r) => this.isEnabled(r)).length;
      return { enabled, total, allOn: total > 0 && enabled === total, allOff: enabled === 0 };
    },

    groupClass(items) {
      const { allOn, allOff } = this.groupState(items);
      if (allOn) return "group-on";
      if (allOff) return "group-off";
      return "group-partial";
    },

    toggleContinent(key) {
      this.expandedContinents[key] = !this.expandedContinents[key];
    },

    toggleCountry(key) {
      this.expandedCountries[key] = !this.expandedCountries[key];
    },

    onToggle(record) {
      this.pending[record.folder_name] = !this.isEnabled(record);
    },

    setGroup(items, enabled) {
      for (const r of items) this.pending[r.folder_name] = enabled;
    },

    setAll(enabled) {
      this.setGroup(this.allRecords, enabled);
    },

    async refresh() {
      this.pending = {};
      this.statusText = "";
      await this.load();
    },

    async apply() {
      if (this.applying) return;
      const desired = {};
      for (const r of this.allRecords) {
        desired[r.folder_name] = this.isEnabled(r);
      }
      this._lastDesired = desired;
      this.applying = true;
      this.progressFraction = 0;
      this.statusText = this.$store.app.t("scenery_applying");
      const result = await Api.sceneryApply(desired);
      if (!result.ok) {
        this.applying = false;
        this.statusText = "";
      }
    },

    onProgress({ idx, total, name, copied, total_bytes }) {
      const fraction = (idx + (total_bytes ? copied / total_bytes : 1)) / Math.max(total, 1);
      this.progressFraction = Math.min(1, fraction);
      this.statusText = this.$store.app.t("scenery_applying_progress", idx + 1, total, name);
    },

    async onDone({ results }) {
      const desired = this._lastDesired || {};
      const enabledCount = results.filter((r) => r.ok && desired[r.folder_name] === true).length;
      const disabledCount = results.filter((r) => r.ok && desired[r.folder_name] === false).length;
      const errors = results.filter((r) => !r.ok);

      let status = this.$store.app.t("scenery_apply_result", enabledCount, disabledCount);
      if (errors.length) {
        status += this.$store.app.t("scenery_apply_errors", errors.length);
        console.error("Scenery apply errors:", errors);
      }

      this.applying = false;
      this.pending = {};
      await this.load();
      this.statusText = status;
    },
  }));
});
