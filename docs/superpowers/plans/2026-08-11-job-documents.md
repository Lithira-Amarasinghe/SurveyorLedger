# Job Documents Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Admin, Surveyor, and Client roles upload, list, preview/download, and delete documents (survey plans, legal documents, photos) attached to a Job, with an Internal/ClientVisible flag controlling what Clients can see.

**Architecture:** `Document` is a Job sub-resource, same shape as `Milestone` — tenant isolation transitive via `JobId -> Job.WorkspaceId`, authorization reuses Job's existing `job.view`/`job.edit` Casbin permissions plus job-assignment scoping (no new permissions, no new migration for RBAC). File bytes live behind an `IFileStorageService` interface; only implementation for now is local disk, so a later Azure Blob swap touches one DI registration, not callers.

**Tech Stack:** .NET 9, EF Core 9 (SQL Server LocalDB), ASP.NET Core `IFormFile` for multipart upload, Casbin.NET (existing enforcer, no new policies), xUnit against `WorkspaceIntegrationTestBase`.

## Global Constraints

- Documents attach to a Job only (not Land) — v1 scope, see spec.
- No version history — re-upload creates a new `Document` row.
- Extension allowlist: `.pdf .doc .docx .xls .xlsx .jpg .jpeg .png`. Max size: 25 MB.
- Soft delete via `IsActive`, matching Job/Milestone.
- Route shape: `api/workspace/{workspaceId}/job/{jobId}/document` (matches `MilestoneController`'s convention, not a generic `/api/jobs/...` shape).
- Migrations are generated via `dotnet ef migrations add`, never hand-edited.
- Spec: `docs/superpowers/specs/2026-08-11-job-documents-design.md`.

---

### Task 1: `Document` entity, enums, EF configuration, migration

**Files:**
- Create: `api/src/SurveyorLedger.Data/Entities/Document.cs`
- Modify: `api/src/SurveyorLedger.Core/Enums.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/DocumentConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`
- Migration: generated under `api/src/SurveyorLedger.Data/Migrations/`

**Interfaces:**
- Produces: `Document` entity with `Id, JobId, FileName, StoredPath, ContentType, FileSizeBytes, Category (DocumentCategory), Visibility (DocumentVisibility), UploadedBy, CreatedAt, UpdatedAt, IsActive` plus navigation `Job`, `UploadedByUser`. Enums `DocumentCategory { SurveyPlan, LegalDocument, Photo, Other }` and `DocumentVisibility { Internal, ClientVisible }` in `SurveyorLedger.Core`. `ApplicationDbContext.Documents` DbSet.

- [ ] **Step 1: Add the enums**

In `api/src/SurveyorLedger.Core/Enums.cs`, append:

```csharp
public enum DocumentCategory
{
    SurveyPlan,
    LegalDocument,
    Photo,
    Other
}

public enum DocumentVisibility
{
    Internal,
    ClientVisible
}
```

- [ ] **Step 2: Create the `Document` entity**

`api/src/SurveyorLedger.Data/Entities/Document.cs`:

```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A file attached to a Job (survey plan, legal document, photo, etc). Tenant isolation
/// is transitive through JobId -> Job.WorkspaceId, same as Milestone. Visibility gates
/// whether the Client role can see it - Internal documents are Admin/Surveyor only.
/// </summary>
public class Document
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string FileName { get; set; }
    public string StoredPath { get; set; }
    public string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DocumentCategory Category { get; set; }
    public DocumentVisibility Visibility { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Job Job { get; set; }
    public User UploadedByUser { get; set; }
}
```

- [ ] **Step 3: Add the EF configuration**

`api/src/SurveyorLedger.Data/Configurations/DocumentConfiguration.cs`, mirroring `MilestoneConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.StoredPath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.JobId);

        builder.HasOne(x => x.Job)
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.UploadedByUser)
            .WithMany()
            .HasForeignKey(x => x.UploadedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

Enums stored as strings (`HasConversion<string>()`) rather than ints — matches how `Status` fields elsewhere in the schema are readable directly in SQL, and this table will be read by non-EF tooling (support queries) more often than most.

- [ ] **Step 4: Register the DbSet**

In `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`, next to `public DbSet<Milestone> Milestones { get; set; }`, add:

```csharp
public DbSet<Document> Documents { get; set; }
```

- [ ] **Step 5: Generate the migration**

Run from `api/`:

```bash
dotnet ef migrations add AddDocumentEntity --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

Expected: a new migration file creating the `Documents` table with FK to `Jobs` and `Users`, matching the configuration above. Run the `migration-check` skill checklist against the generated migration before proceeding.

- [ ] **Step 6: Apply the migration to LocalDB and verify**

```bash
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

Expected: succeeds, `Documents` table exists in LocalDB.

- [ ] **Step 7: Build and commit**

```bash
dotnet build api/SurveyorLedger.sln
git add api/src/SurveyorLedger.Data/Entities/Document.cs api/src/SurveyorLedger.Core/Enums.cs api/src/SurveyorLedger.Data/Configurations/DocumentConfiguration.cs api/src/SurveyorLedger.Data/ApplicationDbContext.cs api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: add Document entity and migration"
```

---

### Task 2: `IFileStorageService` + local disk implementation

**Files:**
- Create: `api/src/SurveyorLedger.API/Services/IFileStorageService.cs`
- Create: `api/src/SurveyorLedger.API/Services/LocalFileStorageService.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Services/LocalFileStorageServiceTests.cs`
- Modify: `api/src/SurveyorLedger.API/appsettings.json`
- Modify: `api/src/SurveyorLedger.API/Program.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `IFileStorageService` with `Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct)`, `Task<Stream> OpenAsync(string relativePath, CancellationToken ct)`, `Task DeleteAsync(string relativePath, CancellationToken ct)`. Task 3's `DocumentService` consumes this interface via constructor injection.

- [ ] **Step 1: Write the failing test**

`api/tests/SurveyorLedger.API.Tests/Services/LocalFileStorageServiceTests.cs`:

```csharp
using System.Text;
using Microsoft.Extensions.Configuration;
using SurveyorLedger.API.Services;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sl-storage-test-{Guid.NewGuid():N}");
    private readonly LocalFileStorageService _sut;

    public LocalFileStorageServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UploadsRootPath"] = _root })
            .Build();
        _sut = new LocalFileStorageService(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_WritesFile_UnderConfiguredRoot()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var relativePath = "workspace1/job1/abc_file.pdf";

        await _sut.SaveAsync(content, relativePath, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_root, "workspace1", "job1", "abc_file.pdf")));
    }

    [Fact]
    public async Task OpenAsync_ReturnsSavedContent()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var relativePath = "workspace1/job1/abc_file.pdf";
        await _sut.SaveAsync(content, relativePath, CancellationToken.None);

        await using var stream = await _sut.OpenAsync(relativePath, CancellationToken.None);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();

        Assert.Equal("hello", text);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var relativePath = "workspace1/job1/abc_file.pdf";
        await _sut.SaveAsync(content, relativePath, CancellationToken.None);

        await _sut.DeleteAsync(relativePath, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_root, "workspace1", "job1", "abc_file.pdf")));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error — types don't exist yet)**

```bash
dotnet test api/tests/SurveyorLedger.API.Tests --filter LocalFileStorageServiceTests
```

Expected: build failure, `IFileStorageService`/`LocalFileStorageService` not found.

- [ ] **Step 3: Write the interface**

`api/src/SurveyorLedger.API/Services/IFileStorageService.cs`:

```csharp
namespace SurveyorLedger.API.Services;

public interface IFileStorageService
{
    Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct);
    Task<Stream> OpenAsync(string relativePath, CancellationToken ct);
    Task DeleteAsync(string relativePath, CancellationToken ct);
}
```

- [ ] **Step 4: Write the local disk implementation**

`api/src/SurveyorLedger.API/Services/LocalFileStorageService.cs`:

```csharp
namespace SurveyorLedger.API.Services;

/// <summary>
/// Local-disk implementation of IFileStorageService, for dev. relativePath is always
/// {workspaceId}/{jobId}/{guid}_{filename} - callers own that shape, this class just
/// resolves it under the configured root and creates directories as needed. Swapping to
/// Azure Blob later means adding a sibling class implementing the same interface and
/// flipping the DI registration in Program.cs - no caller changes.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(IConfiguration configuration)
    {
        _rootPath = configuration["Storage:UploadsRootPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "uploads");
    }

    public async Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct)
    {
        var fullPath = ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, ct);

        return relativePath;
    }

    public Task<Stream> OpenAsync(string relativePath, CancellationToken ct)
    {
        var fullPath = ResolvePath(relativePath);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct)
    {
        var fullPath = ResolvePath(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string ResolvePath(string relativePath) => Path.Combine(_rootPath, relativePath);
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test api/tests/SurveyorLedger.API.Tests --filter LocalFileStorageServiceTests
```

Expected: 3 passed.

- [ ] **Step 6: Wire configuration and DI**

In `api/src/SurveyorLedger.API/appsettings.json`, add a `Storage` section (adjust indentation to match existing file):

```json
"Storage": {
  "UploadsRootPath": "uploads"
}
```

In `api/src/SurveyorLedger.API/Program.cs`, after the milestone service registration (line ~99), add:

```csharp
// Register file storage. Local disk for dev - see LocalFileStorageService for the
// swap-to-Azure-Blob path.
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
```

- [ ] **Step 7: Build and commit**

```bash
dotnet build api/SurveyorLedger.sln
git add api/src/SurveyorLedger.API/Services/IFileStorageService.cs api/src/SurveyorLedger.API/Services/LocalFileStorageService.cs api/tests/SurveyorLedger.API.Tests/Services/LocalFileStorageServiceTests.cs api/src/SurveyorLedger.API/appsettings.json api/src/SurveyorLedger.API/Program.cs
git commit -m "feat: add local file storage service"
```

---

### Task 3: `DocumentService`

**Files:**
- Create: `api/src/SurveyorLedger.API/Services/DocumentService.cs`
- Modify: `api/src/SurveyorLedger.API/Program.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Services/DocumentServiceTests.cs`

**Interfaces:**
- Consumes: `IFileStorageService` (Task 2). `ApplicationDbContext.Documents`, `Document` entity (Task 1). `ICasbinService.EnforceAsync(string user, string resource, string action, string domain)` (existing). `Constants.ScopeTypes.Job` (existing). `NotFoundException`, `ForbiddenException`, `ValidationException` (existing, `SurveyorLedger.Core.Exceptions`).
- Produces: `IDocumentService` with:
  - `Task<List<Document>> GetDocumentsAsync(Guid workspaceId, Guid callerUserId, Guid jobId)`
  - `Task<Document> UploadAsync(Guid workspaceId, Guid callerUserId, Guid jobId, IFormFile file, DocumentCategory category, DocumentVisibility visibility)`
  - `Task<(Document Document, Stream Content)> GetFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId)`
  - `Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId)`

  No `callerRole` parameter: the JWT carries no role claim (confirmed against `TokenService.cs`) — roles are resolved from `UserAccess.Role.Name` at workspace scope, the same way `WorkspaceService.cs:177` does it. `DocumentService` already queries `UserAccess` for job-assignment scoping, so it resolves the caller's role itself via a private helper rather than pushing that lookup onto the controller. Task 4's `DocumentController` consumes these exact signatures.

- [ ] **Step 1: Write the failing tests**

`api/tests/SurveyorLedger.API.Tests/Services/DocumentServiceTests.cs`, following `MilestoneServiceTests.cs`'s shape exactly (same base class, same job-seeding helper):

```csharp
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Job;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class DocumentServiceTests : WorkspaceIntegrationTestBase
{
    private IJobService _jobService = null!;
    private IDocumentService _documentService = null!;
    private Guid _jobAId;
    private Guid _jobBId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-doc-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task SeedJobsAsync()
    {
        _jobService = GetService<IJobService>();
        _documentService = GetService<IDocumentService>();

        var jobA = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job A" });
        var jobB = await _jobService.CreateAsync(WorkspaceId, AdminId, new JobRequest { Title = "Job B" });
        _jobAId = jobA.Id;
        _jobBId = jobB.Id;

        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, SurveyorId);
        await _jobService.AddParticipantAsync(WorkspaceId, AdminId, _jobAId, ClientId);
    }

    private static IFormFile MakeFile(string name = "plan.pdf", string content = "file-bytes", string contentType = "application/pdf")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Fact]
    public async Task Surveyor_CanUpload_OnAssignedJob()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, SurveyorId, _jobAId,
            MakeFile(), DocumentCategory.SurveyPlan, DocumentVisibility.Internal);

        Assert.Equal("plan.pdf", doc.FileName);
        Assert.Equal(DocumentCategory.SurveyPlan, doc.Category);
    }

    [Fact]
    public async Task Client_CanUpload_OnAssignedJob()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, ClientId, _jobAId,
            MakeFile("deed.pdf"), DocumentCategory.LegalDocument, DocumentVisibility.ClientVisible);

        Assert.Equal("deed.pdf", doc.FileName);
    }

    [Fact]
    public async Task Surveyor_CannotUpload_OnUnassignedJob()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _documentService.UploadAsync(WorkspaceId, SurveyorId, _jobBId,
                MakeFile(), DocumentCategory.Other, DocumentVisibility.Internal));
    }

    [Fact]
    public async Task RejectsDisallowedExtension()
    {
        await SeedJobsAsync();
        await Assert.ThrowsAsync<ValidationException>(() =>
            _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
                MakeFile("virus.exe", contentType: "application/octet-stream"), DocumentCategory.Other, DocumentVisibility.Internal));
    }

    [Fact]
    public async Task RejectsOversizedFile()
    {
        await SeedJobsAsync();
        var bytes = new byte[26 * 1024 * 1024];
        var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "big.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

        await Assert.ThrowsAsync<ValidationException>(() =>
            _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
                file, DocumentCategory.Other, DocumentVisibility.Internal));
    }

    [Fact]
    public async Task Client_DoesNotSee_InternalDocuments()
    {
        await SeedJobsAsync();
        await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile("internal.pdf"), DocumentCategory.Other, DocumentVisibility.Internal);
        await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile("public.pdf"), DocumentCategory.SurveyPlan, DocumentVisibility.ClientVisible);

        var docs = await _documentService.GetDocumentsAsync(WorkspaceId, ClientId, _jobAId);

        var doc = Assert.Single(docs);
        Assert.Equal("public.pdf", doc.FileName);
    }

    [Fact]
    public async Task Surveyor_SeesInternalDocuments()
    {
        await SeedJobsAsync();
        await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile("internal.pdf"), DocumentCategory.Other, DocumentVisibility.Internal);

        var docs = await _documentService.GetDocumentsAsync(WorkspaceId, SurveyorId, _jobAId);

        Assert.Single(docs);
    }

    [Fact]
    public async Task Client_GettingInternalDocumentById_ThrowsNotFound()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile("internal.pdf"), DocumentCategory.Other, DocumentVisibility.Internal);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _documentService.GetFileAsync(WorkspaceId, ClientId, _jobAId, doc.Id));
    }

    [Fact]
    public async Task GetFileAsync_ReturnsSavedBytes()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile("plan.pdf", "hello-bytes"), DocumentCategory.SurveyPlan, DocumentVisibility.ClientVisible);

        var (found, content) = await _documentService.GetFileAsync(WorkspaceId, AdminId, _jobAId, doc.Id);

        using var reader = new StreamReader(content);
        Assert.Equal("hello-bytes", await reader.ReadToEndAsync());
        Assert.Equal(doc.Id, found.Id);
    }

    [Fact]
    public async Task Client_CannotDelete()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, ClientId, _jobAId,
            MakeFile(), DocumentCategory.Other, DocumentVisibility.ClientVisible);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _documentService.DeleteAsync(WorkspaceId, ClientId, _jobAId, doc.Id));
    }

    [Fact]
    public async Task Admin_CanDelete_AndDocumentIsExcludedFromList()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile(), DocumentCategory.Other, DocumentVisibility.ClientVisible);

        await _documentService.DeleteAsync(WorkspaceId, AdminId, _jobAId, doc.Id);

        var docs = await _documentService.GetDocumentsAsync(WorkspaceId, AdminId, _jobAId);
        Assert.Empty(docs);
    }

    [Fact]
    public async Task DocumentFromDifferentJob_ThrowsNotFound()
    {
        await SeedJobsAsync();
        var doc = await _documentService.UploadAsync(WorkspaceId, AdminId, _jobAId,
            MakeFile(), DocumentCategory.Other, DocumentVisibility.ClientVisible);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _documentService.GetFileAsync(WorkspaceId, AdminId, _jobBId, doc.Id));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

```bash
dotnet test api/tests/SurveyorLedger.API.Tests --filter DocumentServiceTests
```

Expected: build failure, `IDocumentService`/`DocumentService` not found.

- [ ] **Step 3: Write `DocumentService`**

`api/src/SurveyorLedger.API/Services/DocumentService.cs`. The caller's workspace role is resolved once, from `UserAccess.Role.Name` at workspace scope — same query shape as `WorkspaceService.cs:177` — and reused by both the visibility filter and (implicitly, via `EnsureJobAccessAsync`'s existing Casbin/assignment checks) the access gate:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.Data;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IDocumentService
{
    Task<List<Document>> GetDocumentsAsync(Guid workspaceId, Guid callerUserId, Guid jobId);
    Task<Document> UploadAsync(Guid workspaceId, Guid callerUserId, Guid jobId, IFormFile file, DocumentCategory category, DocumentVisibility visibility);
    Task<(Document Document, Stream Content)> GetFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId);
}

/// <summary>
/// Documents are a job sub-resource, same reasoning as MilestoneService: reuse job.view /
/// job.edit Casbin permissions and the job-assignment scoping rule instead of a new
/// permission set. job.view covers list/upload/download (Client has it - that's how they
/// see the job at all), job.edit covers delete (Client never holds job.edit). The caller's
/// role (needed only for the Internal/ClientVisible filter) is resolved from
/// UserAccess.Role.Name at workspace scope, the same way WorkspaceService does it - there
/// is no role claim on the JWT.
/// </summary>
public class DocumentService : IDocumentService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png" };
    private const long MaxFileSizeBytes = 25 * 1024 * 1024;

    private readonly ApplicationDbContext _context;
    private readonly ICasbinService _casbinService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(ApplicationDbContext context, ICasbinService casbinService, IFileStorageService fileStorageService, ILogger<DocumentService> logger)
    {
        _context = context;
        _casbinService = casbinService;
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task<List<Document>> GetDocumentsAsync(Guid workspaceId, Guid callerUserId, Guid jobId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        var callerRole = await GetCallerRoleAsync(callerUserId, workspaceId);

        var documents = await _context.Documents
            .Where(d => d.JobId == jobId && d.IsActive)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return documents.Where(d => IsVisible(d, callerRole)).ToList();
    }

    public async Task<Document> UploadAsync(Guid workspaceId, Guid callerUserId, Guid jobId, IFormFile file, DocumentCategory category, DocumentVisibility visibility)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedExtensions)}.");
        if (file.Length > MaxFileSizeBytes)
            throw new ValidationException("File exceeds the 25 MB size limit.");

        var storedRelativePath = $"{workspaceId}/{jobId}/{Guid.NewGuid():N}_{file.FileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _fileStorageService.SaveAsync(stream, storedRelativePath, CancellationToken.None);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            FileName = file.FileName,
            StoredPath = storedRelativePath,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            Category = category,
            Visibility = visibility,
            UploadedBy = callerUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Documents.AddAsync(document);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Document {DocumentId} uploaded for job {JobId} by {UserId}", document.Id, jobId, callerUserId);
        return document;
    }

    public async Task<(Document Document, Stream Content)> GetFileAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "view");
        var callerRole = await GetCallerRoleAsync(callerUserId, workspaceId);

        var document = await FindDocumentAsync(jobId, documentId);
        if (!IsVisible(document, callerRole))
            throw new NotFoundException("Document not found");

        var content = await _fileStorageService.OpenAsync(document.StoredPath, CancellationToken.None);
        return (document, content);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, Guid jobId, Guid documentId)
    {
        await FindJobAsync(workspaceId, jobId);
        await EnsureJobAccessAsync(callerUserId, workspaceId, jobId, "edit");

        var document = await FindDocumentAsync(jobId, documentId);
        document.IsActive = false;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private static bool IsVisible(Document document, string callerRole) =>
        callerRole != Constants.SystemRoles.Client || document.Visibility == DocumentVisibility.ClientVisible;

    private async Task<string> GetCallerRoleAsync(Guid callerUserId, Guid workspaceId)
    {
        var role = await _context.UserAccesses
            .Where(ua => ua.UserId == callerUserId && ua.IsActive &&
                         ua.ScopeType == Constants.ScopeTypes.Workspace && ua.ScopeId == workspaceId)
            .Select(ua => ua.Role.Name)
            .FirstOrDefaultAsync();

        return role ?? throw new ForbiddenException("You are not a member of this workspace.");
    }

    private async Task<Job> FindJobAsync(Guid workspaceId, Guid jobId)
    {
        return await _context.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            ?? throw new NotFoundException("Job not found");
    }

    private async Task<Document> FindDocumentAsync(Guid jobId, Guid documentId)
    {
        return await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.JobId == jobId && d.IsActive)
            ?? throw new NotFoundException("Document not found");
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
            throw new ForbiddenException($"You do not have permission to {action} documents in this workspace.");

        if (await HasFullJobAccessAsync(callerUserId, workspaceId))
            return;
        if (!await IsAssignedToJobAsync(callerUserId, jobId))
            throw new ForbiddenException($"You do not have permission to {action} documents on this job.");
    }
}
```

- [ ] **Step 4: Register in DI**

In `api/src/SurveyorLedger.API/Program.cs`, after the file storage registration from Task 2:

```csharp
// Register document service
builder.Services.AddScoped<IDocumentService, DocumentService>();
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test api/tests/SurveyorLedger.API.Tests --filter DocumentServiceTests
```

Expected: all pass (13 tests).

- [ ] **Step 6: Commit**

```bash
dotnet build api/SurveyorLedger.sln
git add api/src/SurveyorLedger.API/Services/DocumentService.cs api/src/SurveyorLedger.API/Program.cs api/tests/SurveyorLedger.API.Tests/Services/DocumentServiceTests.cs
git commit -m "feat: add DocumentService with job-scoped RBAC and visibility filtering"
```

---

### Task 4: `DocumentController` + request/response models

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/Document/DocumentUploadRequest.cs`
- Create: `api/src/SurveyorLedger.API/Models/Document/DocumentResponse.cs`
- Create: `api/src/SurveyorLedger.API/Controllers/DocumentController.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Controllers/DocumentControllerTests.cs` (if an existing `MilestoneControllerTests.cs`-equivalent pattern exists; otherwise skip controller-level tests — service tests already cover authorization, and `AuthControllerTests.cs` is the only controller test file today, so check for a matching precedent before adding one)

**Interfaces:**
- Consumes: `IDocumentService` (Task 3) exact signatures above.
- Produces: HTTP endpoints under `api/workspace/{workspaceId}/job/{jobId}/document`.

- [ ] **Step 1: Check for controller test precedent**

```bash
ls api/tests/SurveyorLedger.API.Tests/Controllers/
```

If only `AuthControllerTests.cs` exists (no `MilestoneControllerTests.cs`), controller-level tests aren't an established pattern for sub-resource controllers in this codebase — skip Step 7 (controller test) and rely on the Task 3 service tests, which already cover every authorization branch. If a `MilestoneControllerTests.cs` (or similar) exists, read it and mirror its shape for `DocumentControllerTests.cs` in Step 7.

- [ ] **Step 2: Write the request model**

`api/src/SurveyorLedger.API/Models/Document/DocumentUploadRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.Document;

/// <summary>
/// Bound from multipart/form-data - File is the upload, the rest are form fields.
/// </summary>
public class DocumentUploadRequest
{
    [Required(ErrorMessage = "File is required.")]
    public required IFormFile File { get; set; }

    [Required(ErrorMessage = "Category is required.")]
    public required DocumentCategory Category { get; set; }

    [Required(ErrorMessage = "Visibility is required.")]
    public required DocumentVisibility Visibility { get; set; }
}
```

- [ ] **Step 3: Write the response model**

`api/src/SurveyorLedger.API/Models/Document/DocumentResponse.cs`:

```csharp
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.Document;

public class DocumentResponse
{
    public Guid DocumentId { get; set; }
    public Guid JobId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DocumentCategory Category { get; set; }
    public DocumentVisibility Visibility { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 4: Write the controller**

`api/src/SurveyorLedger.API/Controllers/DocumentController.cs`, mirroring `MilestoneController.cs`'s route/response shape:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyorLedger.API.Models.Document;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Controllers
{
    [ApiController]
    [Route("api/workspace/{workspaceId}/job/{jobId}/document")]
    [Authorize]
    public class DocumentController : ControllerBase
    {
        private readonly IDocumentService _documentService;

        public DocumentController(IDocumentService documentService)
        {
            _documentService = documentService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<DocumentResponse>>>> List(Guid workspaceId, Guid jobId)
        {
            var documents = await _documentService.GetDocumentsAsync(workspaceId, CallerId(), jobId);
            return Ok(ApiResponse<List<DocumentResponse>>.Ok(documents.Select(ToResponse).ToList()));
        }

        [HttpPost]
        [RequestSizeLimit(25 * 1024 * 1024)]
        public async Task<ActionResult<ApiResponse<DocumentResponse>>> Upload(Guid workspaceId, Guid jobId, [FromForm] DocumentUploadRequest request)
        {
            var document = await _documentService.UploadAsync(workspaceId, CallerId(), jobId, request.File, request.Category, request.Visibility);
            return Ok(ApiResponse<DocumentResponse>.Ok(ToResponse(document)));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid workspaceId, Guid jobId, Guid id, [FromQuery] bool download = false)
        {
            var (document, content) = await _documentService.GetFileAsync(workspaceId, CallerId(), jobId, id);
            Response.Headers.ContentDisposition = download
                ? $"attachment; filename=\"{document.FileName}\""
                : $"inline; filename=\"{document.FileName}\"";
            return File(content, document.ContentType);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid workspaceId, Guid jobId, Guid id)
        {
            await _documentService.DeleteAsync(workspaceId, CallerId(), jobId, id);
            return NoContent();
        }

        private Guid CallerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

        private static DocumentResponse ToResponse(Document d) => new()
        {
            DocumentId = d.Id,
            JobId = d.JobId,
            FileName = d.FileName,
            ContentType = d.ContentType,
            FileSizeBytes = d.FileSizeBytes,
            Category = d.Category,
            Visibility = d.Visibility,
            UploadedBy = d.UploadedBy,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        };
    }
}
```

`DocumentController` stays a thin pass-through, same as `MilestoneController` — it doesn't resolve or reason about roles itself. `DocumentService` resolves the caller's role internally (Task 3) since the JWT carries no role claim (confirmed against `TokenService.cs`); pushing that lookup into the controller would mean querying `UserAccess` twice, once for nothing more than a role string the service already needs for its own check.

- [ ] **Step 5: Build**

```bash
dotnet build api/SurveyorLedger.sln
```

Expected: succeeds.

- [ ] **Step 6: Manual verification against a running API**

Run the `api-layer-review` skill checklist against this controller/service pair. Then, with the API running locally (`dotnet run --project src/SurveyorLedger.API`), use the `run` skill or a REST client to:
1. Log in as an Admin, upload a `.pdf` to an existing job with `Visibility=ClientVisible` — expect 200 with a `DocumentResponse`.
2. `GET` the document list for that job — expect the uploaded document in the response.
3. `GET /{id}?download=true` — expect the file bytes with a `Content-Disposition: attachment` header.
4. Log in as a Client assigned to that job, upload an `Internal` document as Admin first, then list as Client — expect the Internal document excluded.
5. Attempt `DELETE` as Client — expect 403.

- [ ] **Step 7 (conditional on Step 1's finding): Write controller test**

Only if an existing sub-resource controller test file was found in Step 1. Otherwise skip — the manual verification in Step 6 plus the exhaustive `DocumentServiceTests` suite is the coverage this codebase's convention expects for a thin pass-through controller.

- [ ] **Step 8: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/Document/ api/src/SurveyorLedger.API/Controllers/DocumentController.cs
git commit -m "feat: add DocumentController with upload/list/download/delete endpoints"
```

---

## Self-Review Notes

- **Spec coverage:** entity/enums (Task 1), storage abstraction (Task 2), upload validation + visibility filtering + job-scoped RBAC (Task 3), API routes matching `MilestoneController` convention + preview/download via query flag (Task 4). All spec sections have a task.
- **Type consistency:** `IDocumentService` signatures defined in Task 3 are the exact ones Task 4's controller calls — `workspaceId, callerUserId, jobId[, ...]` in the same order `MilestoneService`/`MilestoneController` use.
- **Resolved during writing:** checked `TokenService.cs` directly — the JWT has no role claim, so the plan's first draft (controller reading a `ClaimTypes.Role` claim) would have thrown at runtime. Fixed by having `DocumentService` resolve the caller's role itself from `UserAccess`, matching `WorkspaceService.cs:177`'s existing pattern. No open items remain.
