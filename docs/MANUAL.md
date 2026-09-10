# FlightOps Hub — User Manual

A practical guide to using FlightOps Hub. For installation instructions and technical/build details, see the main [README](../README.md) instead — this page is about what each part of the app actually does and how to use it day to day.

---

## What is FlightOps Hub?

FlightOps Hub is a Windows companion app for Microsoft Flight Simulator 2020/2024. It solves three everyday annoyances for pilots with a lot of add-ons installed:

- **Launching everything together.** One button starts your selected companion apps (SimBrief, GSX, REX, traffic add-ons, ...) and MSFS itself, instead of clicking through a dozen shortcuts every session.
- **Managing an overgrown Community folder.** Enable/disable installed sceneries and aircraft without manually cutting and pasting folders around, see what's actually using your disk space, and catch broken/duplicate installs.
- **Pre-flight sanity checks.** Compare your SimBrief flight plan against what's actually installed, and check your Navigraph AIRAC currency, before you're already sitting on the runway.

It installs like a normal Windows app (Start Menu shortcut, clean uninstall) but never needs admin rights — see the [README](../README.md#-installation--usage) for the installer itself. Minimize the window and it goes to the system tray instead of the taskbar; turn on "Launch FlightOps Hub when Windows starts" in Settings to have it running (minimized) every time you log in, without a separate background service.

---

## First run

The first time you open FlightOps Hub, a welcome panel walks you through the one thing the app actually needs to function: your MSFS **Community** folder path. The app tries to auto-detect it for the standard Steam/Microsoft Store install locations — if it finds one, just click the suggestion to accept it. If not (custom install location, second drive, etc.), use **Browse...** to point it at your `Community` folder yourself.

GSX folder and SimBrief username are optional at this stage — you can always set them later in **Settings**. Click **✓ Done** once you're happy to close the panel; it won't reappear.

Your settings/addon list/log live in `%LocalAppData%\FlightOpsHub`: `msfs_apps.json` (your addon list), `msfs_launcher_config.json` (your settings), and `flightops_hub.log` (a runtime log - see **Diagnostics** under Settings below). That's separate from wherever the app itself is installed, so updating (or even uninstalling and reinstalling) leaves your setup untouched.

---

## Tabs

### ✈️ Flight

This is where you build your pre-flight checklist and launch it.

- **Add an app**: name + path to its `.exe`, a launch mode, and (for admin-only tools) a "Run as Administrator" flag.
- **Launch modes**:
  - **Immediate** — starts right when you click Launch, alongside MSFS.
  - **Timer** — starts after a fixed delay (in seconds) once MSFS launches. Good for apps that need the sim to be fully loaded first but don't need to wait for you to actually be in the cockpit.
  - **Smart Launch** — waits until you've actually spawned in the cockpit (detected live via SimConnect), not just a guessed delay. Best for anything that should only start once you're really flying (live traffic, some ATC tools).
- **Flight profiles**: save a named set of checked apps (e.g. "Airliner setup" vs. "GA setup") and switch between them instead of re-checking boxes every time.
- **After launch completes**: either **Exit** the app once everything's started, or **Stay and watch for sim exit** — FlightOps Hub minimizes to the system tray, keeps an eye on MSFS, and automatically closes every add-on it started once you close the sim. The tray icon also lets you cancel a pending Smart Launch or force-close everything early.
- If a checked app's file can no longer be found (moved, uninstalled, drive unplugged), the app warns you before launching instead of silently skipping it.

### 🗺️ Scenery

Manages every SCENERY package sitting in your Community folder (and your disabled-add-ons folder).

- **List / Map toggle**: browse by continent → country → airport, or see everything plotted on an interactive map by ICAO code. Both views let you enable/disable directly.
- Checking a box only **queues** the change — nothing is moved on disk until you click **Apply**. Toggling off physically moves the package folder out of Community (into a sibling "disabled" folder, configurable in Settings); toggling back on moves it back. Nothing is ever deleted.
- **Search** by ICAO, airport name, or country to jump straight to what you're looking for.
- **GSX badges** (🟢/🟠/🔴) show whether a matching GSX (virtuali) ground-handling profile is installed for each airport, and whether it looks like it's from the same developer as the scenery. Red = no profile found, click it to search flightsim.to directly.
- **Conflict warning**: if two enabled sceneries claim the same ICAO code, they'll be flagged — having both active can cause visual glitches as they fight over the same tiles.
- **Disk usage**: two buttons in Settings show how much space your Community and disabled-add-ons folders are actually using, broken down by package, so you know what's worth uninstalling. A separate, automatic warning appears if either drive is actually running low on free space; if your disabled-add-ons folder lives on a different drive than Community, **Apply** also checks upfront whether that drive has enough room for what you're about to move — if not, nothing is touched instead of failing partway through a copy.
- **Install health check** (Settings): flags two common mistakes — a package with its manifest one folder level too deep (a common zip-extraction slip that MSFS silently ignores), and the same package installed under two or more different folder names. Both checks are read-only; nothing is changed automatically.
- **SimBrief check**: see below.

### 🛩️ Aircraft *(Beta)*

Same idea as Scenery, but for installed aircraft and liveries.

- Aircraft are grouped by developer, with their liveries nested underneath (collapsed by default — click to expand). Liveries FlightOps Hub couldn't confidently match to a specific aircraft sit in an "unassigned" section under their developer instead.
- Classification (is this an aircraft or a livery? which aircraft does this livery belong to?) is automatic, but add-ons don't always describe themselves accurately — use **⚙ Edit classification** on any item to override it manually.
- When something looks like a livery pack pretending to be a full aircraft (a common issue with registration/repaint packs), you'll see a **⚠️ suggestion** with three choices: fix it automatically, set it manually, or dismiss the hint. Nothing is ever changed without you clicking one of those.
- This tab is marked **Beta** because add-on manifests are inconsistent enough in the wild that classification can't be perfect — always double-check the result the first time you toggle something new.

### ⚙️ AutoStart (exe.xml)

A different mechanism from the Scenery/Aircraft tabs above: this manages MSFS's **own internal** add-on list (`exe.xml`) — typically standalone tools that MSFS itself launches, like GSX or some traffic injectors. Toggling here flips a flag inside that file; it does not move any folders.

- Rename an entry's display name (✏️) without touching the underlying data — handy when an add-on's default name is cryptic.
- FlightOps Hub automatically backs up `exe.xml` the first time it ever edits it. If something looks wrong, **Restore from backup** puts it back exactly as it was before FlightOps Hub touched it.
- Search box for long AutoStart lists.

### 🔧 Settings

- **Community folder** and **disabled-add-ons folder** paths (the latter defaults to a folder right next to Community — only change it if you want disabled add-ons parked somewhere else, e.g. a different drive).
- **MSFS version & platform** (2020/2024, Steam/MS Store) — drives auto-detection for the Community folder and `exe.xml` location. FlightOps Hub remembers a separate Community path per version/platform combination, so switching between a 2020 and 2024 install doesn't mix them up.
- **GSX profiles folder** and **exe.xml location** — both auto-detected, only need changing for non-standard installs.
- **SimBrief username** — used by the SimBrief check on the Scenery tab.
- **ATC Network** — Off (default), VATSIM, or IVAO. Turns on the live ATC-online badges described under the SimBrief check below.
- **UI scale**, **theme** (dark/light/system), and **language** (Czech and English are hand-written; German, Spanish, and Chinese are AI-translated and flagged as such in the language picker).
- **Backup & Restore** — export your flight profiles, aircraft classification overrides, and addon list to a single JSON file, and import it back later. Useful before reinstalling Windows or moving to a new PC, so you don't have to rebuild your setup from scratch. Importing replaces your current settings outright, so you'll be asked to confirm first.
- **Diagnostics** — bundles your app version, OS info, current settings, and a recent activity log into one text file. Attach it to a GitHub issue when reporting a problem — it turns "it doesn't work" into something the maintainer can actually act on. Your SimBrief username is left out (redacted), and your Windows account name is stripped out of any file path (`C:\Users\<user>\...`) wherever it appears, since this file is meant to be posted publicly. Everything else (paths otherwise, profiles, overrides) is included as-is because it's actually needed to diagnose most issues.
- **Updates** — FlightOps Hub quietly checks GitHub for a newer release on startup (never a popup). If one's available, a small collapsible notice appears under the version number at the bottom of Settings; expand it to read the release notes and hit **Update now**. After a one-time confirmation, it downloads the new installer in the background (with a progress bar) and then installs it completely silently — no installer window ever appears — closing and reopening itself automatically once the update is done. Your settings/addon list aren't touched, they live outside the install folder (see **First run** above). Prefer to handle it yourself instead? **Open on GitHub** takes you straight to the release page to download and run it manually. The version number itself is also a link straight to that release's notes on GitHub.

---

## SimBrief flight-plan check

On the Scenery tab, **Check against SimBrief flight** fetches your last-generated SimBrief OFP and shows:

- The **origin/destination/alternate** airports, and whether each has a matching scenery installed (and whether it's currently enabled) — with a one-click **Enable** for anything installed but switched off.
- A quick **summary**: planned aircraft, estimated flight time, and when the plan was generated — since SimBrief has no concept of "today's flight," just whatever you last generated, this lets you confirm at a glance you're not looking at a stale plan from last week.
- An **AIRAC mismatch warning** if your plan was generated with a different navdata cycle than what you actually have installed — hover the badge for the exact cycle numbers.
- Your actual planned route drawn on the map (not a straight line), switchable to Map view.

**ATC awareness (optional):** pick VATSIM or IVAO in Settings → ATC Network to see which ATC positions are currently online at your origin, destination, and alternate — a badge next to each leg, and clickable markers on the map, showing the position and frequency (e.g. "Prague TWR — 118.100"). Off by default; switching it on makes one extra request to that network's public data feed each time you check. Purely a heads-up for planning — not a live traffic display.

This only reads your public SimBrief data (via your username) — no login, no write access.

---

## Frequently asked questions

**Windows says "Unknown publisher" / SmartScreen blocked it — is this safe?**
Yes. FlightOps Hub isn't (yet) digitally code-signed, which is what triggers that warning — it's not a sign of anything malicious, just that Windows doesn't recognize the publisher. The project is open source (you can read every line on GitHub) and has applied for free code signing through the SignPath Foundation; the warning should go away once that's approved and wired into the build.

**FlightOps Hub can't find my Community folder.**
Use **Browse...** in Settings (or the first-run panel) to point it there directly — auto-detection only covers the standard Steam/Microsoft Store install paths, so a custom install location or a second drive won't be found automatically.

**An aircraft/scenery is enabled but I don't see it show as enabled — did the move fail?**
Checking a box only queues a change; click **Apply** to actually move anything. If Apply reports an error, check the console/log — a common cause is the app already being open elsewhere or a file being locked by another process.

**Does this modify my actual add-on files?**
No. Scenery/aircraft toggling only *moves the whole package folder* between Community and a disabled-holding folder — nothing inside a package is ever edited, and nothing is ever deleted. AutoStart toggling edits `exe.xml`'s own enable/disable flag (with an automatic backup before the very first edit) — again, no add-on files themselves are touched.

**Why is the Aircraft tab marked Beta?**
Unlike scenery packages (which reliably declare `content_type: SCENERY`), aircraft/livery packages in the wild are much less consistent about how they describe themselves — classification is heuristic and can be wrong, which is why manual overrides and the suggestion system exist. Scenery classification doesn't have this problem.

---

Found something this manual doesn't cover, or something that doesn't match what you're seeing? Open an issue on [GitHub](https://github.com/VacicakCZ/FlightOPS-Hub/issues).
