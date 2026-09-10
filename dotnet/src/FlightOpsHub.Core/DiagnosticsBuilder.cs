using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/diagnostics.py - builds the plain-text bundle for the
/// Settings tab's "Export diagnostics" button. Meant to be attached
/// directly to a *public* GitHub issue (unlike BackupManager's export,
/// which keeps everything since that one is for the user's own restore) -
/// see [[flightops-privacy-discipline]] memory: this is exactly the kind
/// of artifact that needs identity-linked fields redacted before sharing.
/// </summary>
public static class DiagnosticsBuilder
{
    private static readonly string[] RedactedKeys = { "_simbrief_username" };

    // Windows profile paths (C:\Users\<name>\...) show up all over the
    // place here - Community/disabled/GSX/exe.xml paths in the config, and
    // any log line mentioning a file path. Rather than track down every
    // individual field/log line that could contain one, this blanket-
    // redacts the account name segment wherever it appears in the final
    // report text, keeping the rest of the path intact since that part is
    // actually useful for diagnosis.
    private static readonly Regex WindowsUserPathRegex = new(@"([A-Za-z]:\\+Users\\+)([^\\""\r\n]+)");

    private static string RedactWindowsUsername(string text) =>
        WindowsUserPathRegex.Replace(text, m => m.Groups[1].Value + "<user>");

    private static JsonObject RedactConfig(JsonObject config)
    {
        var redacted = (JsonObject)config.DeepClone();
        foreach (var key in RedactedKeys)
        {
            if (redacted[key] is JsonValue value && !string.IsNullOrEmpty(value.GetValue<string>()))
            {
                redacted[key] = "<redacted>";
            }
        }
        return redacted;
    }

    // .NET's default JSON encoder escapes '<', '>', '&', etc. to \uXXXX for
    // web-embedding safety - Python's json.dumps never does that. This is a
    // plain text file a human reads (or a JSON data file another instance
    // of this app reads back), never HTML/JS, so match Python's plain
    // output instead of quietly mangling paths and config values.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string BuildReport(JsonObject config, string osInfo, string logTail)
    {
        var configJson = RedactConfig(config).ToJsonString(JsonOptions);

        var lines = new[]
        {
            $"FlightOps Hub diagnostics - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"App version: {AppVersion.Current}",
            $"OS: {osInfo}",
            "",
            "--- Config ---",
            configJson,
            "",
            "--- Recent log ---",
            string.IsNullOrEmpty(logTail) ? "(no log file yet)" : logTail,
        };

        return RedactWindowsUsername(string.Join("\n", lines));
    }

    public static string DefaultFilename() => $"flightops_hub_diagnostics_{DateTime.Now:yyyy-MM-dd_HHmmss}.txt";
}
