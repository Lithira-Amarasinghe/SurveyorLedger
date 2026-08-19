namespace SurveyorLedger.Data.Entities;

public class Land
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public LandAddress Address { get; set; } = new();
    public decimal? AreaSquareMeters { get; set; }
    /// <summary>Add-a-point link - unauthenticated, add-only (see LandLocationLinkController).</summary>
    public string? LocationShareToken { get; set; }
    /// <summary>View-map link - unauthenticated, read-only (see LandMapViewLinkController). Independent of LocationShareToken, revoked/regenerated separately.</summary>
    public string? MapViewShareToken { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Owner reference - either OwnerId (an existing account, any workspace or none) or
    /// the plain OwnerName/Phone/Email fields, never both. Decoupled from workspace
    /// membership entirely: OwnerId just needs a Person row to exist, not access.
    /// </summary>
    public Guid? OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public string? OwnerPhone { get; set; }
    public string? OwnerEmail { get; set; }

    public Workspace Workspace { get; set; }
    public Person? Owner { get; set; }
    public ICollection<LandSurvey> Surveys { get; set; } = new List<LandSurvey>();
    public ICollection<LandDeed> Deeds { get; set; } = new List<LandDeed>();
    public ICollection<LandBoundary> Boundaries { get; set; } = new List<LandBoundary>();
    public ICollection<LandMapPoint> MapPoints { get; set; } = new List<LandMapPoint>();
}
