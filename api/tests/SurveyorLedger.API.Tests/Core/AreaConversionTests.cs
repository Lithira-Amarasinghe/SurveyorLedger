using SurveyorLedger.Core;
using Xunit;

namespace SurveyorLedger.API.Tests.Core;

public class AreaConversionTests
{
    [Fact]
    public void FromAcresRoodsPerches_TwoAcres_ReturnsExpectedSquareMeters()
    {
        var result = AreaConversion.FromAcresRoodsPerches(2, 0, 0);
        Assert.Equal(8093.7128448m, result);
    }

    [Fact]
    public void FromAcresRoodsPerches_OneRood_ReturnsExpectedSquareMeters()
    {
        var result = AreaConversion.FromAcresRoodsPerches(0, 1, 0);
        Assert.Equal(1011.7141056m, result);
    }

    [Fact]
    public void FromAcresRoodsPerches_OnePerch_ReturnsExpectedSquareMeters()
    {
        var result = AreaConversion.FromAcresRoodsPerches(0, 0, 1);
        Assert.Equal(25.29285264m, result);
    }

    [Fact]
    public void ToAcresRoodsPerches_RoundTripsFromAcresRoodsPerches()
    {
        var squareMeters = AreaConversion.FromAcresRoodsPerches(3, 2, 15.5m);
        var (acres, roods, perches) = AreaConversion.ToAcresRoodsPerches(squareMeters);

        Assert.Equal(3, acres);
        Assert.Equal(2, roods);
        Assert.Equal(15.5m, perches);
    }

    [Fact]
    public void ToAcresRoodsPerches_ZeroSquareMeters_ReturnsAllZero()
    {
        var (acres, roods, perches) = AreaConversion.ToAcresRoodsPerches(0m);

        Assert.Equal(0, acres);
        Assert.Equal(0, roods);
        Assert.Equal(0m, perches);
    }

    [Fact]
    public void ToAcresRoodsPerches_ExactlyOneAcre_RollsOverCorrectly()
    {
        var (acres, roods, perches) = AreaConversion.ToAcresRoodsPerches(4046.8564224m);

        Assert.Equal(1, acres);
        Assert.Equal(0, roods);
        Assert.Equal(0m, perches);
    }
}
