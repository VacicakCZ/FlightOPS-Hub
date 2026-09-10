namespace FlightOpsHub.Core;

/// <summary>
/// Single source of truth for the app version, mirroring backend/version.py
/// on the Python side during the parallel-development period.
/// </summary>
public static class AppVersion
{
    public const string Current = "v3.0.0";
}
