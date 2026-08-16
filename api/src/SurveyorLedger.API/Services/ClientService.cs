using Microsoft.EntityFrameworkCore;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IClientService
{
    Task<Person> CreateAsync(Guid workspaceId, Guid callerUserId, ClientRequest request);
    Task<List<Person>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query);
    Task<Person> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<Person> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid clientId, ClientRequest request);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<decimal> GetBalanceAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
    Task<List<Payment>> GetPaymentHistoryAsync(Guid workspaceId, Guid callerUserId, Guid clientId);
}

/// <summary>
/// Resource string is "billingclient", not "client" - "client" is already taken by the
/// pre-existing workspace-member "Client" role/person concept (see ScopedAccessService).
/// CreateAsync/SearchAsync are deliberately global (no workspaceId) - clients are now
/// bare Person rows, not workspace-scoped entities. Real isolation is Spec 2's job
/// (job-scoped billing). GetByIdAsync/UpdateAsync/DeleteAsync/GetBalanceAsync/
/// GetPaymentHistoryAsync still take workspaceId to gate the caller's permission via
/// EnsureAllowedAsync against that workspace, but no longer filter the Person row itself
/// by workspace.
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

    public async Task<Person> CreateAsync(Guid workspaceId, Guid callerUserId, ClientRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "billingclient", "create", workspaceId);

        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = request.Name.Trim(), // ClientRequest.Name has no first/last split - stays on FirstName, LastName empty
            LastName = "",
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Address = ToAddress(request.Address),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.People.AddAsync(person);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Client-person {PersonId} created by {UserId}", person.Id, callerUserId);
        return person;
    }

    public async Task<List<Person>> SearchAsync(Guid workspaceId, Guid callerUserId, string? query)
    {
        await _access.EnsureListAllowedAsync(callerUserId, workspaceId);

        var people = _context.People.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            people = people.Where(p =>
                EF.Functions.Like(p.FirstName, $"%{term}%") ||
                EF.Functions.Like(p.LastName, $"%{term}%") ||
                (p.Phone != null && EF.Functions.Like(p.Phone, $"%{term}%")) ||
                (p.Email != null && EF.Functions.Like(p.Email, $"%{term}%")));
        }

        return await people.OrderByDescending(p => p.CreatedAt).ToListAsync();
    }

    public async Task<Person> GetByIdAsync(Guid workspaceId, Guid callerUserId, Guid clientId)
    {
        await _access.EnsureAllowedAsync(callerUserId, "billingclient", "view", workspaceId);
        return await FindClientAsync(workspaceId, clientId);
    }

    public async Task<Person> UpdateAsync(Guid workspaceId, Guid callerUserId, Guid clientId, ClientRequest request)
    {
        await _access.EnsureAllowedAsync(callerUserId, "billingclient", "edit", workspaceId);
        var client = await FindClientAsync(workspaceId, clientId);

        client.FirstName = request.Name.Trim();
        client.LastName = "";
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

    internal async Task<Person> FindClientAsync(Guid workspaceId, Guid clientId)
    {
        return await _context.People.FirstOrDefaultAsync(p => p.Id == clientId)
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
