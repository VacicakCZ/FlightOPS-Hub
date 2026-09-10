using System.Text.Json.Nodes;
using FlightOpsHub.Core;

namespace FlightOpsHub.App;

/// <summary>
/// Backs the tray icon's right-click quick-launch: pick a flight profile
/// and/or an exe.xml profile from the tray menu, then hit "Launch" to
/// apply both and start MSFS + the selected companion apps - without ever
/// opening the main window. Selections are remembered via the same
/// _last_profile/_last_exe_profile config fields the main window's own
/// Flight/AutoStart tabs already use (ProfilesService/ExeXmlService.
/// ApplyProfile), so picking one from the tray and picking one in the app
/// itself stay in sync rather than tracking two separate "last used"
/// states.
///
/// Picking a profile from the tray only remembers the choice - nothing
/// is applied (no exe.xml write, no app-selection change) until Launch is
/// clicked, so browsing the submenu can never have a side effect on its
/// own.
/// </summary>
public class TrayProfileLauncher
{
    private readonly ConfigService _config;
    private readonly AppsService _apps;
    private readonly ProfilesService _profiles;
    private readonly ExeXmlService _exeXml;
    private readonly LaunchService _launch;

    public TrayProfileLauncher(ConfigService config, AppsService apps, ProfilesService profiles, ExeXmlService exeXml, LaunchService launch)
    {
        _config = config;
        _apps = apps;
        _profiles = profiles;
        _exeXml = exeXml;
        _launch = launch;
    }

    public IReadOnlyList<string> FlightProfileNames() => ProfileNames("flight");

    public IReadOnlyList<string> ExeProfileNames() => ProfileNames("exe");

    private List<string> ProfileNames(string kind) =>
        _profiles.List(kind).Select(kv => kv.Key).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

    public string? SelectedFlightProfile() => GetConfigString("_last_profile");

    public string? SelectedExeProfile() => GetConfigString("_last_exe_profile");

    public void SelectFlightProfile(string name) => _config.MutateAndSave(c => c["_last_profile"] = name);

    public void SelectExeProfile(string name) => _config.MutateAndSave(c => c["_last_exe_profile"] = name);

    /// <summary>
    /// Applies the selected exe profile (if any) directly to exe.xml,
    /// builds app-selection state from the selected flight profile's
    /// members (if any), then runs the same LaunchSession the main
    /// window's own Launch button starts. No flight/exe profile selected
    /// simply means "launch with whatever on/off state is already
    /// persisted" - the same thing a normal Launch click with nothing
    /// touched would do.
    /// </summary>
    public void Launch()
    {
        var exeProfile = SelectedExeProfile();
        if (!string.IsNullOrEmpty(exeProfile) && _profiles.List("exe").ContainsKey(exeProfile))
        {
            _exeXml.ApplyProfile(exeProfile);
        }

        var flightProfile = SelectedFlightProfile();
        var flightProfiles = _profiles.List("flight");
        var appStates = new JsonObject();

        var members = !string.IsNullOrEmpty(flightProfile) && flightProfiles[flightProfile] is JsonArray membersArray
            ? new HashSet<string>(membersArray.Select(m => m?.GetValue<string>()).Where(s => s is not null)!)
            : null;

        foreach (var (name, _) in _apps.List())
        {
            appStates[name] = members is not null ? members.Contains(name) : GetConfigString(name) == "on";
        }

        _launch.LaunchAll(appStates, flightProfile ?? "");
    }

    private string? GetConfigString(string key) =>
        _config.Get(key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
