using SurveyorLedger.Core;

namespace SurveyorLedger.Data.Entities;

/// <summary>
/// Land counterpart to DocumentRequest - a staff ask for a specific document on a Land,
/// a Survey, or a Deed (e.g. "Deed copy", "Survey plan page 2"). Scoped down from the Job
/// version: role-only targeting (no per-person targeting), since Land has no per-record
/// participant list the way Job does. Fulfilling one uploads through
/// IDocumentService.UploadOwnedDocumentForFulfillmentAsync using OwnerType/OwnerId below -
/// the same generic Document infra survey/deed attachments and general land docs all use.
/// </summary>
public class LandDocumentRequest
{
    public Guid Id { get; set; }
    /// <summary>Which Land this request belongs to, for access-check/listing purposes - always the Land itself, even when OwnerType targets a Survey/Deed under it.</summary>
    public Guid LandId { get; set; }
    /// <summary>"Land" (general documents), "LandSurvey", or "LandDeed" - same values Document.OwnerType uses.</summary>
    public string OwnerType { get; set; } = "Land";
    /// <summary>The land itself when OwnerType="Land", or the survey/deed id otherwise.</summary>
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public string Status { get; set; } = "Pending";
    /// <summary>Every Document with this UploadBatchId is a file this request was fulfilled with - set on first fulfillment, reused (not replaced) on every re-fulfillment after a Reopen, so old and new files accumulate in one group instead of the old file being replaced.</summary>
    public Guid? FulfilledBatchId { get; set; }
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
    public Person RequestedByUser { get; set; } = null!;
    public Person? FulfilledByUser { get; set; }
}
