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
    expandedLiveries: {},
    overrideEditorOpen: {},
    applying: false,
    statusText: "",
    progressFraction: 0,
    _lastDesired: {},
    searchQuery: "",

    async init() {
      await window.AppReady;
      await this.load();

      FlightOpsEvents.on("aircraft_apply_progress", (payload) => this.onProgress(payload));
      FlightOpsEvents.on("aircraft_apply_done", (payload) => this.onDone(payload));
      // See the identical comment in tabs/scenery.js - this tab mounts at
      // startup too, so it needs to re-scan when the Community/disabled
      // path is set or changed afterward in Settings.
      FlightOpsEvents.on("config_changed", () => this.load());

      // Same auto-expand/collapse-on-clear pattern as the Scenery tab's
      // search - see the identical comment there for why.
      this.$watch("searchQuery", (value) => {
        if (value.trim()) {
          this.expandSearchMatches();
        } else {
          this.expandedDevelopers = {};
          this.expandedSections = {};
          this.expandedLiveries = {};
        }
      });
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
      // Picking "livery" reveals the parent-aircraft field right below, so
      // keep the editor open for that follow-up step - anything else
      // (aircraft/auto) has nothing left to do, so close it to avoid
      // leaving a trail of open editors when correcting several in a row.
      if (value !== "livery") {
        this.overrideEditorOpen[record.folder_name] = false;
      }
    },

    // --- "suspected_livery" hint (see scenery_data.py) - a weak, name-only
    // signal shown for AIRCRAFT-declared packages that look like a
    // registration/repaint pack. Never applied automatically - the user
    // approves it (same effect as the manual "Livery" override), opens the
    // full manual editor instead, or dismisses it outright. ---
    async approveLiverySuggestion(record) {
      await this.setTypeOverride(record, "livery");
    },

    async dismissLiverySuggestion(record) {
      this._applyScanResult(await Api.aircraftDismissLiverySuggestion(record.folder_name));
    },

    async setParentOverride(record, value) {
      this._applyScanResult(await Api.aircraftSetParentOverride(record.folder_name, value === "auto" ? null : value));
      // Parent is always the last field in the editor - once it's set,
      // close it the same way setTypeOverride does for the non-livery case.
      this.overrideEditorOpen[record.folder_name] = false;
    },

    get allRecords() {
      return this.aircraftRecords.concat(this.liveryRecords);
    },

    get otherDeveloperKey() {
      return this.$store.app.t("aircraft_other_developer");
    },

    // Matches an aircraft/livery's own name or developer - same idea as the
    // Scenery tab's matchesSearch.
    matchesSearch(record) {
      const q = this.searchQuery.trim().toLowerCase();
      if (!q) return true;
      return (
        (record.display_name || "").toLowerCase().includes(q) ||
        (record.developer || "").toLowerCase().includes(q)
      );
    },

    expandSearchMatches() {
      if (!this.searchQuery.trim()) return;
      for (const developer of this.developers) {
        this.expandedDevelopers[developer] = true;
        const groups = this.grouped.byDeveloper[developer];
        if (groups.aircraft.length) this.expandedSections[developer + "::aircraft"] = true;
        if (groups.unassignedLiveries.length) this.expandedSections[developer + "::liveries"] = true;
        // Also open each visible aircraft's own nested livery list, or a
        // livery-only match (parent shown just for context) would stay
        // hidden behind the per-aircraft collapse toggle.
        for (const aircraft of groups.aircraft) {
          if (this.liveriesFor(aircraft.folder_name).length) {
            this.expandedLiveries[aircraft.folder_name] = true;
          }
        }
      }
    },

    // --- per-aircraft nested livery list collapse (kept separate from
    // expandedDevelopers/expandedSections, in its own dict keyed by
    // folder_name, so re-scanning after an override change - which does
    // not touch this state - never collapses a tree the user has open) ---
    toggleLiveries(folderName) {
      this.expandedLiveries[folderName] = !this.expandedLiveries[folderName];
    },

    // {developer: {aircraft: [...], unassignedLiveries: [...]}} plus a
    // separate parent-folder_name -> [liveries] map, mirroring
    // build_aircraft_tab's liveries_by_aircraft/by_developer split exactly.
    // While searching, an aircraft is kept if it matches directly or has at
    // least one matching livery (so the parent stays visible as context);
    // liveriesFor() below still returns the aircraft's full livery list
    // once expanded rather than also filtering those - narrowing which
    // top-level groups appear is enough without also picking apart what's
    // inside an already-matched one.
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
      for (const r of this.aircraftRecords) {
        const ownLiveries = liveriesByAircraft[r.folder_name] || [];
        if (this.matchesSearch(r) || ownLiveries.some((l) => this.matchesSearch(l))) {
          bucket(r.developer || this.otherDeveloperKey).aircraft.push(r);
        }
      }
      for (const r of unassignedLiveries) {
        if (this.matchesSearch(r)) bucket(r.developer || this.otherDeveloperKey).unassignedLiveries.push(r);
      }

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

    async onDone({ results, insufficient_space }) {
      if (insufficient_space && insufficient_space.length) {
        // Nothing was actually touched - reset the same way a normal
        // completion does (clear the optimistic pending toggles, reload the
        // real on-disk state) before the modal, or the item the user tried
        // to toggle stays looking as if the change went through.
        this.applying = false;
        this.pending = {};
        await this.load();
        this.statusText = "";
        const lines = insufficient_space
          .map((s) => this.$store.app.t("insufficient_disk_space", s.drive, this.formatBytes(s.needed_bytes), this.formatBytes(s.free_bytes)))
          .join("\n");
        await Modal.alertMsg(this.$store.app.t("apply_blocked_title"), lines);
        return;
      }
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
