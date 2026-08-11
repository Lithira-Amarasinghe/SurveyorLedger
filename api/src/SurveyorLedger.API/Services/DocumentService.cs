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
    Task<Document> UploadAsync(Guid workspaceId, Guid callerUserId, Guid jobId, IFormFile file, DocumentCategory category, DocumentVisibility visibility);
    Task<(Document Document, Stream Content)> GetFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId);
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
    private readonly ICasbinService _casbinService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(ApplicationDbContext context, ICasbinService casbinService, IFileStorageService fileStorageService, ILogger<DocumentService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task<List<Document>> GetDocumentsAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        var callerRole = await GetCallerRoleAsync(callerUserId, workspaceId);

        var documents = await _context.Documents
            .Where(d => d.JobId == jobId && d.IsActive)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return documents.Where(d => IsVisible(d, callerRole)).ToList();
    }

    public async Task<Document> UploadAsync(Guid workspaceId, Guid callerUserId, Guid jobId, IFormFile file, DocumentCategory category, DocumentVisibility visibility)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedExtensions)}.");
        if (file.Length > MaxFileSizeBytes)
            throw new ValidationException("File exceeds the 25 MB size limit.");

        var storedRelativePath = $"{workspaceId}/{jobId}/{Guid.NewGuid():N}_{file.FileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _fileStorageService.SaveAsync(stream, storedRelativePath, CancellationToken.None);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            FileName = file.FileName,
            StoredPath = storedRelativePath,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            Category = category,
            Visibility = visibility,
            UploadedBy = callerUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Documents.AddAsync(document);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Document {DocumentId} uploaded for job {JobId} by {UserId}", document.Id, jobId, callerUserId);
        return document;
    }

    public async Task<(Document Document, Stream Content)> GetFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        var callerRole = await GetCallerRoleAsync(callerUserId, workspaceId);

        var document = await FindDocumentAsync(jobId, documentId);
        if (!IsVisible(document, callerRole))
            throw new NotFoundException("Document not found");

        var content = await _fileStorageService.OpenAsync(document.StoredPath, CancellationToken.None);
        return (document, content);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var document = await FindDocumentAsync(jobId, documentId);
        document.IsActive = false;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private static bool IsVisible(Document document, string callerRole) =>
        callerRole != Constants.SystemRoles.Client || document.Visibility == DocumentVisibility.ClientVisible;

    private async Task<string> GetCallerRoleAsync(Guid callerUserId, Guid workspaceId)
    {
        var role = await _context.UserAccesses
            .Where(ua => ua.UserId == callerUserId && ua.IsActive &&
                         ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Select(ua => ua.Role.Name)
            .FirstOrDefaultAsync();

        return role ?? throw new ForbiddenException("You are not a member of this workspace.");
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<Document> FindDocumentAsync(Guid jobId, Guid documentId)
    {
        return await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.JobId == jobId && d.IsActive)
            ?? throw new NotFoundException("Document not found");
    }

    private Task<bool> HasFullJobAccessAsync(Guid callerUserId, Guid workspaceId) =>
        _casbinService.EnforceAsync(callerUserId.ToString(), "job", "view_all", workspaceId.ToString());

    private Task<bool> IsAssignedToJobAsync(Guid callerUserId, Guid jobId) =>
        _context.UserAccesses.AnyAsync(ua =>
            ua.UserId == callerUserId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId);

    private async Task EnsureJobAccessAsync(Guid callerUserId, Guid workspaceId, Guid jobId, string action)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "job", action, workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException($"You do not have permission to {action} documents in this workspace.");

        if (await HasFullJobAccessAsync(callerUserId, workspaceId))
            return;
        if (!await IsAssignedToJobAsync(callerUserId, jobId))
            throw new ForbiddenException($"You do not have permission to {action} documents on this job.");
    }
}
