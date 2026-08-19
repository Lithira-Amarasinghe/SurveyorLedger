namespace SurveyorLedger.Core;

/// <summary>
/// Single source of area-unit conversion truth. Acres/Roods/Perches (Sri Lankan land
/// survey convention: 1 Acre = 4 Roods = 160 Perches, 1 Rood = 40 Perches) and the metric
/// system both convert through square meters, the canonical stored unit on Land.
/// </summary>
public static class AreaConversion
{
    public const decimal SquareMetersPerPerch = 25.29285264m;
    public const decimal SquareMetersPerRood = SquareMetersPerPerch * 40;
    public const decimal SquareMetersPerAcre = SquareMetersPerRood * 4;
    public const decimal SquareMetersPerHectare = 10000m;

    public static decimal FromAcresRoodsPerches(int acres, int roods, decimal perches) =>
        acres * SquareMetersPerAcre + roods * SquareMetersPerRood + perches * SquareMetersPerPerch;

    public static (int Acres, int Roods, decimal Perches) ToAcresRoodsPerches(decimal squareMeters)
    {
        var totalPerches = squareMeters / SquareMetersPerPerch;
        var acres = (int)Math.Floor(totalPerches / 160);
        var remainder = totalPerches - acres * 160;
        var roods = (int)Math.Floor(remainder / 40);
        var perches = Math.Round(remainder - roods * 40, 2);
        return (acres, roods, perches);
    }
}
