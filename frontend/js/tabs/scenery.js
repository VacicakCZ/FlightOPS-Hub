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
    simbriefChecking: false,
    simbriefError: null,
    simbriefLegs: [],
    mapView: false,
    mapMarkers: [],
    _map: null,
    _markersLayer: null,
    _routeLayer: null,
    _lightTiles: null,
    _darkTiles: null,

    async init() {
      await window.AppReady;
      await this.load();

      FlightOpsEvents.on("scenery_apply_progress", (payload) => this.onProgress(payload));
      FlightOpsEvents.on("scenery_apply_done", (payload) => this.onDone(payload));
      // Community/disabled-holding path can change after this tab already
      // mounted (every tab mounts at startup regardless of which is visible)
      // - most commonly a first-time user who sets the Community folder
      // only after the app is already open. Without this, the tab would be
      // stuck showing whatever it saw at startup (usually "no community
      // folder set") forever.
      FlightOpsEvents.on("config_changed", async () => {
        await this.load();
        if (this.mapView) {
          await this.loadMapData();
          this.renderMap();
        }
      });

      // Theme can change (Settings, or the OS-level setting while on
      // "system") while the map already exists - swap its tile layer to
      // match instead of leaving a bright map in a dark UI.
      this.$watch("$store.app.theme", () => this.updateMapTheme());
      window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => this.updateMapTheme());
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
      if (this.mapView) {
        await this.loadMapData();
        this.renderMap();
      }
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
      await this.refreshSimbriefStatuses();
      if (this.mapView) {
        await this.loadMapData();
        this.renderMap();
      }
    },

    // --- map view: same records, plotted by ICAO instead of listed by
    // continent/country. Sceneries with no recognizable ICAO code (freeware
    // landmarks, mesh/POI packs, ...) simply have nothing to plot and are
    // left out - scenery_map_hint below the map says how many that is. ---
    async showMapView() {
      this.mapView = true;
      await this.loadMapData();
      this.$nextTick(() => this.renderMap());
    },

    showListView() {
      this.mapView = false;
    },

    async loadMapData() {
      const result = await Api.sceneryMapData();
      this.mapMarkers = result.markers;
    },

    renderMap() {
      if (!this._map) {
        this._map = L.map(this.$refs.mapContainer).setView([30, 10], 2);
        L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
          maxZoom: 18,
          attribution: "&copy; OpenStreetMap contributors",
        }).addTo(this._map);
        this._markersLayer = L.layerGroup().addTo(this._map);
        this._routeLayer = L.layerGroup().addTo(this._map);
      }
      // The container has zero size while the tab (or the list view inside
      // it) was hidden, so Leaflet needs an explicit nudge once it's
      // actually visible again, or tiles render into a collapsed box.
      this._map.invalidateSize();
      this.updateMapTheme();
      this._markersLayer.clearLayers();
      for (const marker of this.mapMarkers) {
        const icon = L.divIcon({
          className: "scenery-map-marker " + (marker.enabled ? "marker-on" : "marker-off"),
          iconSize: [14, 14],
        });
        L.marker([marker.lat, marker.lon], { icon })
          .addTo(this._markersLayer)
          .bindPopup(() => this.buildPopup(marker));
      }
      this.renderRoute();
    },

    cssVar(name) {
      return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    },

    isDarkMode() {
      const explicit = document.documentElement.getAttribute("data-theme");
      if (explicit === "dark") return true;
      if (explicit === "light") return false;
      return window.matchMedia("(prefers-color-scheme: dark)").matches;
    },

    // No dark tile source is used (free no-key dark basemaps aren't reliably
    // available - CartoDB's now requires an API key) - instead the single
    // OSM tile pane gets a CSS invert filter in dark mode. Scoped to
    // .leaflet-tile-pane only, so markers/popups/controls (styled
    // separately in base.css) aren't affected.
    updateMapTheme() {
      if (!this.$refs.mapContainer) return;
      this.$refs.mapContainer.classList.toggle("map-dark", this.isDarkMode());
    },

    // Draws the SimBrief route (origin -> destination -> alternate) on top
    // of the scenery markers, using coordinates the backend already looked
    // up per leg - independent of whether that airport has scenery
    // installed at all, so an alternate with no scenery still shows up.
    renderRoute() {
      if (!this._routeLayer) return;
      this._routeLayer.clearLayers();
      const legsWithCoords = this.simbriefLegs.filter((leg) => leg.lat != null && leg.lon != null);
      if (legsWithCoords.length < 2) return;

      const accent = this.cssVar("--accent") || "#2f6fed";
      L.polyline(
        legsWithCoords.map((leg) => [leg.lat, leg.lon]),
        { color: accent, weight: 2, dashArray: "6 6" }
      ).addTo(this._routeLayer);

      for (const leg of legsWithCoords) {
        L.circleMarker([leg.lat, leg.lon], {
          radius: 6,
          color: accent,
          fillColor: accent,
          fillOpacity: 1,
          weight: 2,
        })
          .bindTooltip(`${this.$store.app.t("simbrief_role_" + leg.role)} ${leg.icao}`)
          .addTo(this._routeLayer);
      }
    },

    buildPopup(marker) {
      const app = this.$store.app;
      const el = document.createElement("div");
      el.className = "map-popup";

      const title = document.createElement("strong");
      title.textContent = marker.display_name;
      el.appendChild(title);

      const button = document.createElement("button");
      button.className = "btn";
      button.textContent = marker.enabled ? app.t("scenery_map_disable_btn") : app.t("simbrief_enable_btn");
      button.addEventListener("click", () => {
        this._map.closePopup();
        this.quickToggle(marker.folder_name, !marker.enabled);
      });
      el.appendChild(button);

      return el;
    },

    // --- SimBrief flight-plan check: suggests enabling installed-but-
    // disabled sceneries for the planned origin/destination/alternate ---
    get simbriefUsernameSet() {
      return !!(this.$store.app.config._simbrief_username || "").trim();
    },

    async checkSimbrief() {
      this.simbriefChecking = true;
      this.simbriefError = null;
      const result = await Api.simbriefCheck();
      this.simbriefChecking = false;
      if (!result.ok) {
        this.simbriefError = result.error;
        this.simbriefLegs = [];
        this.renderRoute();
        return;
      }
      this.simbriefLegs = result.legs;
      this.renderRoute();
    },

    // After an Apply (whether triggered from the main tree or from a
    // SimBrief quick-enable), refresh any already-shown legs in place so
    // their status reflects reality instead of going stale.
    async refreshSimbriefStatuses() {
      if (!this.simbriefLegs.length) return;
      await this.checkSimbrief();
    },

    // "Zapnout": applies immediately instead of just queueing a pending
    // change, per explicit request - merges into whatever is already
    // pending (if the user had unrelated toggles queued up, this Apply
    // still carries them along) rather than discarding them.
    quickEnable(folderName) {
      this.quickToggle(folderName, true);
    },

    quickToggle(folderName, enabled) {
      this.pending[folderName] = enabled;
      this.apply();
    },
  }));
});
