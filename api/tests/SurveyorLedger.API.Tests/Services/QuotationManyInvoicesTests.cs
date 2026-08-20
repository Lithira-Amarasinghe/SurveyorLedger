using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class QuotationManyInvoicesTests : WorkspaceIntegrationTestBase
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
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-quotation-many-invoices-test-{Guid.NewGuid():N}"),
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
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new()
            {
                new LineItemDto { Description = "Advance", Quantity = 1, UnitPrice = 30000m },
                new LineItemDto { Description = "Final", Quantity = 1, UnitPrice = 120000m }
            },
            TaxRatePercent = 0
        });
    }

    private InvoiceRequest DrawFrom(Quotation quotation, LineItemDto item) => new()
    {
        ClientId = _clientPersonId, JobId = _jobId, QuotationId = quotation.Id,
        LineItems = new() { item }, TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
    };

    [Fact]
    public async Task TwoInvoices_CanDrawFromTheSameQuotation()
    {
        var quotation = await SeedQuotationAsync();
        var first = await _invoiceService.CreateAsync(WorkspaceId, AdminId, DrawFrom(quotation, new LineItemDto { Description = quotation.LineItems[0].Description, Quantity = quotation.LineItems[0].Quantity, UnitPrice = quotation.LineItems[0].UnitPrice }));
        var second = await _invoiceService.CreateAsync(WorkspaceId, AdminId, DrawFrom(quotation, new LineItemDto { Description = quotation.LineItems[1].Description, Quantity = quotation.LineItems[1].Quantity, UnitPrice = quotation.LineItems[1].UnitPrice }));

        Assert.Equal(quotation.Id, first.QuotationId);
        Assert.Equal(quotation.Id, second.QuotationId);
    }

    [Fact]
    public async Task BillingProgress_SumsActiveInvoicesAgainstTheQuotation()
    {
        var quotation = await SeedQuotationAsync();
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, DrawFrom(quotation, new LineItemDto { Description = quotation.LineItems[0].Description, Quantity = quotation.LineItems[0].Quantity, UnitPrice = quotation.LineItems[0].UnitPrice }));

        var refreshed = await _quotationService.GetByIdAsync(WorkspaceId, AdminId, quotation.Id);
        var (invoiced, remaining) = _quotationService.ComputeBillingProgress(refreshed);

        Assert.Equal(30000m, invoiced);
        Assert.Equal(120000m, remaining);
    }

    [Fact]
    public async Task InvoiceRequest_RejectsQuotationFromADifferentJob()
    {
        var quotation = await SeedQuotationAsync();
        var otherJob = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        var otherClientPersonId = await GrantClientBillingRoleAsync(otherJob.Id);

        var request = DrawFrom(quotation, new LineItemDto { Description = quotation.LineItems[0].Description, Quantity = quotation.LineItems[0].Quantity, UnitPrice = quotation.LineItems[0].UnitPrice });
        request.JobId = otherJob.Id;
        request.ClientId = otherClientPersonId;

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, request));
    }

    [Fact]
    public async Task QuotationStatus_IsNotAutoAcceptedByDrawingAnInvoice()
    {
        var quotation = await SeedQuotationAsync();
        Assert.Equal("Draft", quotation.Status);
        await _invoiceService.CreateAsync(WorkspaceId, AdminId, DrawFrom(quotation, new LineItemDto { Description = quotation.LineItems[0].Description, Quantity = quotation.LineItems[0].Quantity, UnitPrice = quotation.LineItems[0].UnitPrice }));

        var refreshed = await _quotationService.GetByIdAsync(WorkspaceId, AdminId, quotation.Id);
        Assert.Equal("Draft", refreshed.Status);
    }
}
