"""Pushes events from any thread into the frontend, replacing the old
`self.after(0, lambda: ...)` idiom used everywhere to marshal background-thread
results onto the Tk UI thread.

pywebview's `window.evaluate_js` is safe to call from any thread, so no
marshaling/queueing is needed here - just JSON-encode the payload and dispatch
it to a JS-side pub/sub object (frontend/js/events.js).
"""
import json


class EventBus:
    def __init__(self):
        self._window = None

    def bind(self, window):
        self._window = window

    def emit(self, event_type, payload=None):
        if self._window is None:
            return
        message = json.dumps({"type": event_type, "payload": payload})
        script = f"window.FlightOpsEvents && window.FlightOpsEvents.dispatch({message})"
        try:
            self._window.evaluate_js(script)
        except Exception:
            # Window may be hidden/closing/not yet loaded - dropping the event
            # is fine, these are UI progress pushes, not the source of truth.
            pass


bus = EventBus()
