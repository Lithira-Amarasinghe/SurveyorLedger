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
    Task<DocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, List<IFormFile> files, Guid batchId, DocumentVisibility visibility, string? displayFileName = null);
    Task<DocumentRequest> ReopenAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, string? note = null);
    Task CancelAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
    Task<DocumentRequest> UpdateTargetAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, string? targetRole, Guid? targetUserId);
    Task<DocumentRequest> GenerateShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
    Task RevokeShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
    Task<DocumentRequest> GetByShareTokenAsync(string token);
    Task<DocumentRequest> UploadViaShareTokenAsync(string token, List<IFormFile> files, string? displayFileName = null);
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
        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

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
            RequestedBy = callerPersonId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.DocumentRequests.AddAsync(request);
        await _context.SaveChangesAsync();
        return request;
    }

    public async Task<DocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, List<IFormFile> files, Guid batchId, DocumentVisibility visibility, string? displayFileName = null)
    {
        await FindJobAsync(workspaceId, jobId);
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        var request = await FindRequestAsync(jobId, requestId);
        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

        if (request.TargetUserId.HasValue && request.TargetUserId != callerPersonId)
            throw new ForbiddenException("This request is for a specific person.");

        if (request.TargetRole != null)
        {
            var callerRoles = await _access.GetEffectiveJobRolesAsync(callerUserId, workspaceId, jobId);
            if (!callerRoles.Contains(request.TargetRole))
                throw new ForbiddenException($"This request is for the {request.TargetRole} role.");
        }

        return await LinkFulfilledDocumentAsync(workspaceId, jobId, request, files, visibility, callerUserId, callerPersonId, batchId, displayFileName);
    }

    /// <summary>
    /// Shared by FulfillAsync (authenticated) and UploadViaShareTokenAsync (anonymous, via
    /// link) - uploads every file in the batch and links them to the request. No more
    /// "supersede the previous document" branch: with batching, re-fulfilling after a Reopen
    /// reuses the same batchId the caller passes (request.fulfilledBatchId, if already set)
    /// so old and new files accumulate in one group instead of the old one being replaced.
    /// attributedUserAccountId is who IDocumentService checks job access against (a
    /// UserAccount.Id); attributedPersonId is who gets recorded as FulfilledBy (a Person.Id).
    /// For FulfillAsync both derive from the real caller; for an anonymous link upload there
    /// is no caller, so both are derived from the request's original requester instead.
    /// </summary>
    private async Task<DocumentRequest> LinkFulfilledDocumentAsync(Guid workspaceId, Guid jobId, DocumentRequest request, List<IFormFile> files, DocumentVisibility visibility, Guid attributedUserAccountId, Guid attributedPersonId, Guid batchId, string? displayFileName)
    {
        foreach (var file in files)
        {
            await _documentService.UploadAsync(workspaceId, attributedUserAccountId, jobId, file, request.Category, visibility, displayFileName, batchId);
        }

        request.FulfilledBatchId = batchId;
        request.FulfilledAt = DateTime.UtcNow;
        request.FulfilledBy = attributedPersonId;
        request.Status = "Fulfilled";
        request.UpdatedAt = DateTime.UtcNow;

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

    public async Task<DocumentRequest> UploadViaShareTokenAsync(string token, List<IFormFile> files, string? displayFileName = null)
    {
        var request = await GetByShareTokenAsync(token);
        var job = await _context.Jobs.FirstAsync(j => j.Id == request.JobId);

        if (request.Status == "Fulfilled")
            throw new ValidationException("This document has already been provided.");

        // No authenticated caller for a share-link upload - attribute the job-access check
        // and the FulfilledBy record to the original requester (reverse-resolved to their
        // UserAccount.Id for the access check, Person.Id for the record).
        var requesterAccountId = await _context.UserAccounts
            .Where(a => a.PersonId == request.RequestedBy)
            .Select(a => a.Id)
            .FirstOrDefaultAsync();

        var batchId = request.FulfilledBatchId ?? Guid.NewGuid();
        return await LinkFulfilledDocumentAsync(job.WorkspaceId, job.Id, request, files, DocumentVisibility.ClientVisible, requesterAccountId, request.RequestedBy, batchId, displayFileName);
    }

    /// <summary>Shared by CreateAsync and UpdateTargetAsync - one place for the three targeting rules.</summary>
    private async Task ValidateTargetAsync(Guid jobId, string? targetRole, Guid? targetUserId)
    {
        if (targetRole != null && targetUserId.HasValue)
            throw new ValidationException("A request can target a role or a person, not both.");

        if (targetRole != null && targetRole != Constants.SystemRoles.Admin && targetRole != Constants.SystemRoles.Surveyor && targetRole != Constants.SystemRoles.Client)
            throw new ValidationException($"Unknown target role '{targetRole}'.");

        if (targetUserId.HasValue)
        {
            // targetUserId is a Person.Id (from the person picker); AccessibleJobIds is
            // keyed by UserAccount.Id, so reverse-resolve before checking.
            var targetAccountId = await _context.UserAccounts
                .Where(a => a.PersonId == targetUserId.Value)
                .Select(a => a.Id)
                .FirstOrDefaultAsync();

            if (!await _access.AccessibleJobIds(targetAccountId).AnyAsync(id => id == jobId))
                throw new ValidationException("The targeted person is not assigned to this job.");
        }
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
