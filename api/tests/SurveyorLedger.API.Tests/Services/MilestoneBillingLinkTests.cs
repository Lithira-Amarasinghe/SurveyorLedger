using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class MilestoneBillingLinkTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IMilestoneService _milestoneService = null!;
    private IInvoiceService _invoiceService = null!;
    private IQuotationService _quotationService = null!;
    private Guid _jobId;
    private Guid _milestoneId;
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
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-milestone-billing-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private async Task SeedAsync()
    {
        _jobService = GetService<IJobService>();
        _milestoneService = GetService<IMilestoneService>();
        _invoiceService = GetService<IInvoiceService>();
        _quotationService = GetService<IQuotationService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        _jobId = job.Id;
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobId, new MilestoneRequest { Title = "Deed Verified", Amount = 25000m });
        _milestoneId = milestone.Id;
        _clientPersonId = await GrantClientBillingRoleAsync(_jobId);
    }

    private InvoiceRequest InvoiceRequestFor(Guid? milestoneId) => new()
    {
        ClientId = _clientPersonId,
        JobId = _jobId,
        LineItems = new() { new LineItemDto { Description = "Deed Verified", Quantity = 1, UnitPrice = 25000m, MilestoneId = milestoneId } },
        TaxRatePercent = 0,
        DiscountAmount = 0,
        Installments = new()
    };

    [Fact]
    public async Task InvoiceLineItem_CarriesMilestoneId()
    {
        await SeedAsync();
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId));
        Assert.Equal(_milestoneId, invoice.LineItems.Single().MilestoneId);
    }

    [Fact]
    public async Task SecondInvoice_CannotClaimSameMilestone_WhileFirstIsActive()
    {
        await SeedAsync();
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId));

        await Assert.ThrowsAsync<ValidationException>(
            () => _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId)));
    }

    [Fact]
    public async Task Quotation_And_Invoice_CanEachHoldTheirOwnActiveLink_Simultaneously()
    {
        await SeedAsync();
        var quotationRequest = new QuotationRequest
        {
            ClientId = _clientPersonId,
            JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Deed Verified", Quantity = 1, UnitPrice = 25000m, MilestoneId = _milestoneId } },
            TaxRatePercent = 0
        };
        await _quotationService.CreateAsync(WorkspaceId, AdminId, quotationRequest);

        // Same milestone tagged on an invoice too - independent document types, both allowed.
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceRequestFor(_milestoneId));
        Assert.Equal(_milestoneId, invoice.LineItems.Single().MilestoneId);
    }
}
