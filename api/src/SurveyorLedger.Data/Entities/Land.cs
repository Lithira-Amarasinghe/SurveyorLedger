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

    public Workspace Workspace { get; set; }
    public ICollection<LandSurvey> Surveys { get; set; } = new List<LandSurvey>();
    public ICollection<LandDeed> Deeds { get; set; } = new List<LandDeed>();
    public ICollection<LandBoundary> Boundaries { get; set; } = new List<LandBoundary>();
}
