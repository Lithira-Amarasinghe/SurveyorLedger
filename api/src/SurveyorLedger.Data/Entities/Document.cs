using SurveyorLedger.Core;

namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A file attached to a Job (survey plan, legal document, photo, etc). Tenant isolation
/// is transitive through JobId -> Job.WorkspaceId, same as Milestone. Visibility gates
/// whether the Client role can see it - Internal documents are Admin/Surveyor only.
/// </summary>
public class Document
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string FileName { get; set; }
    public string StoredPath { get; set; }
    public string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DocumentCategory Category { get; set; }
    public DocumentVisibility Visibility { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Job Job { get; set; }
    public Person UploadedByUser { get; set; }
}
