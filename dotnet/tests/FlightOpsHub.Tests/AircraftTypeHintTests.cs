using FlightOpsHub.Core;
using Xunit;

namespace FlightOpsHub.Tests;

public class AircraftTypeHintTests
{
    [Theory]
    [InlineData("PMDG 737-800", "737-800")]
    [InlineData("FlyByWire A32NX (A320neo)", "A320neo")]
    [InlineData("Fenix A319 & A321", "A319")]
    [InlineData("Aerosoft CRJ 900", "CRJ 900")]
    [InlineData("Cessna 172 Skyhawk", "Cessna 172")]
    public void ExtractTypeHint_FindsKnownFamilies(string displayName, string expected)
    {
        Assert.Equal(expected, AircraftTypeHint.ExtractTypeHint(displayName));
    }

    [Fact]
    public void ExtractTypeHint_ReturnsNullForUnrecognizedName()
    {
        Assert.Null(AircraftTypeHint.ExtractTypeHint("Some Random Livery Pack"));
    }

    [Fact]
    public void ExtractTypeHint_ReturnsNullForEmptyOrNull()
    {
        Assert.Null(AircraftTypeHint.ExtractTypeHint(""));
        Assert.Null(AircraftTypeHint.ExtractTypeHint(null));
    }
}
