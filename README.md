# ✈️ FlightOps Hub

<p align="center">
  <img src="assets/logowide.jpg" alt="FlightOps Hub" width="720" />
</p>

<p align="center">
  <img src="https://img.shields.io/github/v/release/VacicakCZ/FlightOPS-Hub?style=for-the-badge&color=007acc" alt="Latest Release" />
  <img src="https://img.shields.io/github/downloads/VacicakCZ/FlightOPS-Hub/total?style=for-the-badge&color=28a745" alt="Total Downloads" />
  <img src="https://img.shields.io/github/actions/workflow/status/VacicakCZ/FlightOPS-Hub/tests.yml?style=for-the-badge&label=tests" alt="Tests" />
  <img src="https://img.shields.io/badge/MSFS-2020%20%7C%202024-blue?style=for-the-badge" alt="MSFS Support" />
  <img src="https://img.shields.io/badge/UI-WPF%20%2B%20WebView2-orange?style=for-the-badge" alt="Tech Stack" />
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
* **SimBrief Route Validation:** Load your latest SimBrief flight plan and check origin/destination/alternate against your installed sceneries - enable whatever's missing with one click, see your actual planned route drawn on the map (not just a straight line between airports), and get a quick summary (planned aircraft, flight time, when the plan was generated) to confirm you're looking at the right one.
* **Live ATC Awareness (VATSIM/IVAO, optional):** Shows which ATC positions - and their frequencies - are currently online at your origin, destination, and alternate, both as a badge in the SimBrief panel and as clickable markers on the map.
* **AIRAC Cycle Mismatch Warning:** Flags when your SimBrief flight plan was generated with a different navdata cycle than what you actually have installed.
* **GSX (virtuali) Profile Detection:** Each scenery shows whether a matching GSX profile is installed, color-coded:
  * 🟢 **Green:** profile installed, looks like the same developer as the scenery.
  * 🟠 **Orange:** profile installed, developer unconfirmed.
  * 🔴 **Red:** no profile found - click to search flightsim.to directly.
* **Search** across sceneries (ICAO, name, or country) to jump straight to a specific airport.
* **Community Folder Health Check:** Catches misplaced installs (a manifest nested one folder level too deep - a common zip-extraction mistake MSFS silently ignores) and duplicate installs (the same package under two or more different folder names). Read-only - nothing is ever moved or deleted automatically.
* **Disk Usage & Space Protection:** See exactly how much space your Community and disabled-add-ons folders are using, broken down by package - plus a warning when a drive is running low, and an upfront check before enabling/disabling anything across two drives, so a full disk blocks the whole operation cleanly instead of failing partway through a copy.

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
* **One-Click Updates:** Checks GitHub Releases on startup and shows a subtle in-app notice - never a popup. Confirm once and FlightOps Hub downloads, silently installs, and restarts itself automatically - no installer window, nothing to double-click yourself.
* **Minimize to Tray & Launch at Startup:** The native minimize button sends FlightOps Hub to the system tray instead of the taskbar; turn on "Launch FlightOps Hub when Windows starts" to have it launch automatically, straight to tray, every time you log in.
* **Tray Quick-Launch:** Right-click the tray icon for a **Launch** action plus **Flight profile** / **exe.xml profile** submenus - pick which of each to use (remembered for next time), then Launch applies both and starts MSFS + your selected companion apps, no need to open the window at all.

### 🌍 Languages
* Interface available in **English, Czech, German, Spanish, and Chinese** (Czech and English are hand-written; the rest are AI-translated and flagged as such in-app).

---

## 🛠️ Technology Stack

* **Backend:** .NET 10, WPF host + [`Microsoft.Web.WebView2`](https://learn.microsoft.com/microsoft-edge/webview2/), `SimConnect` API
* **Frontend:** HTML5, modern CSS, [Alpine.js](https://alpinejs.dev/), vanilla JavaScript (ES6) - no build step, unchanged from the previous Python build
* **Mapping & Flight Planning:** [Leaflet.js](https://leafletjs.com/), SimBrief REST API

> **v3.0 note:** FlightOps Hub was rewritten from Python/`pywebview` to .NET/WPF for v3.0, mainly so "minimize to tray" and "launch at Windows startup" could be built on first-party Windows APIs instead of a third-party tray library. The frontend (this repo's `frontend/` folder) is unchanged - only the native host and the ~55 methods it exposes to it were rewritten.

---

## 🚀 Installation & Usage

FlightOps Hub ships as a small **installer** - per-user, no admin rights needed.

1. Download `FlightOpsHub-Setup-x.x.x.exe` from [Releases](https://github.com/VacicakCZ/FlightOPS-Hub/releases) or [flightsim.to](https://flightsim.to/addon/114794/flightops-hub) and run it - it asks which of the app's 5 languages (English, Czech, German, Spanish, Chinese Simplified) to run the installer itself in.
2. Installs to `%LocalAppData%\Programs\FlightOpsHub` with a Start Menu shortcut (and an optional desktop icon) - no elevation prompt.
3. Launch it from the Start Menu, or turn on **"Launch FlightOps Hub when Windows starts"** in Settings to have it start automatically (minimized straight to the system tray).

The installer is small (a few MB) because it doesn't bundle the .NET runtime itself - if the **.NET 10 Desktop Runtime** isn't already on your PC (most Windows 10/11 machines don't have it by default, unlike the WebView2 Runtime, which ships with Windows/Edge), the installer downloads and installs it automatically before finishing. This needs an internet connection and, the first time only, may prompt for admin rights for that one component (FlightOps Hub itself never needs them).

Your settings/addon list/log live in `%LocalAppData%\FlightOpsHub` (not the install folder), so they survive an uninstall/reinstall for the same version - see [diagnostics](docs/MANUAL.md#frequently-asked-questions) if you ever need to attach `flightops_hub.log` to a bug report.

> **Note on UAC (Administrator rights):** You do **not** need to run FlightOps Hub itself as administrator, and installing it doesn't need admin rights either. Individual add-ons that require elevated rights can be flagged with "Run as Administrator (UAC)" per-entry in Settings.

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
2. **Run the .NET app from source** (.NET 10 SDK required):
   ```bash
   dotnet run --project dotnet/src/FlightOpsHub.App
   ```
3. **Build the installer** (Inno Setup 6 required - `iscc` on PATH, or the default install location):
   ```bash
   dotnet publish dotnet/src/FlightOpsHub.App -c Release -r win-x64 --self-contained false -o dotnet/publish
   iscc /DMyAppVersion=3.0.0 dotnet/installer/flightops_hub.iss
   ```
   The installer is written to `dotnet/installer/Output/`. `.github/workflows/release-dotnet.yml` runs the same two steps in CI on every version tag push.

The `backend/`/`flightops_hub.pyw` Python sources are still in this repo as the reference implementation the .NET rewrite was ported from and is still checked against - not the shipped app anymore, but not dead either; `python flightops_hub.pyw` (after `pip install -r requirements.txt`) still runs it if you need to compare behavior against the original.

---

## 🧪 Tests

The .NET app has an xUnit suite (`dotnet/tests/FlightOpsHub.Tests`); the Python reference implementation keeps its own pytest suite plus a script that checks all 5 locale files stay in sync:
```bash
dotnet test dotnet/FlightOpsHub.sln

pip install -r requirements-dev.txt
pytest
python scripts/check_locales.py
```
All of these run automatically on every push and pull request via GitHub Actions (`.github/workflows/tests.yml`).

---

## 📄 License

Licensed under the [GNU General Public License v3.0](LICENSE) - you're free to use, modify, and redistribute this project, as long as derivative works stay open source under the same license.

---

Happy flying! ✈️
