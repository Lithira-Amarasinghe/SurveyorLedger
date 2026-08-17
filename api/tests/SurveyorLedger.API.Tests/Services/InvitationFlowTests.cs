using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Invitation;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Covers the redesigned invitation flow: adding a person always creates the User (email
/// required) but never UserAccess up front - access only happens on accept/complete, and
/// decline never has anything to undo since nothing was ever granted before that point.
/// </summary>
public class InvitationFlowTests : WorkspaceIntegrationTestBase
{
    private IInvitationService _invitationService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IJobService, JobService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppSettings:UiBaseUrl"] = "https://test.local" })
            .Build());
    }

    private InvitationRequest NewPersonRequest(string email, string role = "Member") => new()
    {
        Email = email,
        Role = role,
        FirstName = "New",
        LastName = "Person"
    };

    /// <summary>invitation.UserId is a Person.Id; resolves the UserAccount.Id behind it, if any exists yet.</summary>
    private async Task<Guid?> GetAccountIdAsync(Guid personId) =>
        await Context.UserAccounts.Where(a => a.PersonId == personId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync();

    [Fact]
    public async Task CreateInvitation_ForBrandNewEmail_CreatesPersonButNoAccount()
    {
        _invitationService = GetService<IInvitationService>();

        var invitation = await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("new.person@test.local"));

        Assert.Equal("Pending", invitation.Status);
        var person = await Context.People.FirstOrDefaultAsync(p => p.Id == invitation.UserId);
        Assert.NotNull(person);

        Assert.Null(await GetAccountIdAsync(invitation.UserId));
    }

    [Fact]
    public async Task CreateInvitation_AlreadyMember_Rejected()
    {
        _invitationService = GetService<IInvitationService>();

        await Assert.ThrowsAsync<AppException>(
            () => _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("surveyor@test.local", "Surveyor")));
    }

    [Fact]
    public async Task Surveyor_CanInviteAsMember()
    {
        _invitationService = GetService<IInvitationService>();

        var invitation = await _invitationService.CreateInvitationAsync(WorkspaceId, SurveyorId, NewPersonRequest("member.candidate@test.local", "Member"));

        Assert.Equal("Pending", invitation.Status);
    }

    [Fact]
    public async Task Surveyor_CannotInviteAsAdmin()
    {
        _invitationService = GetService<IInvitationService>();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _invitationService.CreateInvitationAsync(WorkspaceId, SurveyorId, NewPersonRequest("sneaky@test.local", "Admin")));
    }

    [Fact]
    public async Task CompleteInvitation_SetsPasswordButDoesNotGrantAccess()
    {
        _invitationService = GetService<IInvitationService>();
        var invitation = await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("complete.me@test.local", "Surveyor"));

        await _invitationService.CompleteInvitationAsync(invitation.Token, new CompleteInvitationRequest
        {
            Password = "SomePassword123!",
            ConfirmPassword = "SomePassword123!",
            FirstName = "Completed",
            LastName = "Person"
        });

        var accountId = await GetAccountIdAsync(invitation.UserId) ?? throw new Exception("Account should exist after completing invitation.");
        var account = await Context.UserAccounts.FirstAsync(a => a.Id == accountId);
        Assert.NotNull(account.PasswordHash);
        Assert.True(account.EmailVerified);

        // Setting a password is not the same as accepting - no access yet, invite still Pending.
        var access = await Context.UserAccesses.FirstOrDefaultAsync(ua =>
            ua.UserId == accountId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == WorkspaceId);
        Assert.Null(access);

        var reloaded = await Context.Invitations.FirstAsync(i => i.Id == invitation.Id);
        Assert.Equal("Pending", reloaded.Status);

        // The explicit Accept step, now separate, is what actually grants access.
        await _invitationService.AcceptInvitationAsync(invitation.Id, accountId);

        access = await Context.UserAccesses.FirstOrDefaultAsync(ua =>
            ua.UserId == accountId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == WorkspaceId);
        Assert.NotNull(access);

        reloaded = await Context.Invitations.FirstAsync(i => i.Id == invitation.Id);
        Assert.Equal("Accepted", reloaded.Status);
    }

    [Fact]
    public async Task AcceptInvitation_ExistingAccount_GrantsAccessForNewWorkspace()
    {
        // Simulate an already-active account (has a password) being invited to a second workspace.
        _invitationService = GetService<IInvitationService>();
        var invitation = await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("existing@test.local", "Surveyor"));

        var accountId = Guid.NewGuid();
        await Context.UserAccounts.AddAsync(new SurveyorLedger.Data.Entities.UserAccount
        {
            Id = accountId, PersonId = invitation.UserId, PasswordHash = "already-has-a-password",
            EmailVerified = true, HasCompletedSignup = true, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        var accepted = await _invitationService.AcceptInvitationAsync(invitation.Id, accountId);

        Assert.Equal("Accepted", accepted.Status);
        var access = await Context.UserAccesses.AnyAsync(ua =>
            ua.UserId == accountId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == WorkspaceId);
        Assert.True(access);
    }

    [Fact]
    public async Task DeclineByToken_ForBrandNewInvitee_WorksWithoutEverLoggingIn()
    {
        // Regression: a brand-new invitee has no password yet, so the authenticated
        // decline endpoint is unreachable for them - this is the only way they can ever
        // decline before setting one.
        _invitationService = GetService<IInvitationService>();
        var invitation = await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("never.logged.in@test.local"));

        await _invitationService.DeclineByTokenAsync(invitation.Token);

        var reloaded = await Context.Invitations.FirstAsync(i => i.Id == invitation.Id);
        Assert.Equal("Declined", reloaded.Status);
    }

    [Fact]
    public async Task DeclineInvitation_NeverGrantedAccess_JustMarksDeclined()
    {
        // The authenticated decline path requires a real account (see DeclineByToken test
        // for the brand-new-invitee, no-account case) - simulate one that already exists.
        _invitationService = GetService<IInvitationService>();
        var invitation = await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("decline.me@test.local"));
        var accountId = Guid.NewGuid();
        await Context.UserAccounts.AddAsync(new SurveyorLedger.Data.Entities.UserAccount
        {
            Id = accountId, PersonId = invitation.UserId, PasswordHash = "already-has-a-password",
            EmailVerified = true, HasCompletedSignup = true, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        await _invitationService.DeclineInvitationAsync(invitation.Id, accountId);

        var reloaded = await Context.Invitations.FirstAsync(i => i.Id == invitation.Id);
        Assert.Equal("Declined", reloaded.Status);

        var access = await Context.UserAccesses.AnyAsync(ua => ua.UserId == accountId);
        Assert.False(access);
    }

    [Fact]
    public async Task DeclineInvitation_WrongUser_Forbidden()
    {
        _invitationService = GetService<IInvitationService>();
        var invitation = await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("someone@test.local"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _invitationService.DeclineInvitationAsync(invitation.Id, SurveyorId));
    }

    [Fact]
    public async Task PendingInvitee_GetsAJobScopeInviteInstead_UntilTheyAccept()
    {
        // A person who has been invited but hasn't accepted holds no UserAccess of any
        // scope yet, so they have no consent coverage for the job - AddParticipantAsync
        // falls back to creating a job-scope invite instead of an instant grant (same
        // job-only-assignment rule ScopedAccessService.HasConsentCoverageAsync enforces
        // everywhere else), rather than rejecting outright: the account already exists
        // (IsActive) even before accept, so there's someone real to invite.
        _invitationService = GetService<IInvitationService>();
        var jobService = GetService<IJobService>();

        var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey job" });
        var invitation = await _invitationService.CreateInvitationAsync(
            WorkspaceId, AdminId, NewPersonRequest("pending.person@test.local", "Surveyor"));

        var firstAttempt = await jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, invitation.UserId, "Surveyor");
        Assert.Null(firstAttempt.Access);
        Assert.NotNull(firstAttempt.Invitation);
        // Surveyor chains to WorkspaceMember, so the job-triggered invite is created at
        // Workspace scope (the highest level actually granted), with the job as its descendant.
        Assert.Equal(Constants.ScopeTypes.Workspace, firstAttempt.Invitation!.ScopeType);
        Assert.Equal(Constants.ScopeTypes.Job, firstAttempt.Invitation.DescendantScopeType);
        Assert.Equal(job.Id, firstAttempt.Invitation.DescendantScopeId);

        // Setting a password isn't enough either - still no workspace-scope access until
        // the workspace invite itself is accepted.
        await _invitationService.CompleteInvitationAsync(invitation.Token, new CompleteInvitationRequest
        {
            Password = "SomePassword123!",
            ConfirmPassword = "SomePassword123!",
            FirstName = "Pending",
            LastName = "Person"
        });

        var secondAttempt = await jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, invitation.UserId, "Surveyor");
        Assert.Null(secondAttempt.Access);
        Assert.NotNull(secondAttempt.Invitation);

        // Accepting the workspace invite gives consent coverage - now assignment is instant.
        var acceptAccountId = await GetAccountIdAsync(invitation.UserId) ?? throw new Exception("Account should exist after completing invitation.");
        await _invitationService.AcceptInvitationAsync(invitation.Id, acceptAccountId);

        var grant = await jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, invitation.UserId, "Surveyor");
        Assert.Equal(Constants.ScopeTypes.Job, grant.Access?.ScopeType);
        Assert.Equal(job.Id, grant.Access?.ScopeId);
    }

    [Fact]
    public async Task DeclinedInvitee_StillGetsAWorkspaceInvite_NotAnInstantGrant()
    {
        // Declining only marks the workspace invite Declined - it doesn't deactivate the
        // account. No consent coverage exists, so job assignment still falls back to a
        // fresh invite (Workspace scope, Job descendant - Surveyor chains) rather than
        // throwing or granting instantly.
        _invitationService = GetService<IInvitationService>();
        var jobService = GetService<IJobService>();

        var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey job" });
        var invitation = await _invitationService.CreateInvitationAsync(
            WorkspaceId, AdminId, NewPersonRequest("declined.person@test.local", "Surveyor"));
        var accountId = Guid.NewGuid();
        await Context.UserAccounts.AddAsync(new SurveyorLedger.Data.Entities.UserAccount
        {
            Id = accountId, PersonId = invitation.UserId, PasswordHash = "already-has-a-password",
            EmailVerified = true, HasCompletedSignup = true, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();
        await _invitationService.DeclineInvitationAsync(invitation.Id, accountId);

        var result = await jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, invitation.UserId, "Surveyor");
        Assert.Null(result.Access);
        Assert.NotNull(result.Invitation);
        Assert.Equal(Constants.ScopeTypes.Workspace, result.Invitation!.ScopeType);
        Assert.Equal(Constants.ScopeTypes.Job, result.Invitation.DescendantScopeType);
    }

    [Fact]
    public async Task AcceptingJobTriggeredInvite_GrantsBothJobRoleAndWorkspaceMember()
    {
        // End-to-end proof: a brand-new person invited via a Job assignment (Surveyor, which
        // chains) ends up, after accepting, with BOTH the specific Job-scope role they were
        // actually assigned AND the Workspace-scope WorkspaceMember baseline - in one accept.
        _invitationService = GetService<IInvitationService>();
        var jobService = GetService<IJobService>();

        var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey job" });
        var result = await jobService.InviteParticipantByEmailAsync(
            WorkspaceId, AdminId, job.Id, "Surveyor", "brandnew.surveyor@test.local", "Brand", "New", null, null);

        Assert.Equal(Constants.ScopeTypes.Workspace, result.ScopeType);
        Assert.Equal(WorkspaceId, result.ScopeId);
        Assert.Equal(Constants.ScopeTypes.Job, result.DescendantScopeType);
        Assert.Equal(job.Id, result.DescendantScopeId);

        await _invitationService.CompleteInvitationAsync(result.Token, new CompleteInvitationRequest
        {
            Password = "SomePassword123!",
            ConfirmPassword = "SomePassword123!",
            FirstName = "Brand",
            LastName = "New"
        });
        var accountId = await GetAccountIdAsync(result.UserId) ?? throw new Exception("Account should exist after completing invitation.");

        await _invitationService.AcceptInvitationAsync(result.Id, accountId);

        var jobAccess = await Context.UserAccesses.AnyAsync(ua =>
            ua.UserId == accountId && ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == job.Id && ua.IsActive);
        Assert.True(jobAccess, "Expected the descendant Job-scope Surveyor grant to exist after accept.");

        var workspaceAccess = await Context.UserAccesses.AnyAsync(ua =>
            ua.UserId == accountId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == WorkspaceId && ua.IsActive);
        Assert.True(workspaceAccess, "Expected the chained WorkspaceMember grant to exist after accept.");
    }

    [Fact]
    public async Task GetMyInvitations_ReturnsInvitationsForThatUserOnly()
    {
        _invitationService = GetService<IInvitationService>();
        var mine = await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("mine@test.local"));
        await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("someone.elses@test.local"));

        var accountId = Guid.NewGuid();
        await Context.UserAccounts.AddAsync(new SurveyorLedger.Data.Entities.UserAccount
        {
            Id = accountId, PersonId = mine.UserId, PasswordHash = "already-has-a-password",
            EmailVerified = true, HasCompletedSignup = true, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _invitationService.GetMyInvitationsAsync(accountId);

        var item = Assert.Single(result);
        Assert.Equal(mine.Id, item.Id);
    }
}
