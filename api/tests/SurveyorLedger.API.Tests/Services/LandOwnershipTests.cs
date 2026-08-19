using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Invitation;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Covers land ownership tracking added this session: an owner is either an existing
/// account (OwnerId, system-wide, no workspace-membership requirement) or plain contact
/// info (OwnerName/Phone/Email), never both - decoupled entirely from workspace access.
/// </summary>
public class LandOwnershipTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILandService, LandService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppSettings:UiBaseUrl"] = "https://test.local",
                ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-landownership-test-{Guid.NewGuid():N}")
            })
            .Build());
    }

    private static LandRequest NewLandRequest() => new()
    {
        Address = new LandAddressDto { District = "Colombo" }
    };

    [Fact]
    public async Task CreateLand_WithExistingAccountOwner_SetsOwnerId()
    {
        _landService = GetService<ILandService>();
        var request = NewLandRequest();
        request.OwnerId = ClientPersonId;

        var land = await _landService.CreateAsync(WorkspaceId, AdminId, request);

        Assert.Equal(ClientPersonId, land.OwnerId);
        Assert.Null(land.OwnerName);
    }

    [Fact]
    public async Task CreateLand_WithPlainOwner_NoAccount_SavesContactInfo()
    {
        _landService = GetService<ILandService>();
        var request = NewLandRequest();
        request.OwnerName = "Someone Not In The System";
        request.OwnerPhone = "0771234567";

        var land = await _landService.CreateAsync(WorkspaceId, AdminId, request);

        Assert.Null(land.OwnerId);
        Assert.Equal("Someone Not In The System", land.OwnerName);
        Assert.Equal("0771234567", land.OwnerPhone);
    }

    [Fact]
    public async Task CreateLand_WithBothOwnerForms_Rejected()
    {
        _landService = GetService<ILandService>();
        var request = NewLandRequest();
        request.OwnerId = ClientPersonId;
        request.OwnerName = "Also A Name";

        await Assert.ThrowsAsync<ValidationException>(
            () => _landService.CreateAsync(WorkspaceId, AdminId, request));
    }

    [Fact]
    public async Task CreateLand_WithUnknownOwnerId_Rejected()
    {
        _landService = GetService<ILandService>();
        var request = NewLandRequest();
        request.OwnerId = Guid.NewGuid();

        await Assert.ThrowsAsync<ValidationException>(
            () => _landService.CreateAsync(WorkspaceId, AdminId, request));
    }

    [Fact]
    public async Task CreateLand_OwnerAccountNeedsNoWorkspaceMembership()
    {
        // Owner search is explicitly system-wide - a User who has never been invited to
        // this (or any) workspace can still be set as a land owner.
        _landService = GetService<ILandService>();

        var outsiderId = Guid.NewGuid();
        await Context.People.AddAsync(new SurveyorLedger.Data.Entities.Person
        {
            Id = outsiderId,
            FirstName = "Outside",
            LastName = "Person",
            Email = "outsider@test.local",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        var request = NewLandRequest();
        request.OwnerId = outsiderId;

        var land = await _landService.CreateAsync(WorkspaceId, AdminId, request);

        Assert.Equal(outsiderId, land.OwnerId);
    }

    [Fact]
    public async Task DeclinedInvitee_UserRowStillUsableAsLandOwner()
    {
        // Proves the decoupling this design relies on: a User row created for an
        // invitation persists (and stays usable as a land owner) regardless of whether
        // that invitation is ever accepted.
        var invitationService = GetService<IInvitationService>();
        _landService = GetService<ILandService>();

        var invitation = await invitationService.CreateInvitationAsync(WorkspaceId, AdminId, new InvitationRequest
        {
            Email = "declined.owner@test.local",
            Role = "Member",
            FirstName = "Declined",
            LastName = "Owner"
        });
        // DeclineInvitationAsync's callerUserId is a UserAccount.Id, distinct from
        // invitation.UserId (a Person.Id) post-split - simulate an already-registered
        // account declining.
        var accountId = Guid.NewGuid();
        await Context.UserAccounts.AddAsync(new SurveyorLedger.Data.Entities.UserAccount
        {
            Id = accountId, PersonId = invitation.UserId, PasswordHash = "already-has-a-password",
            EmailVerified = true, HasCompletedSignup = true, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();
        await invitationService.DeclineInvitationAsync(invitation.Id, accountId);

        var request = NewLandRequest();
        request.OwnerId = invitation.UserId;

        var land = await _landService.CreateAsync(WorkspaceId, AdminId, request);

        Assert.Equal(invitation.UserId, land.OwnerId);
    }

    [Fact]
    public async Task UpdateLand_SwitchingFromAccountOwnerToPlainOwner_ClearsOwnerId()
    {
        _landService = GetService<ILandService>();
        var createRequest = NewLandRequest();
        createRequest.OwnerId = ClientPersonId;
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, createRequest);

        var updateRequest = NewLandRequest();
        updateRequest.OwnerName = "Switched To Plain";
        var updated = await _landService.UpdateAsync(WorkspaceId, AdminId, land.Id, updateRequest);

        Assert.Null(updated.OwnerId);
        Assert.Equal("Switched To Plain", updated.OwnerName);
    }
}
