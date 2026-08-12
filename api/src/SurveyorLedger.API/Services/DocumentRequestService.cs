using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IDocumentRequestService
{
    Task<List<DocumentRequest>> GetForJobAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<DocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string title, string? description, DocumentCategory category, string? targetRole = null, Guid? targetUserId = null);
    Task<DocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, IFormFile file, DocumentVisibility visibility, string? displayFileName = null);
    Task<DocumentRequest> ReopenAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, string? note = null);
    Task CancelAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
    Task<DocumentRequest> UpdateTargetAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, string? targetRole, Guid? targetUserId);
}

/// <summary>
/// Same job-scoped RBAC reuse as DocumentService/MilestoneService - job.view for
/// list/fulfill (Client fulfills their own), job.edit for create/reopen/cancel
/// (Admin/Surveyor only). FulfillAsync delegates the actual file handling to
/// IDocumentService.UploadAsync rather than duplicating validation/storage.
/// </summary>
public class DocumentRequestService : IDocumentRequestService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IDocumentService _documentService;

    public DocumentRequestService(ApplicationDbContext context, IScopedAccessService access, IDocumentService documentService)
    {
        _context = context;
        _access = access;
        _documentService = documentService;
    }

    public async Task<List<DocumentRequest>> GetForJobAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        return await _context.DocumentRequests
            .Include(r => r.TargetUser)
            .Where(r => r.JobId == jobId && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<DocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string title, string? description, DocumentCategory category, string? targetRole = null, Guid? targetUserId = null)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        if (string.IsNullOrWhiteSpace(title))
            throw new ValidationException("Title is required.");

        await ValidateTargetAsync(jobId, targetRole, targetUserId);

        var request = new DocumentRequest
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Title = title.Trim(),
            Description = description,
            Category = category,
            Status = "Pending",
            TargetRole = targetRole,
            TargetUserId = targetUserId,
            RequestedBy = callerUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.DocumentRequests.AddAsync(request);
        await _context.SaveChangesAsync();
        return request;
    }

    public async Task<DocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, IFormFile file, DocumentVisibility visibility, string? displayFileName = null)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        var request = await FindRequestAsync(jobId, requestId);

        if (request.TargetUserId.HasValue && request.TargetUserId != callerUserId)
            throw new ForbiddenException("This request is for a specific person.");

        if (request.TargetRole != null)
        {
            var callerRole = await _access.GetEffectiveJobRoleAsync(callerUserId, workspaceId, jobId);
            if (callerRole != request.TargetRole)
                throw new ForbiddenException($"This request is for the {request.TargetRole} role.");
        }

        // Reopening keeps the previous FulfilledDocumentId as a reference (not cleared) so
        // the old file and the "via request" link stay visible until a replacement lands.
        // No versioning support: once a replacement is uploaded, the old document is
        // superseded and soft-deleted here rather than kept alongside it.
        var previousDocumentId = request.FulfilledDocumentId;

        var document = await _documentService.UploadAsync(workspaceId, callerUserId, jobId, file, request.Category, visibility, displayFileName);

        request.FulfilledDocumentId = document.Id;
        request.FulfilledAt = DateTime.UtcNow;
        request.FulfilledBy = callerUserId;
        request.Status = "Fulfilled";
        request.UpdatedAt = DateTime.UtcNow;

        if (previousDocumentId.HasValue)
        {
            // Not IDocumentService.DeleteAsync: that requires job.edit, but a Client
            // fulfilling their own reopened request must be able to trigger this - the
            // job.view gate already checked above is what actually authorizes this action.
            var previousDocument = await _context.Documents.FindAsync(previousDocumentId.Value);
            if (previousDocument != null)
            {
                previousDocument.IsActive = false;
                previousDocument.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        return request;
    }

    public async Task<DocumentRequest> ReopenAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, string? note = null)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var request = await FindRequestAsync(jobId, requestId);
        // FulfilledDocumentId/At/By stay as-is - the previous document and its "via
        // request" link remain visible until a replacement is uploaded (see FulfillAsync).
        request.Status = "Reopened";
        if (note != null)
            request.Description = note;
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return request;
    }

    public async Task CancelAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var request = await FindRequestAsync(jobId, requestId);
        request.IsActive = false;
        request.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<DocumentRequest> UpdateTargetAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, string? targetRole, Guid? targetUserId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var request = await FindRequestAsync(jobId, requestId);
        if (request.Status == "Fulfilled")
            throw new ValidationException("Cannot change the target of a fulfilled request. Reopen it first.");

        await ValidateTargetAsync(jobId, targetRole, targetUserId);

        request.TargetRole = targetRole;
        request.TargetUserId = targetUserId;
        request.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return request;
    }

    /// <summary>Shared by CreateAsync and UpdateTargetAsync - one place for the three targeting rules.</summary>
    private async Task ValidateTargetAsync(Guid jobId, string? targetRole, Guid? targetUserId)
    {
        if (targetRole != null && targetUserId.HasValue)
            throw new ValidationException("A request can target a role or a person, not both.");

        if (targetRole != null && targetRole != Constants.SystemRoles.Admin && targetRole != Constants.SystemRoles.Surveyor && targetRole != Constants.SystemRoles.Client)
            throw new ValidationException($"Unknown target role '{targetRole}'.");

        if (targetUserId.HasValue && !await _access.AccessibleJobIds(targetUserId.Value).AnyAsync(id => id == jobId))
            throw new ValidationException("The targeted person is not assigned to this job.");
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<DocumentRequest> FindRequestAsync(Guid jobId, Guid requestId)
    {
        return await _context.DocumentRequests.Include(r => r.TargetUser)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.JobId == jobId && r.IsActive)
            ?? throw new NotFoundException("Document request not found");
    }

}
