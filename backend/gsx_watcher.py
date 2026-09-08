"""Polls the GSX profiles folder for changes on a background thread and
triggers a re-check when something changes - simpler and dependency-free
compared to native filesystem change notifications, and cheap enough (a
plain directory listing) to poll every few seconds without being felt.
"""
import os
import threading


class GsxFolderWatcher:
    def __init__(self, get_path, on_change, interval=3.0):
        self._get_path = get_path
        self._on_change = on_change
        self._interval = interval
        self._last_signature = None
        self._stop = threading.Event()

    def _signature(self, path):
        try:
            return frozenset((entry.name, entry.stat().st_mtime_ns) for entry in os.scandir(path) if entry.is_file())
        except OSError:
            return None

    def _loop(self):
        while not self._stop.is_set():
            signature = self._signature(self._get_path())
            if signature is not None and signature != self._last_signature:
                # Skip the very first read (startup baseline) - only
                # changes seen after that should trigger a re-check.
                if self._last_signature is not None:
                    self._on_change()
                self._last_signature = signature
            self._stop.wait(self._interval)

    def start(self):
        threading.Thread(target=self._loop, daemon=True).start()

    def stop(self):
        self._stop.set()
