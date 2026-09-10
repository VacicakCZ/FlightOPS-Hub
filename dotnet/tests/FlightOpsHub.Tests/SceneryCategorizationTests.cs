using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class SceneryCategorizationTests
{
    [Fact]
    public void FindIcaoCode_FromFolderName_SkipsDeveloperPrefix()
    {
        // The first "-"-separated segment is the developer slug and must
        // be skipped, or a developer code like "ORBX"/"FSDG" could be
        // mistaken for a real ICAO prefix.
        Assert.Equal("EDDF", SceneryCategorization.FindIcaoCode("orbx-airport-eddf-frankfurt", ""));
    }

    [Fact]
    public void FindIcaoCode_FromTitle_RequiresUppercaseToken()
    {
        Assert.Equal("KJFK", SceneryCategorization.FindIcaoCode("some-folder", "Welcome to KJFK New York"));
        Assert.Null(SceneryCategorization.FindIcaoCode("some-folder", "Welcome to Jfk New York"));
    }

    [Fact]
    public void FindIcaoCode_ReturnsNullWhenNothingRecognizable()
    {
        Assert.Null(SceneryCategorization.FindIcaoCode("some-random-package", "Just A Title"));
    }

    [Fact]
    public void CategorizeScenery_UsesIcaoPrefixWhenAvailable()
    {
        var (country, continent) = SceneryCategorization.CategorizeScenery("fsdg-airport-lkpr-prague", "");
        Assert.Equal("Czech Republic", country);
        Assert.Equal("europe", continent);
    }

    [Fact]
    public void CategorizeScenery_FallsBackToKeywordMatch()
    {
        var (country, continent) = SceneryCategorization.CategorizeScenery("some-generic-package", "Tokyo Japan City Scenery");
        Assert.Equal("Japan", country);
        Assert.Equal("asia", continent);
    }

    [Fact]
    public void CategorizeScenery_UnknownDefaultsToOther()
    {
        var (country, continent) = SceneryCategorization.CategorizeScenery("totally-unrecognizable", "Nothing Here");
        Assert.Null(country);
        Assert.Equal("other", continent);
    }
}
