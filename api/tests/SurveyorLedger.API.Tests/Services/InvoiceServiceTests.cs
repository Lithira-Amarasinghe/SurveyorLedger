using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class InvoiceServiceTests : WorkspaceIntegrationTestBase
{
    private IClientService _clientService = null!;
    private IInvoiceService _invoiceService = null!;
    private Guid _clientId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-invoice-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task<Guid> SeedInvoiceAsync(DateTime? dueDate = null)
    {
        _clientService = GetService<IClientService>();
        _invoiceService = GetService<IInvoiceService>();
        var client = await _clientService.CreateAsync(AdminId, new ClientRequest { Name = "Acme Ltd" });
        _clientId = client.Id;

        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientId,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 100000m } },
            TaxRatePercent = 0,
            DiscountAmount = 0,
            DueDate = dueDate,
            Status = "Sent"
        });
        return invoice.Id;
    }

    [Fact]
    public async Task RecordPaymentAsync_PartialPayment_SetsPartiallyPaid()
    {
        var invoiceId = await SeedInvoiceAsync();
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 40000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, AdminId, invoiceId);
        Assert.Equal("PartiallyPaid", invoice.Status);
    }

    [Fact]
    public async Task RecordPaymentAsync_FullPayment_SetsPaid()
    {
        var invoiceId = await SeedInvoiceAsync();
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 100000m, Method = "BankTransfer", ReceivedAt = DateTime.UtcNow }, null);

        var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, AdminId, invoiceId);
        Assert.Equal("Paid", invoice.Status);
    }

    [Fact]
    public async Task RecordPaymentAsync_Overpayment_Throws()
    {
        var invoiceId = await SeedInvoiceAsync();
        await Assert.ThrowsAsync<ValidationException>(
            () => _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 150000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null));
    }

    [Fact]
    public async Task RecordPaymentAsync_AssignsSequentialReceiptNumbers()
    {
        var invoiceId = await SeedInvoiceAsync();
        var p1 = await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 30000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);
        var p2 = await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 20000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        Assert.Equal("RCP-0001", p1.ReceiptNumber);
        Assert.Equal("RCP-0002", p2.ReceiptNumber);
    }

    [Fact]
    public async Task DeleteAsync_WithPayments_Throws409()
    {
        var invoiceId = await SeedInvoiceAsync();
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 10000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _invoiceService.DeleteAsync(WorkspaceId, AdminId, invoiceId));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task ComputeInvoiceTotals_OverdueSentInvoice_ReportsDaysOverdue()
    {
        var invoiceId = await SeedInvoiceAsync(DateTime.UtcNow.Date.AddDays(-5));
        var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, AdminId, invoiceId);
        var (_, _, _, isOverdue, daysOverdue) = _invoiceService.ComputeInvoiceTotals(invoice);

        Assert.True(isOverdue);
        Assert.Equal(5, daysOverdue);
    }

    [Fact]
    public async Task CreateAsync_ValidatesClientIdAgainstPerson_NotClientEntity()
    {
        var person = new SurveyorLedger.Data.Entities.Person
        {
            Id = Guid.NewGuid(), FirstName = "Client", LastName = "Person", Email = "client-person@test.local",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.People.Add(person);
        await Context.SaveChangesAsync();

        var svc = GetService<IInvoiceService>();
        var invoice = await svc.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = person.Id,
            LineItems = new() { new LineItemDto { Description = "Survey fee", Quantity = 1, UnitPrice = 5000 } }
        });

        Assert.Equal(person.Id, invoice.ClientId);
    }

    [Fact]
    public async Task ClientService_SearchAsync_IsGlobal_NotWorkspaceFiltered()
    {
        var clientService = GetService<IClientService>();
        var created = await clientService.CreateAsync(AdminId, new ClientRequest { Name = "Global Client", Email = "global@test.local" });

        var results = await clientService.SearchAsync(AdminId, "Global");

        Assert.Contains(results, p => p.Id == created.Id);
    }
}
