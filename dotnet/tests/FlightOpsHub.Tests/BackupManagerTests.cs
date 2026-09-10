using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class BackupManagerTests
{
    [Fact]
    public void BuildBundle_IncludesVersionAndBothPayloads()
    {
        var config = new JsonObject { ["_language"] = "CZ" };
        var apps = new JsonObject { ["A"] = new JsonObject() };

        var bundle = BackupManager.BuildBundle(config, apps);

        Assert.Equal(BackupManager.BundleVersion, bundle["bundle_version"]!.GetValue<int>());
        Assert.Equal("CZ", bundle["config"]!["_language"]!.GetValue<string>());
        Assert.True(((JsonObject)bundle["apps"]!).ContainsKey("A"));
        Assert.True(bundle["exported_at"]!.GetValue<long>() > 0);
    }

    [Fact]
    public void ParseBundle_RoundTripsAValidBundle()
    {
        var config = new JsonObject { ["_language"] = "EN" };
        var apps = new JsonObject { ["A"] = new JsonObject() };
        var bundle = BackupManager.BuildBundle(config, apps);

        var (parsedConfig, parsedApps) = BackupManager.ParseBundle(bundle);

        Assert.Equal("EN", parsedConfig["_language"]!.GetValue<string>());
        Assert.True(parsedApps.ContainsKey("A"));
    }

    [Fact]
    public void ParseBundle_RejectsNonObjectData()
    {
        Assert.Throws<FormatException>(() => BackupManager.ParseBundle(JsonValue.Create("not an object")));
    }

    [Fact]
    public void ParseBundle_RejectsMissingConfigOrApps()
    {
        var missingApps = new JsonObject { ["config"] = new JsonObject() };
        Assert.Throws<FormatException>(() => BackupManager.ParseBundle(missingApps));

        var missingConfig = new JsonObject { ["apps"] = new JsonObject() };
        Assert.Throws<FormatException>(() => BackupManager.ParseBundle(missingConfig));
    }
}
