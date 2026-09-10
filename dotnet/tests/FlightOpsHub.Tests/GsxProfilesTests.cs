using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class GsxProfilesTests
{
    private static string TempDir(string label) =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_gsx_{label}_{Guid.NewGuid():N}");

    [Fact]
    public void ScanProfiles_ExtractsIcaoFromVariousSeparators()
    {
        var dir = TempDir("separators");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in new[] { "LKPR-darriancze-gsxvdgs.ini", "ebbr_aerosoft_v2.ini", "DTMB - Nizar.ini" })
            {
                File.WriteAllText(Path.Combine(dir, name), "");
            }

            var profiles = GsxProfiles.ScanProfiles(dir);

            Assert.Equal(new[] { "LKPR-darriancze-gsxvdgs.ini" }, profiles["LKPR"]);
            Assert.Equal(new[] { "ebbr_aerosoft_v2.ini" }, profiles["EBBR"]);
            Assert.Equal(new[] { "DTMB - Nizar.ini" }, profiles["DTMB"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ScanProfiles_IgnoresFilesWithoutRecognizableIcaoPrefix()
    {
        var dir = TempDir("norecognizable");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "");
            File.WriteAllText(Path.Combine(dir, "notanicao-file.ini"), "");

            var profiles = GsxProfiles.ScanProfiles(dir);

            Assert.Empty(profiles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ScanProfiles_IgnoresSubdirectories()
    {
        var dir = TempDir("subdirs");
        Directory.CreateDirectory(Path.Combine(dir, "LKPR-sub"));
        try
        {
            var profiles = GsxProfiles.ScanProfiles(dir);
            Assert.Empty(profiles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ScanProfiles_MissingOrEmptyPathReturnsEmptyDict()
    {
        Assert.Empty(GsxProfiles.ScanProfiles(""));
        Assert.Empty(GsxProfiles.ScanProfiles(Path.Combine(Path.GetTempPath(), $"flightops_test_gsx_missing_{Guid.NewGuid():N}")));
    }

    [Fact]
    public void StatusFor_MissingWhenNoProfiles()
    {
        Assert.Equal("missing", GsxProfiles.StatusFor("aerosoft", new List<string>()));
    }

    [Fact]
    public void StatusFor_MatchesDeveloperKeyInFilename()
    {
        Assert.Equal("installed_match", GsxProfiles.StatusFor("aerosoft", new List<string> { "ebbr_aerosoft_v2.ini" }));
    }

    [Fact]
    public void StatusFor_UnmatchedWhenDeveloperNotInFilename()
    {
        Assert.Equal("installed_unmatched", GsxProfiles.StatusFor("flightbeam", new List<string> { "lfbo-snekye.ini" }));
    }

    private static JsonObject Record(string folderName, string displayName) =>
        new() { ["folder_name"] = folderName, ["display_name"] = displayName };

    [Fact]
    public void AttachGsxStatus_FullThreeStates()
    {
        var dir = TempDir("threestates");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "ebbr_aerosoft_v2.ini"), "");
            File.WriteAllText(Path.Combine(dir, "lfbo-snekye.ini"), "");

            var records = new List<JsonObject>
            {
                Record("aerosoft-airport-ebbr-brussels", "[EBBR] Brussels Airport"),
                Record("flightbeam-lfbo-toulouse", "[LFBO] Toulouse-Blagnac"),
                Record("orbx-airport-eddf-frankfurt", "[EDDF] Frankfurt Airport"),
            };

            GsxProfiles.AttachGsxStatus(records, dir);

            var byFolder = records.ToDictionary(r => r["folder_name"]!.GetValue<string>());
            Assert.Equal("installed_match", byFolder["aerosoft-airport-ebbr-brussels"]["gsx_status"]!.GetValue<string>());
            Assert.Equal("installed_unmatched", byFolder["flightbeam-lfbo-toulouse"]["gsx_status"]!.GetValue<string>());
            Assert.Equal("missing", byFolder["orbx-airport-eddf-frankfurt"]["gsx_status"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AttachGsxStatus_NoneForRecordWithoutIcao()
    {
        var records = new List<JsonObject> { Record("some-random-scenery", "Some Scenery With No Code") };

        GsxProfiles.AttachGsxStatus(records, "");

        Assert.Null(records[0]["gsx_status"]);
        Assert.Null(records[0]["icao"]);
    }

    [Fact]
    public void AttachGsxStatus_AttachesPrettifiedDeveloperName()
    {
        var records = new List<JsonObject> { Record("aerosoft-airport-ebbr-brussels", "[EBBR] Brussels Airport") };

        GsxProfiles.AttachGsxStatus(records, "");

        Assert.Equal("Aerosoft", records[0]["developer"]!.GetValue<string>());
    }
}
