using System.Text.Json.Nodes;
using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class I18nTests
{
    private static string MakeLocalesDir(string lang, JsonObject strings)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"flightops_test_i18n_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{lang}.json"), strings.ToJsonString());
        return dir;
    }

    [Fact]
    public void Translate_ReturnsKnownString()
    {
        var dir = MakeLocalesDir("en", new JsonObject { ["already_running_title"] = "FlightOps Hub" });
        try
        {
            var i18n = new I18n(dir);
            Assert.Equal("FlightOps Hub", i18n.Translate("EN", "already_running_title"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Translate_IsCaseInsensitiveOnLanguageCode()
    {
        var dir = MakeLocalesDir("cz", new JsonObject { ["greeting"] = "Ahoj" });
        try
        {
            var i18n = new I18n(dir);
            Assert.Equal("Ahoj", i18n.Translate("CZ", "greeting"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Translate_FallsBackToKeyWhenMissing()
    {
        var dir = MakeLocalesDir("en", new JsonObject());
        try
        {
            var i18n = new I18n(dir);
            Assert.Equal("unknown_key", i18n.Translate("EN", "unknown_key"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Translate_DefaultsToEnglishWhenLangIsNullOrEmpty()
    {
        var dir = MakeLocalesDir("en", new JsonObject { ["greeting"] = "Hello" });
        try
        {
            var i18n = new I18n(dir);
            Assert.Equal("Hello", i18n.Translate(null, "greeting"));
            Assert.Equal("Hello", i18n.Translate("", "greeting"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Translate_MissingLocaleFileFallsBackToKey()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"flightops_test_i18n_missing_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var i18n = new I18n(dir);
            Assert.Equal("greeting", i18n.Translate("XX", "greeting"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
