namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Response model for Land endpoints.
/// </summary>
public class LandResponse
{
    public Guid LandId { get; set; }
    public AddressDto Address { get; set; } = new();
    public decimal? Size { get; set; }
    public string? SizeUnit { get; set; }
    public string? GpsCoordinates { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Set when the owner is an existing account. Null when OwnerName is used instead.</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>Display name - the account's name when OwnerId is set, else the plain OwnerName.</summary>
    public string? OwnerName { get; set; }
    public string? OwnerPhone { get; set; }
    public string? OwnerEmail { get; set; }
}
