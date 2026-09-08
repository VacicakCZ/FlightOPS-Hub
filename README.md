# ✈️ FlightOps Hub

<p align="center">
  <img src="https://img.shields.io/github/v/release/VacicakCZ/FlightOPS-Hub?style=for-the-badge&color=007acc" alt="Latest Release" />
  <img src="https://img.shields.io/github/downloads/VacicakCZ/FlightOPS-Hub/total?style=for-the-badge&color=28a745" alt="Total Downloads" />
  <img src="https://img.shields.io/badge/MSFS-2020%20%7C%202024-blue?style=for-the-badge" alt="MSFS Support" />
  <img src="https://img.shields.io/badge/UI-pywebview%20%2B%20HTML%2FJS-orange?style=for-the-badge" alt="Tech Stack" />
  <img src="https://img.shields.io/github/license/VacicakCZ/FlightOPS-Hub?style=for-the-badge" alt="License" />
</p>

<p align="center">
  <b>A modern, portable pre-flight & Community folder manager for Microsoft Flight Simulator.</b><br>
  Organize add-ons, auto-toggle scenery and aircraft, validate SimBrief routes, manage GSX profiles, and automate your flight setup with live SimConnect integration.
</p>

---

## 🌟 Key Features in v2.0

### ✈️ Flight & Smart Launch
* **Smart Launch (SimConnect):** Detects when you actually spawn in the cockpit using live `SimConnect` data and launches delayed add-ons (like FSLTL or REX Atmos) at the right moment - not just after a fixed timer.
* **Smart Process Detection:** Skips re-launching MSFS if the simulator is already running.
* **Launch Feedback & File Guard:** Visible feedback while launching, and a warning (with the option to proceed anyway) if a selected add-on's file can no longer be found.
* **Background Watch Mode:** Stays minimized in the system tray, monitors the flight session, and automatically closes every add-on it started once MSFS exits.

### 🗺️ Interactive Scenery & SimBrief Integration
* **Interactive World Map (Leaflet):** Every installed Community scenery plotted by ICAO code. Enable or disable a scenery right from its map marker.
* **SimBrief Route Validation:** Load your latest SimBrief flight plan and check origin/destination/alternate against your installed sceneries - enable whatever's missing with one click, and see the route drawn on the map.
* **GSX (virtuali) Profile Detection:** Each scenery shows whether a matching GSX profile is installed, color-coded:
  * 🟢 **Green:** profile installed, looks like the same developer as the scenery.
  * 🟠 **Orange:** profile installed, developer unconfirmed.
  * 🔴 **Red:** no profile found - click to search flightsim.to directly.
* **Search** across sceneries (ICAO, name, or country) to jump straight to a specific airport.

### 🛩️ Aircraft & Liveries (Beta)
* **Smart Grouping:** Toggle aircraft and liveries on/off safely (moves folders in/out of Community - nothing is ever deleted). Liveries are automatically nested under their matching aircraft and developer.
* **Manual Classification Overrides:** Search, and manually re-assign an aircraft/livery if the automatic detection gets it wrong.

### ⚙️ AutoStart (`exe.xml`) & System Tools
* **Safe XML Management:** Enable/disable MSFS's own internal AutoStart add-ons, with custom display names that don't touch the underlying XML tags. Includes a search box for long add-on lists.
* **One-Click Backup Restore:** FlightOps Hub automatically backs up `exe.xml` before its first edit - restore it from the UI at any time.
* **AIRAC Cycle Validation:** Shows whether your installed Navigraph AIRAC cycle is current.
* **Configurable paths:** Community folder, GSX profiles folder, and the `exe.xml` location itself can all be overridden in Settings if auto-detection ever picks the wrong one.

### 🌍 Languages & Updates
* Interface available in **English, Czech, German, Spanish, and Chinese** (Czech and English are hand-written; the rest are AI-translated and flagged as such in-app).
* Checks GitHub Releases on startup and shows a subtle in-app notice - never a popup - when a newer version is available, with the changelog right there to read.

---

## 🛠️ Technology Stack

* **Backend:** Python 3, `pywebview`, `SimConnect` API, `pystray`, `requests`
* **Frontend:** HTML5, modern CSS, [Alpine.js](https://alpinejs.dev/), vanilla JavaScript (ES6) - no build step
* **Mapping & Flight Planning:** [Leaflet.js](https://leafletjs.com/), SimBrief REST API

---

## 🚀 Installation & Usage

FlightOps Hub is **100% portable** - no installer, no admin rights needed.

1. Download the latest release `.zip` from [Releases](https://github.com/VacicakCZ/FlightOPS-Hub/releases) or [flightsim.to](https://flightsim.to/addon/114794/flightops-hub).
2. Extract the archive anywhere on your PC.
3. Run `flightops_hub.exe`.

> **Note on UAC (Administrator rights):** You do **not** need to run FlightOps Hub itself as administrator. Individual add-ons that require elevated rights can be flagged with "Run as Administrator (UAC)" per-entry in Settings.

---

## 💻 Local Development Setup

If you want to run the project from source or contribute:

1. **Clone the repository:**
   ```bash
   git clone https://github.com/VacicakCZ/FlightOPS-Hub.git
   cd FlightOPS-Hub
   ```
2. **Install dependencies** (Python 3.10+ recommended):
   ```bash
   pip install -r requirements.txt
   ```
3. **Run from source:**
   ```bash
   python flightops_hub.pyw
   ```
4. **Build a standalone .exe** (PyInstaller, already configured via `flightops_hub.spec` as a single-file build with the `frontend/` assets bundled in):
   ```bash
   pyinstaller flightops_hub.spec
   ```
   The built executable is written to `dist/flightops_hub.exe`.

---

## 🧪 Tests

Pure-logic backend modules (config migration, GSX detection, scenery scanning, version comparison, ...) have a pytest suite:
```bash
pip install -r requirements-dev.txt
pytest
```

---

## 📄 License

Licensed under the [GNU General Public License v3.0](LICENSE) - you're free to use, modify, and redistribute this project, as long as derivative works stay open source under the same license.

---

Happy flying! ✈️
