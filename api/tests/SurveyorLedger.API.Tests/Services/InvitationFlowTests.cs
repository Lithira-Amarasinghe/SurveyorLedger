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

    private InvitationRequest NewPersonRequest(string email, string role = "Client") => new()
    {
        Email = email,
        Role = role,
        FirstName = "New",
        LastName = "Person"
    };

    [Fact]
    public async Task CreateInvitation_ForBrandNewEmail_CreatesUserButNoAccess()
    {
        _invitationService = GetService<IInvitationService>();

        var invitation = await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("new.person@test.local"));

        Assert.Equal("Pending", invitation.Status);
        var user = await Context.Users.FirstOrDefaultAsync(u => u.Id == invitation.UserId);
        Assert.NotNull(user);
        Assert.Null(user!.PasswordHash);

        var access = await Context.UserAccesses.AnyAsync(ua => ua.UserId == invitation.UserId);
        Assert.False(access);
    }

    [Fact]
    public async Task CreateInvitation_AlreadyMember_Rejected()
    {
        _invitationService = GetService<IInvitationService>();

        await Assert.ThrowsAsync<AppException>(
            () => _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("surveyor@test.local", "Surveyor")));
    }

    [Fact]
    public async Task Surveyor_CanInviteAsClient()
    {
        _invitationService = GetService<IInvitationService>();

        var invitation = await _invitationService.CreateInvitationAsync(WorkspaceId, SurveyorId, NewPersonRequest("client.candidate@test.local", "Client"));

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

        var user = await Context.Users.FirstAsync(u => u.Id == invitation.UserId);
        Assert.NotNull(user.PasswordHash);
        Assert.True(user.EmailVerified);

        // Setting a password is not the same as accepting - no access yet, invite still Pending.
        var access = await Context.UserAccesses.FirstOrDefaultAsync(ua =>
            ua.UserId == invitation.UserId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == WorkspaceId);
        Assert.Null(access);

        var reloaded = await Context.Invitations.FirstAsync(i => i.Id == invitation.Id);
        Assert.Equal("Pending", reloaded.Status);

        // The explicit Accept step, now separate, is what actually grants access.
        await _invitationService.AcceptInvitationAsync(invitation.Id, invitation.UserId);

        access = await Context.UserAccesses.FirstOrDefaultAsync(ua =>
            ua.UserId == invitation.UserId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == WorkspaceId);
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

        var user = await Context.Users.FirstAsync(u => u.Id == invitation.UserId);
        user.PasswordHash = "already-has-a-password";
        await Context.SaveChangesAsync();

        var accepted = await _invitationService.AcceptInvitationAsync(invitation.Id, invitation.UserId);

        Assert.Equal("Accepted", accepted.Status);
        var access = await Context.UserAccesses.AnyAsync(ua =>
            ua.UserId == invitation.UserId && ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == WorkspaceId);
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
        _invitationService = GetService<IInvitationService>();
        var invitation = await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("decline.me@test.local"));

        await _invitationService.DeclineInvitationAsync(invitation.Id, invitation.UserId);

        var reloaded = await Context.Invitations.FirstAsync(i => i.Id == invitation.Id);
        Assert.Equal("Declined", reloaded.Status);

        var access = await Context.UserAccesses.AnyAsync(ua => ua.UserId == invitation.UserId);
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
    public async Task PendingInvitee_CannotBeAssignedToAJob_UntilTheyAccept()
    {
        // The end-to-end rule the whole design rests on: workspace first, then job. A
        // person who has been invited but hasn't accepted holds no UserAccess of any
        // scope, so there is nothing to build a job-scope grant from.
        _invitationService = GetService<IInvitationService>();
        var jobService = GetService<IJobService>();

        var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey job" });
        var invitation = await _invitationService.CreateInvitationAsync(
            WorkspaceId, AdminId, NewPersonRequest("pending.person@test.local", "Surveyor"));

        var rejected = await Assert.ThrowsAsync<AppException>(
            () => jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, invitation.UserId));
        Assert.Equal(Constants.ErrorCodes.UserNotFound, rejected.Code);

        // Setting a password isn't enough - assignment still fails until the invite is
        // actually accepted.
        await _invitationService.CompleteInvitationAsync(invitation.Token, new CompleteInvitationRequest
        {
            Password = "SomePassword123!",
            ConfirmPassword = "SomePassword123!",
            FirstName = "Pending",
            LastName = "Person"
        });

        var stillRejected = await Assert.ThrowsAsync<AppException>(
            () => jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, invitation.UserId));
        Assert.Equal(Constants.ErrorCodes.UserNotFound, stillRejected.Code);

        // Accepting makes them a real member, and only then does assignment succeed.
        await _invitationService.AcceptInvitationAsync(invitation.Id, invitation.UserId);

        var grant = await jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, invitation.UserId);
        Assert.Equal(Constants.ScopeTypes.Job, grant.ScopeType);
        Assert.Equal(job.Id, grant.ScopeId);
    }

    [Fact]
    public async Task DeclinedInvitee_StillCannotBeAssignedToAJob()
    {
        _invitationService = GetService<IInvitationService>();
        var jobService = GetService<IJobService>();

        var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey job" });
        var invitation = await _invitationService.CreateInvitationAsync(
            WorkspaceId, AdminId, NewPersonRequest("declined.person@test.local", "Surveyor"));
        await _invitationService.DeclineInvitationAsync(invitation.Id, invitation.UserId);

        await Assert.ThrowsAsync<AppException>(
            () => jobService.AddParticipantAsync(WorkspaceId, AdminId, job.Id, invitation.UserId));
    }

    [Fact]
    public async Task GetMyInvitations_ReturnsInvitationsForThatUserOnly()
    {
        _invitationService = GetService<IInvitationService>();
        var mine = await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("mine@test.local"));
        await _invitationService.CreateInvitationAsync(WorkspaceId, AdminId, NewPersonRequest("someone.elses@test.local"));

        var result = await _invitationService.GetMyInvitationsAsync(mine.UserId);

        var item = Assert.Single(result);
        Assert.Equal(mine.Id, item.Id);
    }
}
