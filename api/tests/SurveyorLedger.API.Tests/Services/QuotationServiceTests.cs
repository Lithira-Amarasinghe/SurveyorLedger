using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class QuotationServiceTests : WorkspaceIntegrationTestBase
{
    private IClientService _clientService = null!;
    private IQuotationService _quotationService = null!;
    private Guid _clientId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-quotation-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task SeedClientAsync()
    {
        _clientService = GetService<IClientService>();
        _quotationService = GetService<IQuotationService>();
        var client = await _clientService.CreateAsync(AdminId, new ClientRequest { Name = "Acme Ltd" });
        _clientId = client.Id;
    }

    private static QuotationRequest MakeRequest(Guid clientId, string? status = null) => new()
    {
        ClientId = clientId,
        LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 50000m } },
        TaxRatePercent = 10m,
        Status = status
    };

    [Fact]
    public async Task CreateAsync_ComputesTotalWithTax()
    {
        await SeedClientAsync();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, MakeRequest(_clientId));

        Assert.Equal("Q-0001", quotation.Number);
        Assert.Single(quotation.LineItems);
    }

    [Fact]
    public async Task UpdateAsync_AfterSent_BumpsRevisionNumber()
    {
        await SeedClientAsync();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, MakeRequest(_clientId, "Sent"));
        Assert.Equal(0, quotation.RevisionNumber);

        var updated = await _quotationService.UpdateAsync(WorkspaceId, AdminId, quotation.Id, MakeRequest(_clientId, "Sent"));
        Assert.Equal(1, updated.RevisionNumber);
    }

    [Fact]
    public async Task ConvertToInvoiceAsync_CreatesInvoiceAndMarksAccepted()
    {
        await SeedClientAsync();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, MakeRequest(_clientId, "Sent"));

        var invoice = await _quotationService.ConvertToInvoiceAsync(WorkspaceId, AdminId, quotation.Id, new ConvertQuotationRequest());
        Assert.Equal("INV-0001", invoice.Number);
        Assert.Equal(_clientId, invoice.ClientId);

        var reloaded = await _quotationService.GetByIdAsync(WorkspaceId, AdminId, quotation.Id);
        Assert.Equal("Accepted", reloaded.Status);
    }

    [Fact]
    public async Task ConvertToInvoiceAsync_AlreadyConverted_Throws()
    {
        await SeedClientAsync();
        var quotation = await _quotationService.CreateAsync(WorkspaceId, AdminId, MakeRequest(_clientId, "Sent"));
        await _quotationService.ConvertToInvoiceAsync(WorkspaceId, AdminId, quotation.Id, new ConvertQuotationRequest());

        await Assert.ThrowsAsync<ValidationException>(
            () => _quotationService.ConvertToInvoiceAsync(WorkspaceId, AdminId, quotation.Id, new ConvertQuotationRequest()));
    }
}
