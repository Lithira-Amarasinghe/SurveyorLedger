using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IClientService
{
    Task<Client> CreateAsync(Guid workspaceId, Guid callerUserId, ClientRequest request);
    Task<List<Client>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query);
    Task<Client> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<Client> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid clientId, ClientRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<decimal> GetBalanceAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<List<Payment>> GetPaymentHistoryAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
}

/// <summary>
/// Resource string is "billingclient", not "client" - "client" is already taken by the
/// pre-existing workspace-member "Client" role/person concept (see ScopedAccessService).
/// </summary>
public class ClientService : IClientService
{
    private readonly ApplicationDbContext _context;
    private readonly IScopedAccessService _access;
    private readonly IInvoiceService _invoices;
    private readonly ILogger<ClientService> _logger;

    public ClientService(ApplicationDbContext context, IScopedAccessService access, IInvoiceService invoices, ILogger<ClientService> logger)
    {
        _context = context;
        _access = access;
        _invoices = invoices;
        _logger = logger;
    }

    public async Task<Client> CreateAsync(Guid workspaceId, Guid callerUserId, ClientRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "billingclient", "create", workspaceId);

        var client = new Client
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Address = ToAddress(request.Address),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Clients.AddAsync(client);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Client {ClientId} created in workspace {WorkspaceId} by {UserId}", client.Id, workspaceId, callerUserId);
        return client;
    }

    public async Task<List<Client>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var clients = _context.Clients.Where(c => c.WorkspaceId == workspaceId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            clients = clients.Where(c =>
                EF.Functions.Like(c.Name, $"%{term}%") ||
                (c.Phone != null && EF.Functions.Like(c.Phone, $"%{term}%")) ||
                (c.Email != null && EF.Functions.Like(c.Email, $"%{term}%")));
        }

        return await clients.OrderByDescending(c => c.CreatedAt).ToListAsync();
    }

    public async Task<Client> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid clientId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "billingclient", "view", workspaceId);
        return await FindClientAsync(workspaceId, clientId);
    }

    public async Task<Client> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid clientId, ClientRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "billingclient", "edit", workspaceId);
        var client = await FindClientAsync(workspaceId, clientId);

        client.Name = request.Name.Trim();
        client.Phone = request.Phone?.Trim();
        client.Email = request.Email?.Trim();
        client.Address = ToAddress(request.Address);
        client.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return client;
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid clientId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "billingclient", "delete", workspaceId);
        var client = await FindClientAsync(workspaceId, clientId);

        client.IsActive = false;
        client.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<decimal> GetBalanceAsync(Guid workspaceId, Guid callerUserId, Guid clientId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "billingclient", "view", workspaceId);
        await FindClientAsync(workspaceId, clientId);

        var invoices = await _context.Invoices.Include(i => i.Payments)
            .Where(i => i.WorkspaceId == workspaceId && i.ClientId == clientId)
            .ToListAsync();

        return invoices.Sum(i => _invoices.ComputeInvoiceTotals(i).Balance);
    }

    public async Task<List<Payment>> GetPaymentHistoryAsync(Guid workspaceId, Guid callerUserId, Guid clientId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "billingclient", "view", workspaceId);
        await FindClientAsync(workspaceId, clientId);

        return await _context.Payments
            .Where(p => p.WorkspaceId == workspaceId && p.Invoice.ClientId == clientId)
            .OrderByDescending(p => p.ReceivedAt)
            .ToListAsync();
    }

    internal async Task<Client> FindClientAsync(Guid workspaceId, Guid clientId)
    {
        return await _context.Clients.FirstOrDefaultAsync(c => c.Id == clientId && c.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Client not found");
    }

    private static Address ToAddress(AddressDto? dto) => new()
    {
        Street = dto?.Street,
        City = dto?.City,
        District = dto?.District,
        PostalCode = dto?.PostalCode,
        Country = dto?.Country
    };
}
