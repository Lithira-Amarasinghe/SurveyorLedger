using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Configurations;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class ReportServiceTests : WorkspaceIntegrationTestBase
{
    private IReportService _reportService = null!;
    private IInvoiceService _invoiceService = null!;
    private IExpenseService _expenseService = null!;
    private IJobService _jobService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-report-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private async Task<(Job Job, Guid ClientPersonId)> SeedJobWithClientAsync()
    {
        _jobService = GetService<IJobService>();
        _invoiceService = GetService<IInvoiceService>();
        _expenseService = GetService<IExpenseService>();
        _reportService = GetService<IReportService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Report Job" });

        var clientPerson = new Person { Id = Guid.NewGuid(), FirstName = "Acme", LastName = "Ltd", Email = $"c-{Guid.NewGuid():N}@test.local", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        Context.People.Add(clientPerson);
        var clientAccount = new UserAccount { Id = Guid.NewGuid(), PersonId = clientPerson.Id, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        Context.UserAccounts.Add(clientAccount);
        await Context.SaveChangesAsync();

        Context.UserAccesses.Add(new UserAccess
        {
            Id = Guid.NewGuid(), UserId = clientAccount.Id, RoleId = RoleConfiguration.ClientRoleId,
            ScopeType = Constants.ScopeTypes.Job, ScopeId = job.Id, IsActive = true,
            AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();
        await CasbinService.AddRoleForUserAsync(clientAccount.Id.ToString(), Constants.SystemRoles.Client, job.Id.ToString());

        return (job, clientPerson.Id);
    }

    [Fact]
    public async Task GetFinancialSummaryAsync_AggregatesAcrossJobs()
    {
        var (job, clientPersonId) = await SeedJobWithClientAsync();
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = clientPersonId, JobId = job.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 100000m } },
            TaxRatePercent = 0, DiscountAmount = 0, Status = "Sent"
        });
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice.Id, new PaymentRequest { Amount = 40000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);
        await _expenseService.CreateAsync(WorkspaceId, AdminId, job.Id, new ExpenseRequest { Category = "Transport", Amount = 10000m, IncurredDate = DateTime.UtcNow });

        var summary = await _reportService.GetFinancialSummaryAsync(WorkspaceId, AdminId, null, null);

        Assert.Equal(100000m, summary.TotalInvoiced);
        Assert.Equal(40000m, summary.TotalPaid);
        Assert.Equal(60000m, summary.TotalOutstanding);
        Assert.Equal(10000m, summary.TotalExpenses);
        Assert.Equal(30000m, summary.GrossProfit);
    }

    [Fact]
    public async Task Surveyor_CannotViewReports()
    {
        var (_, _) = await SeedJobWithClientAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => _reportService.GetFinancialSummaryAsync(WorkspaceId, SurveyorId, null, null));
    }

    [Fact]
    public async Task GetFinancialSummaryAsync_InvalidRange_Throws()
    {
        await SeedJobWithClientAsync();
        await Assert.ThrowsAsync<ValidationException>(() =>
            _reportService.GetFinancialSummaryAsync(WorkspaceId, AdminId, DateTime.UtcNow, DateTime.UtcNow.AddDays(-1)));
    }

    [Fact]
    public async Task GetPaymentHistoryAsync_PaginatesAndOrdersNewestFirst()
    {
        var (job, clientPersonId) = await SeedJobWithClientAsync();
        var invoice = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = clientPersonId, JobId = job.Id,
            LineItems = new List<LineItemDto> { new() { Description = "Survey", Quantity = 1, UnitPrice = 100000m } },
            TaxRatePercent = 0, DiscountAmount = 0, Status = "Sent"
        });
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice.Id, new PaymentRequest { Amount = 10000m, Method = "Cash", ReceivedAt = DateTime.UtcNow.AddDays(-2) }, null);
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, invoice.Id, new PaymentRequest { Amount = 20000m, Method = "Cash", ReceivedAt = DateTime.UtcNow.AddDays(-1) }, null);

        var page1 = await _reportService.GetPaymentHistoryAsync(WorkspaceId, AdminId, null, null, page: 1, pageSize: 1);

        Assert.Equal(2, page1.TotalCount);
        Assert.Single(page1.Items);
        Assert.Equal(20000m, page1.Items[0].Amount); // newest first
    }

    [Fact]
    public async Task GetExpenseHistoryAsync_IncludesPayeeName_ForStaffCost()
    {
        var (job, _) = await SeedJobWithClientAsync();
        await _expenseService.CreateAsync(WorkspaceId, AdminId, job.Id, new ExpenseRequest
        {
            Category = "StaffCost", Amount = 5000m, IncurredDate = DateTime.UtcNow, PayeeId = AdminPersonId, PayeeType = "Salary"
        });

        var result = await _reportService.GetExpenseHistoryAsync(WorkspaceId, AdminId, null, null, page: 1, pageSize: 50);

        Assert.Single(result.Items);
        Assert.Equal("Admin Person", result.Items[0].PayeeName);
    }

    [Fact]
    public async Task GetOutstandingInvoicesAsync_ExcludesFullyPaidAndCancelled()
    {
        var (job, clientPersonId) = await SeedJobWithClientAsync();

        var unpaid = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = clientPersonId, JobId = job.Id,
            LineItems = new List<LineItemDto> { new() { Description = "A", Quantity = 1, UnitPrice = 5000m } },
            TaxRatePercent = 0, DiscountAmount = 0, Status = "Sent"
        });

        var paid = await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = clientPersonId, JobId = job.Id,
            LineItems = new List<LineItemDto> { new() { Description = "B", Quantity = 1, UnitPrice = 3000m } },
            TaxRatePercent = 0, DiscountAmount = 0, Status = "Sent"
        });
        await _invoiceService.RecordPaymentAsync(WorkspaceId, AdminId, paid.Id, new PaymentRequest { Amount = 3000m, Method = "Cash", ReceivedAt = DateTime.UtcNow }, null);

        var result = await _reportService.GetOutstandingInvoicesAsync(WorkspaceId, AdminId);

        Assert.Single(result);
        Assert.Equal(unpaid.Id, result[0].InvoiceId);
        Assert.Equal(5000m, result[0].Balance);
    }
}
