using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Invitation;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IInvitationService
{
    /// <summary>
    /// The single "add a person to this workspace" entry point. Finds or creates the
    /// target User (email required) and always creates a Pending Invitation - never grants
    /// UserAccess here. Access only happens on accept, for a brand-new person and an
    /// existing account alike.
    /// </summary>
    Task<Invitation> CreateInvitationAsync(Guid workspaceId, Guid invitedByUserId, InvitationRequest request);

    Task<List<Invitation>> GetPendingInvitationsAsync(Guid workspaceId, Guid callerUserId);
    Task RevokeInvitationAsync(Guid workspaceId, Guid invitationId, Guid callerUserId);
    Task ResendInvitationAsync(Guid workspaceId, Guid invitationId, Guid callerUserId);
    Task<Invitation> GetByTokenAsync(string token);

    /// <summary>Every invitation for the given user, across every workspace.</summary>
    Task<List<Invitation>> GetMyInvitationsAsync(Guid callerUserId);

    /// <summary>Accept as an already-authenticated account (has a password already).</summary>
    Task<Invitation> AcceptInvitationAsync(Guid invitationId, Guid callerUserId);

    /// <summary>
    /// Accept via the emailed link when the account has no password yet - this call only
    /// sets a password (and lets the person confirm/edit the name/phone/address the admin
    /// entered). It does NOT grant access - the person still has to log in and hit Accept
    /// on this specific invitation, same as anyone who already had a password. No auth
    /// token exists yet for this account, so this is reached by token, not a caller id.
    /// </summary>
    Task CompleteInvitationAsync(string token, CompleteInvitationRequest request);

    /// <summary>Always simple: nothing is ever granted before accept, so this never has anything to revoke.</summary>
    Task DeclineInvitationAsync(Guid invitationId, Guid callerUserId);

    /// <summary>
    /// Decline reachable by token, no auth needed - a brand-new invitee has no password yet
    /// and so no way to ever reach the authenticated decline. Same trivial "nothing to
    /// undo" behavior as the authenticated path.
    /// </summary>
    Task DeclineByTokenAsync(string token);
}

public class InvitationService : IInvitationService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly IUserAccessGrantService _grantService;
    private readonly IEmailService _emailService;
    private readonly IPasswordService _passwordService;
    private readonly IConfiguration _config;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        ApplicationDbContext context,
        ICasbinService casbinService,
        IUserAccessGrantService grantService,
        IEmailService emailService,
        IPasswordService passwordService,
        IConfiguration config,
        ILogger<InvitationService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _grantService = grantService;
        _emailService = emailService;
        _passwordService = passwordService;
        _config = config;
        _logger = logger;
    }

    public async Task<Invitation> CreateInvitationAsync(Guid workspaceId, Guid invitedByUserId, InvitationRequest request)
    {
        // Inviting as Client only needs the narrower client:create permission (Admin/
        // Manager/Surveyor - front-desk staff capturing a client contact). Any other role
        // is a real membership decision and needs manage_members, same gate as before -
        // otherwise a Surveyor could hand themselves Admin by picking that role here.
        var permitted = request.Role == Constants.SystemRoles.Client
            ? await _casbinService.EnforceAsync(invitedByUserId.ToString(), "client", "create", workspaceId.ToString())
            : await _casbinService.EnforceAsync(invitedByUserId.ToString(), "workspace", "manage_members", workspaceId.ToString());
        if (!permitted)
            throw new ForbiddenException("You do not have permission to add a person with this role.");

        var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive)
            ?? throw new NotFoundException("Workspace not found");

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == request.Role && r.IsSystem)
            ?? throw new AppException(Constants.ErrorCodes.ValidationFailed, $"Role '{request.Role}' not found", 400);

        var email = request.Email.Trim();

        var targetUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToUpper() == email.ToUpper() && u.IsActive);

        if (targetUser != null)
        {
            var alreadyMember = await _context.UserAccesses.AnyAsync(ua =>
                ua.UserId == targetUser.Id && ua.IsActive &&
                ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId);
            if (alreadyMember)
                throw new AppException(Constants.ErrorCodes.AlreadyMember, "This person is already a member of the workspace.", 409);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
                throw new AppException(Constants.ErrorCodes.ValidationFailed, "FirstName and LastName are required for a new person.", 400);

            targetUser = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Phone = request.Phone?.Trim(),
                Address = new Address
                {
                    Street = request.Address?.Street,
                    City = request.Address?.City,
                    District = request.Address?.District,
                    PostalCode = request.Address?.PostalCode,
                    Country = request.Address?.Country
                },
                PasswordHash = null,
                EmailVerified = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.Users.AddAsync(targetUser);
        }

        // A new invite for the same person/scope supersedes any existing pending one.
        var existingPending = await _context.Invitations
            .Where(i => i.UserId == targetUser.Id && i.ScopeType == Constants.ScopeTypes.Workspace &&
                i.ScopeId == workspaceId && i.Status == "Pending")
            .ToListAsync();
        foreach (var stale in existingPending)
            stale.Status = "Revoked";

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            UserId = targetUser.Id,
            Email = email,
            ScopeType = Constants.ScopeTypes.Workspace,
            ScopeId = workspaceId,
            RoleId = role.Id,
            Token = Guid.NewGuid().ToString("N"),
            InvitedBy = invitedByUserId,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        invitation.EmailFailed = !await TrySendInviteEmailAsync(invitation, workspace.Name);

        await _context.Invitations.AddAsync(invitation);
        AddAudit("InvitationCreated", "Invitation", invitation.Id, workspaceId, invitedByUserId, null, $"{email}:{role.Name}");
        await _context.SaveChangesAsync();

        _logger.LogInformation("Invitation created for {Email} to workspace {WorkspaceId} by {UserId}", email, workspaceId, invitedByUserId);
        return invitation;
    }

    public async Task<List<Invitation>> GetPendingInvitationsAsync(Guid workspaceId, Guid callerUserId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "workspace", "manage_members", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to view invitations for this workspace.");

        // Accepted invitations become real members and show up on the Members list instead -
        // everything else (Pending, Declined, Expired, Revoked) stays visible here so Admin
        // can see a decline and resend it, e.g. after an accidental click.
        var invitations = await _context.Invitations
            .Include(i => i.InvitedByUser)
            .Include(i => i.Role)
            .Where(i => i.ScopeType == Constants.ScopeTypes.Workspace && i.ScopeId == workspaceId && i.Status != "Accepted")
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        // Only flip actually-Pending rows past their expiry - ExpireStaleAsync would
        // otherwise stomp a Declined/Revoked row's status just because it's old.
        var stillPending = invitations.Where(i => i.Status == "Pending").ToList();
        await ExpireStaleAsync(stillPending);

        return invitations;
    }

    public async Task<List<Invitation>> GetMyInvitationsAsync(Guid callerUserId)
    {
        var invitations = await _context.Invitations
            .Include(i => i.Role)
            .Where(i => i.UserId == callerUserId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        var pending = invitations.Where(i => i.Status == "Pending").ToList();
        await ExpireStaleAsync(pending);

        return invitations;
    }

    public async Task RevokeInvitationAsync(Guid workspaceId, Guid invitationId, Guid callerUserId)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "workspace", "manage_members", workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException("You do not have permission to revoke invitations for this workspace.");

        var invitation = await _context.Invitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.ScopeType == Constants.ScopeTypes.Workspace && i.ScopeId == workspaceId)
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
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.ScopeType == Constants.ScopeTypes.Workspace && i.ScopeId == workspaceId)
            ?? throw new NotFoundException("Invitation not found");

        var workspace = await _context.Workspaces.FirstAsync(w => w.Id == workspaceId);

        if (invitation.Status == "Accepted")
            throw new AppException(Constants.ErrorCodes.AlreadyMember, "This person has already accepted and is a member.", 409);

        // Pending, Declined, Expired, or Revoked can all be resent - reopens it as a fresh
        // pending invite (new token, new expiry), covering an accidental decline too.
        var previousStatus = invitation.Status;
        invitation.Status = "Pending";
        invitation.Token = Guid.NewGuid().ToString("N");
        invitation.ExpiresAt = DateTime.UtcNow.AddDays(7);

        invitation.EmailFailed = !await TrySendInviteEmailAsync(invitation, workspace.Name);
        AddAudit("InvitationResent", "Invitation", invitation.Id, workspaceId, callerUserId, previousStatus, invitation.Email);
        await _context.SaveChangesAsync();
    }

    public async Task<Invitation> GetByTokenAsync(string token)
    {
        var invitation = await _context.Invitations
            .Include(i => i.Role)
            .FirstOrDefaultAsync(i => i.Token == token)
            ?? throw new AppException(Constants.ErrorCodes.InvitationNotFound, "Invitation not found", 404);

        if (invitation.Status == "Pending" && invitation.ExpiresAt <= DateTime.UtcNow)
        {
            invitation.Status = "Expired";
            await _context.SaveChangesAsync();
        }

        return invitation;
    }

    public async Task<Invitation> AcceptInvitationAsync(Guid invitationId, Guid callerUserId)
    {
        var invitation = await LoadAcceptableInvitationAsync(i => i.Id == invitationId);

        if (invitation.UserId != callerUserId)
            throw new ForbiddenException("This invitation is for a different account.");

        await GrantAndMarkAcceptedAsync(invitation);
        return invitation;
    }

    public async Task CompleteInvitationAsync(string token, CompleteInvitationRequest request)
    {
        var invitation = await LoadAcceptableInvitationAsync(i => i.Token == token);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == invitation.UserId && u.IsActive)
            ?? throw new NotFoundException("User not found");

        if (user.PasswordHash != null)
            throw new AppException(Constants.ErrorCodes.UserAlreadyExists,
                "This account already has a password - log in and accept the invitation from there.", 409);

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        if (request.Phone != null) user.Phone = request.Phone.Trim();
        if (request.Address != null)
        {
            user.Address = new Address
            {
                Street = request.Address.Street,
                City = request.Address.City,
                District = request.Address.District,
                PostalCode = request.Address.PostalCode,
                Country = request.Address.Country
            };
        }
        user.PasswordHash = _passwordService.HashPassword(request.Password);
        // Receiving and clicking the tokenized invite link is itself proof of email
        // ownership - equivalent trust to an OTP, so this skips a separate OTP round-trip.
        user.EmailVerified = true;
        user.EmailVerifiedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        // Deliberately does NOT accept the invitation - password setup and accepting
        // membership are separate decisions. The person logs in next and gets an explicit
        // Accept/Decline choice for this invitation, same as anyone who already had a login.
        await _context.SaveChangesAsync();
        _logger.LogInformation("Invitation {InvitationId} account set up for {Email}", invitation.Id, invitation.Email);
    }

    public async Task DeclineInvitationAsync(Guid invitationId, Guid callerUserId)
    {
        var invitation = await _context.Invitations
            .FirstOrDefaultAsync(i => i.Id == invitationId)
            ?? throw new NotFoundException("Invitation not found");

        if (invitation.UserId != callerUserId)
            throw new ForbiddenException("This invitation is for a different account.");

        await MarkDeclinedAsync(invitation);
    }

    public async Task DeclineByTokenAsync(string token)
    {
        var invitation = await _context.Invitations.FirstOrDefaultAsync(i => i.Token == token)
            ?? throw new AppException(Constants.ErrorCodes.InvitationNotFound, "Invitation not found", 404);

        await MarkDeclinedAsync(invitation);
    }

    private async Task MarkDeclinedAsync(Invitation invitation)
    {
        if (invitation.Status != "Pending")
            throw new AppException(Constants.ErrorCodes.InvitationExpired, "This invitation is no longer pending.", 410);

        // Nothing is ever granted before accept, so declining never has anything to undo.
        invitation.Status = "Declined";
        AddAudit("InvitationDeclined", "Invitation", invitation.Id, invitation.ScopeId, invitation.UserId, "Pending", "Declined");
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Loads an invitation matching the given filter and validates it's still acceptable
    /// (found, pending, not expired), flipping it to Expired and persisting if the expiry
    /// just lapsed. Shared by both accept paths.
    /// </summary>
    private async Task<Invitation> LoadAcceptableInvitationAsync(System.Linq.Expressions.Expression<Func<Invitation, bool>> match)
    {
        var invitation = await _context.Invitations.FirstOrDefaultAsync(match)
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
    /// The one place UserAccess actually gets created from an invitation - shared by the
    /// already-has-a-password accept and the just-set-a-password complete paths.
    /// </summary>
    private async Task GrantAndMarkAcceptedAsync(Invitation invitation)
    {
        await _grantService.GrantAsync(invitation.UserId, invitation.RoleId, invitation.ScopeType, invitation.ScopeId, invitation.InvitedBy);

        invitation.Status = "Accepted";
        AddAudit("InvitationAccepted", "Invitation", invitation.Id, invitation.ScopeId, invitation.UserId, null, invitation.ScopeType);
        await _context.SaveChangesAsync();
    }

    private async Task<List<Invitation>> ExpireStaleAsync(List<Invitation> pending)
    {
        var anyExpired = false;
        var stillPending = new List<Invitation>();
        foreach (var invitation in pending)
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
            _logger.LogWarning(ex, "Failed to send invite email to {Email}", invitation.Email);
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
