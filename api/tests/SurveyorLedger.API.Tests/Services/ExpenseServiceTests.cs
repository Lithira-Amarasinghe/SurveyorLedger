using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Expense;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class ExpenseServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IExpenseService _expenseService = null!;
    private Guid _jobId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddSingleton<IEmailService, NoOpEmailService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-expense-test-{Guid.NewGuid():N}"),
                    ["AppSettings:UiBaseUrl"] = "https://test.local"
                })
                .Build());
    }

    private async Task SeedJobAsync()
    {
        _jobService = GetService<IJobService>();
        _expenseService = GetService<IExpenseService>();
        var job = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Survey Job" });
        _jobId = job.Id;
    }

    [Fact]
    public async Task CreateAsync_PersistsExpense()
    {
        await SeedJobAsync();
        var expense = await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest
        {
            Category = "Travel",
            Amount = 5000m,
            Description = "Fuel",
            IncurredDate = DateTime.UtcNow
        });

        Assert.Equal("Travel", expense.Category);
        var fetched = await _expenseService.GetByIdAsync(WorkspaceId, AdminId, _jobId, expense.Id);
        Assert.Equal(expense.Id, fetched.Id);
    }

    [Fact]
    public async Task Client_CannotCreateExpense()
    {
        await SeedJobAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _expenseService.CreateAsync(WorkspaceId, ClientId, _jobId, new ExpenseRequest { Category = "Travel", Amount = 100m, IncurredDate = DateTime.UtcNow }));
    }

    [Fact]
    public async Task Surveyor_CannotDeleteExpense()
    {
        await SeedJobAsync();
        var expense = await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest { Category = "Travel", Amount = 100m, IncurredDate = DateTime.UtcNow });
        await Assert.ThrowsAsync<ForbiddenException>(() => _expenseService.DeleteAsync(WorkspaceId, SurveyorId, _jobId, expense.Id));
    }

    [Fact]
    public async Task JobFromOtherWorkspace_ThrowsNotFound()
    {
        await SeedJobAsync();
        var otherWorkspaceId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(
            () => _expenseService.CreateAsync(otherWorkspaceId, AdminId, _jobId, new ExpenseRequest { Category = "Travel", Amount = 100m, IncurredDate = DateTime.UtcNow }));
    }

    private static IFormFile MakeReceipt(string name = "receipt.jpg", string contentType = "image/jpeg")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-receipt-bytes");
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Fact]
    public async Task UploadReceiptAsync_PersistsReceipt()
    {
        await SeedJobAsync();
        var expense = await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest { Category = "Equipment", Amount = 2000m, IncurredDate = DateTime.UtcNow });

        var updated = await _expenseService.UploadReceiptAsync(WorkspaceId, AdminId, _jobId, expense.Id, MakeReceipt());
        Assert.NotNull(updated.ReceiptFilePath);
    }

    [Fact]
    public async Task UploadReceiptAsync_RejectsDisallowedExtension()
    {
        await SeedJobAsync();
        var expense = await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest { Category = "Equipment", Amount = 2000m, IncurredDate = DateTime.UtcNow });

        await Assert.ThrowsAsync<ValidationException>(
            () => _expenseService.UploadReceiptAsync(WorkspaceId, AdminId, _jobId, expense.Id, MakeReceipt("bad.exe", "application/octet-stream")));
    }

    [Fact]
    public async Task DeleteAsync_RemovesExpense()
    {
        await SeedJobAsync();
        var expense = await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest { Category = "Miscellaneous", Amount = 50m, IncurredDate = DateTime.UtcNow });
        await _expenseService.DeleteAsync(WorkspaceId, AdminId, _jobId, expense.Id);

        var all = await _expenseService.GetAllAsync(WorkspaceId, AdminId, _jobId);
        Assert.DoesNotContain(all, e => e.Id == expense.Id);
    }

    [Fact]
    public async Task CreateAsync_InvalidCategory_Rejected()
    {
        await SeedJobAsync();
        await Assert.ThrowsAsync<ValidationException>(
            () => _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest { Category = "NotACategory", Amount = 100m, IncurredDate = DateTime.UtcNow }));
    }

    [Fact]
    public async Task CreateAsync_SetsRecordedByUser_AsPersonNotUserAccount()
    {
        await SeedJobAsync();
        var expense = await _expenseService.CreateAsync(WorkspaceId, AdminId, _jobId, new ExpenseRequest
        {
            Category = "Travel", Amount = 100m, IncurredDate = DateTime.UtcNow
        });

        var loaded = await Context.Expenses.Include(e => e.RecordedByUser).FirstAsync(e => e.Id == expense.Id);
        Assert.IsType<SurveyorLedger.Data.Entities.Person>(loaded.RecordedByUser);
        Assert.Equal("Admin", loaded.RecordedByUser.FirstName);
        Assert.NotEqual(AdminId, loaded.RecordedBy); // RecordedBy is the Person.Id, not the caller's UserAccount.Id
    }
}
