using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/update_check.py - checks GitHub Releases for a version
/// newer than this build. Unauthenticated, best-effort only: any failure
/// is swallowed and treated as "no update info available".
/// </summary>
public static class UpdateCheck
{
    public const string RepoReleasesUrl = "https://github.com/VacicakCZ/FlightOPS-Hub/releases";
    public const string ReleasesLatestUrl = "https://api.github.com/repos/VacicakCZ/FlightOPS-Hub/releases/latest";
    public static string UpdatePageUrl => $"{RepoReleasesUrl}/latest";

    // The fixed local filename the downloaded installer is saved as in
    // Downloads - unlike the GitHub asset itself (matched by pattern
    // below, since release-dotnet.yml's OutputBaseFilename bakes the
    // version into the actual uploaded filename), this one deliberately
    // stays the same across versions.
    public const string ReleaseAssetName = "FlightOpsHub-Setup.exe";

    private const string ReleaseAssetPrefix = "FlightOpsHub-Setup-";
    private const string ReleaseAssetSuffix = ".exe";

    private static readonly HttpClient Http = CreateHttpClient(TimeSpan.FromSeconds(6));
    private static readonly HttpClient DownloadHttp = CreateHttpClient(TimeSpan.FromSeconds(30));

    // GitHub's API rejects any request with no User-Agent with a 403 - unlike
    // Python's requests library, HttpClient sends none by default. Since
    // CheckForUpdate()'s catch-all treats every failure as "no update
    // available", a missing header here made update-checking silently never
    // work at all rather than erroring visibly.
    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"FlightOpsHub/{AppVersion.Current}");
        return client;
    }

    private static readonly Regex VersionRegex = new(@"^v?(\d+(?:\.\d+)*)");

    /// <summary>"v2.1" / "2.1.3" / "2.1-beta" -> [2,1,3]. Anything that doesn't start with a number becomes [0].</summary>
    public static int[] ParseVersion(string? text)
    {
        var match = VersionRegex.Match((text ?? "").Trim());
        if (!match.Success) return new[] { 0 };
        return match.Groups[1].Value.Split('.').Select(int.Parse).ToArray();
    }

    /// <summary>Mirrors Python's tuple comparison exactly: element-wise, and if one is a prefix of the other, the shorter one is "less".</summary>
    public static bool IsNewer(string? candidateText, string? currentText)
    {
        var a = ParseVersion(candidateText);
        var b = ParseVersion(currentText);
        var minLen = Math.Min(a.Length, b.Length);
        for (var i = 0; i < minLen; i++)
        {
            if (a[i] != b[i]) return a[i] > b[i];
        }
        return a.Length > b.Length;
    }

    /// <summary>
    /// Pure lookup, split out for testing without a network call. Matches
    /// by prefix/suffix (release-dotnet.yml's OutputBaseFilename is
    /// "FlightOpsHub-Setup-{version}") rather than one fixed name - unlike
    /// the old portable-exe release, the actual uploaded asset name
    /// changes every version.
    /// </summary>
    public static string? FindAssetDownloadUrl(JsonObject data)
    {
        if (data["assets"] is not JsonArray assets) return null;
        foreach (var asset in assets)
        {
            if (asset is JsonObject a && a["name"] is JsonValue nv && nv.TryGetValue<string>(out var name)
                && name.StartsWith(ReleaseAssetPrefix, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(ReleaseAssetSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return a["browser_download_url"] is JsonValue uv && uv.TryGetValue<string>(out var url) ? url : null;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns {available:false} (no update, or the check failed/timed out/
    /// offline - all treated the same, silently) or {available:true,
    /// version, notes, download_url}. download_url is null if the release
    /// has no flightops_hub.exe asset attached yet.
    /// </summary>
    public static async Task<JsonObject> CheckForUpdate()
    {
        JsonObject data;
        try
        {
            var response = await Http.GetAsync(ReleasesLatestUrl);
            if (!response.IsSuccessStatusCode) return new JsonObject { ["available"] = false };
            data = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject { ["available"] = false };
        }

        var latestVersion = data["tag_name"] is JsonValue tv && tv.TryGetValue<string>(out var tag) ? tag : "";
        if (!IsNewer(latestVersion, AppVersion.Current))
        {
            return new JsonObject { ["available"] = false };
        }

        return new JsonObject
        {
            ["available"] = true,
            ["version"] = latestVersion,
            ["notes"] = data["body"] is JsonValue bv && bv.TryGetValue<string>(out var body) ? body : "",
            ["download_url"] = FindAssetDownloadUrl(data),
        };
    }

    /// <summary>
    /// Streams the release exe to destPath (a temp ".part" file first, then
    /// an atomic move into place). onProgress(downloaded, total), if
    /// given, is called after each chunk - total is null if the server
    /// didn't send a Content-Length.
    /// </summary>
    public static async Task<JsonObject> DownloadUpdate(string downloadUrl, string destPath, Action<long, long?>? onProgress = null)
    {
        var tmpPath = destPath + ".part";
        try
        {
            using var response = await DownloadHttp.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return new JsonObject { ["ok"] = false, ["error"] = $"HTTP {(int)response.StatusCode}" };
            }

            long? total = response.Content.Headers.ContentLength;
            long downloaded = 0;

            await using (var fileStream = File.Create(tmpPath))
            await using (var httpStream = await response.Content.ReadAsStreamAsync())
            {
                var buffer = new byte[256 * 1024];
                int read;
                while ((read = await httpStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;
                    onProgress?.Invoke(downloaded, total);
                }
            }

            File.Move(tmpPath, destPath, overwrite: true);
            return new JsonObject { ["ok"] = true, ["path"] = destPath };
        }
        catch (Exception ex)
        {
            try { File.Delete(tmpPath); } catch { /* nothing to clean up */ }
            return new JsonObject { ["ok"] = false, ["error"] = ex.Message };
        }
    }
}
