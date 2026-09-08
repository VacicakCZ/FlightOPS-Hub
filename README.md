# ✈️ FlightOps Hub

<p align="center">
  <img src="assets/logowide.jpg" alt="FlightOps Hub" width="720" />
</p>

<p align="center">
  <img src="https://img.shields.io/github/v/release/VacicakCZ/FlightOPS-Hub?style=for-the-badge&color=007acc" alt="Latest Release" />
  <img src="https://img.shields.io/github/downloads/VacicakCZ/FlightOPS-Hub/total?style=for-the-badge&color=28a745" alt="Total Downloads" />
  <img src="https://img.shields.io/github/actions/workflow/status/VacicakCZ/FlightOPS-Hub/tests.yml?style=for-the-badge&label=tests" alt="Tests" />
  <img src="https://img.shields.io/badge/MSFS-2020%20%7C%202024-blue?style=for-the-badge" alt="MSFS Support" />
  <img src="https://img.shields.io/badge/UI-pywebview%20%2B%20HTML%2FJS-orange?style=for-the-badge" alt="Tech Stack" />
  <img src="https://img.shields.io/github/license/VacicakCZ/FlightOPS-Hub?style=for-the-badge" alt="License" />
</p>

<p align="center">
  <b>A modern, portable pre-flight & Community folder manager for Microsoft Flight Simulator.</b><br>
  Organize add-ons, auto-toggle scenery and aircraft, validate SimBrief routes, manage GSX profiles, and automate your flight setup with live SimConnect integration.
</p>

<p align="center">
  📖 <a href="docs/MANUAL.md"><b>User Manual</b></a> — what each tab does and how to use it, plus a troubleshooting FAQ.
</p>

---

## 🌟 Key Features

### ✈️ Flight & Smart Launch
* **Smart Launch (SimConnect):** Detects when you actually spawn in the cockpit using live `SimConnect` data and launches delayed add-ons (like FSLTL or REX Atmos) at the right moment - not just after a fixed timer.
* **Smart Process Detection:** Skips re-launching MSFS if the simulator is already running.
* **Launch Feedback & File Guard:** Visible feedback while launching, and a warning (with the option to proceed anyway) if a selected add-on's file can no longer be found.
* **Background Watch Mode:** Stays minimized in the system tray, monitors the flight session, and automatically closes every add-on it started once MSFS exits.

### 🗺️ Interactive Scenery & SimBrief Integration
* **Interactive World Map (Leaflet):** Every installed Community scenery plotted by ICAO code. Enable or disable a scenery right from its map marker.
* **SimBrief Route Validation:** Load your latest SimBrief flight plan and check origin/destination/alternate against your installed sceneries - enable whatever's missing with one click, see the route drawn on the map, and get a quick summary (planned aircraft, flight time, when the plan was generated) to confirm you're looking at the right one.
* **GSX (virtuali) Profile Detection:** Each scenery shows whether a matching GSX profile is installed, color-coded:
  * 🟢 **Green:** profile installed, looks like the same developer as the scenery.
  * 🟠 **Orange:** profile installed, developer unconfirmed.
  * 🔴 **Red:** no profile found - click to search flightsim.to directly.
* **Search** across sceneries (ICAO, name, or country) to jump straight to a specific airport.
* **Community Folder Health Check:** Catches misplaced installs (a manifest nested one folder level too deep - a common zip-extraction mistake MSFS silently ignores) and duplicate installs (the same package under two or more different folder names). Read-only - nothing is ever moved or deleted automatically.
* **Disk Usage:** See exactly how much space your Community and disabled-add-ons folders are using, broken down by package.

### 🛩️ Aircraft & Liveries (Beta)
* **Smart Grouping:** Toggle aircraft and liveries on/off safely (moves folders in/out of Community - nothing is ever deleted). Liveries are automatically nested under their matching aircraft and developer, collapsible per aircraft.
* **Real Developer Names:** A curated lookup resolves cryptic folder prefixes (`fnx`, `tfdidesign`, ...) to real studio names (Fenix Simulations, TFDi Design, ...) instead of guessing.
* **Reliable Livery Detection:** Cross-checks a package's actual `aircraft.cfg` wiring - not just its self-reported manifest type - to catch registration/repaint packs that mislabel themselves as standalone aircraft. Ambiguous cases get a dismissible suggestion instead of an automatic change.
* **Manual Classification Overrides:** Manually re-assign an aircraft/livery (and its parent) if the automatic detection still gets it wrong.

### ⚙️ AutoStart (`exe.xml`) & System Tools
* **Safe XML Management:** Enable/disable MSFS's own internal AutoStart add-ons, with custom display names that don't touch the underlying XML tags. Includes a search box for long add-on lists.
* **One-Click Backup Restore:** FlightOps Hub automatically backs up `exe.xml` before its first edit - restore it from the UI at any time.
* **AIRAC Cycle Validation:** Shows whether your installed Navigraph AIRAC cycle is current.
* **Configurable paths:** Community folder, GSX profiles folder, and the `exe.xml` location itself can all be overridden in Settings if auto-detection ever picks the wrong one.

### 🔧 Settings, Backups & Diagnostics
* **Backup & Restore:** Export your flight profiles, aircraft classification overrides, and addon list to one portable file - useful before reinstalling or moving to a new PC.
* **Diagnostics Export:** Bundles your app version, OS info, settings, and a recent activity log into one text file, ready to attach to a GitHub issue.
* **One-Click Updates:** Checks GitHub Releases on startup and shows a subtle in-app notice - never a popup. Downloading the update goes straight to your Downloads folder, ready to swap in for the running copy (with a confirmation if a file is already there).

### 🌍 Languages
* Interface available in **English, Czech, German, Spanish, and Chinese** (Czech and English are hand-written; the rest are AI-translated and flagged as such in-app).

---

## 🛠️ Technology Stack

* **Backend:** Python 3, `pywebview`, `SimConnect` API, `pystray`, `requests`
* **Frontend:** HTML5, modern CSS, [Alpine.js](https://alpinejs.dev/), vanilla JavaScript (ES6) - no build step
* **Mapping & Flight Planning:** [Leaflet.js](https://leafletjs.com/), SimBrief REST API

---

## 🚀 Installation & Usage

FlightOps Hub is **100% portable** - no installer, no admin rights needed.

1. Download `flightops_hub.exe` from [Releases](https://github.com/VacicakCZ/FlightOPS-Hub/releases) or [flightsim.to](https://flightsim.to/addon/114794/flightops-hub) and put it in a folder of your choice.
2. Run it.

A few small files appear next to the exe the first time you run it - `msfs_apps.json` (your addon list), `msfs_launcher_config.json` (your settings), and `flightops_hub.log` (a runtime log, useful for [diagnostics](docs/MANUAL.md#frequently-asked-questions)). Keep them next to the exe and your setup carries over between updates.

> **Note on UAC (Administrator rights):** You do **not** need to run FlightOps Hub itself as administrator. Individual add-ons that require elevated rights can be flagged with "Run as Administrator (UAC)" per-entry in Settings.

> **Code signing:** This project has applied for free Windows code signing through the [SignPath Foundation](https://signpath.org/) open-source program. Until that's approved and wired into the release build, Windows SmartScreen may still show an "unknown publisher" warning on first run - the app itself is unaffected either way.

For a full walkthrough of every tab and feature, see the **[User Manual](docs/MANUAL.md)**.

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

Pure-logic backend modules (config migration, aircraft/scenery classification, GSX detection, SimBrief parsing, backup/diagnostics bundling, version comparison, ...) have a pytest suite, plus a script that checks all 5 locale files stay in sync:
```bash
pip install -r requirements-dev.txt
pytest
python scripts/check_locales.py
```
Both run automatically on every push and pull request via GitHub Actions (`.github/workflows/tests.yml`).

---

## 📄 License

Licensed under the [GNU General Public License v3.0](LICENSE) - you're free to use, modify, and redistribute this project, as long as derivative works stay open source under the same license.

---

Happy flying! ✈️
