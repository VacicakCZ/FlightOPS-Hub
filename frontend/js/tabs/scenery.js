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
    simbriefSummary: null,
    simbriefRoutePoints: [],
    atcPopoverFor: null,
    mapView: false,
    mapMarkers: [],
    _map: null,
    _markersLayer: null,
    _routeLayer: null,
    searchQuery: "",

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

      // Auto-expand whatever the search currently matches, so results are
      // visible without also having to manually click through the tree.
      // Clearing the search collapses everything back - otherwise every
      // continent/country the search had opened stays open, which is
      // exactly the cluttered tree the search was meant to cut through.
      this.$watch("searchQuery", (value) => {
        if (value.trim()) {
          this.expandSearchMatches();
        } else {
          this.expandedContinents = {};
          this.expandedCountries = {};
        }
      });
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
        if (!this.matchesSearch(r)) continue;
        (map[r.continent] ??= []).push(r);
      }
      return map;
    },

    get visibleContinents() {
      return this.continentOrder.filter((c) => this.byContinent[c] && this.byContinent[c].length);
    },

    // Matches ICAO/name (via display_name, which already embeds the code),
    // the official airport name, or the country - so "prague", "lkpr" and
    // "czech" all find the same entry.
    matchesSearch(record) {
      const q = this.searchQuery.trim().toLowerCase();
      if (!q) return true;
      return (
        (record.display_name || "").toLowerCase().includes(q) ||
        (record.airport_name || "").toLowerCase().includes(q) ||
        (record.country || "").toLowerCase().includes(q)
      );
    },

    expandSearchMatches() {
      if (!this.searchQuery.trim()) return;
      for (const continentKey of this.visibleContinents) {
        this.expandedContinents[continentKey] = true;
        for (const country of this.countriesFor(this.byContinent[continentKey])) {
          this.expandedCountries[continentKey + "::" + country.name] = true;
        }
      }
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

    // Two+ enabled sceneries sharing the same ICAO fight over the same
    // tiles in the sim and cause visual glitches - easy to catch since the
    // ICAO is already attached to every record (gsx_profiles.attach_gsx_status),
    // and cheap to recompute reactively off isEnabled()/pending as the user
    // toggles things, no backend round-trip needed.
    get conflicts() {
      const byIcao = {};
      for (const r of this.allRecords) {
        if (!r.icao || !this.isEnabled(r)) continue;
        (byIcao[r.icao] ??= []).push(r);
      }
      return Object.entries(byIcao)
        .filter(([, records]) => records.length > 1)
        .map(([icao, records]) => ({ icao, records }));
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

    // Initial compass bearing (degrees, 0 = north, clockwise) from one
    // lat/lon point to another - used to point the direction arrow along
    // each route segment the same way a real course line would.
    _bearingDeg(from, to) {
      const toRad = (deg) => (deg * Math.PI) / 180;
      const toDeg = (rad) => (rad * 180) / Math.PI;
      const phi1 = toRad(from[0]);
      const phi2 = toRad(to[0]);
      const deltaLambda = toRad(to[1] - from[1]);
      const y = Math.sin(deltaLambda) * Math.cos(phi2);
      const x = Math.cos(phi1) * Math.sin(phi2) - Math.sin(phi1) * Math.cos(phi2) * Math.cos(deltaLambda);
      return (toDeg(Math.atan2(y, x)) + 360) % 360;
    },

    _addDirectionArrow(from, to) {
      const bearing = this._bearingDeg(from, to);
      const midpoint = [(from[0] + to[0]) / 2, (from[1] + to[1]) / 2];
      const icon = L.divIcon({
        className: "route-arrow",
        html: `<div style="transform: rotate(${bearing}deg)">&#9650;</div>`,
        iconSize: [16, 16],
        iconAnchor: [8, 8],
      });
      L.marker(midpoint, { icon, interactive: false }).addTo(this._routeLayer);
    },

    // Draws the SimBrief route on top of the scenery markers, using
    // coordinates the backend already looked up per leg - independent of
    // whether that airport has scenery installed at all, so an alternate
    // with no scenery still shows up. Main route (origin -> destination)
    // follows the real navlog waypoints when SimBrief provided them (see
    // simbrief_client._parse_route_points), falling back to a straight
    // line otherwise; it's a solid, bold line either way. The diversion to
    // the alternate (off destination) has no planned navlog of its own -
    // SimBrief doesn't compute one for a "what if" diversion - so it always
    // stays a straight dashed line, visually distinguishing "the flight"
    // from "the backup". A single direction arrow near the middle of each
    // segment shows which way it's flown, and every leg gets a permanent
    // ICAO label instead of a hover-only one - plus, when an ATC network is
    // selected in Settings, a green ring and a click-to-open popover with
    // the position/frequency breakdown (same data as the leg's ATC badge).
    renderRoute() {
      if (!this._routeLayer) return;
      this._routeLayer.clearLayers();
      const byRole = {};
      for (const leg of this.simbriefLegs) {
        if (leg.lat != null && leg.lon != null) byRole[leg.role] = leg;
      }

      const accent = this.cssVar("--accent") || "#2f6fed";
      const point = (leg) => [leg.lat, leg.lon];

      if (byRole.origin && byRole.destination) {
        const navlogPoints = this.simbriefRoutePoints.length >= 2
          ? this.simbriefRoutePoints.map((p) => [p.lat, p.lon])
          : [point(byRole.origin), point(byRole.destination)];
        L.polyline(navlogPoints, { color: accent, weight: 4 }).addTo(this._routeLayer);
        const midIdx = Math.max(1, Math.floor(navlogPoints.length / 2));
        this._addDirectionArrow(navlogPoints[midIdx - 1], navlogPoints[midIdx]);
      }
      if (byRole.destination && byRole.alternate) {
        const from = point(byRole.destination);
        const to = point(byRole.alternate);
        L.polyline([from, to], { color: accent, weight: 2, dashArray: "6 6" }).addTo(this._routeLayer);
        this._addDirectionArrow(from, to);
      }

      const atcNetworkOn = this.$store.app.config._atc_network && this.$store.app.config._atc_network !== "off";
      for (const leg of Object.values(byRole)) {
        // A divIcon L.marker, not L.circleMarker - Leaflet's SVG vector
        // layers (circleMarker/polyline) only redraw at zoomend, so a
        // tooltip bound to one visually freezes mid-zoom and jumps at the
        // end. Icon markers get repositioned every animation frame, so
        // their bound tooltip tracks the zoom smoothly instead.
        const atcOnline = atcNetworkOn && leg.atc_online;
        const icon = L.divIcon({
          className: "route-point-marker" + (atcOnline ? " atc-online" : ""),
          iconSize: [14, 14],
          iconAnchor: [7, 7],
        });
        const marker = L.marker(point(leg), { icon })
          .bindTooltip(leg.icao, { permanent: true, direction: "top", offset: [0, -6], className: "route-icao-label" });
        if (atcNetworkOn) {
          marker.bindPopup(() => this.buildAtcPopup(leg));
        }
        marker.addTo(this._routeLayer);
      }
    },

    // Same content as the leg row's ATC popover (index.html), just built
    // as a plain DOM node since Leaflet popup content isn't Alpine-templated.
    buildAtcPopup(leg) {
      const app = this.$store.app;
      const el = document.createElement("div");
      el.className = "map-popup atc-map-popup";

      const title = document.createElement("strong");
      title.textContent = app.t("atc_popover_title", leg.icao);
      el.appendChild(title);

      if (leg.atc_positions && leg.atc_positions.length) {
        for (const pos of leg.atc_positions) {
          const row = document.createElement("div");
          row.className = "atc-popover-row";
          const posSpan = document.createElement("span");
          posSpan.textContent = pos.position;
          const freqSpan = document.createElement("span");
          freqSpan.className = "hint";
          freqSpan.textContent = pos.frequency ? `(${pos.frequency})` : "";
          row.appendChild(posSpan);
          row.appendChild(freqSpan);
          el.appendChild(row);
        }
      } else {
        const empty = document.createElement("p");
        empty.className = "hint";
        empty.textContent = app.t("atc_popover_empty");
        el.appendChild(empty);
      }

      return el;
    },

    buildPopup(marker) {
      const app = this.$store.app;
      const el = document.createElement("div");
      el.className = "map-popup";

      const title = document.createElement("strong");
      title.textContent = marker.display_name;
      el.appendChild(title);

      if (marker.developer) {
        const developer = document.createElement("span");
        developer.className = "scenery-developer";
        developer.textContent = ` (${marker.developer})`;
        title.appendChild(developer);
      }

      if (marker.airport_name) {
        const officialName = document.createElement("div");
        officialName.className = "map-popup-official-name";
        officialName.textContent = marker.airport_name;
        el.appendChild(officialName);
      }

      if (marker.gsx_status) {
        el.appendChild(this.buildGsxTag(marker.gsx_status, marker.icao));
      }

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

    // Builds the same GSX status tag the list view renders declaratively
    // (index.html) - needed here as a DOM node instead since Leaflet popup
    // content is built in JS, not templated.
    buildGsxTag(status, icao) {
      const app = this.$store.app;
      if (status === "installed_match") {
        const tag = document.createElement("span");
        tag.className = "gsx-tag gsx-match";
        tag.textContent = "GSX";
        tag.title = app.t("scenery_gsx_match_tt");
        return tag;
      }
      if (status === "installed_unmatched") {
        const tag = document.createElement("span");
        tag.className = "gsx-tag gsx-unmatched";
        tag.title = app.t("scenery_gsx_unmatched_tt");
        tag.textContent = "GSX";
        return tag;
      }
      // "missing" - clickable, opens a flightsim.to GSX Pro search for this ICAO
      const tag = document.createElement("button");
      tag.type = "button";
      tag.className = "gsx-tag gsx-missing";
      tag.textContent = "GSX";
      tag.title = app.t("scenery_gsx_missing_tt");
      tag.addEventListener("click", (e) => {
        e.stopPropagation();
        this.openGsxSearch(icao);
      });
      return tag;
    },

    async openGsxSearch(icao) {
      await Api.openGsxSearch(icao);
    },

    async openGsxFolder() {
      await Api.openGsxFolder();
    },

    // --- SimBrief flight-plan check: suggests enabling installed-but-
    // disabled sceneries for the planned origin/destination/alternate ---
    get simbriefUsernameSet() {
      return !!(this.$store.app.config._simbrief_username || "").trim();
    },

    async checkSimbrief() {
      this.simbriefChecking = true;
      this.simbriefError = null;
      this.atcPopoverFor = null; // stale reference into the old legs otherwise
      const result = await Api.simbriefCheck();
      this.simbriefChecking = false;
      if (!result.ok) {
        this.simbriefError = result.error;
        this.simbriefLegs = [];
        this.simbriefSummary = null;
        this.simbriefRoutePoints = [];
        this.renderRoute();
        return;
      }
      this.simbriefLegs = result.legs;
      this.simbriefRoutePoints = result.route_points || [];
      // Lets the user sanity-check this is actually the flight they meant -
      // SimBrief has no "current" flight, just whatever was last generated,
      // which could be an old plan if they forgot to regenerate.
      this.simbriefSummary = {
        aircraft_name: result.aircraft_name,
        duration_minutes: result.duration_minutes,
        planned_at: result.planned_at,
      };
      this.renderRoute();
    },

    toggleAtcPopover(icao) {
      this.atcPopoverFor = this.atcPopoverFor === icao ? null : icao;
    },

    formatDuration(minutes) {
      if (minutes == null) return "";
      const h = Math.floor(minutes / 60);
      const m = minutes % 60;
      return h > 0 ? `${h}h ${m}m` : `${m}m`;
    },

    formatPlannedAt(epochSeconds) {
      if (epochSeconds == null) return "";
      return new Date(epochSeconds * 1000).toLocaleString();
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
