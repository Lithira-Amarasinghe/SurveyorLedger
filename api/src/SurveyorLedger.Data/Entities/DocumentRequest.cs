using SurveyorLedger.Core;

namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A staff ask for a specific document on a Job (e.g. "Legal Deed"). Fulfilling one
/// uploads a Document through the existing DocumentService and links it here - the
/// Document entity itself has no knowledge of requests, this is a one-directional link.
/// </summary>
public class DocumentRequest
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? FulfilledDocumentId { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public Guid? FulfilledBy { get; set; }
    public Guid RequestedBy { get; set; }
    public string? TargetRole { get; set; }
    public Guid? TargetUserId { get; set; }
    public string? ShareToken { get; set; }
    public DateTime? ShareTokenExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Job Job { get; set; }
    public Document? FulfilledDocument { get; set; }
    public User RequestedByUser { get; set; }
    public User? FulfilledByUser { get; set; }
    public User? TargetUser { get; set; }
}
