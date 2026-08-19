using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IDocumentService
{
    Task<List<Document>> GetDocumentsAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<Document> UploadAsync(Guid workspaceId, Guid callerUserId, Guid jobId, IFormFile file, DocumentCategory category, DocumentVisibility visibility, string? displayFileName = null);
    Task<(Document Document, Stream Content)> GetFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId);
    Task<Document> UpdateVisibilityAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId, DocumentVisibility visibility);

    /// <summary>ownerType is "LandSurvey" or "LandDeed"; landId gates the permission check (EnsureLandAccessAsync), ownerId is the survey/deed's own id.</summary>
    Task<List<Document>> GetOwnedDocumentsAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId);
    Task<Document> UploadOwnedDocumentAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, DocumentCategory category, IFormFile file, string? displayFileName = null);
    /// <summary>land.view-gated variant for LandDocumentRequestService.FulfillAsync - see implementation doc comment.</summary>
    Task<Document> UploadOwnedDocumentForFulfillmentAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, DocumentCategory category, IFormFile file, string? displayFileName = null);
    Task<(Document Document, Stream Content)> GetOwnedDocumentFileAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, Guid documentId);
    Task DeleteOwnedDocumentAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, Guid documentId);
    Task<Document> RenameOwnedDocumentAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, Guid documentId, string fileName);

    /// <summary>Hard-deletes every Document row (and its stored file) for one owner - called by LandService when a LandSurvey/LandDeed itself is deleted, so attachments don't outlive their owner.</summary>
    Task DeleteAllForOwnerAsync(string ownerType, Guid ownerId);
}

/// <summary>
/// Documents are a job sub-resource, same reasoning as MilestoneService: reuse job.view /
/// job.edit Casbin permissions and the job-assignment scoping rule instead of a new
/// permission set. job.view covers list/upload/download (Client has it - that's how they
/// see the job at all), job.edit covers delete (Client never holds job.edit). The caller's
/// role (needed only for the Internal/ClientVisible filter) is resolved from
/// UserAccess.Role.Name at workspace scope, the same way WorkspaceService does it - there
/// is no role claim on the JWT.
/// </summary>
public class DocumentService : IDocumentService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png" };

    /// <summary>Also referenced by DocumentController's [RequestSizeLimit] - single source for the cap.</summary>
    public const long MaxFileSizeBytes = 25 * 1024 * 1024;

    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(ApplicationDbContext context, IScopedAccessService access, IFileStorageService fileStorageService, ILogger<DocumentService> logger)
    {
        _context = context;
        _access = access;
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task<List<Document>> GetDocumentsAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        var callerRoles = await _access.GetEffectiveJobRolesAsync(callerUserId, workspaceId, jobId);

        var documents = await _context.Documents
            .Include(d => d.UploadedByUser)
            .Where(d => d.JobId == jobId && d.IsActive)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return documents.Where(d => IsVisible(d, callerRoles)).ToList();
    }

    public async Task<Document> UploadAsync(Guid workspaceId, Guid callerUserId, Guid jobId, IFormFile file, DocumentCategory category, DocumentVisibility visibility, string? displayFileName = null)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedExtensions)}.");
        if (file.Length > MaxFileSizeBytes)
            throw new ValidationException("File exceeds the 25 MB size limit.");

        // Rename keeps the original extension regardless of what the caller typed - the
        // stored/served ContentType is derived from the real file, not the display name.
        var fileName = string.IsNullOrWhiteSpace(displayFileName) ? file.FileName : displayFileName.Trim();
        if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            fileName += extension;

        var storedRelativePath = $"{workspaceId}/{jobId}/{Guid.NewGuid():N}_{fileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _fileStorageService.SaveAsync(stream, storedRelativePath, CancellationToken.None);
        }

        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            FileName = fileName,
            StoredPath = storedRelativePath,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            Category = category,
            Visibility = visibility,
            UploadedBy = callerPersonId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Documents.AddAsync(document);
        await _context.SaveChangesAsync();

        // UploadedBy == callerPersonId always here - fetch once so the caller (ToResponse)
        // can render the uploader's name without a second round trip through this service.
        document.UploadedByUser = await _context.People.FindAsync(callerPersonId)
            ?? throw new NotFoundException("Uploading person not found");

        _logger.LogInformation("Document {DocumentId} uploaded for job {JobId} by {UserId}", document.Id, jobId, callerUserId);
        return document;
    }

    public async Task<(Document Document, Stream Content)> GetFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        var callerRoles = await _access.GetEffectiveJobRolesAsync(callerUserId, workspaceId, jobId);

        var document = await FindDocumentAsync(jobId, documentId);
        if (!IsVisible(document, callerRoles))
            throw new NotFoundException("Document not found");

        var content = await _fileStorageService.OpenAsync(document.StoredPath, CancellationToken.None);
        return (document, content);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var document = await FindDocumentAsync(jobId, documentId);
        document.IsActive = false;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<Document> UpdateVisibilityAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId, DocumentVisibility visibility)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var document = await FindDocumentAsync(jobId, documentId);
        document.Visibility = visibility;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return document;
    }

    public async Task<List<Document>> GetOwnedDocumentsAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");
        await EnsureOwnerBelongsToLandAsync(ownerType, ownerId, landId);

        return await _context.Documents.Include(d => d.UploadedByUser)
            .Where(d => d.OwnerType == ownerType && d.OwnerId == ownerId && d.IsActive)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public Task<Document> UploadOwnedDocumentAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, DocumentCategory category, IFormFile file, string? displayFileName = null) =>
        UploadOwnedDocumentCoreAsync(workspaceId, callerUserId, landId, ownerType, ownerId, category, file, "edit", displayFileName);

    /// <summary>
    /// Same upload, gated by land.view instead of land.edit - used only by
    /// LandDocumentRequestService.FulfillAsync, whose own targeting check already decided
    /// whether this caller may fulfill. A Client fulfilling their own request never holds
    /// land.edit (same asymmetry DocumentService.UploadAsync has for Job: job.view covers
    /// upload/fulfill, job.edit covers delete/manage).
    /// </summary>
    public Task<Document> UploadOwnedDocumentForFulfillmentAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, DocumentCategory category, IFormFile file, string? displayFileName = null) =>
        UploadOwnedDocumentCoreAsync(workspaceId, callerUserId, landId, ownerType, ownerId, category, file, "view", displayFileName);

    private async Task<Document> UploadOwnedDocumentCoreAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, DocumentCategory category, IFormFile file, string requiredAction, string? displayFileName)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, requiredAction);
        await EnsureOwnerBelongsToLandAsync(ownerType, ownerId, landId);

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedExtensions)}.");
        if (file.Length > MaxFileSizeBytes)
            throw new ValidationException("File exceeds the 25 MB size limit.");

        var fileName = string.IsNullOrWhiteSpace(displayFileName) ? file.FileName : displayFileName.Trim();
        if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            fileName += extension;

        var storedRelativePath = $"{workspaceId}/land-{ownerType.ToLowerInvariant()}/{ownerId}/{Guid.NewGuid():N}_{fileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _fileStorageService.SaveAsync(stream, storedRelativePath, CancellationToken.None);
        }

        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            OwnerType = ownerType,
            OwnerId = ownerId,
            FileName = fileName,
            StoredPath = storedRelativePath,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            Category = category,
            // Land-owned documents have no Client-hiding concept the way Job documents do -
            // access is already gated by land.view/land.view_all, not a per-document flag.
            Visibility = DocumentVisibility.ClientVisible,
            UploadedBy = callerPersonId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Documents.AddAsync(document);
        await _context.SaveChangesAsync();

        document.UploadedByUser = await _context.People.FindAsync(callerPersonId)
            ?? throw new NotFoundException("Uploading person not found");

        _logger.LogInformation("Document {DocumentId} uploaded for {OwnerType} {OwnerId} by {UserId}", document.Id, ownerType, ownerId, callerUserId);
        return document;
    }

    public async Task<(Document Document, Stream Content)> GetOwnedDocumentFileAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, Guid documentId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");
        await EnsureOwnerBelongsToLandAsync(ownerType, ownerId, landId);

        var document = await FindOwnedDocumentAsync(ownerType, ownerId, documentId);
        var content = await _fileStorageService.OpenAsync(document.StoredPath, CancellationToken.None);
        return (document, content);
    }

    public async Task DeleteOwnedDocumentAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, Guid documentId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await EnsureOwnerBelongsToLandAsync(ownerType, ownerId, landId);

        var document = await FindOwnedDocumentAsync(ownerType, ownerId, documentId);
        document.IsActive = false;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    /// <summary>Renames the display filename only - StoredPath/extension untouched, same as LandService.RenamePhotoAsync.</summary>
    public async Task<Document> RenameOwnedDocumentAsync(Guid workspaceId, Guid callerUserId, Guid landId, string ownerType, Guid ownerId, Guid documentId, string fileName)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await EnsureOwnerBelongsToLandAsync(ownerType, ownerId, landId);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ValidationException("File name is required.");
        if (fileName.Length > 255)
            throw new ValidationException("File name must be 255 characters or fewer.");

        var document = await FindOwnedDocumentAsync(ownerType, ownerId, documentId);
        document.FileName = fileName.Trim();
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return document;
    }

    /// <summary>Hard delete, not soft - called when the owner record itself (LandSurvey/LandDeed) is being hard-deleted, matching that existing "mis-entered record" reasoning.</summary>
    public async Task DeleteAllForOwnerAsync(string ownerType, Guid ownerId)
    {
        var documents = await _context.Documents.Where(d => d.OwnerType == ownerType && d.OwnerId == ownerId && d.IsActive).ToListAsync();
        foreach (var document in documents)
        {
            await _fileStorageService.DeleteAsync(document.StoredPath, CancellationToken.None);
            _context.Documents.Remove(document);
        }
        await _context.SaveChangesAsync();
    }

    /// <summary>Defense in depth: confirms the caller-supplied landId actually owns this survey/deed, so a mismatched (landId, ownerId) pair can't be used to read/write another land's documents once EnsureLandAccessAsync has passed for the (wrong) landId.</summary>
    private async Task EnsureOwnerBelongsToLandAsync(string ownerType, Guid ownerId, Guid landId)
    {
        var belongs = ownerType switch
        {
            "LandSurvey" => await _context.LandSurveys.AnyAsync(s => s.Id == ownerId && s.LandId == landId),
            "LandDeed" => await _context.LandDeeds.AnyAsync(d => d.Id == ownerId && d.LandId == landId),
            // General/request-driven land documents - the "owner" is the land itself, so ownerId == landId.
            "Land" => ownerId == landId,
            // Site photos - same "owner is the land itself" shape as "Land".
            "LandPhoto" => ownerId == landId,
            _ => throw new ValidationException($"Unknown document owner type '{ownerType}'.")
        };
        if (!belongs)
            throw new NotFoundException($"{ownerType} not found on this land");
    }

    private async Task<Document> FindOwnedDocumentAsync(string ownerType, Guid ownerId, Guid documentId)
    {
        return await _context.Documents.Include(d => d.UploadedByUser)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.OwnerType == ownerType && d.OwnerId == ownerId && d.IsActive)
            ?? throw new NotFoundException("Document not found");
    }

    private static bool IsVisible(Document document, List<string> callerRoles) =>
        !callerRoles.Contains(Constants.SystemRoles.Client) || document.Visibility == DocumentVisibility.ClientVisible;

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<Document> FindDocumentAsync(Guid jobId, Guid documentId)
    {
        return await _context.Documents.Include(d => d.UploadedByUser)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.JobId == jobId && d.IsActive)
            ?? throw new NotFoundException("Document not found");
    }

}
