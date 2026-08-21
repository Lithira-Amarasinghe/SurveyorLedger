using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class QuotationInvoiceLineTraceabilityTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IQuotationService _quotationService = null!;
    private IInvoiceService _invoiceService = null!;
    private Guid _jobId;
    private Guid _clientPersonId;

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
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-quotation-invoice-line-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private async Task<Quotation> SeedQuotationAsync()
    {
        _jobService = GetService<IJobService>();
        _quotationService = GetService<IQuotationService>();
        _invoiceService = GetService<IInvoiceService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        _jobId = job.Id;
        _clientPersonId = await GrantClientBillingRoleAsync(_jobId);

        return await _quotationService.CreateAsync(WorkspaceId, AdminId, new QuotationRequest
        {
            JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 80000m } },
            TaxRatePercent = 0
        });
    }

    private InvoiceRequest InvoiceFor(Guid quotationLineId, decimal amount) => new()
    {
        JobId = _jobId,
        LineItems = new() { new LineItemDto { Description = "Land Survey (partial)", Quantity = 1, UnitPrice = amount, QuotationLineId = quotationLineId } },
        TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
    };

    [Fact]
    public async Task TwoInvoices_CanPartiallyBillTheSameQuotationLine()
    {
        var quotation = await SeedQuotationAsync();
        var lineId = quotation.LineItems[0].Id;

        var first = await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));
        var second = await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));

        Assert.Equal(lineId, first.LineItems[0].QuotationLineId);
        Assert.Equal(lineId, second.LineItems[0].QuotationLineId);
    }

    [Fact]
    public async Task ThirdInvoice_ExceedingRemainingAmount_IsRejected()
    {
        var quotation = await SeedQuotationAsync();
        var lineId = quotation.LineItems[0].Id;

        await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 1m)));
    }

    [Fact]
    public async Task QuotationLineFromADifferentJob_IsRejected()
    {
        var quotation = await SeedQuotationAsync();
        var lineId = quotation.LineItems[0].Id;
        var otherJob = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });

        var request = InvoiceFor(lineId, 10000m);
        request.JobId = otherJob.Id;

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, request));
    }

    [Fact]
    public async Task GetAmountBilledAgainstQuotationLine_SumsAcrossActiveInvoicesOnly()
    {
        var quotation = await SeedQuotationAsync();
        var lineId = quotation.LineItems[0].Id;
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));

        Assert.Equal(40000m, _invoiceService.GetAmountBilledAgainstQuotationLine(_jobId, lineId));

        await _invoiceService.DeleteAsync(WorkspaceId, AdminId, invoice.Id);

        Assert.Equal(0m, _invoiceService.GetAmountBilledAgainstQuotationLine(_jobId, lineId));
    }

    [Fact]
    public async Task QuotationLineProgress_ReflectsInvoicedAndRemainingAfterEachInvoice()
    {
        var quotation = await SeedQuotationAsync();
        var lineId = quotation.LineItems[0].Id;

        var (invoicedBefore, remainingBefore) = _quotationService.ComputeLineProgress(_jobId, lineId, 80000m);
        Assert.Equal(0m, invoicedBefore);
        Assert.Equal(80000m, remainingBefore);

        await _invoiceService.CreateAsync(WorkspaceId, AdminId, InvoiceFor(lineId, 40000m));

        var (invoicedAfter, remainingAfter) = _quotationService.ComputeLineProgress(_jobId, lineId, 80000m);
        Assert.Equal(40000m, invoicedAfter);
        Assert.Equal(40000m, remainingAfter);
    }
}
