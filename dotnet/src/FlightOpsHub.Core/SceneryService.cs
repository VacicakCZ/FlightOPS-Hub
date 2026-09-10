using System.Diagnostics;
using System.Text.Json.Nodes;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of backend/api.py's scenery_scan/scenery_is_busy/scenery_map_data/
/// simbrief_check/aircraft_scan/aircraft_is_busy/aircraft_set_type_override/
/// aircraft_set_parent_override/aircraft_dismiss_livery_suggestion - ties
/// SceneryScanner/AircraftScanner/AircraftOverrides/AircraftTypeHint/
/// GsxProfiles/SceneryMapBuilder/ScenerySimbriefMatch/SimbriefClient/
/// AtcNetwork together with the config.
///
/// Also ports scan_community_usage/scan_disabled_usage/scenery_apply/
/// aircraft_apply/_run_addon_apply - the async/event methods (plan phase
/// 5). emitEvent is a plain delegate rather than a concrete EventBus
/// reference so this class (Core, no WebView2 dependency) stays
/// UI-framework-agnostic; the App project wires the real
/// EventBus.Emit method in.
///
/// scenery_is_busy/aircraft_is_busy share one busy flag, same as the
/// Python original (both touch the same Community folder, so only one
/// apply can run at a time regardless of which tab started it).
/// </summary>
public class SceneryService
{
    private readonly ConfigService _config;
    private readonly IReadOnlyDictionary<string, string> _knownDevelopers;
    private readonly AirportsData _airports;
    private readonly SceneryMapBuilder _map;
    private readonly Action<string, JsonObject?> _emitEvent;

    private volatile bool _addonApplyActive;

    public SceneryService(ConfigService config, string aircraftDevelopersJsonPath, string airportsJsonPath, Action<string, JsonObject?> emitEvent)
    {
        _config = config;
        _knownDevelopers = AircraftDeveloperData.Load(aircraftDevelopersJsonPath);
        _airports = new AirportsData(airportsJsonPath);
        _map = new SceneryMapBuilder(_airports);
        _emitEvent = emitEvent;
    }

    private string? CommunityPath =>
        _config.Get("_community_path") is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Public so the GSX folder watcher (started once at app startup) can poll the current effective path, and so config's _gsx_profiles_path_effective can resolve it - see ConfigService.RegisterEffectiveField.</summary>
    public string GsxPath()
    {
        var overridePath = _config.Get("_gsx_profiles_path") is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        return !string.IsNullOrEmpty(overridePath) ? overridePath : GsxProfiles.DefaultGsxPath();
    }

    // Kept alongside GsxPath() rather than split into SettingsService with
    // the other settings_* methods, since Python's own _gsx_path()/
    // settings_set_gsx_path/settings_reset_gsx_path/open_gsx_search/
    // open_gsx_folder all live together too (just in the one Api facade
    // class that has no Core/App split to worry about).
    public JsonObject SetGsxPath(string path) =>
        _config.Update(new JsonObject { ["_gsx_profiles_path"] = Path.GetFullPath(path) });

    public JsonObject ResetGsxPath() =>
        _config.MutateAndSave(config => config.Remove("_gsx_profiles_path"));

    public JsonObject OpenGsxSearch(string? icao)
    {
        icao = (icao ?? "").Trim().ToUpperInvariant();
        if (icao.Length != 4 || !icao.All(char.IsAsciiLetter))
        {
            return new JsonObject { ["ok"] = false };
        }
        try
        {
            Process.Start(new ProcessStartInfo($"https://flightsim.to/miscellaneous/gsx-pro?q={icao}") { UseShellExecute = true });
            return new JsonObject { ["ok"] = true };
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new JsonObject { ["ok"] = false };
        }
    }

    public JsonObject OpenGsxFolder()
    {
        var path = GsxPath();
        if (string.IsNullOrEmpty(path))
        {
            return new JsonObject { ["ok"] = false };
        }
        try
        {
            // Create it if this is a first-time user who's never had GSX
            // write anything there yet - opening a folder that doesn't
            // exist would otherwise just fail.
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return new JsonObject { ["ok"] = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new JsonObject { ["ok"] = false };
        }
    }

    private List<string> ResolveDisabledLocations(string communityPath)
    {
        var custom = _config.Get("_disabled_holding_path") is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        return DisabledLocations.ResolveDisabledLocations(communityPath, custom);
    }

    public JsonObject SceneryScan()
    {
        var communityPath = CommunityPath;
        if (string.IsNullOrEmpty(communityPath) || !Directory.Exists(communityPath))
        {
            return new JsonObject { ["records"] = new JsonArray(), ["no_community"] = true };
        }

        var records = SceneryScanner.ScanSceneryPackages(communityPath, ResolveDisabledLocations(communityPath));
        _map.AttachAirportNames(records);
        GsxProfiles.AttachGsxStatus(records, GsxPath());
        return new JsonObject
        {
            ["records"] = new JsonArray(records.Select(r => (JsonNode)r).ToArray()),
            ["no_community"] = false,
        };
    }

    public bool SceneryIsBusy() => _addonApplyActive;

    public JsonObject ScanCommunityUsage() => StartFolderUsageScan(CommunityPath, "community_usage_done");

    /// <summary>
    /// Same as ScanCommunityUsage, but for the disabled-holding folder -
    /// resolves the actual effective location (custom override, or the
    /// default sibling-of-Community folder) the same way SceneryScan does.
    /// </summary>
    public JsonObject ScanDisabledUsage()
    {
        var communityPath = CommunityPath;
        if (string.IsNullOrEmpty(communityPath) || !Directory.Exists(communityPath))
        {
            return new JsonObject { ["ok"] = false };
        }
        var primaryDisabled = ResolveDisabledLocations(communityPath)[0];
        return StartFolderUsageScan(primaryDisabled, "disabled_usage_done");
    }

    private JsonObject StartFolderUsageScan(string? folderPath, string eventName)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            return new JsonObject { ["ok"] = false };
        }
        Task.Run(() =>
        {
            var result = FolderSize.ScanFolderUsage(folderPath);
            _emitEvent(eventName, result);
        });
        return new JsonObject { ["ok"] = true };
    }

    public JsonObject SceneryApply(IReadOnlyDictionary<string, bool> desiredStates) => RunAddonApply("scenery", desiredStates);

    public JsonObject AircraftApply(IReadOnlyDictionary<string, bool> desiredStates) => RunAddonApply("aircraft", desiredStates);

    /// <summary>
    /// Shared by SceneryApply/AircraftApply - both just move folders
    /// between Community and a disabled-holding folder by name, so the
    /// move/progress/busy-flag machinery is identical; only the event
    /// names (so each tab's own frontend listener gets its update) differ.
    /// </summary>
    private JsonObject RunAddonApply(string eventPrefix, IReadOnlyDictionary<string, bool> desiredStates)
    {
        if (_addonApplyActive)
        {
            return new JsonObject { ["ok"] = false, ["error"] = "busy" };
        }

        var communityPath = CommunityPath;
        if (string.IsNullOrEmpty(communityPath) || !Directory.Exists(communityPath))
        {
            return new JsonObject { ["ok"] = false, ["error"] = "no_community" };
        }

        _addonApplyActive = true;
        var disabledLocations = ResolveDisabledLocations(communityPath);

        void OnProgress(int idx, int total, string name, long copied, long totalBytes) =>
            _emitEvent($"{eventPrefix}_apply_progress", new JsonObject
            {
                ["idx"] = idx,
                ["total"] = total,
                ["name"] = name,
                ["copied"] = copied,
                ["total_bytes"] = totalBytes,
            });

        Task.Run(() =>
        {
            List<PackageMover.SpaceShortfall> shortfalls;
            try
            {
                shortfalls = PackageMover.EstimateApplySpace(communityPath, disabledLocations, desiredStates);
            }
            catch
            {
                shortfalls = new List<PackageMover.SpaceShortfall>();
            }

            if (shortfalls.Count > 0)
            {
                // Refuse the whole batch before touching a single file - a
                // cross-drive copy that runs out of space partway through
                // leaves a broken partial folder behind instead of
                // failing cleanly, so this is checked upfront.
                _addonApplyActive = false;
                _emitEvent($"{eventPrefix}_apply_done", new JsonObject
                {
                    ["results"] = new JsonArray(),
                    ["insufficient_space"] = new JsonArray(shortfalls.Select(s => (JsonNode)new JsonObject
                    {
                        ["drive"] = s.Drive,
                        ["needed_bytes"] = s.NeededBytes,
                        ["free_bytes"] = s.FreeBytes,
                    }).ToArray()),
                });
                return;
            }

            List<PackageMover.MoveResult> rawResults;
            try
            {
                rawResults = PackageMover.ApplyPackageChanges(communityPath, disabledLocations, desiredStates, OnProgress);
            }
            catch (Exception ex)
            {
                rawResults = new List<PackageMover.MoveResult> { new("?", false, ex.Message) };
            }
            finally
            {
                _addonApplyActive = false;
            }

            var results = rawResults.Select(r => (JsonNode)new JsonObject
            {
                ["folder_name"] = r.FolderName,
                ["ok"] = r.Ok,
                ["error"] = r.Error,
            }).ToArray();

            _emitEvent($"{eventPrefix}_apply_done", new JsonObject { ["results"] = new JsonArray(results) });
        });

        return new JsonObject { ["ok"] = true };
    }

    /// <summary>
    /// Structural sanity check of the Community folder - misplaced
    /// (one-level-too-deep) manifests and duplicate installs across
    /// Community and all known disabled-holding locations.
    /// </summary>
    public JsonObject ScanCommunityDiagnostics()
    {
        var communityPath = CommunityPath;
        if (string.IsNullOrEmpty(communityPath) || !Directory.Exists(communityPath))
        {
            return new JsonObject { ["no_community"] = true, ["misplaced"] = new JsonArray(), ["duplicates"] = new JsonArray() };
        }

        var disabledLocations = ResolveDisabledLocations(communityPath);
        var misplaced = CommunityDiagnostics.FindMisplacedPackages(communityPath);
        var duplicates = CommunityDiagnostics.FindDuplicatePackages(new[] { communityPath }.Concat(disabledLocations));

        return new JsonObject
        {
            ["no_community"] = false,
            ["misplaced"] = new JsonArray(misplaced.Select(m => (JsonNode)m).ToArray()),
            ["duplicates"] = new JsonArray(duplicates.Select(d => (JsonNode)d).ToArray()),
        };
    }

    public JsonObject SceneryMapData()
    {
        var scan = SceneryScan();
        if (scan["no_community"]!.GetValue<bool>())
        {
            return new JsonObject { ["markers"] = new JsonArray(), ["no_community"] = true };
        }
        var records = scan["records"]!.AsArray().Select(r => (JsonObject)r!).ToList();
        var markers = _map.BuildMarkers(records);
        return new JsonObject
        {
            ["markers"] = new JsonArray(markers.Select(m => (JsonNode)m).ToArray()),
            ["no_community"] = false,
        };
    }

    public async Task<JsonObject> SimbriefCheck()
    {
        var username = _config.Get("_simbrief_username") is JsonValue uv && uv.TryGetValue<string>(out var u) ? u : "";
        var ofp = await SimbriefClient.FetchLatestOfp(username);
        if (!ofp["ok"]!.GetValue<bool>())
        {
            return ofp;
        }

        var scan = SceneryScan();
        if (scan["no_community"]!.GetValue<bool>())
        {
            return new JsonObject { ["ok"] = false, ["error"] = "scenery_no_community" };
        }
        var records = scan["records"]!.AsArray().Select(r => (JsonObject)r!).ToList();

        var legs = new (string Role, string? Icao)[]
        {
            ("origin", ofp["origin"]?.GetValue<string>()),
            ("destination", ofp["destination"]?.GetValue<string>()),
            ("alternate", ofp["alternate"]?.GetValue<string>()),
        };
        var matchedLegs = ScenerySimbriefMatch.MatchFlightPlan(records, legs);

        // records already carry gsx_status (from SceneryScan's
        // GsxProfiles.AttachGsxStatus) for every installed scenery - reuse
        // it instead of re-deriving.
        var recordsByIcao = records
            .Where(r => r["icao"] is not null)
            .GroupBy(r => r["icao"]!.GetValue<string>())
            .ToDictionary(g => g.Key, g => g.First());
        var gsxProfilesByIcao = GsxProfiles.ScanProfiles(GsxPath());

        var atcNetwork = _config.Get("_atc_network") is JsonValue av && av.TryGetValue<string>(out var an) ? an : "off";
        var onlineAtc = await AtcNetwork.FetchOnlineAtc(atcNetwork);

        foreach (var leg in matchedLegs)
        {
            var icao = leg["icao"]!.GetValue<string>();

            var airport = _airports.Lookup(icao);
            leg["lat"] = airport?["lat"]?.DeepClone();
            leg["lon"] = airport?["lon"]?.DeepClone();

            if (recordsByIcao.TryGetValue(icao, out var matchedRecord))
            {
                leg["gsx_status"] = matchedRecord["gsx_status"]?.DeepClone();
            }
            else
            {
                // No scenery installed for this leg, so there's no developer
                // to compare a profile against - just report whether any
                // profile exists for the ICAO at all.
                leg["gsx_status"] = gsxProfilesByIcao.ContainsKey(icao) ? "installed_unmatched" : "missing";
            }

            var atcPositions = onlineAtc.GetValueOrDefault(icao, new List<JsonObject>());
            leg["atc_positions"] = new JsonArray(atcPositions.Select(p => (JsonNode)p.DeepClone()).ToArray());
            leg["atc_online"] = atcPositions.Count > 0;
        }

        var installedAirac = AiracManager.GetInstalledAirac(CommunityPath);
        return new JsonObject
        {
            ["ok"] = true,
            ["legs"] = new JsonArray(matchedLegs.Select(l => (JsonNode)l).ToArray()),
            ["duration_minutes"] = ofp["duration_minutes"]?.DeepClone(),
            ["aircraft_name"] = ofp["aircraft_name"]?.DeepClone(),
            ["planned_at"] = ofp["planned_at"]?.DeepClone(),
            ["route_points"] = ofp["route_points"]?.DeepClone() ?? new JsonArray(),
            ["simbrief_airac"] = ofp["airac"]?.DeepClone(),
            ["installed_airac"] = string.IsNullOrEmpty(installedAirac) ? null : installedAirac,
        };
    }

    public JsonObject AircraftScan()
    {
        var communityPath = CommunityPath;
        if (string.IsNullOrEmpty(communityPath) || !Directory.Exists(communityPath))
        {
            return new JsonObject
            {
                ["aircraft"] = new JsonArray(),
                ["liveries"] = new JsonArray(),
                ["no_community"] = true,
                ["type_overrides"] = new JsonObject(),
                ["parent_overrides"] = new JsonObject(),
            };
        }

        var (aircraft, liveries) = AircraftScanner.ScanAircraftAndLiveries(
            communityPath, ResolveDisabledLocations(communityPath), _knownDevelopers);

        var typeOverrides = ToStringDict(_config.Get("_aircraft_type_overrides") as JsonObject);
        var parentOverrides = ToStringDict(_config.Get("_aircraft_parent_overrides") as JsonObject);
        var dismissed = ToStringSet(_config.Get("_aircraft_livery_suggestions_dismissed") as JsonArray);

        (aircraft, liveries) = AircraftOverrides.ApplyOverrides(aircraft, liveries, typeOverrides, parentOverrides, dismissed);

        foreach (var record in aircraft)
        {
            record["type_hint"] = AircraftTypeHint.ExtractTypeHint(record["display_name"]?.GetValue<string>());
        }

        return new JsonObject
        {
            ["aircraft"] = new JsonArray(aircraft.Select(r => (JsonNode)r).ToArray()),
            ["liveries"] = new JsonArray(liveries.Select(r => (JsonNode)r).ToArray()),
            ["no_community"] = false,
            ["type_overrides"] = ToJsonObject(typeOverrides),
            ["parent_overrides"] = ToJsonObject(parentOverrides),
        };
    }

    public bool AircraftIsBusy() => _addonApplyActive;

    /// <summary>typeValue: "aircraft" | "livery" | null (clears the override).</summary>
    public JsonObject SetAircraftTypeOverride(string folderName, string? typeValue)
    {
        _config.MutateAndSave(config =>
        {
            if (config["_aircraft_type_overrides"] is not JsonObject overrides)
            {
                overrides = new JsonObject();
                config["_aircraft_type_overrides"] = overrides;
            }
            if (typeValue is null) overrides.Remove(folderName);
            else overrides[folderName] = typeValue;
        });
        return AircraftScan();
    }

    /// <summary>parentFolderName: a folder to force as parent, "" to force "unassigned", or null to clear the override.</summary>
    public JsonObject SetAircraftParentOverride(string folderName, string? parentFolderName)
    {
        _config.MutateAndSave(config =>
        {
            if (config["_aircraft_parent_overrides"] is not JsonObject overrides)
            {
                overrides = new JsonObject();
                config["_aircraft_parent_overrides"] = overrides;
            }
            if (parentFolderName is null) overrides.Remove(folderName);
            else overrides[folderName] = parentFolderName;
        });
        return AircraftScan();
    }

    public JsonObject DismissLiverySuggestion(string folderName)
    {
        _config.MutateAndSave(config =>
        {
            if (config["_aircraft_livery_suggestions_dismissed"] is not JsonArray dismissed)
            {
                dismissed = new JsonArray();
                config["_aircraft_livery_suggestions_dismissed"] = dismissed;
            }
            if (!dismissed.Any(n => n?.GetValue<string>() == folderName))
            {
                dismissed.Add(folderName);
            }
        });
        return AircraftScan();
    }

    private static Dictionary<string, string> ToStringDict(JsonObject? obj)
    {
        var result = new Dictionary<string, string>();
        if (obj is null) return result;
        foreach (var (key, value) in obj)
        {
            if (value is JsonValue jv && jv.TryGetValue<string>(out var s)) result[key] = s;
        }
        return result;
    }

    private static HashSet<string> ToStringSet(JsonArray? array)
    {
        var result = new HashSet<string>();
        if (array is null) return result;
        foreach (var item in array)
        {
            if (item is JsonValue jv && jv.TryGetValue<string>(out var s)) result.Add(s);
        }
        return result;
    }

    private static JsonObject ToJsonObject(Dictionary<string, string> dict)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in dict) obj[key] = value;
        return obj;
    }
}
