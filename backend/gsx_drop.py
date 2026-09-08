"""Wires up native OS drag-and-drop of GSX profile .zip files onto the
Scenery tab.

This has to go through pywebview's Python-side DOM event API
(window.dom.get_element(...).on("drop", ...)) rather than a plain JS
`drop` event listener - in this pywebview/WebView2 combination, a dropped
File object's real filesystem path (`pywebviewFullPath`) is only resolved
when at least one DOM listener has been registered this way; a vanilla JS
listener only ever sees the file's name, not where it lives on disk,
which is useless for extracting a zip. Binding to #scenery-drop-zone
(the Scenery tab's own <section>, always present in the DOM but only
part of the layout - and therefore only drop-hit-testable - while that
tab is actually visible) means drops elsewhere in the app are ignored for
free, with no need to track the active tab separately.
"""
from webview.dom import DOMEventHandler


def setup(window, api):
    def on_drop(event):
        files = (event.get("dataTransfer") or {}).get("files", [])
        paths = [f["pywebviewFullPath"] for f in files if f.get("pywebviewFullPath")]
        if paths:
            api.gsx_install_dropped_zips(paths)

    def bind():
        zone = window.dom.get_element("#scenery-drop-zone")
        if zone is None:
            return
        # A drop target must cancel dragover's default action, or the
        # browser refuses to fire drop at all.
        zone.on("dragover", lambda event: None, DOMEventHandler(prevent_default=True))
        zone.on("drop", on_drop, DOMEventHandler(prevent_default=True, stop_propagation=True))

    window.events.loaded += bind
