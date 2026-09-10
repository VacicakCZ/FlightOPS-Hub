using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class AppsServiceTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_appssvc_{Guid.NewGuid():N}.json");

    [Fact]
    public void Save_RejectsInvalidInputWithoutWriting()
    {
        var path = TempPath();
        try
        {
            var service = new AppsService(path);
            var result = service.Save(new JsonObject { ["name"] = "", ["path"] = "C:\\a.exe" });

            Assert.False(result["ok"]!.GetValue<bool>());
            Assert.Equal("err_empty", result["error"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_AddsAppAndPersists()
    {
        var path = TempPath();
        try
        {
            var service = new AppsService(path);
            var result = service.Save(new JsonObject
            {
                ["name"] = "GSX Pro",
                ["path"] = "C:\\gsx.exe",
                ["launch_mode"] = "immediate",
                ["delay"] = 0,
                ["admin"] = false,
            });

            Assert.True(result["ok"]!.GetValue<bool>());
            Assert.True(service.List().ContainsKey("GSX Pro"));

            var reloaded = new AppsService(path);
            Assert.True(reloaded.List().ContainsKey("GSX Pro"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_AcceptsDelayAsJsonStringOrNumber()
    {
        var path = TempPath();
        try
        {
            var service = new AppsService(path);
            var result = service.Save(new JsonObject
            {
                ["name"] = "REX Atmos",
                ["path"] = "C:\\rex.exe",
                ["launch_mode"] = "timer",
                ["delay"] = "150", // as sent by a plain HTML input
            });

            Assert.True(result["ok"]!.GetValue<bool>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Remove_DropsAppFromList()
    {
        var path = TempPath();
        try
        {
            var service = new AppsService(path);
            service.Save(new JsonObject { ["name"] = "A", ["path"] = "C:\\a.exe", ["launch_mode"] = "immediate" });

            var result = service.Remove("A");

            Assert.False(((JsonObject)result["apps"]!).ContainsKey("A"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppExeExists_ReflectsRealFileSystemState()
    {
        var path = TempPath();
        var fakeExe = Path.Combine(Path.GetTempPath(), $"flightops_test_fake_{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(fakeExe, "");
            var service = new AppsService(path);
            service.Save(new JsonObject { ["name"] = "Real", ["path"] = fakeExe, ["launch_mode"] = "immediate" });
            service.Save(new JsonObject { ["name"] = "Missing", ["path"] = "C:\\does\\not\\exist.exe", ["launch_mode"] = "immediate" });

            Assert.True(service.AppExeExists("Real"));
            Assert.False(service.AppExeExists("Missing"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(fakeExe);
        }
    }
}
