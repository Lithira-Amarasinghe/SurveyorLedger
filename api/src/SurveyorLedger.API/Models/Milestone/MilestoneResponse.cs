namespace SurveyorLedger.API.Models.Milestone;

public class MilestoneResponse
{
    public Guid MilestoneId { get; set; }
    public Guid JobId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? Amount { get; set; }
    public required string Status { get; set; }
    public int SortOrder { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
