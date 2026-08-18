using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Configurations;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class InvoiceServiceTests : WorkspaceIntegrationTestBase
{
    private IInvoiceService _invoiceService = null!;
    private IEmailService _emailService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddSingleton<IEmailService, StubEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-invoice-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private class StubEmailService : IEmailService
    {
        public List<(string Email, string DocumentType, string DocumentNumber)> Sent { get; } = new();
        public Task SendVerificationOtpAsync(string email, string otpCode, int expirationMinutes) => Task.CompletedTask;
        public Task SendPasswordResetOtpAsync(string email, string otpCode, int expirationMinutes) => Task.CompletedTask;
        public Task SendWelcomeEmailAsync(string email, string firstName) => Task.CompletedTask;
        public Task SendInviteEmailAsync(string email, string workspaceName, string inviteUrl) => Task.CompletedTask;
        public Task SendBillingDocumentAsync(string email, string documentType, string documentNumber, string linkUrl, byte[] pdfBytes, string pdfFileName)
        {
            Sent.Add((email, documentType, documentNumber));
            return Task.CompletedTask;
        }
    }

    private async Task<(Job Job, Guid ClientPersonId, Guid ClientUserAccountId)> SeedJobWithClientParticipantAsync()
    {
        var jobService = GetService<IJobService>();
        var job = await jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Test Job" });

        var clientPerson = new Person
        {
            Id = Guid.NewGuid(), FirstName = "Acme", LastName = "Ltd", Email = $"client-{Guid.NewGuid():N}@test.local",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.People.Add(clientPerson);
        var clientAccount = new UserAccount
        {
            Id = Guid.NewGuid(), PersonId = clientPerson.Id, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.UserAccounts.Add(clientAccount);
        await Context.SaveChangesAsync();

        // AddParticipantAsync only grants instantly when the target already has consent
        // coverage (workspace-level access) - a bare Person with no workspace membership
        // always falls to the invitation branch. Grant UserAccess directly instead,
        // mirroring what AddParticipantAsync would eventually produce, to keep this a
        // fast unit-style seed.
        Context.UserAccesses.Add(new UserAccess
        {
            Id = Guid.NewGuid(), UserId = clientAccount.Id, RoleId = RoleConfiguration.ClientRoleId,
            ScopeType = Constants.ScopeTypes.Job, ScopeId = job.Id, IsActive = true,
            AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        // Casbin runs an in-memory enforcer loaded from the DB at startup - a UserAccess row
        // written directly via EF (bypassing UserAccessGrantService) never reaches it, so the
        // grouping policy needs the same explicit sync GrantAsync would normally do.
        await CasbinService.AddRoleForUserAsync(clientAccount.Id.ToString(), Constants.SystemRoles.Client, job.Id.ToString());

        return (job, clientPerson.Id, clientAccount.Id);
    }

    private async Task<Guid> CreateWorkspaceMemberAsync()
    {
        var person = new Person
        {
            Id = Guid.NewGuid(), FirstName = "Outsider", LastName = "Member", Email = $"outsider-{Guid.NewGuid():N}@test.local",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.People.Add(person);
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), PersonId = person.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.UserAccounts.Add(account);
        Context.UserAccesses.Add(new UserAccess
        {
            Id = Guid.NewGuid(), UserId = account.Id, RoleId = RoleConfiguration.MemberRoleId,
            ScopeType = Constants.ScopeTypes.Workspace, ScopeId = WorkspaceId, IsActive = true,
            AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();
        return account.Id;
    }

    private async Task<Guid> SeedInvoiceOnJobAsync(Guid jobId, Guid clientPersonId, DateTime? dueDate = null)
    {
        _invoiceService = GetService<IInvoiceService>();
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = clientPersonId,
            JobId = jobId,
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
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 40000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, AdminId, invoiceId);
        Assert.Equal("PartiallyPaid", invoice.Status);
    }

    [Fact]
    public async Task RecordPaymentAsync_FullPayment_SetsPaid()
    {
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 100000m, Method = "BankTransfer", ReceivedAt = DateTime.UtcNow }, null);

        var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, AdminId, invoiceId);
        Assert.Equal("Paid", invoice.Status);
    }

    [Fact]
    public async Task RecordPaymentAsync_Overpayment_Throws()
    {
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);
        await Assert.ThrowsAsync<ValidationException>(
            () => _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 150000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null));
    }

    [Fact]
    public async Task RecordPaymentAsync_AssignsSequentialReceiptNumbers()
    {
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);
        var p1 = await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 30000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);
        var p2 = await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 20000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        Assert.Equal("RCP-0001", p1.ReceiptNumber);
        Assert.Equal("RCP-0002", p2.ReceiptNumber);
    }

    [Fact]
    public async Task DeleteAsync_WithPayments_Throws409()
    {
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 10000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _invoiceService.DeleteAsync(WorkspaceId, AdminId, invoiceId));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task ComputeInvoiceTotals_OverdueSentInvoice_ReportsDaysOverdue()
    {
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId, DateTime.UtcNow.Date.AddDays(-5));
        var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, AdminId, invoiceId);
        var (_, _, _, isOverdue, daysOverdue) = _invoiceService.ComputeInvoiceTotals(invoice);

        Assert.True(isOverdue);
        Assert.Equal(5, daysOverdue);
    }

    [Fact]
    public async Task CreateAsync_ClientIdNotOnJob_Throws()
    {
        _invoiceService = GetService<IInvoiceService>();
        var (job, _, _) = await SeedJobWithClientParticipantAsync();
        var strangerPerson = new Person
        {
            Id = Guid.NewGuid(), FirstName = "Stranger", LastName = "Person", Email = "stranger@test.local",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.People.Add(strangerPerson);
        await Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = strangerPerson.Id,
            JobId = job.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 1000m } },
            TaxRatePercent = 0,
            DiscountAmount = 0,
            Status = "Draft"
        }));
    }

    [Fact]
    public async Task GetByIdAsync_ClientRoleOnJob_CanView()
    {
        _invoiceService = GetService<IInvoiceService>();
        var (job, clientPersonId, clientUserAccountId) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);

        var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, clientUserAccountId, invoiceId);
        Assert.Equal(invoiceId, invoice.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NoJobRole_ThrowsForbidden()
    {
        _invoiceService = GetService<IInvoiceService>();
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);

        var outsiderAccountId = await CreateWorkspaceMemberAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => _invoiceService.GetByIdAsync(WorkspaceId, outsiderAccountId, invoiceId));
    }

    [Fact]
    public async Task SendAsync_RecipientNotClientOrFinanceOnJob_Throws()
    {
        _invoiceService = GetService<IInvoiceService>();
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);

        var strangerPerson = new Person
        {
            Id = Guid.NewGuid(), FirstName = "Not", LastName = "OnJob", Email = "notonjob@test.local",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Context.People.Add(strangerPerson);
        await Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _invoiceService.SendAsync(WorkspaceId, AdminId, invoiceId, new List<Guid> { strangerPerson.Id }, "https://app.test.local"));
    }

    [Fact]
    public async Task SendAsync_ClientOnJob_Succeeds()
    {
        _invoiceService = GetService<IInvoiceService>();
        _emailService = GetService<IEmailService>();
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);

        await _invoiceService.SendAsync(WorkspaceId, AdminId, invoiceId, new List<Guid> { clientPersonId }, "https://app.test.local");

        var stub = (StubEmailService)_emailService;
        Assert.Contains(stub.Sent, s => s.DocumentType == "Invoice");
    }

    [Fact]
    public async Task CreateAsync_InstallmentsSummingToTotal_Persists()
    {
        _invoiceService = GetService<IInvoiceService>();
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();

        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = clientPersonId,
            JobId = job.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 100000m } },
            TaxRatePercent = 0,
            DiscountAmount = 0,
            Status = "Draft",
            Installments = new List<InstallmentDto>
            {
                new() { Amount = 30000m, DueDate = DateTime.UtcNow.Date },
                new() { Amount = 70000m, DueDate = DateTime.UtcNow.Date.AddDays(30) }
            }
        });

        Assert.Equal(2, invoice.Installments.Count);
    }

    [Fact]
    public async Task CreateAsync_InstallmentsNotMatchingTotal_Throws()
    {
        _invoiceService = GetService<IInvoiceService>();
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = clientPersonId,
            JobId = job.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 100000m } },
            TaxRatePercent = 0,
            DiscountAmount = 0,
            Status = "Draft",
            Installments = new List<InstallmentDto> { new() { Amount = 50000m, DueDate = DateTime.UtcNow.Date } }
        }));
    }

    [Fact]
    public async Task ComputeInstallmentStatuses_ReflectsPaymentsAndDueDates()
    {
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        _invoiceService = GetService<IInvoiceService>();

        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = clientPersonId,
            JobId = job.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 100000m } },
            TaxRatePercent = 0,
            DiscountAmount = 0,
            Status = "Sent",
            Installments = new List<InstallmentDto>
            {
                new() { Amount = 30000m, DueDate = DateTime.UtcNow.Date.AddDays(-10) }, // overdue if unpaid
                new() { Amount = 70000m, DueDate = DateTime.UtcNow.Date.AddDays(30) }   // pending
            }
        });

        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice.Id, new PaymentRequest { Amount = 30000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var refreshed = await _invoiceService.GetByIdAsync(WorkspaceId, AdminId, invoice.Id);
        var statuses = _invoiceService.ComputeInstallmentStatuses(refreshed);

        Assert.Equal("Paid", statuses[0].Status);
        Assert.Equal("Pending", statuses[1].Status);
    }

    [Fact]
    public async Task UpdateAsync_AfterPayment_CannotChangeLineItems()
    {
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 10000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        await Assert.ThrowsAsync<ConflictException>(() => _invoiceService.UpdateAsync(WorkspaceId, AdminId, invoiceId, new InvoiceRequest
        {
            ClientId = clientPersonId,
            JobId = job.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 5000m } }, // shrunk below AmountPaid
            TaxRatePercent = 0,
            DiscountAmount = 0,
            Status = "Sent",
            Installments = new List<InstallmentDto>()
        }));
    }

    [Fact]
    public async Task UpdateAsync_AfterPayment_CanStillChangeDueDate()
    {
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 10000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);
        var invoice = await _invoiceService.GetByIdAsync(WorkspaceId, AdminId, invoiceId);
        var newDueDate = DateTime.UtcNow.Date.AddDays(14);

        var updated = await _invoiceService.UpdateAsync(WorkspaceId, AdminId, invoiceId, new InvoiceRequest
        {
            ClientId = invoice.ClientId,
            JobId = invoice.JobId,
            LineItems = invoice.LineItems.Select(li => new LineItemDto { Description = li.Description, Quantity = li.Quantity, UnitPrice = li.UnitPrice }).ToList(),
            TaxRatePercent = invoice.TaxRatePercent,
            DiscountAmount = invoice.DiscountAmount,
            DueDate = newDueDate,
            Status = invoice.Status,
            Installments = new List<InstallmentDto>()
        });

        Assert.Equal(newDueDate, updated.DueDate);
    }

    [Fact]
    public async Task CreateAsync_DiscountExceedsSubtotal_Throws()
    {
        _invoiceService = GetService<IInvoiceService>();
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();

        await Assert.ThrowsAsync<ValidationException>(() => _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = clientPersonId,
            JobId = job.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 1000m } },
            TaxRatePercent = 0,
            DiscountAmount = 5000m,
            Status = "Draft",
            Installments = new List<InstallmentDto>()
        }));
    }

    [Fact]
    public async Task RecordPaymentAsync_FutureDated_Throws()
    {
        var (job, clientPersonId, _) = await SeedJobWithClientParticipantAsync();
        var invoiceId = await SeedInvoiceOnJobAsync(job.Id, clientPersonId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoiceId, new PaymentRequest { Amount = 1000m, Method = "Cash", ReceivedAt = DateTime.UtcNow.AddDays(1) }, null));
    }
}
