using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.LandDocumentRequest;

public class LandDocumentRequestLinkPreviewResponse
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DocumentCategory? Category { get; set; }
    public string? WorkspaceName { get; set; }
    public string? LandAddressLine { get; set; }
    public required bool Expired { get; set; }
    public required bool AlreadyFulfilled { get; set; }
}
