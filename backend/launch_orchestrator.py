"""Orchestrates one "Launch selected + MSFS" run: buckets selected apps into
immediate/delayed/smart-launch groups, starts MSFS itself if needed, hides
the window while background tasks run, and (depending on the post-launch
setting) either exits once everything is done or switches to a "stay and
watch the sim" loop that force-closes everything it started when MSFS quits.

One LaunchSession is created per launch; it owns all the state that used to
live as scattered self._* attributes on the old MSFSLauncher instance.
"""
import os
import threading
import time

from . import process_utils
from .tray import TrayIcon

SMART_LAUNCH_TIMEOUT_S = 20 * 60
SIM_WATCH_GRACE_PERIOD_S = 5 * 60
SIM_WATCH_POLL_INTERVAL_S = 15


class LaunchSession:
    def __init__(self, window, translate):
        self.window = window
        self.tr = translate
        self.tray = TrayIcon()
        self.launched_pids = []
        self.pending_tasks = 0
        self.post_launch_behavior = "exit"
        self._smart_cancel_event = None
        self._watch_stop_event = None
        self._sim_confirmed_running = False
        self._watch_confirm_deadline = 0

    def start(self, apps, selected_names, sim_version, sim_platform, post_launch_behavior):
        self.post_launch_behavior = post_launch_behavior

        immediate_apps, delayed_apps, smart_apps = [], [], []
        for name in selected_names:
            data = apps.get(name)
            if not data:
                continue
            mode_key = data.get("launch_mode", "timer" if data.get("delay", 0) > 0 else "immediate")
            if mode_key == "smart":
                smart_apps.append((data["path"], data.get("admin", False)))
            elif mode_key == "timer" and data.get("delay", 0) > 0:
                delayed_apps.append((data["delay"], data["path"], data.get("admin", False)))
            else:
                immediate_apps.append((data["path"], data.get("admin", False)))

        current_tasks = process_utils.tasklist_lower()

        for path, as_admin in immediate_apps:
            self._launch_if_not_running(path, as_admin, current_tasks)

        if not process_utils.is_msfs_running(current_tasks):
            process_utils.launch_msfs(sim_version, sim_platform)

        self.window.hide()

        self.pending_tasks = 0
        if delayed_apps:
            self.pending_tasks += 1
            delayed_apps.sort(key=lambda x: x[0])
            self._launch_delayed_apps(delayed_apps, 0, 0)

        if smart_apps:
            self.pending_tasks += 1
            self._start_smart_launch(smart_apps)

        if self.pending_tasks == 0:
            # Matches the old behavior exactly: an immediate-only launch always
            # exits right away, regardless of the stay-and-watch setting.
            self.window.destroy()

    def _launch_if_not_running(self, path, as_admin, tasks):
        if not os.path.exists(path):
            return
        if process_utils.is_exe_running(path, tasks):
            return
        pid = process_utils.run_app(path, as_admin)
        if pid:
            self.launched_pids.append(pid)

    # --- delayed (timer) launches ---
    def _launch_delayed_apps(self, delayed_apps, index, elapsed_time):
        if index >= len(delayed_apps):
            self._task_finished()
            return

        delay, path, as_admin = delayed_apps[index]
        wait_s = max(0, delay - elapsed_time)

        def do_launch():
            self._launch_if_not_running(path, as_admin, process_utils.tasklist_lower())
            self._launch_delayed_apps(delayed_apps, index + 1, max(elapsed_time, delay))

        threading.Timer(wait_s, do_launch).start()

    # --- smart launch (SimConnect) ---
    def _start_smart_launch(self, smart_apps):
        deadline = time.time() + SMART_LAUNCH_TIMEOUT_S
        self._smart_cancel_event = threading.Event()

        self.tray.ensure(self.tr("tray_tooltip"))
        self.tray.set_menu(self.tr("tray_status"), self.tr("tray_cancel"), self._smart_cancel_event.set)

        threading.Thread(
            target=self._smart_launch_worker,
            args=(smart_apps, deadline, self._smart_cancel_event),
            daemon=True,
        ).start()

    def _fire_smart_apps(self, smart_apps):
        tasks = process_utils.tasklist_lower()
        for path, as_admin in smart_apps:
            self._launch_if_not_running(path, as_admin, tasks)

    def _smart_launch_worker(self, smart_apps, deadline, cancel_event):
        sm = None
        try:
            try:
                from SimConnect import SimConnect, AircraftRequests
            except Exception:
                return

            while sm is None:
                if cancel_event.is_set() or time.time() >= deadline:
                    return
                try:
                    sm = SimConnect()
                except Exception:
                    time.sleep(2)

            aq = AircraftRequests(sm, _time=2000)

            while True:
                if cancel_event.is_set() or time.time() >= deadline:
                    return

                title = aq.get("TITLE")
                if title is not None and title != b"":
                    if isinstance(title, bytes):
                        try:
                            title = title.decode("utf-8", errors="ignore")
                        except Exception:
                            pass
                    if isinstance(title, str) and title.strip():
                        self._fire_smart_apps(smart_apps)
                        return

                time.sleep(2)
        finally:
            if sm is not None:
                try:
                    sm.exit()
                except Exception:
                    pass
            self._task_finished()

    # --- completion bookkeeping ---
    def _task_finished(self):
        self.pending_tasks -= 1
        if self.pending_tasks <= 0:
            self._all_launches_done()

    def _all_launches_done(self):
        if self.post_launch_behavior == "stay_and_watch" and self.launched_pids:
            self._start_sim_watch()
        else:
            self.tray.stop()
            self.window.destroy()

    # --- stay-and-watch (waits for MSFS to close, then cleans up) ---
    def _start_sim_watch(self):
        self._sim_confirmed_running = False
        self._watch_confirm_deadline = time.time() + SIM_WATCH_GRACE_PERIOD_S
        self._watch_stop_event = threading.Event()

        self.tray.ensure(self.tr("tray_tooltip"))
        self.tray.set_menu(self.tr("tray_watch_status"), self.tr("tray_force_stop"), self.force_stop_watch)

        threading.Thread(target=self._watch_loop, daemon=True).start()

    def _watch_loop(self):
        while True:
            if self._watch_stop_event.is_set():
                break

            tasks = process_utils.tasklist_lower()
            if process_utils.is_msfs_running(tasks):
                self._sim_confirmed_running = True
            elif self._sim_confirmed_running or time.time() >= self._watch_confirm_deadline:
                break
            # else: MSFS hasn't shown up in tasklist yet - give it more time.

            if self._watch_stop_event.wait(SIM_WATCH_POLL_INTERVAL_S):
                break

        self._terminate_launched_apps()
        self.tray.stop()
        self.window.destroy()

    def force_stop_watch(self):
        if self._watch_stop_event is not None:
            self._watch_stop_event.set()

    def _terminate_launched_apps(self):
        for pid in self.launched_pids:
            process_utils.terminate_pid(pid)
