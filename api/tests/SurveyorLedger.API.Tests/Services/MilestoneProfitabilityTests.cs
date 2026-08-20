using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Billing;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.Milestone;
using SurveyorLedger.API.Services;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class MilestoneProfitabilityTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IMilestoneService _milestoneService = null!;
    private IInvoiceService _invoiceService = null!;
    private IExpenseService _expenseService = null!;
    private Guid _jobId;
    private Guid _clientPersonId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IMilestoneService, MilestoneService>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-milestone-profit-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    [Fact]
    public async Task ComputeProfitabilityAsync_RevenueMinusExpenses()
    {
        _jobService = GetService<IJobService>();
        _milestoneService = GetService<IMilestoneService>();
        _invoiceService = GetService<IInvoiceService>();
        _expenseService = GetService<IExpenseService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        _jobId = job.Id;
        _clientPersonId = await GrantClientBillingRoleAsync(_jobId);
        var milestone = await _milestoneService.CreateAsync(WorkspaceId, AdminId, _jobId, new MilestoneRequest { Title = "Land Survey", Amount = 50000m });

        await _invoiceService.CreateAsync(WorkspaceId, AdminId, new InvoiceRequest
        {
            ClientId = _clientPersonId, JobId = _jobId,
            LineItems = new() { new LineItemDto { Description = "Land Survey", Quantity = 1, UnitPrice = 50000m, MilestoneId = milestone.Id } },
            TaxRatePercent = 0, DiscountAmount = 0, Installments = new()
        });
        await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest
        {
            Category = "Transport", Amount = 15000m, IncurredDate = DateTime.UtcNow, MilestoneId = milestone.Id
        });
        // Untagged expense - must not count against this milestone's profitability.
        await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest
        {
            Category = "Other", Amount = 999m, IncurredDate = DateTime.UtcNow
        });

        var (revenue, expenses, profit) = await _milestoneService.ComputeProfitabilityAsync(WorkspaceId, AdminId, _jobId, milestone.Id);
        Assert.Equal(50000m, revenue);
        Assert.Equal(15000m, expenses);
        Assert.Equal(35000m, profit);
    }
}
