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
