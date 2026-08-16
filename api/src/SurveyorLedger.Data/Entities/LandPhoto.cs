namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A site photo attached to a Land record - deliberately separate from Document
/// (which is Job-scoped and carries Category/Visibility concepts this doesn't need).
/// Hard delete on removal, same reasoning as LandSurvey/LandDeed/LandBoundary: corrects
/// a mis-uploaded photo, not meaningful history to preserve once wrong.
/// </summary>
public class LandPhoto
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Land Land { get; set; } = null!;
    public Person UploadedByUser { get; set; } = null!;
}
