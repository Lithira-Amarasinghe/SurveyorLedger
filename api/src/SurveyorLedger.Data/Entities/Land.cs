namespace SurveyorLedger.Data.Entities;

public class Land
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Address Address { get; set; } = new();
    public decimal? Size { get; set; }
    public string? SizeUnit { get; set; }
    public string? GpsCoordinates { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Owner reference - either OwnerId (an existing account, any workspace or none) or
    /// the plain OwnerName/Phone/Email fields, never both. Decoupled from workspace
    /// membership entirely: OwnerId just needs a User row to exist, not access.
    /// </summary>
    public Guid? OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public string? OwnerPhone { get; set; }
    public string? OwnerEmail { get; set; }

    public Workspace Workspace { get; set; }
    public User? Owner { get; set; }
    public ICollection<LandSurvey> Surveys { get; set; } = new List<LandSurvey>();
    public ICollection<LandDeed> Deeds { get; set; } = new List<LandDeed>();
    public ICollection<LandBoundary> Boundaries { get; set; } = new List<LandBoundary>();
}
