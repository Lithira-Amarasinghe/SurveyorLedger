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
    Task<DocumentRequest> GenerateShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
    Task RevokeShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
    Task<DocumentRequest> GetByShareTokenAsync(string token);
    Task<DocumentRequest> UploadViaShareTokenAsync(string token, IFormFile file, string? displayFileName = null);
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

        return await LinkFulfilledDocumentAsync(workspaceId, jobId, request, file, visibility, callerUserId, displayFileName);
    }

    /// <summary>
    /// Shared by FulfillAsync (authenticated) and UploadViaShareTokenAsync (anonymous, via
    /// link) - one implementation of "upload, replace-deletes-previous, link, mark Fulfilled"
    /// regardless of which path got here. attributedUserId is the caller for FulfillAsync,
    /// or the request's RequestedBy for an anonymous link upload (no real caller to attribute to).
    /// </summary>
    private async Task<DocumentRequest> LinkFulfilledDocumentAsync(Guid workspaceId, Guid jobId, DocumentRequest request, IFormFile file, DocumentVisibility visibility, Guid attributedUserId, string? displayFileName)
    {
        // Reopening keeps the previous FulfilledDocumentId as a reference (not cleared) so
        // the old file and the "via request" link stay visible until a replacement lands.
        // No versioning support: once a replacement is uploaded, the old document is
        // superseded and soft-deleted here rather than kept alongside it.
        var previousDocumentId = request.FulfilledDocumentId;

        var document = await _documentService.UploadAsync(workspaceId, attributedUserId, jobId, file, request.Category, visibility, displayFileName);

        request.FulfilledDocumentId = document.Id;
        request.FulfilledAt = DateTime.UtcNow;
        request.FulfilledBy = attributedUserId;
        request.Status = "Fulfilled";
        request.UpdatedAt = DateTime.UtcNow;

        if (previousDocumentId.HasValue)
        {
            // Not IDocumentService.DeleteAsync: that requires job.edit, but a Client (or an
            // anonymous link uploader) fulfilling their own request must be able to trigger
            // this - the access check already done by the caller is what actually authorizes it.
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

    public async Task<DocumentRequest> GenerateShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var request = await FindRequestAsync(jobId, requestId);
        // Overwriting an existing token is deliberate - the old link stops resolving
        // immediately, so "generate again" doubles as instant revoke-and-reissue.
        request.ShareToken = Guid.NewGuid().ToString("N");
        request.ShareTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return request;
    }

    public async Task RevokeShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var request = await FindRequestAsync(jobId, requestId);
        request.ShareToken = null;
        request.ShareTokenExpiresAt = null;
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task<DocumentRequest> GetByShareTokenAsync(string token)
    {
        var request = await _context.DocumentRequests
            .FirstOrDefaultAsync(r => r.ShareToken == token && r.IsActive)
            ?? throw new NotFoundException("Link not found");

        if (request.ShareTokenExpiresAt is null || request.ShareTokenExpiresAt <= DateTime.UtcNow)
            throw new NotFoundException("Link not found");

        return request;
    }

    public async Task<DocumentRequest> UploadViaShareTokenAsync(string token, IFormFile file, string? displayFileName = null)
    {
        var request = await GetByShareTokenAsync(token);
        var job = await _context.Jobs.FirstAsync(j => j.Id == request.JobId);

        if (request.Status == "Fulfilled")
            throw new ValidationException("This document has already been provided.");

        return await LinkFulfilledDocumentAsync(job.WorkspaceId, job.Id, request, file, DocumentVisibility.ClientVisible, request.RequestedBy, displayFileName);
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
