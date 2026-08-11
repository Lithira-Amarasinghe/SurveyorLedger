using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestResponse
{
    public Guid RequestId { get; set; }
    public Guid JobId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public required string Status { get; set; }
    public Guid? FulfilledDocumentId { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public Guid? FulfilledBy { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
