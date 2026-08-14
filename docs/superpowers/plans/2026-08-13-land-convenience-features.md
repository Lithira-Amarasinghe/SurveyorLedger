# Land Convenience Features Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Click-to-call/WhatsApp owner links, a printable land summary, a QR code for the land's location, and a site-photos gallery on Land records.

**Architecture:** Features 1–3 are additive frontend-only (Feature 2/3 use client-side print/QR, no server dependency). Feature 4 adds a new `LandPhoto` entity (deliberately separate from `Document`, per spec — avoids widening the tenant-isolation join path and avoids Job-fulfillment concepts on a table that doesn't need them), methods added directly to the existing `LandService`/`LandController` (matching how Surveys/Deeds/Boundaries already live there), reusing `IFileStorageService` unchanged.

**Tech Stack:** .NET 9/EF Core 9 (backend, no new packages). Angular 21 + new `qrcode` npm package (client-side QR, no API key).

## Global Constraints

- Tenant isolation: `LandPhoto` queries always scope through `LandId` + the existing `EnsureLandAccessAsync` check — no exceptions.
- Migrations generated via `dotnet ef migrations add`, never hand-edited.
- Photo uploads: image-only (`.jpg .jpeg .png`), reuse `DocumentService.MaxFileSizeBytes` (25MB) by reference, not a duplicated constant.
- No new external network dependency: QR and PDF are both client-side (`window.print()` + local `qrcode` package).
- Permission gate for photo mutations: `EnsureLandAccessAsync(..., "edit")`; for listing: `"view"` — identical to Surveys/Deeds/Boundaries.
- Verification: build after each task (fast, catches type errors); run the new/affected test file only, not the whole suite, matching this repo's existing per-feature test scoping.

---

### Task 1: `LandPhoto` entity, config, migration

**Files:**
- Create: `api/src/SurveyorLedger.Data/Entities/LandPhoto.cs`
- Create: `api/src/SurveyorLedger.Data/Configurations/LandPhotoConfiguration.cs`
- Modify: `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`
- Modify: `api/src/SurveyorLedger.Data/Entities/Land.cs`
- Create (generated): migration files

**Interfaces:**
- Produces: `LandPhoto { Id, LandId, FileName, StoredPath, ContentType, FileSizeBytes, UploadedBy, CreatedAt, Land, UploadedByUser }`, `Land.Photos` collection.

- [ ] **Step 1: Create the entity**

```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A site photo attached to a Land record - deliberately separate from Document
/// (which is Job-scoped and carries Category/Visibility concepts this doesn't need).
/// Hard delete on removal, same reasoning as LandSurvey/LandDeed/LandBoundary: corrects
/// a mis-uploaded photo, not meaningful history to preserve once wrong.
/// </summary>
public class LandPhoto
{
    public Guid Id { get; set; }
    public Guid LandId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Land Land { get; set; } = null!;
    public User UploadedByUser { get; set; } = null!;
}
```

- [ ] **Step 2: Add the `Photos` collection to `Land`**

In `api/src/SurveyorLedger.Data/Entities/Land.cs`, add after the `Boundaries` collection:

```csharp
    public ICollection<LandPhoto> Photos { get; set; } = new List<LandPhoto>();
```

- [ ] **Step 3: Create the EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.Data.Configurations;

public class LandPhotoConfiguration : IEntityTypeConfiguration<LandPhoto>
{
    public void Configure(EntityTypeBuilder<LandPhoto> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StoredPath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.LandId);

        builder.HasOne(x => x.Land).WithMany(x => x.Photos).HasForeignKey(x => x.LandId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 4: Register the DbSet**

In `api/src/SurveyorLedger.Data/ApplicationDbContext.cs`, find the `DbSet<LandBoundary>` line and add directly after it:

```csharp
    public DbSet<LandPhoto> LandPhotos { get; set; }
```

- [ ] **Step 5: Generate and apply the migration**

```bash
cd api && dotnet ef migrations add AddLandPhoto --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

Expected: new migration creates a `LandPhotos` table with the columns above, FK to `Lands` (cascade) and `Users` (restrict), index on `LandId`. Inspect the generated migration — it should touch nothing else.

- [ ] **Step 6: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add api/src/SurveyorLedger.Data/
git commit -m "feat: add LandPhoto entity and migration"
```

---

### Task 2: Photo upload/list/delete on `LandService` + `LandController`

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/LandService.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/LandController.cs`
- Create: `api/src/SurveyorLedger.API/Models/Land/LandPhotoResponse.cs`

**Interfaces:**
- Consumes: `IFileStorageService` (existing, injected), `DocumentService.MaxFileSizeBytes` (existing constant, referenced not duplicated).
- Produces: `ILandService.UploadPhotoAsync(Guid workspaceId, Guid callerUserId, Guid landId, IFormFile file) : Task<LandPhoto>`, `GetPhotosAsync(Guid workspaceId, Guid callerUserId, Guid landId) : Task<List<LandPhoto>>`, `GetPhotoFileAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid photoId) : Task<(LandPhoto photo, Stream content)>`, `DeletePhotoAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid photoId) : Task`. Routes: `POST/GET /{id}/photos`, `GET/DELETE /{id}/photos/{photoId}`.

- [ ] **Step 1: Inject `IFileStorageService` into `LandService`**

In `api/src/SurveyorLedger.API/Services/LandService.cs`, add the field and constructor parameter:

```csharp
    private readonly IFileStorageService _fileStorage;
```

Update the constructor:

```csharp
    public LandService(ApplicationDbContext context, IScopedAccessService access, IFileStorageService fileStorage, ILogger<LandService> logger)
    {
        _context = context;
        _access = access;
        _fileStorage = fileStorage;
        _logger = logger;
    }
```

- [ ] **Step 2: Add the four method signatures to `ILandService`**

Add after the location/share-link signatures:

```csharp
    Task<LandPhoto> UploadPhotoAsync(Guid workspaceId, Guid callerUserId, Guid landId, IFormFile file);
    Task<List<LandPhoto>> GetPhotosAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<(LandPhoto photo, Stream content)> GetPhotoFileAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid photoId);
    Task DeletePhotoAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid photoId);
```

- [ ] **Step 3: Implement the four methods**

Add near the end of the `LandService` class, before the private helper methods:

```csharp
    private static readonly HashSet<string> AllowedPhotoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    public async Task<LandPhoto> UploadPhotoAsync(Guid workspaceId, Guid callerUserId, Guid landId, IFormFile file)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        await FindLandAsync(workspaceId, landId);

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedPhotoExtensions.Contains(extension))
            throw new ValidationException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedPhotoExtensions)}.");
        if (file.Length > DocumentService.MaxFileSizeBytes)
            throw new ValidationException($"File exceeds the {DocumentService.MaxFileSizeBytes / (1024 * 1024)}MB size limit.");

        var storedFileName = $"{Guid.NewGuid():N}_{file.FileName}";
        var relativePath = $"{workspaceId}/land/{landId}/{storedFileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _fileStorage.SaveAsync(stream, relativePath, CancellationToken.None);
        }

        var photo = new LandPhoto
        {
            Id = Guid.NewGuid(),
            LandId = landId,
            FileName = file.FileName,
            StoredPath = relativePath,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            UploadedBy = callerUserId,
            CreatedAt = DateTime.UtcNow
        };

        await _context.LandPhotos.AddAsync(photo);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Photo {PhotoId} uploaded to land {LandId} by {UserId}", photo.Id, landId, callerUserId);
        return await _context.LandPhotos.Include(p => p.UploadedByUser).FirstAsync(p => p.Id == photo.Id);
    }

    public async Task<List<LandPhoto>> GetPhotosAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");
        await FindLandAsync(workspaceId, landId);

        return await _context.LandPhotos.Include(p => p.UploadedByUser)
            .Where(p => p.LandId == landId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<(LandPhoto photo, Stream content)> GetPhotoFileAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid photoId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "view");
        var photo = await FindPhotoAsync(landId, photoId);
        var content = await _fileStorage.OpenAsync(photo.StoredPath, CancellationToken.None);
        return (photo, content);
    }

    public async Task DeletePhotoAsync(Guid workspaceId, Guid callerUserId, Guid landId, Guid photoId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        var photo = await FindPhotoAsync(landId, photoId);

        await _fileStorage.DeleteAsync(photo.StoredPath, CancellationToken.None);
        _context.LandPhotos.Remove(photo);
        await _context.SaveChangesAsync();
    }

    private async Task<LandPhoto> FindPhotoAsync(Guid landId, Guid photoId)
    {
        return await _context.LandPhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.LandId == landId)
            ?? throw new NotFoundException("Photo not found");
    }
```

Add the required `using` directives at the top of the file if not already present: `using Microsoft.AspNetCore.Http;`

- [ ] **Step 4: Create `LandPhotoResponse`**

```csharp
namespace SurveyorLedger.API.Models.Land;

public class LandPhotoResponse
{
    public Guid PhotoId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 5: Add routes to `LandController`**

Add after the `DeleteBoundary` action:

```csharp
        [HttpGet("{id}/photos")]
        public async Task<ActionResult<ApiResponse<List<LandPhotoResponse>>>> GetPhotos(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var photos = await _landService.GetPhotosAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<List<LandPhotoResponse>>.Ok(photos.Select(ToResponse).ToList()));
        }

        [HttpPost("{id}/photos")]
        [RequestSizeLimit(DocumentService.MaxFileSizeBytes)]
        public async Task<ActionResult<ApiResponse<LandPhotoResponse>>> UploadPhoto(Guid workspaceId, Guid id, IFormFile file)
        {
            var callerId = CallerId();
            var photo = await _landService.UploadPhotoAsync(workspaceId, callerId, id, file);
            return Ok(ApiResponse<LandPhotoResponse>.Ok(ToResponse(photo)));
        }

        [HttpGet("{id}/photos/{photoId}")]
        public async Task<IActionResult> GetPhotoFile(Guid workspaceId, Guid id, Guid photoId)
        {
            var callerId = CallerId();
            var (photo, content) = await _landService.GetPhotoFileAsync(workspaceId, callerId, id, photoId);
            return File(content, photo.ContentType, photo.FileName);
        }

        [HttpDelete("{id}/photos/{photoId}")]
        public async Task<IActionResult> DeletePhoto(Guid workspaceId, Guid id, Guid photoId)
        {
            var callerId = CallerId();
            await _landService.DeletePhotoAsync(workspaceId, callerId, id, photoId);
            return NoContent();
        }
```

Add the `ToResponse(LandPhoto p)` mapper next to the other `ToResponse` overloads:

```csharp
        private static LandPhotoResponse ToResponse(LandPhoto p) => new()
        {
            PhotoId = p.Id,
            FileName = p.FileName,
            ContentType = p.ContentType,
            FileSizeBytes = p.FileSizeBytes,
            UploadedByName = $"{p.UploadedByUser.FirstName} {p.UploadedByUser.LastName}",
            CreatedAt = p.CreatedAt
        };
```

- [ ] **Step 6: Register `IFileStorageService` where `ILandService` is used in tests**

No production DI change needed (`IFileStorageService` is already registered in `Program.cs`) — this step is a reminder for Task 3's test setup, not a code change here.

- [ ] **Step 7: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded. `DocumentService.MaxFileSizeBytes` must be `public const` (it already is, confirmed during design) for the cross-service reference to compile.

- [ ] **Step 8: Commit**

```bash
git add api/src/SurveyorLedger.API/
git commit -m "feat: add land photo upload/list/delete endpoints"
```

---

### Task 3: Backend photo service tests

**Files:**
- Create: `api/tests/SurveyorLedger.API.Tests/Services/LandPhotoServiceTests.cs`

**Interfaces:**
- Consumes: `WorkspaceIntegrationTestBase`, `ILandService.UploadPhotoAsync/GetPhotosAsync/DeletePhotoAsync` (Task 2).

- [ ] **Step 1: Write the test file**

```csharp
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class LandPhotoServiceTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;
    private Guid _landId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILandService, LandService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:UploadsRootPath"] = Path.Combine(Path.GetTempPath(), $"sl-landphoto-test-{Guid.NewGuid():N}")
                })
                .Build());
    }

    private async Task SeedLandAsync()
    {
        _landService = GetService<ILandService>();
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, new LandRequest
        {
            Address = new AddressDto { Street = "123 Main St", City = "Colombo" }
        });
        _landId = land.Id;
    }

    private static IFormFile MakePhoto(string name = "site.jpg", string contentType = "image/jpeg")
    {
        var bytes = Encoding.UTF8.GetBytes("fake-image-bytes");
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Fact]
    public async Task UploadPhotoAsync_PersistsPhoto()
    {
        await SeedLandAsync();
        var photo = await _landService.UploadPhotoAsync(WorkspaceId, AdminId, _landId, MakePhoto());
        Assert.Equal("site.jpg", photo.FileName);

        var photos = await _landService.GetPhotosAsync(WorkspaceId, AdminId, _landId);
        Assert.Single(photos);
    }

    [Fact]
    public async Task UploadPhotoAsync_RejectsDisallowedExtension()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ValidationException>(
            () => _landService.UploadPhotoAsync(WorkspaceId, AdminId, _landId, MakePhoto("plan.pdf", "application/pdf")));
    }

    [Fact]
    public async Task Client_CannotUploadPhoto()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _landService.UploadPhotoAsync(WorkspaceId, ClientId, _landId, MakePhoto()));
    }

    [Fact]
    public async Task DeletePhotoAsync_RemovesPhoto()
    {
        await SeedLandAsync();
        var photo = await _landService.UploadPhotoAsync(WorkspaceId, AdminId, _landId, MakePhoto());
        await _landService.DeletePhotoAsync(WorkspaceId, AdminId, _landId, photo.Id);

        var photos = await _landService.GetPhotosAsync(WorkspaceId, AdminId, _landId);
        Assert.Empty(photos);
    }

    [Fact]
    public async Task GetPhotoFileAsync_ReturnsUploadedContent()
    {
        await SeedLandAsync();
        var photo = await _landService.UploadPhotoAsync(WorkspaceId, AdminId, _landId, MakePhoto());
        var (found, content) = await _landService.GetPhotoFileAsync(WorkspaceId, AdminId, _landId, photo.Id);

        Assert.Equal(photo.Id, found.Id);
        using var reader = new StreamReader(content);
        Assert.Equal("fake-image-bytes", await reader.ReadToEndAsync());
    }
}
```

- [ ] **Step 2: Run only this test file**

Run: `cd api && dotnet test --filter LandPhotoServiceTests`
Expected: All 5 tests pass. This targeted filter is the verification for this task — no need to run the full suite.

- [ ] **Step 3: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/LandPhotoServiceTests.cs
git commit -m "test: cover land photo upload/list/delete"
```

---

### Task 4: Angular `LandService` photo methods + `PhotoGridComponent`

**Files:**
- Modify: `ui/src/app/core/land.service.ts`
- Create: `ui/src/app/shared/photo-grid/photo-grid.component.ts`

**Interfaces:**
- Produces: `LandPhoto { photoId, fileName, contentType, fileSizeBytes, uploadedByName, createdAt }`, `LandService.listPhotos/uploadPhoto/deletePhoto/getPhotoBlob`, `PhotoGridComponent` — `@Input() photos: LandPhoto[]`, `@Input() readonly = false`, `@Input() photoUrls: Record<string, string>` (blob object-URLs keyed by photoId, since `<img src>` can't carry the auth header — same reasoning `DocumentService.getFileBlob` already documents), `@Output() upload: EventEmitter<File>`, `@Output() remove: EventEmitter<string>`.

- [ ] **Step 1: Add photo types and methods to `land.service.ts`**

Add the interface near `LandBoundary`:

```typescript
export interface LandPhoto {
  photoId: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  uploadedByName: string;
  createdAt: string;
}
```

Add methods to `LandService`, after `deleteBoundary(...)`:

```typescript
  listPhotos(workspaceId: string, landId: string): Observable<LandPhoto[]> {
    return this.http.get<ApiResponse<LandPhoto[]>>(`${this.base(workspaceId)}/${landId}/photos`).pipe(map(res => res.data));
  }

  uploadPhoto(workspaceId: string, landId: string, file: File): Observable<LandPhoto> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ApiResponse<LandPhoto>>(`${this.base(workspaceId)}/${landId}/photos`, form).pipe(map(res => res.data));
  }

  /** Blob fetch, not a bare <img src> - the JWT rides an Authorization header the jwtInterceptor only attaches to HttpClient requests, same reasoning as DocumentService.getFileBlob. */
  getPhotoBlob(workspaceId: string, landId: string, photoId: string): Observable<Blob> {
    return this.http.get(`${this.base(workspaceId)}/${landId}/photos/${photoId}`, { responseType: 'blob' });
  }

  deletePhoto(workspaceId: string, landId: string, photoId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${landId}/photos/${photoId}`);
  }
```

- [ ] **Step 2: Create `PhotoGridComponent`**

```typescript
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LandPhoto } from '../../core/land.service';

/**
 * Thumbnail grid with an upload input and per-photo delete - no HTTP inside the
 * component, same "picker owns no save logic" pattern as LandLocationPickerComponent.
 * Callers fetch photo bytes (auth-header-gated) and pass object-URLs in via photoUrls.
 */
@Component({
  selector: 'app-photo-grid',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="flex flex-wrap gap-sm">
      @for (photo of photos; track photo.photoId) {
        <div class="relative w-24 h-24 rounded-md overflow-hidden border border-neutral-200 bg-neutral-100">
          @if (photoUrls[photo.photoId]) {
            <img [src]="photoUrls[photo.photoId]" [alt]="photo.fileName" class="w-full h-full object-cover" />
          }
          @if (!readonly) {
            <button
              type="button"
              class="absolute top-0 right-0 bg-black/60 text-white text-xs w-6 h-6 leading-6 text-center"
              [attr.aria-label]="'Delete ' + photo.fileName"
              (click)="remove.emit(photo.photoId)"
            >
              ×
            </button>
          }
        </div>
      }
      @if (!readonly) {
        <label class="w-24 h-24 rounded-md border-2 border-dashed border-neutral-300 flex items-center justify-center text-xs text-neutral-500 cursor-pointer hover:bg-neutral-50">
          + Add
          <input type="file" accept="image/jpeg,image/png" class="hidden" (change)="onFileSelected($event)" />
        </label>
      }
    </div>
  `
})
export class PhotoGridComponent {
  @Input() photos: LandPhoto[] = [];
  @Input() readonly = false;
  @Input() photoUrls: Record<string, string> = {};
  @Output() upload = new EventEmitter<File>();
  @Output() remove = new EventEmitter<string>();

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.upload.emit(file);
    input.value = '';
  }
}
```

- [ ] **Step 3: Build**

Run: `cd ui && npm run build`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/core/land.service.ts ui/src/app/shared/photo-grid/
git commit -m "feat: add PhotoGridComponent and land photo API client"
```

---

### Task 5: Wire photos, click-to-call, and QR into `land-detail-panel`

**Files:**
- Modify: `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`
- Modify: `ui/src/app/core/land.service.ts`
- Create: `ui/src/app/shared/land-location-qr/land-location-qr.component.ts`

**Interfaces:**
- Consumes: `PhotoGridComponent` (Task 4), `LandService.listPhotos/uploadPhoto/deletePhoto/getPhotoBlob` (Task 4).
- Produces: `telHref(phone)`, `whatsAppHref(phone)` (exported functions in `land.service.ts`, same pattern as `addressLine`), `LandLocationQrComponent` — `@Input() lat/lng`, `@Input() sizePx = 160`.

- [ ] **Step 1: Add phone link helpers to `land.service.ts`**

Add next to the existing `addressLine` function:

```typescript
/** tel:/wa.me hrefs from free-text OwnerPhone - strips formatting for the link only, display text is untouched. Malformed numbers simply won't resolve on tap; no validation is added (matches OwnerPhone staying unvalidated free text). */
export function telHref(phone: string): string {
  return `tel:${phone.replace(/[^\d+]/g, '')}`;
}

export function whatsAppHref(phone: string): string {
  return `https://wa.me/${phone.replace(/[^\d+]/g, '')}`;
}
```

- [ ] **Step 2: Install `qrcode`**

```bash
cd ui && npm install qrcode && npm install --save-dev @types/qrcode
```

- [ ] **Step 3: Create `LandLocationQrComponent`**

```typescript
import { AfterViewInit, Component, ElementRef, Input, OnChanges, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as QRCode from 'qrcode';

/** Local, offline QR generation (no external QR-image API) - encodes the same Google Maps deep link the "Open in Google Maps" button already uses. */
@Component({
  selector: 'app-land-location-qr',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="flex flex-col items-center gap-xs">
      <canvas #canvasEl [width]="sizePx" [height]="sizePx"></canvas>
      <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="download()">
        Download PNG
      </button>
    </div>
  `
})
export class LandLocationQrComponent implements AfterViewInit, OnChanges {
  @Input() lat!: number;
  @Input() lng!: number;
  @Input() sizePx = 160;

  @ViewChild('canvasEl') canvasEl!: ElementRef<HTMLCanvasElement>;

  ngAfterViewInit(): void {
    this.render();
  }

  ngOnChanges(): void {
    if (this.canvasEl) this.render();
  }

  private render(): void {
    const url = `https://www.google.com/maps?q=${this.lat},${this.lng}`;
    QRCode.toCanvas(this.canvasEl.nativeElement, url, { width: this.sizePx });
  }

  download(): void {
    const link = document.createElement('a');
    link.download = 'land-location-qr.png';
    link.href = this.canvasEl.nativeElement.toDataURL('image/png');
    link.click();
  }
}
```

- [ ] **Step 4: Wire click-to-call links into the owner picker area**

In `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`, update the import line:

```typescript
import { Address, Land, LandBoundary, LandDeed, LandPhoto, LandService, LandSurvey, telHref, whatsAppHref } from '../../../core/land.service';
import { PhotoGridComponent } from '../../../shared/photo-grid/photo-grid.component';
import { LandLocationQrComponent } from '../../../shared/land-location-qr/land-location-qr.component';
```

Update the `imports` array to include `PhotoGridComponent, LandLocationQrComponent`.

Add call/WhatsApp links right after the `<app-owner-picker>` block, still inside the Details `<div>`:

```html
          @if (land()?.ownerPhone) {
            <div class="flex gap-md mt-xs text-xs">
              <a [href]="telHref(land()!.ownerPhone!)" class="text-primary-600 hover:text-primary-700">Call {{ land()!.ownerPhone }}</a>
              <a [href]="whatsAppHref(land()!.ownerPhone!)" target="_blank" rel="noopener" class="text-primary-600 hover:text-primary-700">WhatsApp</a>
            </div>
          }
```

Add `telHref = telHref;` and `whatsAppHref = whatsAppHref;` as class fields (same pattern already used for `addressLine` elsewhere in this codebase), right after `boundaries = signal<LandBoundary[]>([]);`.

- [ ] **Step 5: Add the QR code next to the Google Maps buttons**

In the Location block, right after the `app-land-location-picker` (readonly preview map), before the button row:

```html
            <div class="mt-sm">
              <app-land-location-qr [lat]="land()!.latitude!" [lng]="land()!.longitude!" />
            </div>
```

- [ ] **Step 6: Add a Photos block**

Add a new `<div>` after the Boundaries block, before the closing `</div>` of `space-y-lg`:

```html
        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Photos</h3>
          <app-photo-grid
            [photos]="photos()"
            [photoUrls]="photoUrls()"
            (upload)="onPhotoUpload($event)"
            (remove)="onPhotoDelete($event)"
          />
          @if (photoError()) {
            <p class="text-xs text-primary-500 mt-xs">{{ photoError() }}</p>
          }
        </div>
```

- [ ] **Step 7: Add photo state and methods to the component class**

Add signals after `boundaries = signal<LandBoundary[]>([]);`:

```typescript
  photos = signal<LandPhoto[]>([]);
  photoUrls = signal<Record<string, string>>({});
  photoError = signal('');
```

Add to `fetch()`'s `forkJoin` object: `photos: this.landService.listPhotos(this.workspaceId, this.landId)`, and in the `next` handler add `this.photos.set(photos); this.loadPhotoThumbnails(photos);` (destructure `photos` from the resolved object alongside `land, surveys, deeds, boundaries`).

Add methods after `deleteBoundary(...)`:

```typescript
  private loadPhotoThumbnails(photos: LandPhoto[]): void {
    photos.forEach(photo => {
      this.landService.getPhotoBlob(this.workspaceId, this.landId, photo.photoId).subscribe(blob => {
        this.photoUrls.update(urls => ({ ...urls, [photo.photoId]: URL.createObjectURL(blob) }));
      });
    });
  }

  onPhotoUpload(file: File): void {
    this.photoError.set('');
    this.landService.uploadPhoto(this.workspaceId, this.landId, file).subscribe({
      next: (photo) => {
        this.photos.update(list => [photo, ...list]);
        this.loadPhotoThumbnails([photo]);
      },
      error: (err) => this.photoError.set(err.error?.message ?? 'Could not upload photo.')
    });
  }

  onPhotoDelete(photoId: string): void {
    this.photoError.set('');
    this.landService.deletePhoto(this.workspaceId, this.landId, photoId).subscribe({
      next: () => {
        this.photos.update(list => list.filter(p => p.photoId !== photoId));
        this.photoUrls.update(urls => {
          const { [photoId]: _, ...rest } = urls;
          return rest;
        });
      },
      error: (err) => this.photoError.set(err.error?.message ?? 'Could not delete photo.')
    });
  }
```

- [ ] **Step 8: Build and verify in-browser**

Run: `cd ui && npm run build` — expect success.

Then start the API+UI preview, open a land record with an owner phone and a location set: confirm "Call"/"WhatsApp" links render with correct `href`s, the QR canvas renders (not blank), scanning/inspecting it encodes the right Maps URL, uploading a photo shows a thumbnail, deleting removes it. This in-browser check is the verification for this task — no full test suite run needed for a frontend-only change.

- [ ] **Step 9: Commit**

```bash
git add ui/src/app/pages/land/land-detail-panel/ ui/src/app/core/land.service.ts ui/src/app/shared/land-location-qr/ ui/package.json ui/package-lock.json
git commit -m "feat: wire click-to-call, QR code, and photos into land-detail-panel"
```

---

### Task 6: Printable land summary

**Files:**
- Create: `ui/src/app/pages/land/land-print.component.ts`
- Modify: `ui/src/app/app.routes.ts`
- Modify: `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`

**Interfaces:**
- Consumes: `LandService` (existing), `LandLocationQrComponent` (Task 5), `PhotoGridComponent` (Task 4, in readonly mode), `telHref`/`whatsAppHref` (Task 5).
- Produces: route `/app/workspace/:id/lands/:landId/print`.

- [ ] **Step 1: Create `LandPrintComponent`**

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Land, LandBoundary, LandDeed, LandPhoto, LandService, LandSurvey, addressLine, telHref, whatsAppHref } from '../../core/land.service';
import { LandLocationQrComponent } from '../../shared/land-location-qr/land-location-qr.component';
import { PhotoGridComponent } from '../../shared/photo-grid/photo-grid.component';

/**
 * Standalone print view - no app shell, laid out for one page. window.print() with the
 * browser's native "Save as PDF" IS the export mechanism; no server-side PDF library.
 */
@Component({
  selector: 'app-land-print',
  standalone: true,
  imports: [CommonModule, LandLocationQrComponent, PhotoGridComponent],
  template: `
    @if (loading()) {
      <p class="p-lg text-sm text-neutral-500">Loading…</p>
    } @else if (land(); as land) {
      <div class="max-w-2xl mx-auto p-lg">
        <div class="flex justify-between items-start mb-lg print:hidden">
          <h1 class="text-lg font-semibold">Land Summary</h1>
          <button type="button" class="btn-primary" (click)="print()">Print / Save as PDF</button>
        </div>

        <h1 class="text-xl font-semibold text-neutral-900">{{ addressLine(land) }}</h1>
        @if (land.size) {
          <p class="text-sm text-neutral-600">{{ land.size }} {{ land.sizeUnit }}</p>
        }

        @if (land.ownerName) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase">Owner</h2>
            <p class="text-sm text-neutral-900">{{ land.ownerName }}</p>
            @if (land.ownerPhone) {
              <p class="text-sm">
                {{ land.ownerPhone }}
                <a [href]="telHref(land.ownerPhone)">Call</a> ·
                <a [href]="whatsAppHref(land.ownerPhone)">WhatsApp</a>
              </p>
            }
          </div>
        }

        @if (land.latitude !== null && land.longitude !== null) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Location</h2>
            <img
              [src]="'https://staticmap.openstreetmap.de/staticmap.php?center=' + land.latitude + ',' + land.longitude + '&zoom=16&size=600x300&markers=' + land.latitude + ',' + land.longitude + ',red-pushpin'"
              alt="Map of land location"
              class="w-full max-w-md rounded-md border border-neutral-200"
            />
            <app-land-location-qr [lat]="land.latitude" [lng]="land.longitude" [sizePx]="120" />
          </div>
        }

        @if (deeds().length > 0) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Deeds</h2>
            @for (d of deeds(); track d.id) {
              <p class="text-sm">{{ d.deedNumber }} — {{ d.issuedDate | date: 'mediumDate' }}</p>
            }
          </div>
        }

        @if (surveys().length > 0) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Surveys</h2>
            @for (s of surveys(); track s.id) {
              <p class="text-sm">{{ s.surveyPlanNumber }} — {{ s.surveyDate | date: 'mediumDate' }}</p>
            }
          </div>
        }

        @if (boundaries().length > 0) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Boundaries</h2>
            @for (b of boundaries(); track b.id) {
              <p class="text-sm">{{ b.label }}@if (b.description) { — {{ b.description }} }</p>
            }
          </div>
        }

        @if (photos().length > 0) {
          <div class="mt-md">
            <h2 class="text-xs font-semibold text-neutral-500 uppercase mb-xs">Photos</h2>
            <app-photo-grid [photos]="photos()" [photoUrls]="photoUrls()" [readonly]="true" />
          </div>
        }
      </div>
    }
  `
})
export class LandPrintComponent implements OnInit {
  loading = signal(true);
  land = signal<Land | null>(null);
  surveys = signal<LandSurvey[]>([]);
  deeds = signal<LandDeed[]>([]);
  boundaries = signal<LandBoundary[]>([]);
  photos = signal<LandPhoto[]>([]);
  photoUrls = signal<Record<string, string>>({});

  addressLine = addressLine;
  telHref = telHref;
  whatsAppHref = whatsAppHref;

  private workspaceId = '';
  private landId = '';

  constructor(private route: ActivatedRoute, private landService: LandService) {}

  ngOnInit(): void {
    this.workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    this.landId = this.route.snapshot.paramMap.get('landId') ?? '';

    forkJoin({
      land: this.landService.getById(this.workspaceId, this.landId),
      surveys: this.landService.getSurveys(this.workspaceId, this.landId),
      deeds: this.landService.getDeeds(this.workspaceId, this.landId),
      boundaries: this.landService.getBoundaries(this.workspaceId, this.landId),
      photos: this.landService.listPhotos(this.workspaceId, this.landId)
    }).subscribe(({ land, surveys, deeds, boundaries, photos }) => {
      this.land.set(land);
      this.surveys.set(surveys);
      this.deeds.set(deeds);
      this.boundaries.set(boundaries);
      this.photos.set(photos);
      photos.forEach(photo => {
        this.landService.getPhotoBlob(this.workspaceId, this.landId, photo.photoId).subscribe(blob => {
          this.photoUrls.update(urls => ({ ...urls, [photo.photoId]: URL.createObjectURL(blob) }));
        });
      });
      this.loading.set(false);
    });
  }

  print(): void {
    window.print();
  }
}
```

- [ ] **Step 2: Register the route**

In `ui/src/app/app.routes.ts`, add the import:

```typescript
import { LandPrintComponent } from './pages/land/land-print.component';
```

Add inside the `workspace/:id` children array, after the `lands/:landId` route:

```typescript
          { path: 'lands/:landId/print', component: LandPrintComponent },
```

- [ ] **Step 3: Add a "Print summary" button to `land-detail-panel`**

In `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`, add a `RouterLink` import and add a link next to the existing Delete button in the Details header row:

```typescript
import { RouterLink } from '@angular/router';
```

Add `RouterLink` to the component's `imports` array.

In the template, in the header row's non-dirty/non-confirming `@else` branch (where the lone "Delete" button currently sits), add before it:

```html
              <a
                class="text-xs text-neutral-500 hover:text-neutral-700"
                [routerLink]="['/app/workspace', workspaceId, 'lands', landId, 'print']"
              >
                Print summary
              </a>
```

- [ ] **Step 4: Build and verify in-browser**

Run: `cd ui && npm run build` — expect success.

Navigate to `/app/workspace/{id}/lands/{landId}/print` for a land with a location and photos set: confirm the page renders standalone (no sidebar), the static map image loads, the QR renders, deeds/surveys/boundaries/photos lists show, and "Print / Save as PDF" opens the browser print dialog with a clean single-page layout. This in-browser check is the verification for this task.

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/pages/land/land-print.component.ts ui/src/app/app.routes.ts ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts
git commit -m "feat: add printable land summary page"
```

---

## Self-Review Notes

- **Spec coverage:** Feature 1 (Task 5 step 4), Feature 2 (Task 6), Feature 3 (Task 5 steps 2-3-5, reused in Task 6), Feature 4 (Tasks 1-4, wired in Task 5 step 6-7) — all covered.
- **Rejected alternatives honored:** no shared polymorphic Document, no Job/Land join table — `LandPhoto` is its own entity as decided.
- **Type consistency:** `LandPhoto` (backend entity, Task 1) → `LandPhotoResponse` (Task 2) → Angular `LandPhoto` interface (Task 4) — field names verified consistent (`photoId`/`fileName`/`contentType`/`fileSizeBytes`/`uploadedByName`/`createdAt`) at each translation point.
- **Verification approach honored:** each task's step ends with either a targeted `dotnet test --filter <TestClass>` or a build + scoped in-browser check — never a full-suite run, per the instruction to avoid re-running everything on every change.
