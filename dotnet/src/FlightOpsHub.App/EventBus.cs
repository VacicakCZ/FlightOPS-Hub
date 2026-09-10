using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Wpf;

namespace FlightOpsHub.App;

/// <summary>
/// Port of backend/events.py - pushes events from any thread into the
/// frontend's existing frontend/js/events.js pub/sub object, unmodified:
/// Python's window.evaluate_js(script) and WebView2's
/// CoreWebView2.ExecuteScriptAsync(script) both just run a JS statement,
/// so the exact same "window.FlightOpsEvents.dispatch(...)" call works
/// for both builds without touching that file at all.
///
/// Unlike pywebview's evaluate_js (safe from any thread per that file's
/// own docstring), WebView2's ExecuteScriptAsync must run on the UI
/// thread that owns the control - Emit() marshals via Dispatcher.
/// </summary>
public class EventBus
{
    private WebView2? _browser;

    public void Bind(WebView2 browser) => _browser = browser;

    public void Emit(string eventType, JsonNode? payload = null)
    {
        var browser = _browser;
        if (browser is null) return;

        var message = new JsonObject { ["type"] = eventType, ["payload"] = payload }.ToJsonString();
        var script = $"window.FlightOpsEvents && window.FlightOpsEvents.dispatch({message})";

        browser.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                if (browser.CoreWebView2 != null)
                {
                    await browser.CoreWebView2.ExecuteScriptAsync(script);
                }
            }
            catch
            {
                // Window may be hidden/closing/not yet loaded - dropping
                // the event is fine, these are UI progress pushes, not
                // the source of truth.
            }
        });
    }
}
