using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Routes {id, method, args} JSON envelopes (see frontend/js/api.js's
/// WebView2 path) to registered handlers and returns a {id, result|error}
/// envelope. Pure logic, no WebView2 dependency - the App project wires its
/// raw string in/out to CoreWebView2.WebMessageReceived/PostWebMessageAsJson.
///
/// A handler must return a node with no existing parent (a fresh value, or
/// one obtained via JsonNode.DeepClone()) - JsonNode only ever belongs to
/// one parent at a time.
/// </summary>
public class ApiDispatcher
{
    private readonly Dictionary<string, Func<JsonArray, Task<JsonNode?>>> _handlers = new();

    public void Register(string method, Func<JsonArray, JsonNode?> handler)
    {
        _handlers[method] = args => Task.FromResult(handler(args));
    }

    /// <summary>For handlers that need to await something (e.g. an HTTP call) - see SceneryService.SimbriefCheck.</summary>
    public void RegisterAsync(string method, Func<JsonArray, Task<JsonNode?>> handler)
    {
        _handlers[method] = handler;
    }

    public async Task<string> DispatchAsync(string requestJson)
    {
        JsonObject? request;
        try
        {
            request = JsonNode.Parse(requestJson) as JsonObject;
        }
        catch (Exception ex)
        {
            return BuildError(null, $"Malformed request: {ex.Message}");
        }

        var id = request?["id"]?.DeepClone();
        var method = request?["method"]?.GetValue<string>();
        var args = request?["args"] as JsonArray ?? new JsonArray();

        if (method is null || !_handlers.TryGetValue(method, out var handler))
        {
            return BuildError(id, $"Unknown method: {method ?? "(none)"}");
        }

        try
        {
            var result = await handler(args);
            return BuildResult(id, result);
        }
        catch (Exception ex)
        {
            return BuildError(id, ex.Message);
        }
    }

    private static string BuildResult(JsonNode? id, JsonNode? result)
    {
        var envelope = new JsonObject { ["id"] = id, ["result"] = result };
        return envelope.ToJsonString();
    }

    private static string BuildError(JsonNode? id, string message)
    {
        var envelope = new JsonObject { ["id"] = id, ["error"] = message };
        return envelope.ToJsonString();
    }
}
