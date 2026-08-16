using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class ClientServiceTests : WorkspaceIntegrationTestBase
{
    private IClientService _clientService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-client-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    [Fact]
    public async Task CreateAsync_PersistsClient()
    {
        _clientService = GetService<IClientService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd", Phone = "0771234567" });

        Assert.Equal("Acme Ltd", client.FirstName);
        var fetched = await _clientService.GetByIdAsync(WorkspaceId, AdminId, client.Id);
        Assert.Equal(client.Id, fetched.Id);
    }

    [Fact]
    public async Task GetByIdAsync_CallerNotMemberOfWorkspace_ThrowsForbidden()
    {
        // Matches Land/Job convention: the Casbin permission check runs against the
        // scope (workspaceId) argument first, before any record lookup - a caller with
        // no role in that workspace at all gets 403, not 404.
        _clientService = GetService<IClientService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" });

        var otherWorkspaceId = Guid.NewGuid();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _clientService.GetByIdAsync(otherWorkspaceId, AdminId, client.Id));
    }

    [Fact]
    public async Task CreateAsync_CallerWithoutPermission_ThrowsForbidden()
    {
        // Entity scoping (Person is global) is orthogonal to the Casbin permission gate,
        // which still runs against workspaceId. A bare Client-role caller has no
        // billingclient.create permission, so this must still 403.
        _clientService = GetService<IClientService>();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _clientService.CreateAsync(WorkspaceId, ClientId, new ClientRequest { Name = "Acme Ltd" }));
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        _clientService = GetService<IClientService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" });
        await _clientService.DeleteAsync(WorkspaceId, AdminId, client.Id);

        // Client has a global IsActive query filter, so a soft-deleted row drops out of
        // SearchAsync entirely rather than showing up with IsActive=false.
        var results = await _clientService.SearchAsync(WorkspaceId, AdminId, null);
        Assert.DoesNotContain(results, c => c.Id == client.Id);
    }

    [Fact]
    public async Task GetBalanceAsync_SumsOutstandingAcrossInvoices()
    {
        _clientService = GetService<IClientService>();
        var invoiceService = GetService<IInvoiceService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" });

        var invoice1 = await invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = client.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey A", Quantity = 1, UnitPrice = 100000m } },
            Status = "Sent"
        });
        await invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice1.Id, new PaymentRequest { Amount = 40000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var invoice2 = await invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = client.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey B", Quantity = 1, UnitPrice = 50000m } },
            Status = "Sent"
        });

        var balance = await _clientService.GetBalanceAsync(WorkspaceId, AdminId, client.Id);
        Assert.Equal(60000m + 50000m, balance);
    }

    [Fact]
    public async Task GetPaymentHistoryAsync_ReturnsPaymentsAcrossInvoices()
    {
        _clientService = GetService<IClientService>();
        var invoiceService = GetService<IInvoiceService>();
        var client = await _clientService.CreateAsync(WorkspaceId, AdminId, new ClientRequest { Name = "Acme Ltd" });

        var invoice = await invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = client.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 100000m } },
            Status = "Sent"
        });
        await invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice.Id, new PaymentRequest { Amount = 40000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);
        await invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice.Id, new PaymentRequest { Amount = 20000m, Method = "Cheque", ReceivedAt = DateTime.UtcNow }, null);

        var history = await _clientService.GetPaymentHistoryAsync(WorkspaceId, AdminId, client.Id);
        Assert.Equal(2, history.Count);
    }
}
