using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class ApiDispatcherTests
{
    [Fact]
    public async Task Dispatch_RoutesToRegisteredHandlerAndEchoesId()
    {
        var dispatcher = new ApiDispatcher();
        dispatcher.Register("ping", _ => "pong");

        var response = JsonNode.Parse(await dispatcher.DispatchAsync("""{"id":7,"method":"ping","args":[]}"""))!;

        Assert.Equal(7, response["id"]!.GetValue<int>());
        Assert.Equal("pong", response["result"]!.GetValue<string>());
        Assert.Null(response["error"]);
    }

    [Fact]
    public async Task Dispatch_PassesArgsThroughToHandler()
    {
        var dispatcher = new ApiDispatcher();
        dispatcher.Register("add_one", args => args[0]!.GetValue<int>() + 1);

        var response = JsonNode.Parse(await dispatcher.DispatchAsync("""{"id":1,"method":"add_one","args":[41]}"""))!;

        Assert.Equal(42, response["result"]!.GetValue<int>());
    }

    [Fact]
    public async Task Dispatch_UnknownMethodReturnsError()
    {
        var dispatcher = new ApiDispatcher();

        var response = JsonNode.Parse(await dispatcher.DispatchAsync("""{"id":1,"method":"does_not_exist","args":[]}"""))!;

        Assert.NotNull(response["error"]);
        Assert.Null(response["result"]);
    }

    [Fact]
    public async Task Dispatch_HandlerExceptionReturnsErrorInsteadOfThrowing()
    {
        var dispatcher = new ApiDispatcher();
        dispatcher.Register("boom", _ => throw new InvalidOperationException("kaboom"));

        var response = JsonNode.Parse(await dispatcher.DispatchAsync("""{"id":1,"method":"boom","args":[]}"""))!;

        Assert.Equal("kaboom", response["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Dispatch_MalformedRequestReturnsError()
    {
        var dispatcher = new ApiDispatcher();

        var response = JsonNode.Parse(await dispatcher.DispatchAsync("not json"))!;

        Assert.NotNull(response["error"]);
    }
}

public class ConfigServiceTests
{
    [Fact]
    public void Update_PersistsAndReturnsSnapshotWithPatchApplied()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_svc_{Guid.NewGuid():N}.json");
        try
        {
            var service = new ConfigService(path);

            var snapshot = service.Update(new JsonObject { ["_language"] = "CZ" });
            Assert.Equal("CZ", snapshot["_language"]!.GetValue<string>());

            // A fresh instance re-reading the same path sees the persisted value.
            var reloaded = new ConfigService(path);
            Assert.Equal("CZ", reloaded.Snapshot()["_language"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Snapshot_IsIndependentFromSubsequentUpdates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flightops_test_svc2_{Guid.NewGuid():N}.json");
        try
        {
            var service = new ConfigService(path);
            var first = service.Snapshot();
            service.Update(new JsonObject { ["_language"] = "EN" });

            Assert.False(first.ContainsKey("_language"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
