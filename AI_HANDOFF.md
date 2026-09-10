# Context for a new Claude Code session (read this first)

Written 2026-09-10 so a copy of this folder, loaded into a fresh Claude Code session, has continuity. Claude's own per-project memory is keyed to the folder's exact path (`C:\Users\Vacicak\.claude\projects\...\memory\`), so a copy to a new location starts with **empty memory** even though the code/git history carries over - this file exists to bridge that gap. Not pushed anywhere; local only.

## What FlightOps Hub is

A portable Windows companion app for MSFS 2020/2024: pywebview (Python backend + HTML/Alpine.js frontend) + a vanilla-JS frontend, no build step. Solves three things: launching a checklist of add-ons together, managing an overgrown Community folder (enable/disable scenery & aircraft without manual file moves), and pre-flight sanity checks (SimBrief plan validation, AIRAC currency, disk space).

## Current state

- **Public repo**: `git@github.com:VacicakCZ/FlightOPS-Hub.git`. Also listed on flightsim.to (addon #114794).
- **Latest published release: v2.2.0** (GitHub Release + attached `flightops_hub.exe`, built via `.github/workflows/release-build.yml` which fires on `release: published`).
- User manual at `docs/MANUAL.md`, feature list in `README.md` - both were brought up to date as of v2.2.0.
- Code signing: applied for the free SignPath Foundation open-source program (qualifies via the GPL-3.0 license); application submitted, not yet approved/wired in as of this writing. README already carries a short note about this per SignPath's own requirement.
- Tests: `pytest` (156 tests as of v2.2.0) + `python scripts/check_locales.py` (5 locale files: CZ, EN hand-written; DE, ES, CN AI-translated and flagged as such in-app). Both run in CI on every push/PR via `.github/workflows/tests.yml`.

## A feature was attempted and then fully reverted - know this before retrying it

After v2.2.0 shipped, "Launch at Windows startup" (a Settings checkbox writing a `HKCU\...\Run` registry entry with a `--minimized` flag) + "minimize to tray" (clicking the native minimize button hides the window and shows a pystray icon with Show/Exit instead of leaving it in the taskbar) was built, tested against a real PyInstaller build, and **hit a reproducible crash**: the *first* minimize→restore cycle always worked correctly, but the *second* one made the whole process disappear with **zero exception logged anywhere** - not in the app's own log, not even after wiring pystray's own internal logger (`logging.getLogger("pystray")`, which by default has no handler and silently swallows anything it catches, since this is a windowless `.pyw` app with no console) into the same log file.

Things that were tried and fixed along the way, in order, none of which resolved the actual crash:
1. `window.show()` doesn't reliably beat Windows' foreground-lock protection when restore is triggered from a tray click - fixed by toggling `window.on_top = True` then `False` right after showing.
2. Windows already flips `WindowState` to `Minimized` before the app's minimize handler even runs; `show()`/`hide()` only control visibility, not `WindowState` - fixed by also calling `window.restore()`.
3. Suspected the crash was `pystray.Icon` being fully destroyed (`.stop()`) and immediately recreated (`.ensure()`) on every single minimize/restore cycle - a native-level race where the second icon's setup might collide with the first one's not-yet-finished teardown (`stop()` doesn't block until its thread actually exits). Restructured to create one persistent icon for the app's whole lifetime and just toggle its `.visible` property instead. **This did not fix the crash either** - the exact same symptom (silent death on the second cycle) still happened afterward.
4. Wired pystray's own logger into the app's log file (see above) as the next diagnostic step, to see whatever pystray itself might be catching and silently discarding - **never got to see the result**, since the user asked to fully revert before reproducing again with this in place.

The whole feature (6 commits) was reverted via `git reset --hard` back to `ca6a2c6` ("Document ATC integration, AIRAC mismatch check, disk space protection, and update-download" - the last commit of the v2.2.0 doc pass). **None of this was ever pushed** - it only ever existed as local commits, so the revert is clean and the public repo/README/MANUAL never mentioned it.

**If this feature comes up again**: the pystray-logger wiring (item 4) is the natural next diagnostic step - reproduce the crash with that in place and actually read what pystray logged, before trying anything else. Also worth considering as an alternative approach entirely: skip pystray for the tray icon (it's a Linux/Mac/Windows abstraction library with its own win32 message-loop implementation) and use a direct, minimal win32 `Shell_NotifyIcon` implementation instead (matching `win_native.py`'s existing ctypes-only style, no extra dependency) - narrower surface area to debug than a third-party library's own internal event loop.

## Working conventions established this whole project

- **Testing/verification**: prefer live DOM-injection verification (a throwaway pywebview window driving the real Alpine components via `evaluate_js`) over synthetic clicks or trusting code-review alone. Always call `bus.bind(window)` in any standalone test/verification script or backend events silently drop. Never write-test against the real `msfs_launcher_config.json`/`msfs_apps.json` - back them up first if a script might touch them, or point the script at a synthetic sandbox directory instead (established pattern throughout this project).
- **Git commit messages must not contain embedded double-quote characters** - breaks the `git commit -m @'...'@` PowerShell here-string pattern used in this environment. Rewrite around it rather than fighting it.
- **Don't build the `.exe` automatically** - only run PyInstaller when the user explicitly asks for it, not reflexively after every fix.
- **Propose before applying** for any broad "clean this up" / audit-style request - list concrete findings first, let the user pick which to act on, rather than fixing everything unprompted.
- **Privacy discipline**: anything meant to be shared publicly (diagnostics export, a GitHub issue attachment, etc.) needs identity-linked fields redacted (SimBrief username, Windows account name in file paths) - a "for my own restore" artifact (Backup & Restore export) keeps everything; a "meant to be posted publicly" artifact does not. This was learned the hard way from two real near-miss privacy leaks caught by the user pasting real exported files.
- **Versioning**: patch bump for fixes/polish only; minor (or more) bump when a batch adds new visible functionality. `backend/version.py` is the single source of truth (`APP_VERSION`).
- **Release flow**: `gh release create vX.Y.Z --draft --notes-file ... --target master`, user reviews and clicks Publish themselves - Claude never has publish authority, only enough token access (pasted fresh into the conversation each time, session-only by the user's own choice) to prepare drafts and rerun a failed build workflow.
