using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Invitation;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IInvitationService
{
    Task<Invitation> CreateInvitationAsync(Guid workspaceId, Guid invitedByUserId, InvitationRequest request);
    Task<List<Invitation>> GetPendingInvitationsAsync(Guid workspaceId, Guid callerUserId);
    Task RevokeInvitationAsync(Guid workspaceId, Guid invitationId, Guid callerUserId);
    Task ResendInvitationAsync(Guid workspaceId, Guid invitationId, Guid callerUserId);
    Task<Invitation> GetByTokenAsync(string token);
    Task<(Guid WorkspaceId, string Role)> AcceptInvitationAsync(string token, Guid callerUserId, string callerEmail);

    /// <summary>
    /// Create an account directly from an invitation link and auto-accept it. The invite
    /// link itself is proof of email ownership, so the account is created already verified
    /// - no OTP round-trip. No tokens are issued; the caller logs in separately afterward.
    /// </summary>
    Task RegisterFromInvitationAsync(string token, Models.Invitation.RegisterFromInvitationRequest request);
}

public class InvitationService : IInvitationService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly IEmailService _emailService;
    private readonly IPasswordService _passwordService;
    private readonly IConfiguration _config;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        ApplicationDbContext context,
        ICasbinService casbinService,
        IEmailService emailService,
        IPasswordService passwordService,
        IConfiguration config,
        ILogger<InvitationService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _emailService = emailService;
        _passwordService = passwordService;
        _config = config;
        _logger = logger;
    }

    public async Task<Invitation> CreateInvitationAsync(Guid workspaceId, Guid invitedByUserId, InvitationRequest request)
    {
        var allowed = await _casbinService.EnforceAsync(invitedByUserId.ToString(), "workspace", "manage_members", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to invite members to this workspace.");

        var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive)
            ?? throw new NotFoundException("Workspace not found");

        var email = request.Email.Trim();

        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToUpper() == email.ToUpper() && u.IsActive);
        if (existingUser != null)
        {
            var alreadyMember = await _context.UserAccesses.AnyAsync(ua =>
                ua.UserId == existingUser.Id && ua.IsActive &&
                ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId);
            if (alreadyMember)
                throw new AppException(Constants.ErrorCodes.AlreadyMember, "This person is already a member of the workspace.", 409);
        }

        // A new invite for the same email supersedes any existing pending one.
        var existingPending = await _context.Invitations
            .Where(i => i.WorkspaceId == workspaceId && i.Email.ToUpper() == email.ToUpper() && i.Status == "Pending")
            .ToListAsync();
        foreach (var stale in existingPending)
        {
            stale.Status = "Revoked";
        }

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Email = email,
            Role = request.Role,
            Token = Guid.NewGuid().ToString("N"),
            InvitedBy = invitedByUserId,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        invitation.EmailFailed = !await TrySendInviteEmailAsync(invitation, workspace.Name);

        await _context.Invitations.AddAsync(invitation);
        AddAudit("InvitationCreated", "Invitation", invitation.Id, workspaceId, invitedByUserId, null, $"{email}:{request.Role}");
        await _context.SaveChangesAsync();

        _logger.LogInformation("Invitation created for {Email} to workspace {WorkspaceId} by {UserId}", email, workspaceId, invitedByUserId);
        return invitation;
    }

    public async Task<List<Invitation>> GetPendingInvitationsAsync(Guid workspaceId, Guid callerUserId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "workspace", "manage_members", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to view invitations for this workspace.");

        var invitations = await _context.Invitations
            .Include(i => i.InvitedByUser)
            .Where(i => i.WorkspaceId == workspaceId && i.Status == "Pending")
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        var stillPending = new List<Invitation>();
        var anyExpired = false;
        foreach (var invitation in invitations)
        {
            if (invitation.ExpiresAt <= DateTime.UtcNow)
            {
                invitation.Status = "Expired";
                anyExpired = true;
                continue;
            }
            stillPending.Add(invitation);
        }

        if (anyExpired)
            await _context.SaveChangesAsync();

        return stillPending;
    }

    public async Task RevokeInvitationAsync(Guid workspaceId, Guid invitationId, Guid callerUserId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "workspace", "manage_members", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to revoke invitations for this workspace.");

        var invitation = await _context.Invitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Invitation not found");

        invitation.Status = "Revoked";
        AddAudit("InvitationRevoked", "Invitation", invitation.Id, workspaceId, callerUserId, "Pending", "Revoked");
        await _context.SaveChangesAsync();
    }

    public async Task ResendInvitationAsync(Guid workspaceId, Guid invitationId, Guid callerUserId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "workspace", "manage_members", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to resend invitations for this workspace.");

        var invitation = await _context.Invitations
            .Include(i => i.Workspace)
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Invitation not found");

        if (invitation.ExpiresAt <= DateTime.UtcNow && invitation.Status == "Pending")
            invitation.Status = "Expired";

        if (invitation.Status != "Pending")
            throw new AppException(Constants.ErrorCodes.InvitationExpired, "This invitation is no longer pending and cannot be resent.", 410);

        invitation.EmailFailed = !await TrySendInviteEmailAsync(invitation, invitation.Workspace.Name);
        AddAudit("InvitationResent", "Invitation", invitation.Id, workspaceId, callerUserId, null, invitation.Email);
        await _context.SaveChangesAsync();
    }

    public async Task<Invitation> GetByTokenAsync(string token)
    {
        var invitation = await _context.Invitations
            .Include(i => i.Workspace)
            .FirstOrDefaultAsync(i => i.Token == token)
            ?? throw new AppException(Constants.ErrorCodes.InvitationNotFound, "Invitation not found", 404);

        if (invitation.Status == "Pending" && invitation.ExpiresAt <= DateTime.UtcNow)
        {
            invitation.Status = "Expired";
            await _context.SaveChangesAsync();
        }

        return invitation;
    }

    public async Task<(Guid WorkspaceId, string Role)> AcceptInvitationAsync(string token, Guid callerUserId, string callerEmail)
    {
        var invitation = await LoadAcceptableInvitationAsync(token);

        if (!string.Equals(invitation.Email.Trim(), callerEmail.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new AppException(Constants.ErrorCodes.InvitationEmailMismatch, "This invitation is for a different account.", 403);

        await GrantWorkspaceAccessAsync(invitation, callerUserId);

        _logger.LogInformation("Invitation {InvitationId} accepted by {UserId}", invitation.Id, callerUserId);
        return (invitation.WorkspaceId, invitation.Role);
    }

    public async Task RegisterFromInvitationAsync(string token, Models.Invitation.RegisterFromInvitationRequest request)
    {
        var invitation = await LoadAcceptableInvitationAsync(token);
        var email = invitation.Email.Trim();

        var existingUser = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);
        if (existingUser != null)
        {
            _logger.LogWarning("Register-from-invitation attempted but an account already exists for {Email}", email);
            throw new AppException(Constants.ErrorCodes.UserAlreadyExists,
                "An account already exists for this email. Log in and accept the invite from there.", 409);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PasswordHash = _passwordService.HashPassword(request.Password),
            // Receiving and clicking the tokenized invite link is itself proof of email
            // ownership - equivalent trust to an OTP, so registration skips the OTP round-trip.
            EmailVerified = true,
            EmailVerifiedAt = DateTime.UtcNow,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        await GrantWorkspaceAccessAsync(invitation, user.Id);

        _logger.LogInformation("Account created and invitation {InvitationId} auto-accepted for {Email}", invitation.Id, email);
    }

    /// <summary>
    /// Loads an invitation by token and validates it's still acceptable (found, pending,
    /// not expired), flipping it to Expired and persisting if the expiry just lapsed.
    /// Shared by AcceptInvitationAsync and RegisterFromInvitationAsync.
    /// </summary>
    private async Task<Invitation> LoadAcceptableInvitationAsync(string token)
    {
        var invitation = await _context.Invitations
            .FirstOrDefaultAsync(i => i.Token == token)
            ?? throw new AppException(Constants.ErrorCodes.InvitationNotFound, "Invitation not found", 404);

        if (invitation.Status != "Pending" || invitation.ExpiresAt <= DateTime.UtcNow)
        {
            if (invitation.Status == "Pending")
            {
                invitation.Status = "Expired";
                await _context.SaveChangesAsync();
            }
            throw new AppException(Constants.ErrorCodes.InvitationExpired, "This invitation has expired or is no longer valid.", 410);
        }

        return invitation;
    }

    /// <summary>
    /// Creates/updates the UserAccess + Casbin role grant for a user accepting an
    /// invitation, and marks the invitation Accepted. Shared by both the "existing account
    /// accepts" and "brand-new account registers and auto-accepts" paths.
    /// </summary>
    private async Task GrantWorkspaceAccessAsync(Invitation invitation, Guid userId)
    {
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == invitation.Role && r.IsSystem)
            ?? throw new InvalidOperationException($"Role '{invitation.Role}' not found");

        var existingAccess = await _context.UserAccesses.FirstOrDefaultAsync(ua =>
            ua.UserId == userId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == invitation.WorkspaceId);

        if (existingAccess != null)
        {
            // Already a member (e.g. added another way since this invite was sent) - update
            // their role to match the invite rather than creating a duplicate UserAccess row.
            if (existingAccess.RoleId != role.Id)
            {
                var oldRole = await _context.Roles.FirstAsync(r => r.Id == existingAccess.RoleId);
                existingAccess.RoleId = role.Id;
                await _context.SaveChangesAsync();
                await _casbinService.RemoveRoleForUserAsync(userId.ToString(), oldRole.Name, invitation.WorkspaceId.ToString());
                await _casbinService.AddRoleForUserAsync(userId.ToString(), role.Name, invitation.WorkspaceId.ToString());
            }
        }
        else
        {
            var userAccess = new UserAccess
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RoleId = role.Id,
                ScopeType = Constants.ScopeTypes.Workspace,
                ScopeId = invitation.WorkspaceId,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = invitation.InvitedBy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await _context.UserAccesses.AddAsync(userAccess);
            await _context.SaveChangesAsync();
            await _casbinService.AddRoleForUserAsync(userId.ToString(), role.Name, invitation.WorkspaceId.ToString());
        }

        invitation.Status = "Accepted";
        AddAudit("InvitationAccepted", "Invitation", invitation.Id, invitation.WorkspaceId, userId, null, role.Name);
        await _context.SaveChangesAsync();
    }

    /// <returns>true if the email was sent successfully, false if it failed (invitation still created either way).</returns>
    private async Task<bool> TrySendInviteEmailAsync(Invitation invitation, string workspaceName)
    {
        var uiBaseUrl = _config["AppSettings:UiBaseUrl"] ?? throw new InvalidOperationException("AppSettings:UiBaseUrl not configured");
        var inviteUrl = $"{uiBaseUrl}/invite/{invitation.Token}";

        try
        {
            await _emailService.SendInviteEmailAsync(invitation.Email, workspaceName, inviteUrl);
            return true;
        }
        catch (Exception ex)
        {
            // Don't fail invite creation/resend if the email send fails - the invitation row
            // (and its link) is still valid; EmailFailed surfaces this to the Admin instead.
            _logger.LogWarning(ex, "Failed to send invite email to {Email} for workspace {WorkspaceId}", invitation.Email, invitation.WorkspaceId);
            return false;
        }
    }

    private void AddAudit(string action, string resourceType, Guid resourceId, Guid? workspaceId, Guid userId, string? oldValue, string? newValue)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ScopeType = Constants.ScopeTypes.Workspace,
            ScopeId = workspaceId,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = DateTime.UtcNow
        });
    }
}
