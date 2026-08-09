using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Request model for creating or updating a Land record.
/// </summary>
public class LandRequest
{
    public AddressDto? Address { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Size must be zero or greater.")]
    public decimal? Size { get; set; }

    [StringLength(20)]
    public string? SizeUnit { get; set; }

    [StringLength(100)]
    public string? GpsCoordinates { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}
