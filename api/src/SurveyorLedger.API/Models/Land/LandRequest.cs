using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Request model for creating or updating a Land record.
/// </summary>
public class LandRequest
{
    public LandAddressDto? Address { get; set; }

    public AreaDto? Area { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    /// <summary>Existing account as owner. Mutually exclusive with OwnerName - set exactly one, or neither.</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>Plain-text owner name for someone with no account. Mutually exclusive with OwnerId.</summary>
    [StringLength(200)]
    public string? OwnerName { get; set; }

    [StringLength(30)]
    public string? OwnerPhone { get; set; }

    [StringLength(256)]
    [EmailAddress(ErrorMessage = "OwnerEmail must be a valid email address.")]
    public string? OwnerEmail { get; set; }
}
