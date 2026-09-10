using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class ProfilesServiceTests
{
    private static string TempConfigPath() =>
        Path.Combine(Path.GetTempPath(), $"flightops_test_profiles_{Guid.NewGuid():N}.json");

    [Fact]
    public void Save_ThenList_RoundTrips()
    {
        var path = TempConfigPath();
        try
        {
            var profiles = new ProfilesService(new ConfigService(path));
            var members = new JsonArray("PMDG 737-800", "GSX Pro");

            var saveResult = profiles.Save("flight", "My Profile", members);
            Assert.Equal("My Profile", saveResult["last"]!.GetValue<string>());

            var list = profiles.List("flight");
            Assert.True(list.ContainsKey("My Profile"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Delete_RemovesProfileAndClearsLast()
    {
        var path = TempConfigPath();
        try
        {
            var profiles = new ProfilesService(new ConfigService(path));
            profiles.Save("exe", "Profile A", new JsonArray("X"));

            var deleteResult = profiles.Delete("exe", "Profile A");

            Assert.Null(deleteResult["last"]);
            Assert.False(((JsonObject)deleteResult["profiles"]!).ContainsKey("Profile A"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FlightAndExeKindsAreIndependentStores()
    {
        var path = TempConfigPath();
        try
        {
            var profiles = new ProfilesService(new ConfigService(path));
            profiles.Save("flight", "Shared Name", new JsonArray("A"));
            profiles.Save("exe", "Shared Name", new JsonArray("B"));

            Assert.True(profiles.List("flight").ContainsKey("Shared Name"));
            Assert.True(profiles.List("exe").ContainsKey("Shared Name"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
