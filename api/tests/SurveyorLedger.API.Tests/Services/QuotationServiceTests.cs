using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Configurations;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class QuotationServiceTests : WorkspaceIntegrationTestBase
{
    private IQuotationService _quotationService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-quotation-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private async Task<(Job Job, Guid ClientPersonId)> SeedJobWithClientParticipantAsync()
    {
        var jobService = GetService<IJobService>();
        _quotationService = GetService<IQuotationService>();
        var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Test Job" });

        var clientPerson = new Person
        {
            Id = Guid.NewGuid(), FirstName = "Acme", LastName = "Ltd", Email = $"client-{Guid.NewGuid():N}@test.local",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.People.Add(clientPerson);
        var clientAccount = new UserAccount
        {
            Id = Guid.NewGuid(), PersonId = clientPerson.Id, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.UserAccounts.Add(clientAccount);
        await Context.SaveChangesAsync();

        Context.UserAccesses.Add(new UserAccess
        {
            Id = Guid.NewGuid(), UserId = clientAccount.Id, RoleId = RoleConfiguration.ClientRoleId,
            ScopeType = Constants.ScopeTypes.Job, ScopeId = job.Id, IsActive = true,
            AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        await CasbinService.AddRoleForUserAsync(clientAccount.Id.ToString(), Constants.SystemRoles.Client, job.Id.ToString());

        return (job, clientPerson.Id);
    }

    private static QuotationRequest MakeRequest(Guid clientId, Guid jobId, string? status = null) => new()
    {
        ClientId = clientId,
        JobId = jobId,
        LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 50000m } },
        TaxRatePercent = 10m,
        Status = status
    };

    [Fact]
    public async Task CreateAsync_ComputesTotalWithTax()
    {
        var (job, clientPersonId) = await SeedJobWithClientParticipantAsync();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, MakeRequest(clientPersonId, job.Id));

        Assert.Equal("Q-0001", quotation.Number);
        Assert.Single(quotation.LineItems);
    }

    [Fact]
    public async Task UpdateAsync_AfterSent_BumpsRevisionNumber()
    {
        var (job, clientPersonId) = await SeedJobWithClientParticipantAsync();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, MakeRequest(clientPersonId, job.Id, "Sent"));
        Assert.Equal(0, quotation.RevisionNumber);

        var updated = await _quotationService.UpdateAsync(WorkspaceId, AdminId, quotation.Id, MakeRequest(clientPersonId, job.Id, "Sent"));
        Assert.Equal(1, updated.RevisionNumber);
    }

    [Fact]
    public async Task CreateAsync_NegativeTaxRate_Throws()
    {
        var (job, clientId) = await SeedJobWithClientParticipantAsync();
        var request = MakeRequest(clientId, job.Id);
        request.TaxRatePercent = -5m;

        await Assert.ThrowsAsync<ValidationException>(() => _quotationService.CreateAsync(WorkspaceId, AdminId, request));
    }

}
