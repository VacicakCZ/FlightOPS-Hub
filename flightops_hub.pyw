import json
import traceback

import webview

from backend import app_logging, config_manager, paths, win_native
from backend.api import Api
from backend.events import bus
from backend.version import APP_VERSION


def _get_saved_language():
    # Read directly, without going through config_manager.load_config()'s
    # migration/save path - a second instance shouldn't write to the config
    # file the running instance might be using at the same time.
    try:
        with open(config_manager.CONFIG_FILE, "r", encoding="utf-8") as f:
            return json.load(f).get("_language", "EN")
    except Exception:
        return "EN"


def _show_already_running_message():
    with open(paths.resource_path("frontend", "locales", "already_running.json"), "r", encoding="utf-8") as f:
        messages = json.load(f)
    lang = _get_saved_language()
    text = messages.get(lang, messages.get("EN"))
    win_native.message_box("FlightOps Hub", text)


def _on_closing(window, api):
    api.save_window_geometry(window.width, window.height, window.x, window.y)


_STARTUP_FAILURE_MESSAGES = {
    "CZ": (
        "Aplikaci se nepodařilo spustit.\n\n"
        "Nejčastější příčina: chybí Microsoft Edge WebView2 Runtime "
        "(na Windows 11 bývá součástí systému, na Windows 10 ho může "
        "být potřeba doinstalovat - stačí mít nainstalovaný/aktuální "
        "prohlížeč Edge).\n\n"
        "Podrobnosti byly uloženy do souboru crash_log.txt vedle aplikace."
    ),
    "EN": (
        "FlightOps Hub failed to start.\n\n"
        "Most common cause: the Microsoft Edge WebView2 Runtime is "
        "missing (included by default on Windows 11; on Windows 10, "
        "having Edge installed/up to date is usually enough).\n\n"
        "Details were saved to crash_log.txt next to the application."
    ),
}


def _show_startup_failure_message():
    lang = _get_saved_language()
    text = _STARTUP_FAILURE_MESSAGES.get(lang, _STARTUP_FAILURE_MESSAGES["EN"])
    win_native.message_box("FlightOps Hub", text, icon=win_native.MB_ICONERROR)


def main():
    # The mutex handle must stay alive for the app's lifetime - if it were
    # garbage collected the mutex would be released and a second instance
    # could start despite this one still running.
    mutex_handle, already_running = win_native.acquire_single_instance_lock()
    if already_running:
        _show_already_running_message()
        return

    app_logging.setup()

    try:
        api = Api()
        config = api.config_get()

        window = webview.create_window(
            f"FlightOps Hub {APP_VERSION}",
            paths.resource_path("frontend", "index.html"),
            js_api=api,
            width=config.get("_window_width", 500),
            height=config.get("_window_height", 860),
            x=config.get("_window_x"),
            y=config.get("_window_y"),
            resizable=True,
            min_size=(420, 600),
        )
        window.events.closing += lambda: _on_closing(window, api)
        bus.bind(window)
        api.start_gsx_watcher()

        webview.start(icon=paths.resource_path("OIG3.ico"))
        app_logging.get_logger().info("FlightOps Hub exiting normally")
    except Exception:
        app_logging.get_logger().exception("Startup failed")
        try:
            with open("crash_log.txt", "w", encoding="utf-8") as f:
                f.write("CRASH LOG:\n")
                f.write(traceback.format_exc())
        except Exception:
            pass
        _show_startup_failure_message()


if __name__ == "__main__":
    main()
