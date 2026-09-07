// Receives pushes from backend/events.py's EventBus (via evaluate_js) and
// fans them out to whichever tab subscribed - replaces the old Tk
// `self.after(0, ...)` marshaling idiom.
window.FlightOpsEvents = (() => {
  const listeners = {};

  function on(type, fn) {
    (listeners[type] = listeners[type] || []).push(fn);
    return () => {
      listeners[type] = listeners[type].filter((f) => f !== fn);
    };
  }

  function dispatch({ type, payload }) {
    (listeners[type] || []).forEach((fn) => fn(payload));
  }

  return { on, dispatch };
})();
