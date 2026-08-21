using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class WorkspaceLevelBillingTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IQuotationService _quotationService = null!;
    private IInvoiceService _invoiceService = null!;
    private IMilestoneService _milestoneService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-workspace-billing-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private void Init()
    {
        _jobService = GetService<IJobService>();
        _quotationService = GetService<IQuotationService>();
        _invoiceService = GetService<IInvoiceService>();
        _milestoneService = GetService<IMilestoneService>();
    }

    [Fact]
    public async Task CreateAsync_WorkspaceLevelQuotation_HasNoJobId()
    {
        Init();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            JobId = null,
            LineItems = new() { new LineItemDto { Description = "Consulting", Quantity = 1, UnitPrice = 5000m } },
            TaxRatePercent = 0
        });

        Assert.Null(quotation.JobId);
        Assert.Equal(WorkspaceId, quotation.WorkspaceId);
    }

    [Fact]
    public async Task CreateAsync_WorkspaceLevelInvoice_HasNoJobId()
    {
        Init();
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            JobId = null,
            LineItems = new() { new LineItemDto { Description = "Consulting", Quantity = 1, UnitPrice = 5000m } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });

        Assert.Null(invoice.JobId);
        Assert.Equal(WorkspaceId, invoice.WorkspaceId);
    }

    [Fact]
    public async Task CreateAsync_WorkspaceLevelQuotation_WithMilestoneId_IsRejected()
    {
        Init();
        var request = new QuotationRequest
        {
            JobId = null,
            LineItems = new() { new LineItemDto { Description = "Consulting", Quantity = 1, UnitPrice = 5000m, MilestoneId = Guid.NewGuid() } },
            TaxRatePercent = 0
        };

        await Assert.ThrowsAsync<ValidationException>(() => _quotationService.CreateAsync(WorkspaceId, AdminId, request));
    }

    [Fact]
    public async Task CreateAsync_WorkspaceLevelInvoice_WithQuotationLineId_IsRejected()
    {
        Init();
        var request = new InvoiceRequest
        {
            JobId = null,
            LineItems = new() { new LineItemDto { Description = "Consulting", Quantity = 1, UnitPrice = 5000m, QuotationLineId = Guid.NewGuid() } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        };

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, request));
    }

    [Fact]
    public async Task CreateAsync_WorkspaceLevelQuotation_NumberedPerWorkspace()
    {
        Init();
        var first = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            JobId = null,
            LineItems = new() { new LineItemDto { Description = "A", Quantity = 1, UnitPrice = 100m } },
            TaxRatePercent = 0
        });
        var second = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            JobId = null,
            LineItems = new() { new LineItemDto { Description = "B", Quantity = 1, UnitPrice = 200m } },
            TaxRatePercent = 0
        });

        Assert.Equal("Q-0001", first.Number);
        Assert.Equal("Q-0002", second.Number);
    }

    [Fact]
    public async Task Search_ReturnsBothJobScopedAndWorkspaceLevelQuotations()
    {
        Init();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var jobScoped = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            JobId = job.Id,
            LineItems = new() { new LineItemDto { Description = "Survey", Quantity = 1, UnitPrice = 1000m } },
            TaxRatePercent = 0
        });
        var workspaceLevel = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            JobId = null,
            LineItems = new() { new LineItemDto { Description = "Consulting", Quantity = 1, UnitPrice = 2000m } },
            TaxRatePercent = 0
        });

        var results = await _quotationService.SearchAsync(WorkspaceId, AdminId);

        Assert.Contains(results, q => q.Id == jobScoped.Id);
        Assert.Contains(results, q => q.Id == workspaceLevel.Id);
    }

    [Fact]
    public async Task GetPaymentStatusAsync_ReflectsLinkedQuotationsAndInvoices()
    {
        Init();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, job.Id, new MilestoneRequest { Title = "Land Survey", Amount = 80000m });

        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            JobId = job.Id,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 50000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0
        });
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            JobId = job.Id,
            LineItems = new() { new LineItemDto { Description = "Land Survey (direct)", Quantity = 1, UnitPrice = 20000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });

        var status = await _milestoneService.GetPaymentStatusAsync(WorkspaceId, AdminId, job.Id, milestone.Id);

        Assert.Single(status.LinkedQuotations);
        Assert.Equal(quotation.Id, status.LinkedQuotations[0].QuotationId);
        Assert.Single(status.LinkedInvoices);
        Assert.Equal(70000m, status.CommittedAmount);
    }
}
