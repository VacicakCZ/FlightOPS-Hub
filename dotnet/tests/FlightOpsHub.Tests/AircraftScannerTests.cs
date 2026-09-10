using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class AircraftScannerTests
{
    // --- developer key heuristic ---

    [Fact]
    public void DeveloperKey_TakesFirstSegment()
    {
        Assert.Equal("fnx", AircraftScanner.DeveloperKey("fnx-aircraft-320"));
        Assert.Equal("bksq", AircraftScanner.DeveloperKey("bksq_a339x"));
    }

    [Fact]
    public void PrettifyDevKey_CapitalizesFirstLetterOnly()
    {
        Assert.Equal("Aerosoft", AircraftScanner.PrettifyDevKey("aerosoft"));
        Assert.Equal("", AircraftScanner.PrettifyDevKey(""));
    }

    // --- LooksLikeALiveryByName: weak text-only "suggestion" signal ---

    [Fact]
    public void LooksLikeALiveryByName_MatchesLiveryWord()
    {
        Assert.True(AircraftScanner.LooksLikeALiveryByName("livery-c750-N750XC-2024", "Cessna Citation X | N750XC"));
    }

    [Fact]
    public void LooksLikeALiveryByName_MatchesUsNNumber()
    {
        Assert.True(AircraftScanner.LooksLikeALiveryByName("some-pack", "N707RA Repaint"));
    }

    [Fact]
    public void LooksLikeALiveryByName_MatchesIcaoStyleRegistration()
    {
        Assert.True(AircraftScanner.LooksLikeALiveryByName("headwind-a330neo-Lufthansa D-AIKL", "Airbus A330-900"));
    }

    [Fact]
    public void LooksLikeALiveryByName_FalseForNormalAircraftName()
    {
        Assert.False(AircraftScanner.LooksLikeALiveryByName("fnx-aircraft-319-321", "Fenix Airbus A319 & A321"));
    }

    // --- ScanAircraftAndLiveries: developer lookup + base_container-based
    // reclassification (manifest content_type is self-reported and often
    // wrong for registration/repaint packs) ---

    private static string TempDir(string label) =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_aircraftscan_{label}_{Guid.NewGuid():N}");

    /// <summary>simobjects: {simObjectFolderName: baseContainerValueOrNull}.</summary>
    private static void MakeAircraftPackage(
        string baseDir, string folderName, string title, string contentType = "AIRCRAFT",
        string? creator = null, IReadOnlyDictionary<string, string?>? simobjects = null)
    {
        var pkgDir = Path.Combine(baseDir, folderName);
        Directory.CreateDirectory(pkgDir);
        var manifest = new JsonObject { ["content_type"] = contentType, ["title"] = title };
        if (!string.IsNullOrEmpty(creator)) manifest["creator"] = creator;
        File.WriteAllText(Path.Combine(pkgDir, "manifest.json"), manifest.ToJsonString());

        foreach (var (simObjName, baseContainer) in simobjects ?? new Dictionary<string, string?>())
        {
            var simObjDir = Path.Combine(pkgDir, "SimObjects", "Airplanes", simObjName);
            Directory.CreateDirectory(simObjDir);
            var cfgLines = new List<string> { "[VARIATION]" };
            if (baseContainer != null) cfgLines.Add($"base_container = \"{baseContainer}\"");
            File.WriteAllText(Path.Combine(simObjDir, "aircraft.cfg"), string.Join("\n", cfgLines));
        }
    }

    private static readonly Dictionary<string, string> KnownDevelopers = new() { ["pmdg"] = "PMDG" };

    [Fact]
    public void DeveloperLookup_PrefersCuratedListOverCreatorField()
    {
        var community = TempDir("curated");
        Directory.CreateDirectory(community);
        try
        {
            MakeAircraftPackage(
                community, "pmdg-aircraft-77w", "PMDG 777-300ER", creator: "Some Random Creator Name",
                simobjects: new Dictionary<string, string?> { ["PMDG 777-300ER"] = null });

            var (aircraft, _) = AircraftScanner.ScanAircraftAndLiveries(community, Array.Empty<string>(), KnownDevelopers);

            Assert.Equal("PMDG", aircraft[0]["developer"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(community, recursive: true);
        }
    }

    [Fact]
    public void DeveloperLookup_FallsBackToCreatorConsensusForUnknownPrefix()
    {
        var community = TempDir("consensus");
        Directory.CreateDirectory(community);
        try
        {
            MakeAircraftPackage(
                community, "somestudio-aircraft-a", "Aircraft A", creator: "Some Studio Inc.",
                simobjects: new Dictionary<string, string?> { ["Aircraft A"] = null });
            MakeAircraftPackage(
                community, "somestudio-aircraft-b", "Aircraft B", creator: "Some Studio Inc.",
                simobjects: new Dictionary<string, string?> { ["Aircraft B"] = null });

            var (aircraft, _) = AircraftScanner.ScanAircraftAndLiveries(community, Array.Empty<string>(), KnownDevelopers);

            Assert.Equal(new HashSet<string> { "Some Studio Inc." }, aircraft.Select(r => r["developer"]!.GetValue<string>()).ToHashSet());
        }
        finally
        {
            Directory.Delete(community, recursive: true);
        }
    }

    [Fact]
    public void AircraftDeclaredPackageWithExternalBaseContainer_ReclassifiedAsLivery()
    {
        // Mirrors a real-world pattern: a registration/repaint pack whose
        // manifest.json wrongly says content_type "AIRCRAFT", but whose
        // own aircraft.cfg base_container points at a *different*
        // package's SimObjects folder - structurally a livery no matter
        // what the manifest claims.
        var community = TempDir("reclassify");
        Directory.CreateDirectory(community);
        try
        {
            MakeAircraftPackage(
                community, "base-aircraft-320", "Base Airbus A320", "AIRCRAFT",
                simobjects: new Dictionary<string, string?> { ["Base_A320"] = null });
            MakeAircraftPackage(
                community, "livery-a320-fake-reg", "A320 Fake Registration", "AIRCRAFT",
                simobjects: new Dictionary<string, string?> { ["livery-a320-fake-reg"] = "Base_A320" });

            var (aircraft, liveries) = AircraftScanner.ScanAircraftAndLiveries(community, Array.Empty<string>(), KnownDevelopers);

            Assert.Equal(new[] { "base-aircraft-320" }, aircraft.Select(r => r["folder_name"]!.GetValue<string>()));
            Assert.Single(liveries);
            Assert.Equal("livery-a320-fake-reg", liveries[0]["folder_name"]!.GetValue<string>());
            Assert.Equal("base-aircraft-320", liveries[0]["parent_aircraft"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(community, recursive: true);
        }
    }

    [Fact]
    public void AircraftWithBaseContainerPointingToOwnVariant_StaysAircraft()
    {
        // A package can legitimately bundle multiple related SimObjects
        // where one variant's aircraft.cfg derives from a sibling within
        // the *same* package (e.g. a shortened-fuselage variant based on
        // the main model) - must NOT be mistaken for an external dependency.
        var community = TempDir("ownvariant");
        Directory.CreateDirectory(community);
        try
        {
            MakeAircraftPackage(
                community, "fnx-aircraft-319-321", "Fenix A319 & A321", "AIRCRAFT",
                simobjects: new Dictionary<string, string?> { ["FNX321"] = null, ["FNX319"] = "FNX321" });

            var (aircraft, liveries) = AircraftScanner.ScanAircraftAndLiveries(community, Array.Empty<string>(), KnownDevelopers);

            Assert.Equal(new[] { "fnx-aircraft-319-321" }, aircraft.Select(r => r["folder_name"]!.GetValue<string>()));
            Assert.Empty(liveries);
        }
        finally
        {
            Directory.Delete(community, recursive: true);
        }
    }

    [Fact]
    public void LiveryDeclaredPackage_IsUnaffectedByReclassificationCheck()
    {
        var community = TempDir("liverydeclared");
        Directory.CreateDirectory(community);
        try
        {
            MakeAircraftPackage(
                community, "pmdg-aircraft-77w", "PMDG 777-300ER", "AIRCRAFT",
                simobjects: new Dictionary<string, string?> { ["PMDG 777-300ER"] = null });
            MakeAircraftPackage(
                community, "pmdg-aircraft-77w-liveries", "Liveries", "LIVERY",
                simobjects: new Dictionary<string, string?> { ["pmdg-aircraft-77w-liveries"] = "PMDG 777-300ER" });

            var (aircraft, liveries) = AircraftScanner.ScanAircraftAndLiveries(community, Array.Empty<string>(), KnownDevelopers);

            Assert.Equal(new[] { "pmdg-aircraft-77w" }, aircraft.Select(r => r["folder_name"]!.GetValue<string>()));
            Assert.Equal("pmdg-aircraft-77w", liveries[0]["parent_aircraft"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(community, recursive: true);
        }
    }

    [Fact]
    public void SelfContainedAircraftThatLooksLikeRepaint_FlaggedSuspectedLivery()
    {
        // No external base_container here (self-contained package, e.g. a
        // payware repaint bundling a full copy) so it stays classified
        // AIRCRAFT - but its name matches the weak text heuristic, so it
        // should carry the suggestion flag for the UI (never auto-reclassified).
        var community = TempDir("suspected");
        Directory.CreateDirectory(community);
        try
        {
            MakeAircraftPackage(
                community, "livery-c750-N750XC-2024", "Cessna Citation X | N750XC", "AIRCRAFT",
                simobjects: new Dictionary<string, string?> { ["livery-c750-N750XC-2024"] = null });

            var (aircraft, _) = AircraftScanner.ScanAircraftAndLiveries(community, Array.Empty<string>(), KnownDevelopers);

            Assert.True(aircraft[0]["suspected_livery"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(community, recursive: true);
        }
    }

    [Fact]
    public void NormalAircraftPackage_NotFlaggedSuspectedLivery()
    {
        var community = TempDir("normal");
        Directory.CreateDirectory(community);
        try
        {
            MakeAircraftPackage(
                community, "pmdg-aircraft-77w", "PMDG 777-300ER", "AIRCRAFT",
                simobjects: new Dictionary<string, string?> { ["PMDG 777-300ER"] = null });

            var (aircraft, _) = AircraftScanner.ScanAircraftAndLiveries(community, Array.Empty<string>(), KnownDevelopers);

            Assert.False(aircraft[0]["suspected_livery"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(community, recursive: true);
        }
    }

    [Fact]
    public void ReclassifiedLivery_DoesNotCarrySuspectedLiveryFlag()
    {
        // Once a package is reclassified to LIVERY by the reliable
        // base_container check, the weak name-based flag is irrelevant -
        // it only applies to records that end up AIRCRAFT.
        var community = TempDir("reclassifiedflag");
        Directory.CreateDirectory(community);
        try
        {
            MakeAircraftPackage(
                community, "base-aircraft-320", "Base Airbus A320", "AIRCRAFT",
                simobjects: new Dictionary<string, string?> { ["Base_A320"] = null });
            MakeAircraftPackage(
                community, "livery-a320-fake-reg", "A320 Fake Registration", "AIRCRAFT",
                simobjects: new Dictionary<string, string?> { ["livery-a320-fake-reg"] = "Base_A320" });

            var (_, liveries) = AircraftScanner.ScanAircraftAndLiveries(community, Array.Empty<string>(), KnownDevelopers);

            Assert.False(liveries[0]["suspected_livery"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(community, recursive: true);
        }
    }
}
