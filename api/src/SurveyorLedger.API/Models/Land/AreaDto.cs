using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Write: populate exactly one unit system (Acres/Roods/Perches, or SquareMeters, or
/// Hectares) - LandService rejects more than one. Read: every field is always populated,
/// computed server-side from the one stored canonical value.
/// </summary>
public class AreaDto
{
    [Range(0, 100000, ErrorMessage = "Acres must be between 0 and 100000.")]
    public int? Acres { get; set; }

    [Range(0, 3, ErrorMessage = "Roods must be between 0 and 3.")]
    public int? Roods { get; set; }

    [Range(0, 39.99, ErrorMessage = "Perches must be between 0 and 39.99.")]
    public decimal? Perches { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "SquareMeters must be zero or greater.")]
    public decimal? SquareMeters { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Hectares must be zero or greater.")]
    public decimal? Hectares { get; set; }
}
