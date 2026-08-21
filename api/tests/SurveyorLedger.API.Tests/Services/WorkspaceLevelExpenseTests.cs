using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class WorkspaceLevelExpenseTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IExpenseService _expenseService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-workspace-expense-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    [Fact]
    public async Task WorkspaceLevelExpense_DoesNotAppearInJobScopedList()
    {
        _jobService = GetService<IJobService>();
        _expenseService = GetService<IExpenseService>();

        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        await _expenseService.CreateAsync(WorkspaceId, AdminId, job.Id, new ExpenseRequest
        {
            Category = "Other", Amount = 100m, IncurredDate = DateTime.UtcNow
        });
        var workspaceExpense = await _expenseService.CreateWorkspaceLevelAsync(WorkspaceId, AdminId, new ExpenseRequest
        {
            Category = "Other", Amount = 500m, IncurredDate = DateTime.UtcNow
        });

        var jobScoped = await _expenseService.GetAllAsync(WorkspaceId, AdminId, job.Id);
        Assert.DoesNotContain(jobScoped, e => e.Id == workspaceExpense.Id);

        var workspaceScoped = await _expenseService.GetAllWorkspaceLevelAsync(WorkspaceId, AdminId);
        Assert.Contains(workspaceScoped, e => e.Id == workspaceExpense.Id);
        Assert.DoesNotContain(workspaceScoped, e => e.Amount == 100m);
    }

    [Fact]
    public async Task WorkspaceLevelExpense_WithMilestoneId_IsRejected()
    {
        _jobService = GetService<IJobService>();
        _expenseService = GetService<IExpenseService>();

        var request = new ExpenseRequest
        {
            Category = "Other", Amount = 100m, IncurredDate = DateTime.UtcNow, MilestoneId = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<ValidationException>(
            () => _expenseService.CreateWorkspaceLevelAsync(WorkspaceId, AdminId, request));
    }
}
