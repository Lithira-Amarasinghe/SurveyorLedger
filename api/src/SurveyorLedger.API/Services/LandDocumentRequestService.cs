using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface ILandDocumentRequestService
{
    Task<List<LandDocumentRequest>> GetForLandAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<LandDocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid landId, string title, string? description, DocumentCategory category, string? targetRole = null);
    Task<LandDocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId, IFormFile file, string? displayFileName = null);
    Task<LandDocumentRequest> ReopenAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId, string? note = null);
    Task CancelAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId);
    Task<LandDocumentRequest> UpdateTargetAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId, string? targetRole);
    Task<LandDocumentRequest> GenerateShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId);
    Task RevokeShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId);
    Task<LandDocumentRequest> GetByShareTokenAsync(string token);
    Task<LandDocumentRequest> UploadViaShareTokenAsync(string token, IFormFile file, string? displayFileName = null);
}

/// <summary>
/// Land counterpart to DocumentRequestService - same shape, minus per-person targeting
/// (Land has no per-record participant list like Job's assignments). land.view covers
/// list/fulfill (Client fulfills their own), land.edit covers create/reopen/cancel/target
/// (Admin/Surveyor only). Fulfillment delegates to IDocumentService.UploadOwnedDocumentAsync
/// with OwnerType="Land" - the same generic Document infra survey/deed attachments use.
/// </summary>
public class LandDocumentRequestService : ILandDocumentRequestService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IDocumentService _documentService;

    public LandDocumentRequestService(ApplicationDbContext context, IScopedAccessService access, IDocumentService documentService)
    {
        _context = context;
        _access = access;
        _documentService = documentService;
    }

    public async Task<List<LandDocumentRequest>> GetForLandAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await FindLandAsync(workspaceId, landId);
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");

        return await _context.LandDocumentRequests
            .Where(r => r.LandId == landId && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<LandDocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid landId, string title, string? description, DocumentCategory category, string? targetRole = null)
    {
        await FindLandAsync(workspaceId, landId);
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");

        if (string.IsNullOrWhiteSpace(title))
            throw new ValidationException("Title is required.");

        ValidateTargetRole(targetRole);
        var callerPersonId = await _access.ResolvePersonIdAsync(callerUserId);

        var request = new LandDocumentRequest
        {
            Id = Guid.NewGuid(),
            LandId = landId,
            Title = title.Trim(),
            Description = description,
            Category = category,
            Status = "Pending",
            TargetRole = targetRole,
            RequestedBy = callerPersonId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.LandDocumentRequests.AddAsync(request);
        await _context.SaveChangesAsync();
        return request;
    }

    public async Task<LandDocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId, IFormFile file, string? displayFileName = null)
    {
        await FindLandAsync(workspaceId, landId);
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");

        var request = await FindRequestAsync(landId, requestId);

        if (request.TargetRole != null)
        {
            var callerRoles = await GetWorkspaceRolesAsync(callerUserId, workspaceId);
            if (!RoleMatches(request.TargetRole, callerRoles))
                throw new ForbiddenException($"This request is for the {request.TargetRole} role.");
        }

        return await LinkFulfilledDocumentAsync(workspaceId, landId, request, file, callerUserId, displayFileName);
    }

    private async Task<LandDocumentRequest> LinkFulfilledDocumentAsync(Guid workspaceId, Guid landId, LandDocumentRequest request, IFormFile file, Guid attributedUserAccountId, string? displayFileName)
    {
        var attributedPersonId = await _access.ResolvePersonIdAsync(attributedUserAccountId);
        var previousDocumentId = request.FulfilledDocumentId;

        var document = await _documentService.UploadOwnedDocumentForFulfillmentAsync(workspaceId, attributedUserAccountId, landId, "Land", landId, request.Category, file, displayFileName);

        request.FulfilledDocumentId = document.Id;
        request.FulfilledAt = DateTime.UtcNow;
        request.FulfilledBy = attributedPersonId;
        request.Status = "Fulfilled";
        request.UpdatedAt = DateTime.UtcNow;

        if (previousDocumentId.HasValue)
        {
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

    public async Task<LandDocumentRequest> ReopenAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId, string? note = null)
    {
        await FindLandAsync(workspaceId, landId);
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");

        var request = await FindRequestAsync(landId, requestId);
        request.Status = "Reopened";
        if (note != null)
            request.Description = note;
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return request;
    }

    public async Task CancelAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId)
    {
        await FindLandAsync(workspaceId, landId);
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");

        var request = await FindRequestAsync(landId, requestId);
        request.IsActive = false;
        request.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<LandDocumentRequest> UpdateTargetAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId, string? targetRole)
    {
        await FindLandAsync(workspaceId, landId);
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");

        var request = await FindRequestAsync(landId, requestId);
        if (request.Status == "Fulfilled")
            throw new ValidationException("Cannot change the target of a fulfilled request. Reopen it first.");

        ValidateTargetRole(targetRole);

        request.TargetRole = targetRole;
        request.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return request;
    }

    public async Task<LandDocumentRequest> GenerateShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId)
    {
        await FindLandAsync(workspaceId, landId);
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");

        var request = await FindRequestAsync(landId, requestId);
        request.ShareToken = Guid.NewGuid().ToString("N");
        request.ShareTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return request;
    }

    public async Task RevokeShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid requestId)
    {
        await FindLandAsync(workspaceId, landId);
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");

        var request = await FindRequestAsync(landId, requestId);
        request.ShareToken = null;
        request.ShareTokenExpiresAt = null;
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task<LandDocumentRequest> GetByShareTokenAsync(string token)
    {
        var request = await _context.LandDocumentRequests
            .FirstOrDefaultAsync(r => r.ShareToken == token && r.IsActive)
            ?? throw new NotFoundException("Link not found");

        if (request.ShareTokenExpiresAt is null || request.ShareTokenExpiresAt <= DateTime.UtcNow)
            throw new NotFoundException("Link not found");

        return request;
    }

    public async Task<LandDocumentRequest> UploadViaShareTokenAsync(string token, IFormFile file, string? displayFileName = null)
    {
        var request = await GetByShareTokenAsync(token);
        var land = await _context.Lands.FirstAsync(l => l.Id == request.LandId);

        if (request.Status == "Fulfilled")
            throw new ValidationException("This document has already been provided.");

        var requesterAccountId = await _context.UserAccounts
            .Where(a => a.PersonId == request.RequestedBy)
            .Select(a => a.Id)
            .FirstOrDefaultAsync();

        return await LinkFulfilledDocumentAsyncForToken(land.WorkspaceId, land.Id, request, file, requesterAccountId, request.RequestedBy, displayFileName);
    }

    /// <summary>Anonymous-link variant of LinkFulfilledDocumentAsync - there is no authenticated caller, so both the access-check identity and the FulfilledBy record are derived from the request's original requester instead.</summary>
    private async Task<LandDocumentRequest> LinkFulfilledDocumentAsyncForToken(Guid workspaceId, Guid landId, LandDocumentRequest request, IFormFile file, Guid attributedUserAccountId, Guid attributedPersonId, string? displayFileName)
    {
        var previousDocumentId = request.FulfilledDocumentId;

        var document = await _documentService.UploadOwnedDocumentForFulfillmentAsync(workspaceId, attributedUserAccountId, landId, "Land", landId, request.Category, file, displayFileName);

        request.FulfilledDocumentId = document.Id;
        request.FulfilledAt = DateTime.UtcNow;
        request.FulfilledBy = attributedPersonId;
        request.Status = "Fulfilled";
        request.UpdatedAt = DateTime.UtcNow;

        if (previousDocumentId.HasValue)
        {
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

    private static void ValidateTargetRole(string? targetRole)
    {
        if (targetRole != null && targetRole != Constants.SystemRoles.Admin && targetRole != Constants.SystemRoles.Surveyor && targetRole != Constants.SystemRoles.Client)
            throw new ValidationException($"Unknown target role '{targetRole}'.");
    }

    /// <summary>Workspace-scoped role names for this caller - Land has no per-record assignment list to fall back to the way GetEffectiveJobRolesAsync falls back from job-scoped grants.</summary>
    private async Task<List<string>> GetWorkspaceRolesAsync(Guid callerUserId, Guid workspaceId)
    {
        return await _context.UserAccesses
            .Where(ua => ua.UserId == callerUserId && ua.IsActive && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Select(ua => ua.Role.Name)
            .ToListAsync();
    }

    /// <summary>
    /// A real Client is job-scoped only (see WorkspaceIntegrationTestBase) and typically holds
    /// no workspace-scope role at all, so "TargetRole == Client" can't be a literal membership
    /// check the way Admin/Surveyor are. Anyone who reached this point already passed
    /// EnsureLandAccessAsync(..., "view"); "Client" is satisfied by not being staff (Admin/
    /// Surveyor), Admin/Surveyor are satisfied by an exact workspace-role match.
    /// </summary>
    private static bool RoleMatches(string targetRole, List<string> callerWorkspaceRoles)
    {
        if (targetRole == Constants.SystemRoles.Client)
            return !callerWorkspaceRoles.Contains(Constants.SystemRoles.Admin) && !callerWorkspaceRoles.Contains(Constants.SystemRoles.Surveyor);

        return callerWorkspaceRoles.Contains(targetRole);
    }

    private async Task<Land> FindLandAsync(Guid workspaceId, Guid landId)
    {
        return await _context.Lands.FirstOrDefaultAsync(l => l.Id == landId && l.WorkspaceId == workspaceId && l.IsActive)
            ?? throw new NotFoundException("Land not found");
    }

    private async Task<LandDocumentRequest> FindRequestAsync(Guid landId, Guid requestId)
    {
        return await _context.LandDocumentRequests
            .FirstOrDefaultAsync(r => r.Id == requestId && r.LandId == landId && r.IsActive)
            ?? throw new NotFoundException("Document request not found");
    }
}
