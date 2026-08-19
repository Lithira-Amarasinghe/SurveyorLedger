using SurveyorLedger.Core;

namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A file attached to a Job, or to a Land sub-resource (LandSurvey/LandDeed) via
/// OwnerType/OwnerId - one shared table/upload/list/download/delete pipeline for every
/// owner kind, extensible to future owners with zero schema change (just a new
/// OwnerType value). JobId stays a dedicated, DB-enforced FK for the original Job-document
/// flow (unchanged behavior, cascade delete still works); OwnerType/OwnerId is a weakly-typed
/// link with no DB-level referential integrity - the owning service is responsible for
/// cleaning up Document rows when it deletes an owner (LandService does this for
/// LandSurvey/LandDeed, same as it already does for LandPhoto's file storage cleanup).
/// Tenant isolation for Job documents is transitive through JobId -> Job.WorkspaceId; for
/// Land-owned documents it's enforced by the caller resolving/checking the parent Land
/// before calling into DocumentService (see LandController's survey/deed document routes).
/// Visibility gates whether the Client role can see a Job document - Land-owned documents
/// don't use it (no client-hiding concept there), defaulted to ClientVisible.
/// </summary>
public class Document
{
    public Guid Id { get; set; }
    public Guid? JobId { get; set; }

    /// <summary>"LandSurvey" or "LandDeed" today; null for Job documents.</summary>
    public string? OwnerType { get; set; }
    public Guid? OwnerId { get; set; }

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

    public Job? Job { get; set; }
    public Person UploadedByUser { get; set; }
}
