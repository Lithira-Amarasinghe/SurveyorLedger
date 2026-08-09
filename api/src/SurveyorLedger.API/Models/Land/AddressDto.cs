using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Shared address shape for request/response bodies - mirrors the Address owned type.
/// </summary>
public class AddressDto
{
    [StringLength(255)]
    public string? Street { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? District { get; set; }

    [StringLength(20)]
    public string? PostalCode { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }
}
