"""System tray icon shown while waiting for Smart Launch / watching the sim.

Has zero relationship to the webview window (pywebview has no tray icon
support) - runs as an independent pystray icon in its own daemon thread,
exactly as it did next to the old Tk window. Optional: if pystray/PIL are
missing, the app just runs without a tray icon.
"""
import threading

from . import paths


class TrayIcon:
    def __init__(self):
        self._icon = None

    def ensure(self, tooltip):
        if self._icon is not None:
            return self._icon
        try:
            import pystray
            from PIL import Image

            image = Image.open(paths.resource_path("OIG3.ico"))
            icon = pystray.Icon("FlightOpsHub", image, tooltip)
            icon.menu = pystray.Menu(pystray.MenuItem(tooltip, None, enabled=False))
            threading.Thread(target=icon.run, daemon=True).start()
            self._icon = icon
        except Exception:
            self._icon = None
        return self._icon

    def set_menu(self, status_text, action_text, action_callback):
        if self._icon is None:
            return
        try:
            import pystray

            self._icon.title = status_text
            self._icon.menu = pystray.Menu(
                pystray.MenuItem(status_text, None, enabled=False),
                pystray.MenuItem(action_text, lambda icon, item: action_callback()),
            )
        except Exception:
            pass

    def stop(self):
        if self._icon is not None:
            try:
                self._icon.stop()
            except Exception:
                pass
            self._icon = None
