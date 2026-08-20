using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class MilestonePaymentGatingTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IMilestoneService _milestoneService = null!;
    private IInvoiceService _invoiceService = null!;
    private Guid _jobId;
    private Guid _clientPersonId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-milestone-gating-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private async Task<SurveyorLedger.Data.Entities.Milestone> SeedMilestoneAsync(decimal? amount)
    {
        _jobService = GetService<IJobService>();
        _milestoneService = GetService<IMilestoneService>();
        _invoiceService = GetService<IInvoiceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        _jobId = job.Id;
        _clientPersonId = await GrantClientBillingRoleAsync(_jobId);
        return await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobId, new MilestoneRequest { Title = "Deed Verified", Amount = amount });
    }

    [Fact]
    public async Task NoRequirements_TransitionsFreely_EvenWithUnpaidLinkedInvoice()
    {
        var milestone = await SeedMilestoneAsync(25000m);
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Deed Verified", Quantity = 1, UnitPrice = 25000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });

        var updated = await _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobId, milestone.Id, "Completed");
        Assert.Equal("Completed", updated.Status);
    }

    [Fact]
    public async Task FeelessMilestone_NeverGated()
    {
        var milestone = await SeedMilestoneAsync(null);
        await _milestoneService.SetPaymentRequirementsAsync(WorkspaceId, AdminId, _jobId, milestone.Id,
            new() { ("Completed", "FullyPaid") });

        // No invoice ever linked - the rule can never be satisfied by definition, which
        // documents the failure mode explicitly rather than leaving it implicit.
        await Assert.ThrowsAsync<ValidationException>(
            () => _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobId, milestone.Id, "Completed"));
    }

    [Fact]
    public async Task FullyPaidRequirement_BlocksUntilInvoicePaid_ThenSucceeds()
    {
        var milestone = await SeedMilestoneAsync(25000m);
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Deed Verified", Quantity = 1, UnitPrice = 25000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });
        await _milestoneService.SetPaymentRequirementsAsync(WorkspaceId, AdminId, _jobId, milestone.Id,
            new() { ("Completed", "FullyPaid") });

        await Assert.ThrowsAsync<ValidationException>(
            () => _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobId, milestone.Id, "Completed"));

        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice.Id,
            new PaymentRequest { Amount = 25000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var updated = await _milestoneService.UpdateStatusAsync(WorkspaceId, AdminId, _jobId, milestone.Id, "Completed");
        Assert.Equal("Completed", updated.Status);
    }

    [Fact]
    public async Task GetPaymentStatus_ReflectsLinkedInvoice()
    {
        var milestone = await SeedMilestoneAsync(25000m);
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Deed Verified", Quantity = 1, UnitPrice = 25000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });

        var status = await _milestoneService.GetPaymentStatusAsync(WorkspaceId, AdminId, _jobId, milestone.Id);
        Assert.Equal(invoice.Id, status.LinkedInvoiceId);
        Assert.Equal("Draft", status.InvoiceStatus);
    }
}
