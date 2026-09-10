using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FlightOpsHub.Core;

/// <summary>Port of backend/airac.py - Navigraph AIRAC cycle check, pure logic.</summary>
public static class AiracManager
{
    public static string GetCurrentAirac()
    {
        try
        {
            var baseDate = new DateTime(2024, 1, 25, 0, 0, 0, DateTimeKind.Utc);
            var now = DateTime.UtcNow;
            var daysDiff = (now - baseDate).Days;
            var cyclesDiff = daysDiff / 28;
            var currDate = baseDate.AddDays(cyclesDiff * 28);

            var year = currDate.Year;
            var tempDate = currDate;
            var cycleNum = 1;
            while (true)
            {
                tempDate = tempDate.AddDays(-28);
                if (tempDate.Year < year) break;
                cycleNum++;
            }
            return $"{year.ToString()[^2..]}{cycleNum:D2}";
        }
        catch
        {
            return "N/A";
        }
    }

    public static string GetInstalledAirac(string? communityPath)
    {
        if (string.IsNullOrEmpty(communityPath) || !Directory.Exists(communityPath))
        {
            return "";
        }

        try
        {
            foreach (var item in Directory.GetDirectories(communityPath))
            {
                var name = Path.GetFileName(item);
                if (name.IndexOf("navigraph", StringComparison.OrdinalIgnoreCase) < 0) continue;

                var manifestPath = Path.Combine(item, "manifest.json");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var json = JsonNode.Parse(File.ReadAllText(manifestPath));
                    var title = json?["title"]?.GetValue<string>() ?? "";
                    if (title.Contains("AIRAC") || title.Contains("Navdata"))
                    {
                        var match = Regex.Match(title, @"\d{4}");
                        if (match.Success) return match.Value;
                    }
                }
                catch
                {
                    // Malformed manifest - skip it, matches Python's bare except.
                }
            }
        }
        catch
        {
            // Unreadable Community folder - matches Python's outer bare except.
        }
        return "";
    }
}
