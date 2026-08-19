using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.LandDocumentRequest;

public class LandDocumentRequestResponse
{
    public Guid RequestId { get; set; }
    public Guid LandId { get; set; }
    public required string OwnerType { get; set; }
    public Guid OwnerId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public string? TargetRole { get; set; }
    public bool HasActiveShareLink { get; set; }
    public required string Status { get; set; }
    public Guid? FulfilledBatchId { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public Guid? FulfilledBy { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
