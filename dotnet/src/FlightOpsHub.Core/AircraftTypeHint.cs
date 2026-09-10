using System.Text.RegularExpressions;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/aircraft_type_hint.py - best-effort aircraft model tag
/// (A320, 737-800, CRJ900, ...) extracted from a display name via regex. A
/// pure display hint, never used for grouping/classification. Not
/// exhaustive; returns null rather than guessing when nothing matches.
/// </summary>
public static class AircraftTypeHint
{
    private static readonly Regex[] Patterns =
    {
        new(@"\bA3(\d{2})(?:neo|ceo)?\b", RegexOptions.IgnoreCase),
        new(@"\bA220[-\s]?(?:100|300)?\b", RegexOptions.IgnoreCase),
        new(@"\b7[0-8]7(?:[-\s]?\d{1,3}[A-Z]{0,3})?\b"),
        new(@"\bCRJ[-\s]?\d{3,4}\b", RegexOptions.IgnoreCase),
        new(@"\bERJ[-\s]?\d{3}\b", RegexOptions.IgnoreCase),
        new(@"\bE[-\s]?(?:170|175|190|195)\b"),
        new(@"\bATR[-\s]?(?:42|72)\b", RegexOptions.IgnoreCase),
        new(@"\bCessna\s?(?:150|152|162|172|182|206|208|210|340|414|421)\b", RegexOptions.IgnoreCase),
        new(@"\bC[-\s]?(?:150|152|162|172|182|206|208|210|340|414|421)\b"),
        new(@"\bTBM[-\s]?(?:700|850|900|930|940)\b", RegexOptions.IgnoreCase),
        new(@"\bPC[-\s]?(?:6|12|24)\b", RegexOptions.IgnoreCase),
        new(@"\bKing\s?Air(?:\s?\d{3})?\b", RegexOptions.IgnoreCase),
        new(@"\bMD[-\s]?(?:11|8[0-8])\b", RegexOptions.IgnoreCase),
        new(@"\bDHC[-\s]?[268]\b", RegexOptions.IgnoreCase),
        new(@"\bDash\s?8\b", RegexOptions.IgnoreCase),
        new(@"\bDC[-\s]?(?:3|6|9|10)\b", RegexOptions.IgnoreCase),
        new(@"\bLearjet\s?\d{2}\b", RegexOptions.IgnoreCase),
        new(@"\bCitation\s?\w*\b", RegexOptions.IgnoreCase),
        new(@"\bConcorde\b", RegexOptions.IgnoreCase),
        new(@"\bSpitfire\b", RegexOptions.IgnoreCase),
    };

    public static string? ExtractTypeHint(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return null;
        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(displayName);
            if (match.Success) return match.Value.Trim();
        }
        return null;
    }
}
