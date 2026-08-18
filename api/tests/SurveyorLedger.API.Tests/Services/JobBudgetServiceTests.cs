using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Budget;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class JobBudgetServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IJobBudgetService _budgetService = null!;
    private Guid _jobId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IJobBudgetService, JobBudgetService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppSettings:UiBaseUrl"] = "https://test.local" })
                .Build());
    }

    private async Task SeedJobAsync()
    {
        _jobService = GetService<IJobService>();
        _budgetService = GetService<IJobBudgetService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey Job" });
        _jobId = job.Id;
    }

    [Fact]
    public async Task UpsertAsync_Admin_CreatesBudget()
    {
        await SeedJobAsync();
        var budget = await _budgetService.UpsertAsync(WorkspaceId, AdminId, _jobId, new JobBudgetRequest { EstimatedFee = 1000, EstimatedCost = 400 });

        Assert.Equal(1000, budget.EstimatedFee);
        Assert.Equal(400, budget.EstimatedCost);
    }

    [Fact]
    public async Task UpsertAsync_SecondCall_Edits()
    {
        await SeedJobAsync();
        await _budgetService.UpsertAsync(WorkspaceId, AdminId, _jobId, new JobBudgetRequest { EstimatedFee = 1000, EstimatedCost = 400 });
        var updated = await _budgetService.UpsertAsync(WorkspaceId, AdminId, _jobId, new JobBudgetRequest { EstimatedFee = 1200, EstimatedCost = 500 });

        Assert.Equal(1200, updated.EstimatedFee);
        Assert.Equal(500, updated.EstimatedCost);
    }

    [Fact]
    public async Task Surveyor_CannotSetBudget()
    {
        await SeedJobAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _budgetService.UpsertAsync(WorkspaceId, SurveyorId, _jobId, new JobBudgetRequest { EstimatedFee = 1000, EstimatedCost = 400 }));
    }

    [Fact]
    public async Task Surveyor_CannotViewBudget()
    {
        await SeedJobAsync();
        await _budgetService.UpsertAsync(WorkspaceId, AdminId, _jobId, new JobBudgetRequest { EstimatedFee = 1000, EstimatedCost = 400 });

        await Assert.ThrowsAsync<ForbiddenException>(() => _budgetService.GetAsync(WorkspaceId, SurveyorId, _jobId));
    }

    [Fact]
    public async Task GetAsync_NoBudgetSet_ReturnsNull()
    {
        await SeedJobAsync();
        var budget = await _budgetService.GetAsync(WorkspaceId, AdminId, _jobId);
        Assert.Null(budget);
    }

    [Fact]
    public async Task DeleteAsync_RemovesBudget()
    {
        await SeedJobAsync();
        await _budgetService.UpsertAsync(WorkspaceId, AdminId, _jobId, new JobBudgetRequest { EstimatedFee = 1000, EstimatedCost = 400 });
        await _budgetService.DeleteAsync(WorkspaceId, AdminId, _jobId);

        var budget = await _budgetService.GetAsync(WorkspaceId, AdminId, _jobId);
        Assert.Null(budget);
    }

    [Fact]
    public async Task UpsertAsync_NegativeAmount_Rejected()
    {
        await SeedJobAsync();
        await Assert.ThrowsAsync<ValidationException>(() =>
            _budgetService.UpsertAsync(WorkspaceId, AdminId, _jobId, new JobBudgetRequest { EstimatedFee = -1, EstimatedCost = 400 }));
    }
}
