document.addEventListener("alpine:init", () => {
  Alpine.data("aircraftTab", () => ({
    aircraftRecords: [],
    liveryRecords: [],
    typeOverrides: {},
    parentOverrides: {},
    noCommunity: false,
    dataLoaded: false,
    pending: {},
    expandedDevelopers: {},
    expandedSections: {},
    overrideEditorOpen: {},
    applying: false,
    statusText: "",
    progressFraction: 0,
    _lastDesired: {},

    async init() {
      await window.AppReady;
      await this.load();

      FlightOpsEvents.on("aircraft_apply_progress", (payload) => this.onProgress(payload));
      FlightOpsEvents.on("aircraft_apply_done", (payload) => this.onDone(payload));
    },

    async load() {
      // Shares the busy flag with the Scenery tab (same Community folder) -
      // skip scanning entirely while either tab's apply is moving folders
      // around, rather than reading a filesystem mid-move.
      if (await Api.aircraftIsBusy()) {
        this.applying = true;
        this.statusText = this.$store.app.t("addon_apply_busy");
        return;
      }
      this._applyScanResult(await Api.aircraftScan());
      this.dataLoaded = true;
    },

    _applyScanResult(result) {
      this.aircraftRecords = result.aircraft;
      this.liveryRecords = result.liveries;
      this.noCommunity = result.no_community;
      this.typeOverrides = result.type_overrides || {};
      this.parentOverrides = result.parent_overrides || {};
    },

    // --- manual classification overrides (auto-detection is a heuristic
    // and can be wrong: a payware "livery" manifested as AIRCRAFT, or a
    // livery whose base_container doesn't resolve to the right parent) ---
    isOverrideOpen(folderName) {
      return !!this.overrideEditorOpen[folderName];
    },

    toggleOverrideEditor(folderName) {
      this.overrideEditorOpen[folderName] = !this.overrideEditorOpen[folderName];
    },

    effectiveTypeOverride(folderName) {
      return this.typeOverrides[folderName] || "auto";
    },

    effectiveParentOverride(folderName) {
      const value = this.parentOverrides[folderName];
      return value === undefined ? "auto" : value; // "" is a real value (force-unassigned), keep it
    },

    async setTypeOverride(record, value) {
      this._applyScanResult(await Api.aircraftSetTypeOverride(record.folder_name, value === "auto" ? null : value));
    },

    async setParentOverride(record, value) {
      this._applyScanResult(await Api.aircraftSetParentOverride(record.folder_name, value === "auto" ? null : value));
    },

    get allRecords() {
      return this.aircraftRecords.concat(this.liveryRecords);
    },

    get otherDeveloperKey() {
      return this.$store.app.t("aircraft_other_developer");
    },

    // {developer: {aircraft: [...], unassignedLiveries: [...]}} plus a
    // separate parent-folder_name -> [liveries] map, mirroring
    // build_aircraft_tab's liveries_by_aircraft/by_developer split exactly.
    get grouped() {
      const aircraftFolders = new Set(this.aircraftRecords.map((r) => r.folder_name));
      const liveriesByAircraft = {};
      const unassignedLiveries = [];
      for (const r of this.liveryRecords) {
        if (r.parent_aircraft && aircraftFolders.has(r.parent_aircraft)) {
          (liveriesByAircraft[r.parent_aircraft] ??= []).push(r);
        } else {
          unassignedLiveries.push(r);
        }
      }

      const byDeveloper = {};
      const bucket = (dev) => (byDeveloper[dev] ??= { aircraft: [], unassignedLiveries: [] });
      for (const r of this.aircraftRecords) bucket(r.developer || this.otherDeveloperKey).aircraft.push(r);
      for (const r of unassignedLiveries) bucket(r.developer || this.otherDeveloperKey).unassignedLiveries.push(r);

      return { byDeveloper, liveriesByAircraft };
    },

    get developers() {
      const other = this.otherDeveloperKey;
      return Object.keys(this.grouped.byDeveloper).sort((a, b) => {
        if ((a === other) !== (b === other)) return a === other ? 1 : -1;
        return a.toLowerCase().localeCompare(b.toLowerCase());
      });
    },

    developerTotal(developer) {
      const g = this.grouped;
      const groups = g.byDeveloper[developer];
      const nestedLiveryCount = groups.aircraft.reduce(
        (sum, a) => sum + (g.liveriesByAircraft[a.folder_name]?.length || 0),
        0
      );
      return groups.aircraft.length + groups.unassignedLiveries.length + nestedLiveryCount;
    },

    liveriesFor(folderName) {
      return (this.grouped.liveriesByAircraft[folderName] || [])
        .slice()
        .sort((a, b) => a.display_name.toLowerCase().localeCompare(b.display_name.toLowerCase()));
    },

    sortedByName(items) {
      return items.slice().sort((a, b) => a.display_name.toLowerCase().localeCompare(b.display_name.toLowerCase()));
    },

    isEnabled(record) {
      const override = this.pending[record.folder_name];
      return override === undefined ? record.enabled : override;
    },

    onToggleAircraft(record) {
      const newValue = !this.isEnabled(record);
      this.pending[record.folder_name] = newValue;
      if (!newValue) {
        // An aircraft going offline shouldn't leave its liveries enabled
        // and orphaned in Community. Deliberately one-directional: turning
        // the aircraft back on does NOT force its liveries back on too,
        // since you usually only want some of them active again.
        for (const livery of this.liveriesFor(record.folder_name)) {
          this.pending[livery.folder_name] = false;
        }
      }
    },

    onToggleLivery(record) {
      this.pending[record.folder_name] = !this.isEnabled(record);
    },

    toggleDeveloper(developer) {
      this.expandedDevelopers[developer] = !this.expandedDevelopers[developer];
    },

    toggleSection(sectionId) {
      this.expandedSections[sectionId] = !this.expandedSections[sectionId];
    },

    setAll(enabled) {
      for (const r of this.allRecords) this.pending[r.folder_name] = enabled;
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
      const result = await Api.aircraftApply(desired);
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
        console.error("Aircraft apply errors:", errors);
      }

      this.applying = false;
      this.pending = {};
      await this.load();
      this.statusText = status;
    },
  }));
});
