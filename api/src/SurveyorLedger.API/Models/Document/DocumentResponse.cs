using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.Document;

public class DocumentResponse
{
    public Guid DocumentId { get; set; }
    public Guid JobId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DocumentCategory Category { get; set; }
    public DocumentVisibility Visibility { get; set; }
    public Guid UploadedBy { get; set; }
    public required string UploadedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
