# Document Requests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admin/Surveyor can ask a Client (or anyone) for a specific document on a Job; the ask shows up as a pending row in the existing Documents card, and uploading against it fulfills it in place.

**Architecture:** `DocumentRequest` is a Job sub-resource, same job-scoped RBAC reuse as `Milestone`/`Document` (`job.view`/`job.edit`, no new permissions). Fulfilling a request calls the existing `IDocumentService.UploadAsync` internally rather than duplicating upload/validation/storage logic. UI merges `documents()` and `documentRequests()` into one sorted row list — no separate section, no tabs (per brainstorming decision).

**Tech Stack:** .NET 9/EF Core 9 (backend, matches the existing Documents feature), Angular 21 standalone components/signals (UI, matches the existing Documents feature).

## Global Constraints

- Reuses `DocumentCategory` enum — no new enum for request category.
- No new Casbin permissions — `job.view`/`job.edit` reuse, same as every other job sub-resource.
- Migrations generated via `dotnet ef migrations add`, never hand-edited.
- Do not run `git commit` for any step — commit only when the user explicitly says to.
- Spec: `docs/superpowers/specs/2026-08-11-document-requests-design.md`.

---

### Task 1: `DocumentRequest` entity, EF config, migration

**Files:**
- Create: `api/src/SurveyorLedger.Data/Entities/DocumentRequest.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/DocumentRequestConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`
- Migration: generated under `api/src/SurveyorLedger.Data/Migrations/`

**Interfaces:**
- Produces: `DocumentRequest` entity (`Id, JobId, Title, Description?, Category (DocumentCategory), Status, FulfilledDocumentId?, FulfilledAt?, FulfilledBy?, RequestedBy, CreatedAt, UpdatedAt, IsActive`) plus navigations `Job`, `FulfilledDocument?`, `RequestedByUser`, `FulfilledByUser?`. `ApplicationDbContext.DocumentRequests` DbSet.

- [ ] **Step 1: Create the entity**

`api/src/SurveyorLedger.Data/Entities/DocumentRequest.cs`:

```csharp
using SurveyorLedger.Core;

namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A staff ask for a specific document on a Job (e.g. "Legal Deed"). Fulfilling one
/// uploads a Document through the existing DocumentService and links it here - the
/// Document entity itself has no knowledge of requests, this is a one-directional link.
/// </summary>
public class DocumentRequest
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? FulfilledDocumentId { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public Guid? FulfilledBy { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Job Job { get; set; }
    public Document? FulfilledDocument { get; set; }
    public User RequestedByUser { get; set; }
    public User? FulfilledByUser { get; set; }
}
```

- [ ] **Step 2: Add the EF configuration**

`api/src/SurveyorLedger.Data/Configurations/DocumentRequestConfiguration.cs`, mirroring `DocumentConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class DocumentRequestConfiguration : IEntityTypeConfiguration<DocumentRequest>
{
    public void Configure(EntityTypeBuilder<DocumentRequest> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.JobId);

        builder.HasOne(x => x.Job)
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FulfilledDocument)
            .WithMany()
            .HasForeignKey(x => x.FulfilledDocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.RequestedByUser)
            .WithMany()
            .HasForeignKey(x => x.RequestedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.FulfilledByUser)
            .WithMany()
            .HasForeignKey(x => x.FulfilledBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

`FulfilledDocumentId` uses `SetNull` on delete (not `Restrict`) — if a fulfilled document is ever hard-deleted at the DB level, the request should fall back to unlinked rather than block the delete. Soft delete (`IsActive = false`) is the normal path and doesn't trigger this either way.

- [ ] **Step 3: Register the DbSet**

In `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`, next to `public DbSet<Document> Documents { get; set; }`:

```csharp
public DbSet<DocumentRequest> DocumentRequests { get; set; }
```

- [ ] **Step 4: Generate and apply the migration**

```bash
cd api
dotnet ef migrations add AddDocumentRequestEntity --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

Expected: new `DocumentRequests` table, FKs to `Jobs`/`Documents`/`Users` as configured above. Run the `migration-check` skill checklist against it.

- [ ] **Step 5: Build**

```bash
cd api && dotnet build SurveyorLedger.sln
```

Expected: succeeds.

---

### Task 2: `DocumentRequestService`

**Files:**
- Create: `api/src/SurveyorLedger.API/Services/DocumentRequestService.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Services/DocumentRequestServiceTests.cs`

**Interfaces:**
- Consumes: `IDocumentService.UploadAsync(...)` (existing, from the Job Documents feature) for the actual file handling in `FulfillAsync`. `ApplicationDbContext.DocumentRequests`/`Documents` (Task 1). `ICasbinService`, `Constants.ScopeTypes.Job`, `NotFoundException`/`ForbiddenException`/`ValidationException` (existing).
- Produces: `IDocumentRequestService` with:
  - `Task<List<DocumentRequest>> GetForJobAsync(Guid workspaceId, Guid callerUserId, Guid jobId)`
  - `Task<DocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string title, string? description, DocumentCategory category)`
  - `Task<DocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, IFormFile file, DocumentVisibility visibility)`
  - `Task<DocumentRequest> ReopenAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)`
  - `Task CancelAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)`

  Task 3's `DocumentRequestController` consumes these exact signatures.

- [ ] **Step 1: Write the failing tests**

`api/tests/SurveyorLedger.API.Tests/Services/DocumentRequestServiceTests.cs`, same base/seeding pattern as `DocumentServiceTests.cs`:

```csharp
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class DocumentRequestServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IDocumentRequestService _requestService = null!;
    private Guid _jobAId;
    private Guid _jobBId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentRequestService, DocumentRequestService>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-docreq-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task SeedJobsAsync()
    {
        _jobService = GetService<IJobService>();
        _requestService = GetService<IDocumentRequestService>();

        var jobA = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var jobB = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        _jobAId = jobA.Id;
        _jobBId = jobB.Id;

        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId);
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, ClientId);
    }

    private static IFormFile MakeFile(string name = "deed.pdf", string content = "file-bytes") =>
        new FormFile(new MemoryStream(Encoding.UTF8.GetBytes(content)), 0, Encoding.UTF8.GetByteCount(content), "file", name)
            { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

    [Fact]
    public async Task Admin_CanCreateRequest()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        Assert.Equal("Legal Deed", request.Title);
        Assert.Equal("Pending", request.Status);
    }

    [Fact]
    public async Task Client_CannotCreateRequest()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _requestService.CreateAsync(WorkspaceId, ClientId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument));
    }

    [Fact]
    public async Task Client_CanFulfillRequest_OnAssignedJob()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

        Assert.Equal("Fulfilled", fulfilled.Status);
        Assert.NotNull(fulfilled.FulfilledDocumentId);
        Assert.Equal(ClientId, fulfilled.FulfilledBy);
    }

    [Fact]
    public async Task Reopen_ClearsLink_WithoutDeletingDocument()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        var fulfilled = await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);
        var documentId = fulfilled.FulfilledDocumentId;

        var reopened = await _requestService.ReopenAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        Assert.Equal("Pending", reopened.Status);
        Assert.Null(reopened.FulfilledDocumentId);
        Assert.NotNull(documentId);
    }

    [Fact]
    public async Task Client_CannotReopen()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);
        await _requestService.FulfillAsync(WorkspaceId, ClientId, _jobAId, request.Id, MakeFile(), DocumentVisibility.ClientVisible);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _requestService.ReopenAsync(WorkspaceId, ClientId, _jobAId, request.Id));
    }

    [Fact]
    public async Task Cancel_SoftDeletes_AndExcludesFromList()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        await _requestService.CancelAsync(WorkspaceId, AdminId, _jobAId, request.Id);

        var requests = await _requestService.GetForJobAsync(WorkspaceId, AdminId, _jobAId);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task Client_CanListRequests_OnAssignedJob()
    {
        await SeedJobsAsync();
        await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        var requests = await _requestService.GetForJobAsync(WorkspaceId, ClientId, _jobAId);

        Assert.Single(requests);
    }

    [Fact]
    public async Task RequestFromDifferentJob_ThrowsNotFound()
    {
        await SeedJobsAsync();
        var request = await _requestService.CreateAsync(WorkspaceId, AdminId, _jobAId, "Legal Deed", null, DocumentCategory.LegalDocument);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _requestService.FulfillAsync(WorkspaceId, AdminId, _jobBId, request.Id, MakeFile(), DocumentVisibility.ClientVisible));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

```bash
cd api && dotnet test tests/SurveyorLedger.API.Tests --filter DocumentRequestServiceTests
```

Expected: build failure, `IDocumentRequestService`/`DocumentRequestService` not found.

- [ ] **Step 3: Write `DocumentRequestService`**

`api/src/SurveyorLedger.API/Services/DocumentRequestService.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IDocumentRequestService
{
    Task<List<DocumentRequest>> GetForJobAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<DocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string title, string? description, DocumentCategory category);
    Task<DocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, IFormFile file, DocumentVisibility visibility);
    Task<DocumentRequest> ReopenAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
    Task CancelAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId);
}

/// <summary>
/// Same job-scoped RBAC reuse as DocumentService/MilestoneService - job.view for
/// list/fulfill (Client fulfills their own), job.edit for create/reopen/cancel
/// (Admin/Surveyor only). FulfillAsync delegates the actual file handling to
/// IDocumentService.UploadAsync rather than duplicating validation/storage.
/// </summary>
public class DocumentRequestService : IDocumentRequestService
{
    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly IDocumentService _documentService;

    public DocumentRequestService(ApplicationDbContext context, ICasbinService casbinService, IDocumentService documentService)
    {
        _context = context;
        _casbinService = casbinService;
        _documentService = documentService;
    }

    public async Task<List<DocumentRequest>> GetForJobAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        return await _context.DocumentRequests
            .Where(r => r.JobId == jobId && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<DocumentRequest> CreateAsync(Guid workspaceId, Guid callerUserId, Guid jobId, string title, string? description, DocumentCategory category)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        if (string.IsNullOrWhiteSpace(title))
            throw new ValidationException("Title is required.");

        var request = new DocumentRequest
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Title = title.Trim(),
            Description = description,
            Category = category,
            Status = "Pending",
            RequestedBy = callerUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.DocumentRequests.AddAsync(request);
        await _context.SaveChangesAsync();
        return request;
    }

    public async Task<DocumentRequest> FulfillAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId, IFormFile file, DocumentVisibility visibility)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        var request = await FindRequestAsync(jobId, requestId);

        var document = await _documentService.UploadAsync(workspaceId, callerUserId, jobId, file, request.Category, visibility);

        request.FulfilledDocumentId = document.Id;
        request.FulfilledAt = DateTime.UtcNow;
        request.FulfilledBy = callerUserId;
        request.Status = "Fulfilled";
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return request;
    }

    public async Task<DocumentRequest> ReopenAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var request = await FindRequestAsync(jobId, requestId);
        request.FulfilledDocumentId = null;
        request.FulfilledAt = null;
        request.FulfilledBy = null;
        request.Status = "Pending";
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return request;
    }

    public async Task CancelAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid requestId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var request = await FindRequestAsync(jobId, requestId);
        request.IsActive = false;
        request.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<DocumentRequest> FindRequestAsync(Guid jobId, Guid requestId)
    {
        return await _context.DocumentRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.JobId == jobId && r.IsActive)
            ?? throw new NotFoundException("Document request not found");
    }

    private Task<bool> HasFullJobAccessAsync(Guid callerUserId, Guid workspaceId) =>
        _casbinService.EnforceAsync(callerUserId.ToString(), "job", "view_all", workspaceId.ToString());

    private Task<bool> IsAssignedToJobAsync(Guid callerUserId, Guid jobId) =>
        _context.UserAccesses.AnyAsync(ua =>
            ua.UserId == callerUserId && ua.IsActive &&
            ua.ScopeType == Constants.ScopeTypes.Job && ua.ScopeId == jobId);

    private async Task EnsureJobAccessAsync(Guid callerUserId, Guid workspaceId, Guid jobId, string action)
    {
        var allowed = await _casbinService.EnforceAsync(callerUserId.ToString(), "job", action, workspaceId.ToString());
        if (!allowed)
            throw new ForbiddenException($"You do not have permission to {action} document requests in this workspace.");

        if (await HasFullJobAccessAsync(callerUserId, workspaceId))
            return;
        if (!await IsAssignedToJobAsync(callerUserId, jobId))
            throw new ForbiddenException($"You do not have permission to {action} document requests on this job.");
    }
}
```

- [ ] **Step 4: Register in DI**

In `api/src/SurveyorLedger.API/Program.cs`, after the document service registration:

```csharp
// Register document request service
builder.Services.AddScoped<IDocumentRequestService, DocumentRequestService>();
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd api && dotnet test tests/SurveyorLedger.API.Tests --filter DocumentRequestServiceTests
```

Expected: 8 passed.

---

### Task 3: `DocumentRequestController`

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestCreateRequest.cs`
- Create: `api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestFulfillRequest.cs`
- Create: `api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestResponse.cs`
- Create: `api/src/SurveyorLedger.API/Controllers/DocumentRequestController.cs`

**Interfaces:**
- Consumes: `IDocumentRequestService` (Task 2) exact signatures.
- Produces: HTTP endpoints under `api/workspace/{workspaceId}/job/{jobId}/document-request`.

- [ ] **Step 1: Write the request/response models**

`api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestCreateRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestCreateRequest
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, MinimumLength = 1)]
    public required string Title { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Category is required.")]
    public required DocumentCategory Category { get; set; }
}
```

`api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestFulfillRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestFulfillRequest
{
    [Required(ErrorMessage = "File is required.")]
    public required IFormFile File { get; set; }

    [Required(ErrorMessage = "Visibility is required.")]
    public required DocumentVisibility Visibility { get; set; }
}
```

`api/src/SurveyorLedger.API/Models/DocumentRequest/DocumentRequestResponse.cs`:

```csharp
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestResponse
{
    public Guid RequestId { get; set; }
    public Guid JobId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DocumentCategory Category { get; set; }
    public required string Status { get; set; }
    public Guid? FulfilledDocumentId { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public Guid? FulfilledBy { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Write the controller**

`api/src/SurveyorLedger.API/Controllers/DocumentRequestController.cs`, matching `MilestoneController`/`DocumentController`'s shape:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.DocumentRequest;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/document-request")]
    [Authorize]
    public class DocumentRequestController : ControllerBase
    {
        private readonly IDocumentRequestService _requestService;

        public DocumentRequestController(IDocumentRequestService requestService)
        {
            _requestService = requestService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<DocumentRequestResponse>>>> List(Guid workspaceId, Guid jobId)
        {
            var requests = await _requestService.GetForJobAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<List<DocumentRequestResponse>>.Ok(requests.Select(ToResponse).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<DocumentRequestResponse>>> Create(Guid workspaceId, Guid jobId, [FromBody] DocumentRequestCreateRequest request)
        {
            var created = await _requestService.CreateAsync(workspaceId, CallerId(), jobId, request.Title, request.Description, request.Category);
            return Ok(ApiResponse<DocumentRequestResponse>.Ok(ToResponse(created)));
        }

        [HttpPost("{id}/fulfill")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<DocumentRequestResponse>>> Fulfill(Guid workspaceId, Guid jobId, Guid id, [FromForm] DocumentRequestFulfillRequest request)
        {
            var fulfilled = await _requestService.FulfillAsync(workspaceId, CallerId(), jobId, id, request.File, request.Visibility);
            return Ok(ApiResponse<DocumentRequestResponse>.Ok(ToResponse(fulfilled)));
        }

        [HttpPost("{id}/reopen")]
        public async Task<ActionResult<ApiResponse<DocumentRequestResponse>>> Reopen(Guid workspaceId, Guid jobId, Guid id)
        {
            var reopened = await _requestService.ReopenAsync(workspaceId, CallerId(), jobId, id);
            return Ok(ApiResponse<DocumentRequestResponse>.Ok(ToResponse(reopened)));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Cancel(Guid workspaceId, Guid jobId, Guid id)
        {
            await _requestService.CancelAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static DocumentRequestResponse ToResponse(DocumentRequest r) => new()
        {
            RequestId = r.Id,
            JobId = r.JobId,
            Title = r.Title,
            Description = r.Description,
            Category = r.Category,
            Status = r.Status,
            FulfilledDocumentId = r.FulfilledDocumentId,
            FulfilledAt = r.FulfilledAt,
            FulfilledBy = r.FulfilledBy,
            RequestedBy = r.RequestedBy,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }
}
```

`DocumentService.MaxFileSizeBytes` is the same public constant `DocumentController` already references — single source for the upload size cap (established in the Job Documents plan's api-layer-review fix).

- [ ] **Step 3: Build and run the full backend suite**

```bash
cd api
dotnet build SurveyorLedger.sln
dotnet test tests/SurveyorLedger.API.Tests
```

Expected: build succeeds, all tests pass (existing 88 + new 8 = 96).

---

### Task 4: `DocumentRequestService` (Angular)

**Files:**
- Create: `ui/src/app/core/document-request.service.ts`
- Create: `ui/src/app/core/document-request.service.spec.ts`

**Interfaces:**
- Produces: `DocumentRequest` interface (`requestId, jobId, title, description, category, status, fulfilledDocumentId, fulfilledAt, fulfilledBy, requestedBy, createdAt, updatedAt`) and `DocumentRequestService` with `list()`, `create(title, description, category)`, `fulfill(requestId, file, visibility)`, `reopen(requestId)`, `cancel(requestId)`. Task 5 consumes these exact signatures.

- [ ] **Step 1: Write the failing tests**

`ui/src/app/core/document-request.service.spec.ts`, matching `document.service.spec.ts`'s shape:

```ts
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DocumentRequestService } from './document-request.service';
import { environment } from '../../environments/environment';

describe('DocumentRequestService', () => {
  let service: DocumentRequestService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';
  const jobId = 'j1';
  const base = `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/document-request`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [DocumentRequestService]
    });
    service = TestBed.inject(DocumentRequestService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const sample = {
    requestId: 'r1', jobId, title: 'Legal Deed', description: null, category: 'LegalDocument',
    status: 'Pending', fulfilledDocumentId: null, fulfilledAt: null, fulfilledBy: null,
    requestedBy: 'u1', createdAt: '2026-01-01', updatedAt: '2026-01-01'
  };

  it('list() unwraps ApiResponse', () => {
    service.list(workspaceId, jobId).subscribe(result => expect(result).toEqual([sample]));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [sample] });
  });

  it('create() posts title/description/category', () => {
    service.create(workspaceId, jobId, 'Legal Deed', null, 'LegalDocument').subscribe(result => expect(result).toEqual(sample));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ title: 'Legal Deed', description: null, category: 'LegalDocument' });
    req.flush({ success: true, data: sample });
  });

  it('fulfill() posts FormData to /{id}/fulfill', () => {
    const file = new File(['bytes'], 'deed.pdf', { type: 'application/pdf' });
    const fulfilled = { ...sample, status: 'Fulfilled', fulfilledDocumentId: 'd1' };
    service.fulfill(workspaceId, jobId, 'r1', file, 'ClientVisible').subscribe(result => expect(result).toEqual(fulfilled));
    const req = httpMock.expectOne(`${base}/r1/fulfill`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush({ success: true, data: fulfilled });
  });

  it('reopen() posts to /{id}/reopen', () => {
    service.reopen(workspaceId, jobId, 'r1').subscribe(result => expect(result).toEqual(sample));
    const req = httpMock.expectOne(`${base}/r1/reopen`);
    expect(req.request.method).toBe('POST');
    req.flush({ success: true, data: sample });
  });

  it('cancel() deletes with no body', () => {
    service.cancel(workspaceId, jobId, 'r1').subscribe();
    const req = httpMock.expectOne(`${base}/r1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

```bash
cd ui && ng test --watch=false --include='**/document-request.service.spec.ts'
```

Expected: fails, module not found.

- [ ] **Step 3: Write `DocumentRequestService`**

`ui/src/app/core/document-request.service.ts`:

```ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface DocumentRequest {
  requestId: string;
  jobId: string;
  title: string;
  description: string | null;
  category: 'SurveyPlan' | 'LegalDocument' | 'Photo' | 'Other';
  status: 'Pending' | 'Fulfilled';
  fulfilledDocumentId: string | null;
  fulfilledAt: string | null;
  fulfilledBy: string | null;
  requestedBy: string;
  createdAt: string;
  updatedAt: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class DocumentRequestService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string, jobId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/document-request`;
  }

  list(workspaceId: string, jobId: string): Observable<DocumentRequest[]> {
    return this.http.get<ApiResponse<DocumentRequest[]>>(this.base(workspaceId, jobId)).pipe(map(res => res.data));
  }

  create(workspaceId: string, jobId: string, title: string, description: string | null, category: string): Observable<DocumentRequest> {
    return this.http
      .post<ApiResponse<DocumentRequest>>(this.base(workspaceId, jobId), { title, description, category })
      .pipe(map(res => res.data));
  }

  fulfill(workspaceId: string, jobId: string, requestId: string, file: File, visibility: string): Observable<DocumentRequest> {
    const form = new FormData();
    form.append('File', file);
    form.append('Visibility', visibility);
    return this.http
      .post<ApiResponse<DocumentRequest>>(`${this.base(workspaceId, jobId)}/${requestId}/fulfill`, form)
      .pipe(map(res => res.data));
  }

  reopen(workspaceId: string, jobId: string, requestId: string): Observable<DocumentRequest> {
    return this.http
      .post<ApiResponse<DocumentRequest>>(`${this.base(workspaceId, jobId)}/${requestId}/reopen`, {})
      .pipe(map(res => res.data));
  }

  cancel(workspaceId: string, jobId: string, requestId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId, jobId)}/${requestId}`);
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd ui && ng test --watch=false --include='**/document-request.service.spec.ts'
```

Expected: 5 passed.

---

### Task 5: Wire into `job-detail.component.ts`

**Files:**
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `DocumentRequestService` (Task 4), existing `DocumentService`/`documents()` signal.
- Produces: nothing further — integration point.

- [ ] **Step 1: Add imports and signals**

Add to imports:

```ts
import { DocumentRequest, DocumentRequestService } from '../../core/document-request.service';
```

Add near the other document-related signals:

```ts
documentRequests = signal<DocumentRequest[]>([]);
requestingDocument = signal(false);
requestTitleDraft = '';
requestDescriptionDraft = '';
requestCategoryDraft = 'Other';
requestError = signal('');
```

Inject `DocumentRequestService` in the constructor alongside `DocumentService`.

- [ ] **Step 2: Include requests in the initial fetch**

In `fetch()`'s `forkJoin`, add `documentRequests: this.documentRequestService.list(this.workspaceId, this.jobId)`, and in `next`: `this.documentRequests.set(documentRequests);`.

- [ ] **Step 3: Add the merged row computed signal**

Add a `computed` import from `@angular/core` (alongside the existing `signal` import), and this computed signal near the other document signals:

```ts
documentRows = computed(() => {
  const requests = this.documentRequests();
  const linkedDocIds = new Set(requests.filter(r => r.fulfilledDocumentId).map(r => r.fulfilledDocumentId));

  const plainDocRows = this.documents()
    .filter(d => !linkedDocIds.has(d.documentId))
    .map(d => ({ kind: 'document' as const, document: d, request: null as DocumentRequest | null, createdAt: d.createdAt }));

  const requestRows = requests.map(r => ({
    kind: 'request' as const,
    document: r.fulfilledDocumentId ? this.documents().find(d => d.documentId === r.fulfilledDocumentId) ?? null : null,
    request: r,
    createdAt: r.createdAt
  }));

  return [...plainDocRows, ...requestRows].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
});
```

A plain document (never came from a request) is a `'document'` row. A request is always a `'request'` row — its `document` field is `null` while `Pending`, and populated once `Fulfilled`. This is the one merge point the spec calls for: a fulfilled request's row and its document's row are the same row, not two.

- [ ] **Step 4: Replace the Documents card's list rendering**

Replace the existing `@if (documents().length > 0) { ... }` block inside the Documents card (the one iterating `documents()`) with:

```html
@if (documentRows().length > 0) {
  <div class="space-y-xs mb-md">
    @for (row of documentRows(); track (row.request?.requestId ?? row.document?.documentId)) {
      @if (row.kind === 'request' && row.request!.status === 'Pending') {
        <div class="flex items-center justify-between gap-sm px-md py-sm rounded border border-dashed border-neutral-300">
          <div class="min-w-0">
            <span class="text-sm text-neutral-900 truncate block">Requested: {{ row.request!.title }}</span>
            <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ row.request!.category }}</span>
          </div>
          <div class="flex items-center gap-sm flex-shrink-0 whitespace-nowrap">
            <input #fulfillInput type="file" class="hidden" (change)="fulfillRequest(row.request!, fulfillInput.files); fulfillInput.value = ''" />
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="fulfillInput.click()">Upload</button>
            @if (!isClient()) {
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="cancelRequest(row.request!)">Cancel</button>
            }
          </div>
        </div>
      } @else if (row.document; as d) {
        <div class="flex items-center justify-between gap-sm px-md py-sm rounded bg-neutral-50">
          <div class="min-w-0">
            <span class="text-sm text-neutral-900 truncate block">{{ d.fileName }}</span>
            <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600 mr-xs">{{ d.category }}</span>
            @if (!isClient()) {
              <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ d.visibility }}</span>
            }
          </div>
          <div class="flex items-center gap-sm flex-shrink-0 whitespace-nowrap">
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="viewDocument(d)">View</button>
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="downloadDocument(d)">Download</button>
            @if (!isClient() && row.request) {
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="reopenRequest(row.request!)">Reopen</button>
            }
            @if (!isClient() && !row.request) {
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="confirmingDeleteDocument.set(d)">Remove</button>
            }
          </div>
        </div>
      }
    }
  </div>
}
```

Plain uploaded documents (`!row.request`) keep Remove. Documents that fulfilled a request get Reopen instead — removing a fulfilled document without reopening the request first would leave the request permanently `Fulfilled` pointing at a soft-deleted document, so Reopen is the correct action there, not Remove.

- [ ] **Step 5: Add the "+ Request document" form and error line**

After the existing `<app-document-upload-widget ... />` line, still inside the Documents card, add (only for non-Client):

```html
@if (requestError()) {
  <p class="text-xs text-primary-500 mt-sm">{{ requestError() }}</p>
}
@if (!isClient()) {
  @if (requestingDocument()) {
    <div class="rounded bg-neutral-50 p-md space-y-sm mt-sm">
      <input class="input-field text-sm" placeholder="What do you need? (e.g. Legal Deed)" [(ngModel)]="requestTitleDraft" />
      <textarea class="input-field text-sm" rows="2" placeholder="Description (optional)" [(ngModel)]="requestDescriptionDraft"></textarea>
      <select class="input-field text-sm" [(ngModel)]="requestCategoryDraft">
        <option value="SurveyPlan">SurveyPlan</option>
        <option value="LegalDocument">LegalDocument</option>
        <option value="Photo">Photo</option>
        <option value="Other">Other</option>
      </select>
      <div class="flex items-center justify-end gap-sm">
        <button type="button" class="btn-secondary text-xs" (click)="cancelAddRequest()">Cancel</button>
        <button type="button" class="btn-primary text-xs" (click)="submitRequest()">Request</button>
      </div>
    </div>
  } @else {
    <button type="button" class="text-xs text-primary-500 hover:text-primary-600 mt-sm" (click)="requestingDocument.set(true)">
      + Request document
    </button>
  }
}
```

- [ ] **Step 6: Add the component methods**

Alongside the existing document methods:

```ts
submitRequest(): void {
  if (!this.requestTitleDraft.trim()) {
    this.requestError.set('Title is required.');
    return;
  }
  this.requestError.set('');
  this.documentRequestService
    .create(this.workspaceId, this.jobId, this.requestTitleDraft.trim(), this.requestDescriptionDraft.trim() || null, this.requestCategoryDraft)
    .subscribe({
      next: (request) => {
        this.documentRequests.update(list => [request, ...list]);
        this.cancelAddRequest();
      },
      error: (err) => this.requestError.set(err.error?.message ?? 'Could not create request.')
    });
}

cancelAddRequest(): void {
  this.requestingDocument.set(false);
  this.requestTitleDraft = '';
  this.requestDescriptionDraft = '';
  this.requestCategoryDraft = 'Other';
  this.requestError.set('');
}

fulfillRequest(request: DocumentRequest, files: FileList | null): void {
  const file = files?.item(0);
  if (!file) return;
  const visibility = this.isClient() ? 'ClientVisible' : 'Internal';

  this.documentError.set('');
  this.documentRequestService.fulfill(this.workspaceId, this.jobId, request.requestId, file, visibility).subscribe({
    next: (fulfilled) => {
      this.documentRequests.update(list => list.map(r => (r.requestId === fulfilled.requestId ? fulfilled : r)));
      this.documentService.list(this.workspaceId, this.jobId).subscribe(documents => this.documents.set(documents));
    },
    error: (err) => this.documentError.set(err.error?.message ?? 'Could not upload document.')
  });
}

reopenRequest(request: DocumentRequest): void {
  this.documentRequestService.reopen(this.workspaceId, this.jobId, request.requestId).subscribe({
    next: (reopened) => this.documentRequests.update(list => list.map(r => (r.requestId === reopened.requestId ? reopened : r))),
    error: (err) => this.documentError.set(err.error?.message ?? 'Could not reopen request.')
  });
}

cancelRequest(request: DocumentRequest): void {
  this.documentRequestService.cancel(this.workspaceId, this.jobId, request.requestId).subscribe({
    next: () => this.documentRequests.update(list => list.filter(r => r.requestId !== request.requestId)),
    error: (err) => this.documentError.set(err.error?.message ?? 'Could not cancel request.')
  });
}
```

`fulfillRequest` re-fetches the document list rather than constructing the new `Document` client-side — the server response from `fulfill()` is a `DocumentRequestResponse`, not a `DocumentResponse`, so the actual uploaded document's fields (fileName, contentType, etc) aren't in hand without a second call; re-listing is simpler than a bespoke endpoint just to avoid one GET.

- [ ] **Step 7: Build and manually verify**

```bash
cd ui && ng build --configuration development
cd api && dotnet build SurveyorLedger.sln && dotnet test tests/SurveyorLedger.API.Tests
```

Expected: both succeed, full backend suite green.

Manually, with API + UI running:
1. As Admin: "+ Request document" — create "Legal Deed" (LegalDocument). Dashed row appears.
2. As Client on that job: see the same dashed row, click Upload, pick a file — row flips to a normal document row with View/Download, no Remove (Reopen shown to Admin/Surveyor only, not Client).
3. As Admin: click Reopen on that row — flips back to the dashed "Requested" row; the previously uploaded document is not gone (check via a direct list call or that Reopen didn't error).
4. As Admin: Cancel a still-pending request — row disappears.

- [ ] **Step 8: Do not commit** (per Global Constraints — wait for explicit instruction)

---

## Self-Review Notes

- **Spec coverage:** entity/migration (Task 1), service with job-scoped RBAC + reuse of `IDocumentService.UploadAsync` (Task 2), controller matching existing route conventions (Task 3), Angular service (Task 4), merged-row UI with request-vs-document branching, request form, fulfill/reopen/cancel (Task 5). All spec sections covered.
- **Type consistency:** `IDocumentRequestService` signatures from Task 2 match Task 3's controller calls exactly; `DocumentRequestService` (Angular) signatures from Task 4 match Task 5's component calls exactly.
- **Commit discipline:** every task ends without a commit step; Task 5 explicitly restates the user's standing "commit only when told" instruction.
