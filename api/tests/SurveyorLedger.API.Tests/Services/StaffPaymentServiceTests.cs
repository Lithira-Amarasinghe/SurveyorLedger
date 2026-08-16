using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Models.StaffPayment;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class StaffPaymentServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IStaffPaymentService _staffPaymentService = null!;
    private Guid _jobId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IStaffPaymentService, StaffPaymentService>();
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
        _staffPaymentService = GetService<IStaffPaymentService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey Job" });
        _jobId = job.Id;
    }

    [Fact]
    public async Task CreateAsync_PersistsStaffPayment()
    {
        await SeedJobAsync();
        var payment = await _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest
        {
            UserId = SurveyorId,
            Type = "Salary",
            Amount = 30000m,
            PaidDate = DateTime.UtcNow
        });

        Assert.Equal("Salary", payment.Type);
    }

    [Fact]
    public async Task CreateAsync_UnknownUserId_Rejected()
    {
        await SeedJobAsync();
        await Assert.ThrowsAsync<ValidationException>(() => _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest
        {
            UserId = Guid.NewGuid(),
            Type = "Bonus",
            Amount = 1000m,
            PaidDate = DateTime.UtcNow
        }));
    }

    [Fact]
    public async Task Surveyor_CannotCreateStaffPayment()
    {
        await SeedJobAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => _staffPaymentService.CreateAsync(WorkspaceId, SurveyorId, _jobId, new StaffPaymentRequest
        {
            UserId = SurveyorId,
            Type = "Salary",
            Amount = 1000m,
            PaidDate = DateTime.UtcNow
        }));
    }

    [Fact]
    public async Task Surveyor_SeesOnlyOwnPayments()
    {
        await SeedJobAsync();
        await _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest { UserId = SurveyorId, Type = "Salary", Amount = 30000m, PaidDate = DateTime.UtcNow });
        await _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest { UserId = AdminId, Type = "Bonus", Amount = 5000m, PaidDate = DateTime.UtcNow });

        var surveyorView = await _staffPaymentService.GetAllAsync(WorkspaceId, SurveyorId, _jobId);
        Assert.Single(surveyorView);
        Assert.All(surveyorView, p => Assert.Equal(SurveyorId, p.UserId));

        var adminView = await _staffPaymentService.GetAllAsync(WorkspaceId, AdminId, _jobId);
        Assert.Equal(2, adminView.Count);
    }

    [Fact]
    public async Task Surveyor_CannotGetByIdForAnotherUsersPayment()
    {
        await SeedJobAsync();
        var payment = await _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest { UserId = AdminId, Type = "Bonus", Amount = 5000m, PaidDate = DateTime.UtcNow });

        await Assert.ThrowsAsync<NotFoundException>(() => _staffPaymentService.GetByIdAsync(WorkspaceId, SurveyorId, _jobId, payment.Id));
    }

    [Fact]
    public async Task DeleteAsync_RemovesStaffPayment()
    {
        await SeedJobAsync();
        var payment = await _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest { UserId = SurveyorId, Type = "Commission", Amount = 1000m, PaidDate = DateTime.UtcNow });
        await _staffPaymentService.DeleteAsync(WorkspaceId, AdminId, _jobId, payment.Id);

        var all = await _staffPaymentService.GetAllAsync(WorkspaceId, AdminId, _jobId);
        Assert.DoesNotContain(all, p => p.Id == payment.Id);
    }

    [Fact]
    public async Task CreateAsync_InvalidType_Rejected()
    {
        await SeedJobAsync();
        await Assert.ThrowsAsync<ValidationException>(() => _staffPaymentService.CreateAsync(WorkspaceId, AdminId, _jobId, new StaffPaymentRequest
        {
            UserId = SurveyorId,
            Type = "NotAType",
            Amount = 1000m,
            PaidDate = DateTime.UtcNow
        }));
    }
}
