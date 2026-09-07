import json
import traceback

import webview

from backend import config_manager, paths, win_native
from backend.api import Api
from backend.events import bus
from backend.version import APP_VERSION


def _get_saved_language_for_already_running_message():
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
    lang = _get_saved_language_for_already_running_message()
    text = messages.get(lang, messages.get("EN"))
    win_native.message_box("FlightOps Hub", text)


def _on_closing(window, config):
    config.update({
        "_window_width": window.width,
        "_window_height": window.height,
        "_window_x": window.x,
        "_window_y": window.y,
    })
    config_manager.save_config(config)


def main():
    # The mutex handle must stay alive for the app's lifetime - if it were
    # garbage collected the mutex would be released and a second instance
    # could start despite this one still running.
    mutex_handle, already_running = win_native.acquire_single_instance_lock()
    if already_running:
        _show_already_running_message()
        return

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
    window.events.closing += lambda: _on_closing(window, config)
    bus.bind(window)

    try:
        webview.start(icon=paths.resource_path("OIG3.ico"))
    except Exception:
        with open("crash_log.txt", "w", encoding="utf-8") as f:
            f.write("CRASH LOG:\n")
            f.write(traceback.format_exc())


if __name__ == "__main__":
    main()
