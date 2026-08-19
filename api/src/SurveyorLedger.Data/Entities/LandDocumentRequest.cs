using SurveyorLedger.Core;

namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Land counterpart to DocumentRequest - a staff ask for a specific document on a Land
/// (e.g. "Deed copy"). Scoped down from the Job version: role-only targeting (no
/// per-person targeting), since Land has no per-record participant list the way Job does.
/// Fulfilling one uploads through IDocumentService.UploadOwnedDocumentAsync with
/// OwnerType="Land", the same generic Document infra the survey/deed attachments use.
/// </summary>
public class LandDocumentRequest
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? FulfilledDocumentId { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public Guid? FulfilledBy { get; set; }
    public Guid RequestedBy { get; set; }
    public string? TargetRole { get; set; }
    public string? ShareToken { get; set; }
    public DateTime? ShareTokenExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Land Land { get; set; } = null!;
    public Document? FulfilledDocument { get; set; }
    public Person RequestedByUser { get; set; } = null!;
    public Person? FulfilledByUser { get; set; }
}
