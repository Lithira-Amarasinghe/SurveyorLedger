using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Invitation;
using SurveyorLedger.API.Models.Land;
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

    /// <summary>
    /// The scope-agnostic core of CreateInvitationAsync, for callers outside the workspace
    /// controller (e.g. JobService, when a job assignment target has no consent coverage).
    /// No permission check here - the caller has already authorized the action for its own
    /// scope; this only handles the find-or-create-user / invitation bookkeeping.
    /// </summary>
    Task<Invitation> CreateScopedInvitationAsync(
        string scopeType, Guid scopeId, Guid roleId, string displayName, Guid invitedByUserId,
        string email, string? firstName, string? lastName, string? phone, AddressDto? address,
        string? descendantScopeType = null, Guid? descendantScopeId = null, Guid? descendantRoleId = null);

    Task<List<Invitation>> GetPendingInvitationsAsync(Guid workspaceId, Guid callerUserId);

    /// <summary>
    /// Pending invitations that resolve to this specific job - either a plain Job-scope invite
    /// (Client/Finance) or a Workspace-scope invite whose role chains down to this job via
    /// DescendantScopeId (Surveyor). Backs the job page's people table, so a pending invite
    /// shows there the same way an accepted grant does.
    /// </summary>
    Task<List<Invitation>> GetPendingInvitationsForJobAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
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
    private readonly IScopedAccessService _access;
    private readonly IEmailService _emailService;
    private readonly IPasswordService _passwordService;
    private readonly IConfiguration _config;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        ApplicationDbContext context,
        ICasbinService casbinService,
        IUserAccessGrantService grantService,
        IScopedAccessService access,
        IEmailService emailService,
        IPasswordService passwordService,
        IConfiguration config,
        ILogger<InvitationService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _grantService = grantService;
        _access = access;
        _emailService = emailService;
        _passwordService = passwordService;
        _config = config;
        _logger = logger;
    }

    public async Task<Invitation> CreateInvitationAsync(Guid workspaceId, Guid invitedByUserId, InvitationRequest request)
    {
        // Inviting as Member only needs the narrower client:create permission (front-desk
        // staff adding a harmless, view-only person - Client no longer exists at workspace
        // scope, it's granted per job instead). Admin/Surveyor are real membership decisions
        // and need manage_members, same gate as before - otherwise a Surveyor could hand
        // themselves Admin by picking that role here.
        var permitted = request.Role == Constants.SystemRoles.Member
            ? await _casbinService.EnforceAsync(invitedByUserId.ToString(), "client", "create", workspaceId.ToString())
            : await _casbinService.EnforceAsync(invitedByUserId.ToString(), "workspace", "manage_members", workspaceId.ToString());
        if (!permitted)
            throw new ForbiddenException("You do not have permission to add a person with this role.");

        var workspace = await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive)
            ?? throw new NotFoundException("Workspace not found");

        // Scope-checked against RoleScopes (mirrors WorkspaceService.AddMemberRoleAsync /
        // JobService.ResolveJobRoleAsync) - not just "does a role with this name exist".
        var role = await _context.Roles
            .Where(r => r.Name == request.Role && r.IsSystem)
            .Where(r => r.RoleScopes.Any(rs => rs.ScopeType == Constants.ScopeTypes.Workspace))
            .FirstOrDefaultAsync()
            ?? throw new AppException(Constants.ErrorCodes.ValidationFailed, $"'{request.Role}' is not a valid workspace role.", 400);

        return await CreateScopedInvitationAsync(
            Constants.ScopeTypes.Workspace, workspaceId, role.Id, workspace.Name, invitedByUserId,
            request.Email, request.FirstName, request.LastName, request.Phone, request.Address);
    }

    public async Task<Invitation> CreateScopedInvitationAsync(
        string scopeType, Guid scopeId, Guid roleId, string displayName, Guid invitedByUserId,
        string email, string? firstName, string? lastName, string? phone, AddressDto? address,
        string? descendantScopeType = null, Guid? descendantScopeId = null, Guid? descendantRoleId = null)
    {
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId)
            ?? throw new AppException(Constants.ErrorCodes.ValidationFailed, "Role not found", 400);

        email = email.Trim();

        var targetPerson = await _context.People
            .FirstOrDefaultAsync(p => p.Email != null && p.Email.ToUpper() == email.ToUpper() && p.IsActive);

        if (targetPerson != null)
        {
            var account = await _context.UserAccounts.FirstOrDefaultAsync(a => a.PersonId == targetPerson.Id && a.IsActive);
            if (account != null)
            {
                var alreadyHasAccess = await _context.UserAccesses.AnyAsync(ua =>
                    ua.UserId == account.Id && ua.IsActive && ua.ScopeType == scopeType && ua.ScopeId == scopeId);
                if (alreadyHasAccess)
                    throw new AppException(Constants.ErrorCodes.AlreadyMember, "This person already has access at this scope.", 409);
            }
            // A Person with no UserAccount yet (e.g. an existing billing client, or a still-
            // pending invitee from a different scope) is a valid invite target - falls through
            // to reuse the existing Person row, same as before under the old User model.
        }
        else
        {
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
                throw new AppException(Constants.ErrorCodes.ValidationFailed, "FirstName and LastName are required for a new person.", 400);

            targetPerson = new Person
            {
                Id = Guid.NewGuid(),
                Email = email,
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Phone = phone?.Trim(),
                Address = new Address
                {
                    Street = address?.Street,
                    City = address?.City,
                    District = address?.District,
                    PostalCode = address?.PostalCode,
                    Country = address?.Country
                },
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.People.AddAsync(targetPerson);
        }

        // A new invite for the same person/scope supersedes any existing pending one - but
        // keyed on the FULL grant (including descendant), not just the primary scope. Two
        // different job invites for the same never-accepted person both resolve to the same
        // primary (Workspace, WorkspaceMember) once chaining moved the invite up a level; only
        // the descendant (the specific job) tells them apart, so both must survive side by side.
        var existingPending = await _context.Invitations
            .Where(i => i.UserId == targetPerson.Id && i.ScopeType == scopeType &&
                i.ScopeId == scopeId && i.Status == "Pending" &&
                i.DescendantScopeType == descendantScopeType && i.DescendantScopeId == descendantScopeId)
            .ToListAsync();
        foreach (var stale in existingPending)
            stale.Status = "Revoked";

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            UserId = targetPerson.Id,
            Email = email,
            ScopeType = scopeType,
            ScopeId = scopeId,
            RoleId = role.Id,
            DescendantScopeType = descendantScopeType,
            DescendantScopeId = descendantScopeId,
            DescendantRoleId = descendantRoleId,
            Token = Guid.NewGuid().ToString("N"),
            InvitedBy = invitedByUserId,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        invitation.EmailFailed = !await TrySendInviteEmailAsync(invitation, displayName);

        await _context.Invitations.AddAsync(invitation);
        // AddAudit's workspaceId param is a real FK to Workspaces - only safe to pass scopeId
        // when the scope actually is a workspace. A job-scoped invite has no workspace id to
        // hand it, so the audit row is left unattributed to a workspace (still has ResourceId
        // pointing at the Invitation itself).
        AddAudit("InvitationCreated", "Invitation", invitation.Id,
            scopeType == Constants.ScopeTypes.Workspace ? scopeId : null, invitedByUserId, null, $"{email}:{role.Name}");
        await _context.SaveChangesAsync();

        _logger.LogInformation("Invitation created for {Email} to {ScopeType} {ScopeId} by {UserId}", email, scopeType, scopeId, invitedByUserId);
        return invitation;
    }

    public async Task<List<Invitation>> GetPendingInvitationsForJobAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await _access.EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        var invitations = await _context.Invitations
            .Include(i => i.Role)
            .Where(i => i.Status == "Pending" &&
                ((i.ScopeType == Constants.ScopeTypes.Job && i.ScopeId == jobId) ||
                 (i.DescendantScopeType == Constants.ScopeTypes.Job && i.DescendantScopeId == jobId)))
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        await ExpireStaleAsync(invitations);
        return invitations.Where(i => i.Status == "Pending").ToList();
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
            .Include(i => i.InvitedByUser).ThenInclude(a => a.Person)
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
        var callerAccount = await _context.UserAccounts.FirstOrDefaultAsync(a => a.Id == callerUserId);
        if (callerAccount == null) return new List<Invitation>();

        var invitations = await _context.Invitations
            .Include(i => i.Role)
            .Where(i => i.UserId == callerAccount.PersonId)
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

        var callerAccount = await _context.UserAccounts.FirstOrDefaultAsync(a => a.Id == callerUserId)
            ?? throw new ForbiddenException("This invitation is for a different account.");
        if (invitation.UserId != callerAccount.PersonId)
            throw new ForbiddenException("This invitation is for a different account.");

        await GrantAndMarkAcceptedAsync(invitation);
        return invitation;
    }

    public async Task CompleteInvitationAsync(string token, CompleteInvitationRequest request)
    {
        var invitation = await LoadAcceptableInvitationAsync(i => i.Token == token);

        var person = await _context.People.FirstOrDefaultAsync(p => p.Id == invitation.UserId && p.IsActive)
            ?? throw new NotFoundException("Person not found");

        var existingAccount = await _context.UserAccounts.FirstOrDefaultAsync(a => a.PersonId == person.Id);
        if (existingAccount is { HasCompletedSignup: true })
            throw new AppException(Constants.ErrorCodes.UserAlreadyExists,
                "This account already has a password - log in and accept the invitation from there.", 409);

        person.FirstName = request.FirstName.Trim();
        person.LastName = request.LastName.Trim();
        if (request.Phone != null) person.Phone = request.Phone.Trim();
        if (request.Address != null)
        {
            person.Address = new Address
            {
                Street = request.Address.Street,
                City = request.Address.City,
                District = request.Address.District,
                PostalCode = request.Address.PostalCode,
                Country = request.Address.Country
            };
        }
        person.UpdatedAt = DateTime.UtcNow;

        if (existingAccount == null)
        {
            var account = new UserAccount
            {
                Id = Guid.NewGuid(),
                PersonId = person.Id,
                PasswordHash = _passwordService.HashPassword(request.Password),
                HasCompletedSignup = true,
                // Receiving and clicking the tokenized invite link is itself proof of email
                // ownership - equivalent trust to an OTP, so this skips a separate OTP round-trip.
                EmailVerified = true,
                EmailVerifiedAt = DateTime.UtcNow,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.UserAccounts.AddAsync(account);
        }
        else
        {
            existingAccount.PasswordHash = _passwordService.HashPassword(request.Password);
            existingAccount.HasCompletedSignup = true;
            existingAccount.EmailVerified = true;
            existingAccount.EmailVerifiedAt = DateTime.UtcNow;
            existingAccount.UpdatedAt = DateTime.UtcNow;
        }

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

        var callerAccount = await _context.UserAccounts.FirstOrDefaultAsync(a => a.Id == callerUserId)
            ?? throw new ForbiddenException("This invitation is for a different account.");
        if (invitation.UserId != callerAccount.PersonId)
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

        // Nothing is ever granted before accept, so declining never has anything to undo -
        // that holds regardless of scope depth (plain Job invite, chained Workspace+Job
        // invite, or any future level above Workspace), since only Accept ever grants.
        invitation.Status = "Declined";
        // invitation.UserId is a Person.Id; AuditLog.UserId is a FK to UserAccount, so this
        // needs the account, not the person - a token-based decline may have no account yet
        // (e.g. a brand-new invitee declining before ever setting a password), in which case
        // the audit row is left unattributed rather than violating the FK.
        var account = await _context.UserAccounts.FirstOrDefaultAsync(a => a.PersonId == invitation.UserId);
        // AddAudit's workspaceId param is a real FK to Workspaces - a Job-scope invite's
        // ScopeId is a job, not a workspace, so passing it unconditionally violates the FK.
        // Same rule CreateScopedInvitationAsync already follows for the same reason.
        AddAudit("InvitationDeclined", "Invitation", invitation.Id,
            invitation.ScopeType == Constants.ScopeTypes.Workspace ? invitation.ScopeId : null,
            account?.Id, "Pending", "Declined");
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
        var account = await _context.UserAccounts.FirstOrDefaultAsync(a => a.PersonId == invitation.UserId)
            ?? throw new AppException(Constants.ErrorCodes.ValidationFailed,
                "This person has not completed account setup yet.", 409);

        await _grantService.GrantAsync(account.Id, invitation.RoleId, invitation.ScopeType, invitation.ScopeId, invitation.InvitedBy);

        // The invitation's own primary grant may have been lifted to a higher scope than what
        // was actually intended (see CreateScopedInvitationAsync's descendant fields) - grant
        // the original, more specific role too. Its own ancestor walk fills in any level in
        // between that the primary grant skipped, and no-ops on levels already granted.
        if (invitation.DescendantScopeType != null && invitation.DescendantScopeId != null && invitation.DescendantRoleId != null)
        {
            await _grantService.GrantAsync(account.Id, invitation.DescendantRoleId.Value,
                invitation.DescendantScopeType, invitation.DescendantScopeId.Value, invitation.InvitedBy);
        }

        invitation.Status = "Accepted";
        // Same FK constraint as CreateScopedInvitationAsync's AddAudit call - workspaceId is
        // a real FK to Workspaces, only safe to pass when the scope actually is a workspace.
        AddAudit("InvitationAccepted", "Invitation", invitation.Id,
            invitation.ScopeType == Constants.ScopeTypes.Workspace ? invitation.ScopeId : null,
            account.Id, null, invitation.ScopeType);
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

    private void AddAudit(string action, string resourceType, Guid resourceId, Guid? workspaceId, Guid? userId, string? oldValue, string? newValue)
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
