using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class SceneryServiceApplyTests
{
    private static string TempDir(string label) =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_scenerysvc_{label}_{Guid.NewGuid():N}");

    private static void MakeSceneryPackage(string baseDir, string folderName, string title)
    {
        var pkgDir = Path.Combine(baseDir, folderName);
        Directory.CreateDirectory(pkgDir);
        var manifest = new JsonObject { ["content_type"] = "SCENERY", ["title"] = title };
        File.WriteAllText(Path.Combine(pkgDir, "manifest.json"), manifest.ToJsonString());
    }

    private static (SceneryService Service, ConfigService Config, ConcurrentQueue<(string Type, JsonObject? Payload)> Events) MakeService(string configPath)
    {
        var events = new ConcurrentQueue<(string, JsonObject?)>();
        var config = new ConfigService(configPath);
        var service = new SceneryService(
            config,
            Path.Combine(Path.GetTempPath(), "flightops_test_nonexistent_developers.json"),
            Path.Combine(Path.GetTempPath(), "flightops_test_nonexistent_airports.json"),
            (type, payload) => events.Enqueue((type, payload)));
        return (service, config, events);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    [Fact]
    public async Task ScanCommunityUsage_EmitsCommunityUsageDoneWithTotals()
    {
        var root = TempDir("communityusage");
        var configPath = Path.Combine(root, "config.json");
        var community = Path.Combine(root, "Community");
        Directory.CreateDirectory(community);
        File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(community, "pkg")).FullName, "f.bgl"), new byte[500]);
        try
        {
            var (service, config, events) = MakeService(configPath);
            config.Update(new JsonObject { ["_community_path"] = community });

            var kickoff = service.ScanCommunityUsage();
            Assert.True(kickoff["ok"]!.GetValue<bool>());

            var found = await WaitUntil(() => events.Any(e => e.Type == "community_usage_done"), TimeSpan.FromSeconds(2));
            Assert.True(found);

            var (_, payload) = events.First(e => e.Type == "community_usage_done");
            Assert.Equal(500, payload!["total_bytes"]!.GetValue<long>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanCommunityUsage_NoCommunityPathReturnsNotOk()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"flightops_test_scenerysvc_nocomm_{Guid.NewGuid():N}.json");
        try
        {
            var (service, _, _) = MakeService(configPath);
            var result = service.ScanCommunityUsage();
            Assert.False(result["ok"]!.GetValue<bool>());
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task SceneryApply_MovesPackageAndEmitsDoneEvent()
    {
        var root = TempDir("applymove");
        var configPath = Path.Combine(root, "config.json");
        var community = Path.Combine(root, "Community");
        Directory.CreateDirectory(community);
        MakeSceneryPackage(community, "orbx-airport-eddf-frankfurt", "Frankfurt");
        try
        {
            var (service, config, events) = MakeService(configPath);
            config.Update(new JsonObject { ["_community_path"] = community });

            var kickoff = service.SceneryApply(new Dictionary<string, bool> { ["orbx-airport-eddf-frankfurt"] = false });
            Assert.True(kickoff["ok"]!.GetValue<bool>());

            var found = await WaitUntil(() => events.Any(e => e.Type == "scenery_apply_done"), TimeSpan.FromSeconds(3));
            Assert.True(found);

            Assert.False(Directory.Exists(Path.Combine(community, "orbx-airport-eddf-frankfurt")));
            Assert.False(service.SceneryIsBusy());

            var (_, payload) = events.First(e => e.Type == "scenery_apply_done");
            var results = payload!["results"]!.AsArray();
            Assert.Single(results);
            Assert.True(results[0]!["ok"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SceneryApply_NoCommunityPathReturnsError()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"flightops_test_scenerysvc_applynocomm_{Guid.NewGuid():N}.json");
        try
        {
            var (service, _, _) = MakeService(configPath);
            var result = service.SceneryApply(new Dictionary<string, bool> { ["whatever"] = true });

            Assert.False(result["ok"]!.GetValue<bool>());
            Assert.Equal("no_community", result["error"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task SceneryApplyAndAircraftApply_ShareOneBusyFlag()
    {
        var root = TempDir("sharedbusy");
        var configPath = Path.Combine(root, "config.json");
        var community = Path.Combine(root, "Community");
        Directory.CreateDirectory(community);
        // A handful of packages so the apply takes at least a moment.
        for (var i = 0; i < 5; i++) MakeSceneryPackage(community, $"pkg-{i}", $"Package {i}");
        try
        {
            var (service, config, events) = MakeService(configPath);
            config.Update(new JsonObject { ["_community_path"] = community });

            var desired = Enumerable.Range(0, 5).ToDictionary(i => $"pkg-{i}", _ => false);
            service.SceneryApply(desired);

            // Immediately after kicking one off, both flags should report busy.
            Assert.True(service.SceneryIsBusy());
            Assert.True(service.AircraftIsBusy());

            var second = service.AircraftApply(new Dictionary<string, bool> { ["pkg-0"] = true });
            Assert.False(second["ok"]!.GetValue<bool>());
            Assert.Equal("busy", second["error"]!.GetValue<string>());

            await WaitUntil(() => events.Any(e => e.Type == "scenery_apply_done"), TimeSpan.FromSeconds(3));
            Assert.False(service.SceneryIsBusy());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
