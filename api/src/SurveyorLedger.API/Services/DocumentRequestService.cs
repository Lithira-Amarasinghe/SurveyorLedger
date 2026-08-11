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
    Task<DocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string title, string? description, DocumentCategory category);
    Task<DocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, IFormFile file, DocumentVisibility visibility);
    Task<DocumentRequest> ReopenAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
    Task CancelAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
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
    private readonly ICasbinService _casbinService;
    private readonly IDocumentService _documentService;

    public DocumentRequestService(ApplicationDbContext context, ICasbinService casbinService, IDocumentService documentService)
    {
        _context = context;
        _casbinService = casbinService;
        _documentService = documentService;
    }

    public async Task<List<DocumentRequest>> GetForJobAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        return await _context.DocumentRequests
            .Where(r => r.JobId == jobId && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<DocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string title, string? description, DocumentCategory category)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        if (string.IsNullOrWhiteSpace(title))
            throw new ValidationException("Title is required.");

        var request = new DocumentRequest
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Title = title.Trim(),
            Description = description,
            Category = category,
            Status = "Pending",
            RequestedBy = callerUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.DocumentRequests.AddAsync(request);
        await _context.SaveChangesAsync();
        return request;
    }

    public async Task<DocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, IFormFile file, DocumentVisibility visibility)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        var request = await FindRequestAsync(jobId, requestId);

        var document = await _documentService.UploadAsync(workspaceId, callerUserId, jobId, file, request.Category, visibility);

        request.FulfilledDocumentId = document.Id;
        request.FulfilledAt = DateTime.UtcNow;
        request.FulfilledBy = callerUserId;
        request.Status = "Fulfilled";
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return request;
    }

    public async Task<DocumentRequest> ReopenAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var request = await FindRequestAsync(jobId, requestId);
        request.FulfilledDocumentId = null;
        request.FulfilledAt = null;
        request.FulfilledBy = null;
        request.Status = "Pending";
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return request;
    }

    public async Task CancelAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var request = await FindRequestAsync(jobId, requestId);
        request.IsActive = false;
        request.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<DocumentRequest> FindRequestAsync(Guid jobId, Guid requestId)
    {
        return await _context.DocumentRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.JobId == jobId && r.IsActive)
            ?? throw new NotFoundException("Document request not found");
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
            throw new ForbiddenException($"You do not have permission to {action} document requests in this workspace.");

        if (await HasFullJobAccessAsync(callerUserId, workspaceId))
            return;
        if (!await IsAssignedToJobAsync(callerUserId, jobId))
            throw new ForbiddenException($"You do not have permission to {action} document requests on this job.");
    }
}
