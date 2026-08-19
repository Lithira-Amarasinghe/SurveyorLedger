namespace SurveyorLedger.API.Models.Land;

/// <summary>Response for a Document attached to a LandSurvey/LandDeed via Document.OwnerType/OwnerId.</summary>
public class OwnedDocumentResponse
{
    public Guid DocumentId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public Guid UploadedBy { get; set; }
    public required string UploadedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}
