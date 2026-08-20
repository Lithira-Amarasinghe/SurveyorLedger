using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class MilestoneFeeCeilingTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IMilestoneService _milestoneService = null!;
    private IQuotationService _quotationService = null!;
    private IInvoiceService _invoiceService = null!;
    private Guid _jobId;
    private Guid _clientPersonId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-milestone-ceiling-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private async Task<Milestone> SeedMilestoneAsync(decimal? amount)
    {
        _jobService = GetService<IJobService>();
        _milestoneService = GetService<IMilestoneService>();
        _quotationService = GetService<IQuotationService>();
        _invoiceService = GetService<IInvoiceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        _jobId = job.Id;
        _clientPersonId = await GrantClientBillingRoleAsync(_jobId);
        return await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobId, new MilestoneRequest { Title = "Land Survey", Amount = amount });
    }

    private InvoiceRequest DirectInvoiceFor(Guid milestoneId, decimal amount) => new()
    {
        ClientId = _clientPersonId, JobId = _jobId,
        LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = amount, MilestoneId = milestoneId } },
        TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
    };

    [Fact]
    public async Task QuotationLine_PlusDirectInvoice_UnderTheFee_BothSucceed()
    {
        var milestone = await SeedMilestoneAsync(80000m);
        await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 30000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });

        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, DirectInvoiceFor(milestone.Id, 40000m));
        Assert.Equal(milestone.Id, invoice.LineItems.Single().MilestoneId);
    }

    [Fact]
    public async Task QuotationLine_PlusDirectInvoice_OverTheFee_InvoiceRejected()
    {
        var milestone = await SeedMilestoneAsync(80000m);
        await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 50000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });

        await Assert.ThrowsAsync<ValidationException>(
            () => _invoiceService.CreateAsync(WorkspaceId, AdminId, DirectInvoiceFor(milestone.Id, 40000m)));
    }

    [Fact]
    public async Task DirectInvoice_ThenQuotationLine_OverTheFee_QuotationRejected()
    {
        var milestone = await SeedMilestoneAsync(80000m);
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, DirectInvoiceFor(milestone.Id, 50000m));

        var request = new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 40000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        };

        await Assert.ThrowsAsync<ValidationException>(() => _quotationService.CreateAsync(WorkspaceId, AdminId, request));
    }

    [Fact]
    public async Task QuotationDrawnInvoiceLine_DoesNotDoubleCount()
    {
        var milestone = await SeedMilestoneAsync(80000m);
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 80000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });
        var quotationLineId = quotation.LineItems[0].Id;

        // Drawing the full 80000 from the quotation line should succeed - it's already
        // counted via the quotation line, not double-charged against the ceiling.
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 80000m, QuotationLineId = quotationLineId } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });

        // MilestoneId auto-copied from the quotation line onto the invoice line.
        Assert.Equal(milestone.Id, invoice.LineItems.Single().MilestoneId);
    }

    [Fact]
    public async Task MilestoneWithNoFee_AllowsUnlimitedLines()
    {
        var milestone = await SeedMilestoneAsync(null);
        await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 999999m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });

        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, DirectInvoiceFor(milestone.Id, 999999m));
        Assert.Equal(milestone.Id, invoice.LineItems.Single().MilestoneId);
    }

    [Fact]
    public async Task ConflictingExplicitMilestoneId_OnQuotationDrawnLine_IsRejected()
    {
        var milestone = await SeedMilestoneAsync(80000m);
        var otherMilestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobId, new MilestoneRequest { Title = "Plan Preparation", Amount = 20000m });
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 80000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });
        var quotationLineId = quotation.LineItems[0].Id;

        var request = new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 80000m, QuotationLineId = quotationLineId, MilestoneId = otherMilestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        };

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, request));
    }
}
