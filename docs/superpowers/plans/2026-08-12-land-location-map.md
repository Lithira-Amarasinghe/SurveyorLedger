# Land GPS Location Map + Client Set-Location Link + Collapsed Summary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let land records carry a precise map pin (lat/lng), give staff a Leaflet/OSM picker plus a Google Maps deep link, let a client set the pin themselves via an unauthenticated share link, and show more useful info on collapsed land rows.

**Architecture:** `Land` gains `Latitude`/`Longitude`/`LocationShareToken` columns (EF migration). `LandService` gains location + share-link methods gated by the existing `land.edit`/`land.view` Casbin checks via `IScopedAccessService.EnsureLandAccessAsync`. Authenticated routes live on the existing `LandController`; two unauthenticated routes live on a new `LandLocationLinkController`, mirroring the `DocumentRequestLinkController` trust-boundary split. Frontend: a standalone `LandLocationPickerComponent` (Leaflet + OSM tiles + Nominatim search) reused by both the authenticated `land-detail-panel` and a new public `/set-location/:token` page outside the app shell.

**Tech Stack:** .NET 9 / EF Core 9 / Casbin.NET (backend, unchanged deps). Angular 21 standalone components + `leaflet` npm package (new frontend dep, no API key).

## Global Constraints

- Tenant isolation: every land query filters by `WorkspaceId` — no exceptions (project rule).
- Migrations are generated via `dotnet ef migrations add`, never hand-edited (project rule).
- Permission gate for all authenticated location/share-link mutations: `IScopedAccessService.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit")` — the same gate `UpdateAsync` already uses. No new Casbin permission.
- Public token endpoints: no workspace/land id in the URL or response — the token alone resolves the record (spec: "leak existence not secret").
- Lat/lng stored as `decimal(9,6)`, validated server-side to `[-90,90]` / `[-180,180]` regardless of client-side validation.
- No Google Maps SDK/API key anywhere — "Open/Copy in Google Maps" is a plain `https://www.google.com/maps?q={lat},{lng}` link.

---

### Task 1: `Land` entity + EF configuration + migration

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/Land.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/LandConfiguration.cs`
- Create (generated): `api/src/SurveyorLedger.Data/Migrations/<timestamp>_AddLandLocation.cs`

**Interfaces:**
- Produces: `Land.Latitude` (`decimal?`), `Land.Longitude` (`decimal?`), `Land.LocationShareToken` (`string?`) — consumed by every later backend task.

- [ ] **Step 1: Add the three properties to `Land`**

In `api/src/SurveyorLedger.Data/Entities/Land.cs`, add after `GpsCoordinates`:

```csharp
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? LocationShareToken { get; set; }
```

- [ ] **Step 2: Configure the new columns**

In `api/src/SurveyorLedger.Data/Configurations/LandConfiguration.cs`, add after the `GpsCoordinates` line:

```csharp
        builder.Property(x => x.Latitude).HasColumnType("decimal(9,6)");
        builder.Property(x => x.Longitude).HasColumnType("decimal(9,6)");
        builder.Property(x => x.LocationShareToken).HasMaxLength(64);
        builder.HasIndex(x => x.LocationShareToken).IsUnique().HasFilter("[LocationShareToken] IS NOT NULL");
```

- [ ] **Step 3: Generate the migration**

Run:
```bash
cd api && dotnet ef migrations add AddLandLocation --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

Expected: new migration file with `AddColumn` for `Latitude`, `Longitude`, `LocationShareToken` on `Lands`, plus the filtered unique index. Inspect the generated file — confirm no unrelated model drift got swept in (run `git diff` on the migration if `ModelSnapshot.cs` changes beyond these three columns, stop and investigate before continuing).

- [ ] **Step 4: Apply the migration to LocalDB**

Run:
```bash
cd api && dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

Expected: succeeds, no errors.

- [ ] **Step 5: Build to confirm no compile errors**

Run: `cd api && dotnet build`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.Data/Entities/Land.cs api/src/SurveyorLedger.Data/Configurations/LandConfiguration.cs api/src/SurveyorLedger.Data/Migrations/
git commit -m "feat: add Latitude/Longitude/LocationShareToken to Land"
```

---

### Task 2: Request/response DTOs for location + share link

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/Land/LandLocationRequest.cs`
- Create: `api/src/SurveyorLedger.API/Models/Land/LandLocationShareLinkResponse.cs`
- Create: `api/src/SurveyorLedger.API/Models/Land/LandLocationLinkPreviewResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Land/LandResponse.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `LandLocationRequest { Latitude, Longitude }`, `LandLocationShareLinkResponse { Token }`, `LandLocationLinkPreviewResponse { AddressLine, Latitude, Longitude }`, `LandResponse.Latitude/Longitude/HasActiveLocationShareLink` — consumed by Task 3 (controller) and Task 5 (public controller).

- [ ] **Step 1: Create `LandLocationRequest`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

/// <summary>Request body for setting a Land's map pin, authenticated or via share token.</summary>
public class LandLocationRequest
{
    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
    public decimal Latitude { get; set; }

    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
    public decimal Longitude { get; set; }
}
```

- [ ] **Step 2: Create `LandLocationShareLinkResponse`**

```csharp
namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Returned only from generate/regenerate - the raw token never appears in LandResponse,
/// so it doesn't casually show up in every list/get call an authenticated browser makes.
/// </summary>
public class LandLocationShareLinkResponse
{
    public string Token { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Create `LandLocationLinkPreviewResponse`**

```csharp
namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Returned from the public preview endpoint. Deliberately excludes land id, owner,
/// and workspace name - the recipient already knows which land this is about from
/// context, and nothing here should be useful to someone who merely intercepts the URL.
/// </summary>
public class LandLocationLinkPreviewResponse
{
    public string AddressLine { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}
```

- [ ] **Step 4: Add fields to `LandResponse`**

In `api/src/SurveyorLedger.API/Models/Land/LandResponse.cs`, add after `GpsCoordinates`:

```csharp
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    /// <summary>Leaks existence of an active share link, never the token itself.</summary>
    public bool HasActiveLocationShareLink { get; set; }
```

- [ ] **Step 5: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.API/Models/Land/
git commit -m "feat: add DTOs for land location and share link"
```

---

### Task 3: `LandService` — location + share-link methods

**Files:**
- Modify: `api/src/SurveyorLedger.API/Services/LandService.cs`

**Interfaces:**
- Consumes: `Land.Latitude/Longitude/LocationShareToken` (Task 1), `LandLocationRequest` (Task 2), `IScopedAccessService.EnsureLandAccessAsync(Guid, Guid, Guid, string)` (existing).
- Produces (added to `ILandService`): 
  - `Task<Land> SetLocationAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandLocationRequest request)`
  - `Task<string> GenerateLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId)`
  - `Task<string> RegenerateLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId)`
  - `Task RevokeLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId)`
  - `Task<Land> GetByLocationShareTokenAsync(string token)` — static/no-DI-scoping variant, used by public controller.
  - `Task<Land> SetLocationViaShareTokenAsync(string token, LandLocationRequest request)`

- [ ] **Step 1: Add the six method signatures to `ILandService`**

In `api/src/SurveyorLedger.API/Services/LandService.cs`, add inside the `ILandService` interface, after the `DeleteAsync` line:

```csharp
    Task<Land> SetLocationAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandLocationRequest request);
    Task<string> GenerateLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<string> RegenerateLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task RevokeLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId);
    Task<Land> GetByLocationShareTokenAsync(string token);
    Task<Land> SetLocationViaShareTokenAsync(string token, LandLocationRequest request);
```

- [ ] **Step 2: Implement `SetLocationAsync`**

Add to the `LandService` class, after `UpdateAsync`:

```csharp
    public async Task<Land> SetLocationAsync(Guid workspaceId, Guid callerUserId, Guid landId, LandLocationRequest request)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        var land = await FindLandAsync(workspaceId, landId);

        land.Latitude = request.Latitude;
        land.Longitude = request.Longitude;
        land.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return land;
    }
```

- [ ] **Step 3: Implement share-link generate/regenerate/revoke**

Add after `SetLocationAsync`:

```csharp
    public async Task<string> GenerateLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        var land = await FindLandAsync(workspaceId, landId);

        // Idempotent: an existing active token is returned as-is, not overwritten -
        // regenerating is a distinct, explicit action (see RegenerateLocationShareLinkAsync).
        if (land.LocationShareToken != null)
            return land.LocationShareToken;

        land.LocationShareToken = Guid.NewGuid().ToString("N");
        await _context.SaveChangesAsync();
        return land.LocationShareToken;
    }

    public async Task<string> RegenerateLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        var land = await FindLandAsync(workspaceId, landId);

        land.LocationShareToken = Guid.NewGuid().ToString("N");
        await _context.SaveChangesAsync();
        return land.LocationShareToken;
    }

    public async Task RevokeLocationShareLinkAsync(Guid workspaceId, Guid callerUserId, Guid landId)
    {
        await _access.EnsureLandAccessAsync(callerUserId, workspaceId, landId, "edit");
        var land = await FindLandAsync(workspaceId, landId);

        land.LocationShareToken = null;
        await _context.SaveChangesAsync();
    }
```

- [ ] **Step 4: Implement the two public-token methods**

Add after `RevokeLocationShareLinkAsync`:

```csharp
    public async Task<Land> GetByLocationShareTokenAsync(string token)
    {
        return await _context.Lands.FirstOrDefaultAsync(l => l.LocationShareToken == token && l.IsActive)
            ?? throw new NotFoundException("Link not found");
    }

    public async Task<Land> SetLocationViaShareTokenAsync(string token, LandLocationRequest request)
    {
        var land = await GetByLocationShareTokenAsync(token);

        land.Latitude = request.Latitude;
        land.Longitude = request.Longitude;
        land.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return land;
    }
```

- [ ] **Step 5: Add the `using` for `LandLocationRequest`'s namespace**

`LandService.cs` already has `using SurveyorLedger.API.Models.Land;` at the top — confirm it's present (it is, from the existing `LandRequest` usage). No change needed if so.

- [ ] **Step 6: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add api/src/SurveyorLedger.API/Services/LandService.cs
git commit -m "feat: add location and share-link methods to LandService"
```

---

### Task 4: `LandController` — authenticated location + share-link routes, `ToResponse` update

**Files:**
- Modify: `api/src/SurveyorLedger.API/Controllers/LandController.cs`

**Interfaces:**
- Consumes: `ILandService.SetLocationAsync/GenerateLocationShareLinkAsync/RegenerateLocationShareLinkAsync/RevokeLocationShareLinkAsync` (Task 3), `LandLocationRequest`, `LandLocationShareLinkResponse` (Task 2).
- Produces: routes `PUT .../location`, `POST .../location-share-link`, `POST .../location-share-link/regenerate`, `DELETE .../location-share-link`.

- [ ] **Step 1: Add the four endpoints**

In `api/src/SurveyorLedger.API/Controllers/LandController.cs`, add after the `Delete` action (before `GetSurveys`):

```csharp
        [HttpPut("{id}/location")]
        public async Task<ActionResult<ApiResponse<LandResponse>>> SetLocation(Guid workspaceId, Guid id, [FromBody] LandLocationRequest request)
        {
            var callerId = CallerId();
            var land = await _landService.SetLocationAsync(workspaceId, callerId, id, request);
            return Ok(ApiResponse<LandResponse>.Ok(ToResponse(land)));
        }

        [HttpPost("{id}/location-share-link")]
        public async Task<ActionResult<ApiResponse<LandLocationShareLinkResponse>>> GenerateLocationShareLink(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var token = await _landService.GenerateLocationShareLinkAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<LandLocationShareLinkResponse>.Ok(new LandLocationShareLinkResponse { Token = token }));
        }

        [HttpPost("{id}/location-share-link/regenerate")]
        public async Task<ActionResult<ApiResponse<LandLocationShareLinkResponse>>> RegenerateLocationShareLink(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            var token = await _landService.RegenerateLocationShareLinkAsync(workspaceId, callerId, id);
            return Ok(ApiResponse<LandLocationShareLinkResponse>.Ok(new LandLocationShareLinkResponse { Token = token }));
        }

        [HttpDelete("{id}/location-share-link")]
        public async Task<IActionResult> RevokeLocationShareLink(Guid workspaceId, Guid id)
        {
            var callerId = CallerId();
            await _landService.RevokeLocationShareLinkAsync(workspaceId, callerId, id);
            return NoContent();
        }
```

- [ ] **Step 2: Update `ToResponse(Land l)` to include the new fields**

In the same file, change the `ToResponse(Land l)` method (currently ending at `OwnerEmail = ...`) to add three lines before the closing `};`:

```csharp
            OwnerEmail = l.Owner != null ? l.Owner.Email : l.OwnerEmail,
            Latitude = l.Latitude,
            Longitude = l.Longitude,
            HasActiveLocationShareLink = l.LocationShareToken != null
        };
```

(Replace the existing final `OwnerEmail = ...` line, which currently has no trailing comma, with this block.)

- [ ] **Step 3: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add api/src/SurveyorLedger.API/Controllers/LandController.cs
git commit -m "feat: expose land location and share-link endpoints"
```

---

### Task 5: Public `LandLocationLinkController`

**Files:**
- Create: `api/src/SurveyorLedger.API/Controllers/LandLocationLinkController.cs`

**Interfaces:**
- Consumes: `ILandService.GetByLocationShareTokenAsync/SetLocationViaShareTokenAsync` (Task 3), `LandLocationLinkPreviewResponse`, `LandLocationRequest` (Task 2).
- Produces: `GET /api/land-location-links/{token}`, `PUT /api/land-location-links/{token}`.

- [ ] **Step 1: Create the controller**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Controllers
{
    /// <summary>
    /// Deliberately separate from LandController: every action here is unauthenticated
    /// by design (the token is the only credential), mirroring DocumentRequestLinkController's
    /// split so the trust boundary is visible at a glance.
    /// </summary>
    [ApiController]
    [Route("api/land-location-links")]
    [EnableRateLimiting("auth")]
    public class LandLocationLinkController : ControllerBase
    {
        private readonly ILandService _landService;

        public LandLocationLinkController(ILandService landService)
        {
            _landService = landService;
        }

        [HttpGet("{token}")]
        public async Task<ActionResult<ApiResponse<LandLocationLinkPreviewResponse>>> Preview(string token)
        {
            var land = await _landService.GetByLocationShareTokenAsync(token);
            return Ok(ApiResponse<LandLocationLinkPreviewResponse>.Ok(new LandLocationLinkPreviewResponse
            {
                AddressLine = FormatAddressLine(land),
                Latitude = land.Latitude,
                Longitude = land.Longitude
            }));
        }

        [HttpPut("{token}")]
        public async Task<IActionResult> SetLocation(string token, [FromBody] LandLocationRequest request)
        {
            await _landService.SetLocationViaShareTokenAsync(token, request);
            return NoContent();
        }

        private static string FormatAddressLine(Data.Entities.Land land)
        {
            var parts = new[] { land.Address.Street, land.Address.City }.Where(p => !string.IsNullOrWhiteSpace(p));
            var line = string.Join(", ", parts);
            return string.IsNullOrEmpty(line) ? "Unnamed land record" : line;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add api/src/SurveyorLedger.API/Controllers/LandLocationLinkController.cs
git commit -m "feat: add public land location share-link controller"
```

---

### Task 6: Backend service tests

**Files:**
- Create: `api/tests/SurveyorLedger.API.Tests/Services/LandLocationServiceTests.cs`

**Interfaces:**
- Consumes: `WorkspaceIntegrationTestBase` (existing test base, provides `WorkspaceId`, `AdminId`, `SurveyorId`, `ClientId`, `GetService<T>()`), `ILandService` (Task 3), `LandRequest`/`LandLocationRequest`.

- [ ] **Step 1: Write the test file**

```csharp
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Location/share-link permission mirrors every other Land mutation: land.edit
/// (Admin/Surveyor with access to the record) required, Client forbidden.
/// </summary>
public class LandLocationServiceTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;
    private Guid _landId;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILandService, LandService>();
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

    [Fact]
    public async Task SetLocationAsync_PersistsLatLng()
    {
        await SeedLandAsync();
        var updated = await _landService.SetLocationAsync(WorkspaceId, AdminId, _landId, new LandLocationRequest { Latitude = 6.9271m, Longitude = 79.8612m });
        Assert.Equal(6.9271m, updated.Latitude);
        Assert.Equal(79.8612m, updated.Longitude);
    }

    [Fact]
    public async Task Client_CannotSetLocation()
    {
        await SeedLandAsync();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _landService.SetLocationAsync(WorkspaceId, ClientId, _landId, new LandLocationRequest { Latitude = 1, Longitude = 1 }));
    }

    [Fact]
    public async Task GenerateLocationShareLinkAsync_IsIdempotent()
    {
        await SeedLandAsync();
        var first = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);
        var second = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task RegenerateLocationShareLinkAsync_IssuesNewToken_OldTokenNoLongerResolves()
    {
        await SeedLandAsync();
        var oldToken = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);
        var newToken = await _landService.RegenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);

        Assert.NotEqual(oldToken, newToken);
        await Assert.ThrowsAsync<NotFoundException>(() => _landService.GetByLocationShareTokenAsync(oldToken));
        var land = await _landService.GetByLocationShareTokenAsync(newToken);
        Assert.Equal(_landId, land.Id);
    }

    [Fact]
    public async Task RevokeLocationShareLinkAsync_TokenNoLongerResolves()
    {
        await SeedLandAsync();
        var token = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);
        await _landService.RevokeLocationShareLinkAsync(WorkspaceId, AdminId, _landId);

        await Assert.ThrowsAsync<NotFoundException>(() => _landService.GetByLocationShareTokenAsync(token));
    }

    [Fact]
    public async Task SetLocationViaShareTokenAsync_UpdatesSameLandRow()
    {
        await SeedLandAsync();
        var token = await _landService.GenerateLocationShareLinkAsync(WorkspaceId, AdminId, _landId);

        var updated = await _landService.SetLocationViaShareTokenAsync(token, new LandLocationRequest { Latitude = 6.0m, Longitude = 80.0m });

        Assert.Equal(_landId, updated.Id);
        var land = await _landService.GetByIdAsync(WorkspaceId, AdminId, _landId);
        Assert.Equal(6.0m, land.Latitude);
        Assert.Equal(80.0m, land.Longitude);
    }

    [Fact]
    public async Task GetByLocationShareTokenAsync_UnknownToken_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _landService.GetByLocationShareTokenAsync("not-a-real-token"));
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `cd api && dotnet test --filter LandLocationServiceTests`
Expected: All 7 tests pass. If `AddressDto`/`LandRequest` field names differ from what's used here, fix to match the actual types (they were confirmed to be `Address`, `AddressDto.Street/City` in exploration — adjust only if the build errors say otherwise).

- [ ] **Step 3: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/LandLocationServiceTests.cs
git commit -m "test: cover land location and share-link service methods"
```

---

### Task 7: Install Leaflet, `LandLocationPickerComponent`

**Files:**
- Modify: `ui/package.json`
- Create: `ui/src/app/shared/land-location-picker/land-location-picker.component.ts`

**Interfaces:**
- Produces: `LandLocationPickerComponent` — `@Input() initialLat: number | null`, `@Input() initialLng: number | null`, `@Output() locationChosen: EventEmitter<{lat: number; lng: number}>`. Consumed by Task 8 (land-detail-panel) and Task 10 (public page).

- [ ] **Step 1: Install `leaflet` and its types**

```bash
cd ui && npm install leaflet && npm install --save-dev @types/leaflet
```

Expected: `leaflet` added to `dependencies`, `@types/leaflet` to `devDependencies` in `ui/package.json`.

- [ ] **Step 2: Create the picker component**

```typescript
import { Component, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';

interface NominatimResult {
  display_name: string;
  lat: string;
  lon: string;
}

/**
 * Leaflet + OpenStreetMap pin picker - no API key, no billing. Used both from the
 * authenticated land-detail-panel and the public unauthenticated set-location page,
 * so it owns no save/HTTP logic of its own - it only emits the chosen point.
 */
@Component({
  selector: 'app-land-location-picker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="space-y-sm">
      <div class="flex gap-sm">
        <input
          class="input-field flex-1"
          type="text"
          placeholder="Search for an address…"
          [(ngModel)]="searchQuery"
          (keydown.enter)="search()"
        />
        <button type="button" class="btn-secondary" (click)="search()" [disabled]="searching()">
          {{ searching() ? 'Searching…' : 'Search' }}
        </button>
      </div>
      @if (searchError()) {
        <p class="text-xs text-neutral-500">{{ searchError() }}</p>
      }
      @if (searchResults().length > 0) {
        <div class="border border-neutral-200 rounded-md divide-y divide-neutral-100 max-h-40 overflow-y-auto">
          @for (r of searchResults(); track r.display_name) {
            <button
              type="button"
              class="w-full text-left px-md py-sm text-sm hover:bg-neutral-50"
              (click)="chooseSearchResult(r)"
            >
              {{ r.display_name }}
            </button>
          }
        </div>
      }
      <div #mapEl class="w-full h-72 rounded-md border border-neutral-200"></div>
      <p class="text-xs text-neutral-500">
        {{ chosenLat !== null ? (chosenLat | number: '1.6-6') + ', ' + (chosenLng | number: '1.6-6') : 'Click the map or search to place the pin.' }}
      </p>
      <div class="flex justify-end">
        <button type="button" class="btn-primary" [disabled]="chosenLat === null" (click)="confirm()">
          Use this location
        </button>
      </div>
    </div>
  `
})
export class LandLocationPickerComponent implements OnInit, OnDestroy {
  @Input() initialLat: number | null = null;
  @Input() initialLng: number | null = null;
  @Output() locationChosen = new EventEmitter<{ lat: number; lng: number }>();

  @ViewChild('mapEl', { static: true }) mapEl!: ElementRef<HTMLDivElement>;

  searchQuery = '';
  searchResults = signal<NominatimResult[]>([]);
  searching = signal(false);
  searchError = signal('');
  chosenLat: number | null = null;
  chosenLng: number | null = null;

  private map!: L.Map;
  private marker: L.Marker | null = null;

  ngOnInit(): void {
    const startLat = this.initialLat ?? 7.8731; // Sri Lanka centroid - a reasonable default when no pin exists yet
    const startLng = this.initialLng ?? 80.7718;
    const startZoom = this.initialLat !== null ? 16 : 7;

    this.map = L.map(this.mapEl.nativeElement).setView([startLat, startLng], startZoom);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 19
    }).addTo(this.map);

    if (this.initialLat !== null && this.initialLng !== null) {
      this.placePin(this.initialLat, this.initialLng);
    }

    this.map.on('click', (e: L.LeafletMouseEvent) => {
      this.placePin(e.latlng.lat, e.latlng.lng);
    });
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }

  search(): void {
    const q = this.searchQuery.trim();
    if (!q) return;

    this.searching.set(true);
    this.searchError.set('');
    this.searchResults.set([]);

    fetch(`https://nominatim.openstreetmap.org/search?format=json&limit=5&q=${encodeURIComponent(q)}`)
      .then(res => {
        if (!res.ok) throw new Error('Search request failed');
        return res.json() as Promise<NominatimResult[]>;
      })
      .then(results => {
        this.searchResults.set(results);
        this.searching.set(false);
        if (results.length === 0) {
          this.searchError.set('No results found — click the map to place the pin.');
        }
      })
      .catch(() => {
        this.searching.set(false);
        this.searchError.set('Search unavailable — click the map to place the pin.');
      });
  }

  chooseSearchResult(r: NominatimResult): void {
    const lat = parseFloat(r.lat);
    const lng = parseFloat(r.lon);
    this.map.setView([lat, lng], 16);
    this.placePin(lat, lng);
    this.searchResults.set([]);
    this.searchQuery = r.display_name;
  }

  private placePin(lat: number, lng: number): void {
    this.chosenLat = lat;
    this.chosenLng = lng;

    if (this.marker) {
      this.marker.setLatLng([lat, lng]);
    } else {
      this.marker = L.marker([lat, lng], { draggable: true }).addTo(this.map);
      this.marker.on('dragend', () => {
        const pos = this.marker!.getLatLng();
        this.chosenLat = pos.lat;
        this.chosenLng = pos.lng;
      });
    }
  }

  confirm(): void {
    if (this.chosenLat === null || this.chosenLng === null) return;
    this.locationChosen.emit({ lat: this.chosenLat, lng: this.chosenLng });
  }
}
```

Note: this file uses Angular's `signal` — add the import: change the first import line to
`import { Component, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, ViewChild, signal } from '@angular/core';`

- [ ] **Step 3: Import Leaflet's CSS**

In `ui/src/styles.scss` (or the project's global stylesheet — check which file `angular.json`'s `styles` array points to first), add at the top:

```scss
@import 'leaflet/dist/leaflet.css';
```

- [ ] **Step 4: Build the UI to confirm no compile errors**

Run: `cd ui && npm run build`
Expected: Build succeeds. If Leaflet's default marker icons 404 at runtime (a known Leaflet+bundler quirk — its default icon URLs are relative paths that don't resolve under webpack/esbuild), that's fixed in the verification step of Task 8, not here — this step only confirms compilation.

- [ ] **Step 5: Commit**

```bash
git add ui/package.json ui/package-lock.json ui/src/app/shared/land-location-picker/ ui/src/styles.scss
git commit -m "feat: add LandLocationPickerComponent (Leaflet + OSM, no API key)"
```

---

### Task 8: `LandService` (Angular) + `land-detail-panel` — location block

**Files:**
- Modify: `ui/src/app/core/land.service.ts`
- Modify: `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`

**Interfaces:**
- Consumes: `LandLocationPickerComponent` (Task 7), backend routes from Task 4.
- Produces: `LandService.setLocation(workspaceId, landId, {lat, lng})`, `.generateLocationShareLink(...)`, `.regenerateLocationShareLink(...)`, `.revokeLocationShareLink(...)`. `Land` interface gains `latitude`, `longitude`, `hasActiveLocationShareLink`.

- [ ] **Step 1: Extend the `Land` interface and add service methods**

In `ui/src/app/core/land.service.ts`, add to the `Land` interface after `gpsCoordinates`:

```typescript
  latitude: number | null;
  longitude: number | null;
  hasActiveLocationShareLink: boolean;
```

Add a new interface near the top (after `LandDeedRequest`):

```typescript
export interface LandLocation {
  lat: number;
  lng: number;
}
```

Add methods to `LandService`, after `update(...)`:

```typescript
  setLocation(workspaceId: string, landId: string, location: LandLocation): Observable<Land> {
    return this.http
      .put<ApiResponse<Land>>(`${this.base(workspaceId)}/${landId}/location`, { latitude: location.lat, longitude: location.lng })
      .pipe(map(res => res.data));
  }

  generateLocationShareLink(workspaceId: string, landId: string): Observable<string> {
    return this.http
      .post<ApiResponse<{ token: string }>>(`${this.base(workspaceId)}/${landId}/location-share-link`, {})
      .pipe(map(res => res.data.token));
  }

  regenerateLocationShareLink(workspaceId: string, landId: string): Observable<string> {
    return this.http
      .post<ApiResponse<{ token: string }>>(`${this.base(workspaceId)}/${landId}/location-share-link/regenerate`, {})
      .pipe(map(res => res.data.token));
  }

  revokeLocationShareLink(workspaceId: string, landId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${landId}/location-share-link`);
  }
```

- [ ] **Step 2: Add the Location block to `land-detail-panel`**

In `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`:

Add imports at the top:
```typescript
import { LandLocationPickerComponent } from '../../../shared/land-location-picker/land-location-picker.component';
```
Add `LandLocationPickerComponent` to the `imports` array.

Add to the template, as a new block right after the closing `</div>` of the existing Details block (after the owner-picker `<div class="mt-sm">...</div>` and before the Surveys `<div>` block):

```html
        <div>
          <h3 class="text-xs font-semibold text-neutral-500 uppercase mb-sm">Location</h3>
          @if (land()?.latitude !== null && land()?.latitude !== undefined) {
            <p class="text-sm text-neutral-900">{{ land()!.latitude }}, {{ land()!.longitude }}</p>
          } @else {
            <p class="text-sm text-neutral-500">Not set</p>
          }
          <div class="flex flex-wrap gap-sm mt-sm">
            <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="pickerOpen.set(true)">
              {{ land()?.latitude != null ? 'Update location' : 'Set location' }}
            </button>
            @if (land()?.latitude !== null && land()?.latitude !== undefined) {
              <a
                class="text-xs text-primary-600 hover:text-primary-700"
                [href]="'https://www.google.com/maps?q=' + land()!.latitude + ',' + land()!.longitude"
                target="_blank"
                rel="noopener"
              >
                Open in Google Maps
              </a>
              <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="copyMapsLink()">
                {{ mapsLinkCopied() ? 'Copied!' : 'Copy Google Maps link' }}
              </button>
            }
            @if (land()?.hasActiveLocationShareLink) {
              <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="copyShareLink()">
                {{ shareLinkCopied() ? 'Copied!' : 'Copy share link' }}
              </button>
              <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="regenerateShareLink()">
                Regenerate link
              </button>
              <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" (click)="revokeShareLink()">
                Revoke link
              </button>
            } @else {
              <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="copyShareLink()">
                Copy share link (for client)
              </button>
            }
          </div>
          @if (locationError()) {
            <p class="text-xs text-primary-500 mt-xs">{{ locationError() }}</p>
          }
        </div>
```

Add the picker modal at the very end of the template, right before the final closing `}` of the outer `@else` block (after the Boundaries `<div>` closes, still inside `<div class="space-y-lg">`):

```html
      </div>
    }
    @if (pickerOpen()) {
      <div class="fixed inset-0 bg-black/40 flex items-center justify-center z-50 p-lg" (click)="pickerOpen.set(false)">
        <div class="bg-white rounded-md p-lg max-w-lg w-full" (click)="$event.stopPropagation()">
          <h3 class="text-sm font-semibold text-neutral-900 mb-md">Set land location</h3>
          <app-land-location-picker
            [initialLat]="land()?.latitude ?? null"
            [initialLng]="land()?.longitude ?? null"
            (locationChosen)="onLocationChosen($event)"
          />
        </div>
      </div>
    }
```

(This replaces the final two lines of the existing template, which currently end with `</div>\n    }\n  \`\n})`. Adjust so the closing structure stays valid — the picker modal block sits as a sibling `@if` after the main `@if/@else`, both still inside the template string.)

Add to the component class, after the `boundaries` signal declarations:

```typescript
  pickerOpen = signal(false);
  locationError = signal('');
  mapsLinkCopied = signal(false);
  shareLinkCopied = signal(false);
```

Add methods, after `saveDetails(...)`:

```typescript
  onLocationChosen(location: { lat: number; lng: number }): void {
    this.locationError.set('');
    this.landService.setLocation(this.workspaceId, this.landId, location).subscribe({
      next: (land) => {
        this.land.set(land);
        this.pickerOpen.set(false);
      },
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not save location.')
    });
  }

  copyMapsLink(): void {
    const land = this.land();
    if (!land?.latitude || !land?.longitude) return;
    navigator.clipboard.writeText(`https://www.google.com/maps?q=${land.latitude},${land.longitude}`);
    this.mapsLinkCopied.set(true);
    setTimeout(() => this.mapsLinkCopied.set(false), 2000);
  }

  copyShareLink(): void {
    this.locationError.set('');
    const existing = this.land();
    const link$ = existing?.hasActiveLocationShareLink
      ? this.landService.generateLocationShareLink(this.workspaceId, this.landId)
      : this.landService.generateLocationShareLink(this.workspaceId, this.landId);
    link$.subscribe({
      next: (token) => {
        navigator.clipboard.writeText(`${location.origin}/set-location/${token}`);
        this.shareLinkCopied.set(true);
        setTimeout(() => this.shareLinkCopied.set(false), 2000);
        this.land.update(l => (l ? { ...l, hasActiveLocationShareLink: true } : l));
      },
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not create share link.')
    });
  }

  regenerateShareLink(): void {
    this.locationError.set('');
    this.landService.regenerateLocationShareLink(this.workspaceId, this.landId).subscribe({
      next: (token) => {
        navigator.clipboard.writeText(`${location.origin}/set-location/${token}`);
        this.shareLinkCopied.set(true);
        setTimeout(() => this.shareLinkCopied.set(false), 2000);
      },
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not regenerate share link.')
    });
  }

  revokeShareLink(): void {
    this.locationError.set('');
    this.landService.revokeLocationShareLink(this.workspaceId, this.landId).subscribe({
      next: () => this.land.update(l => (l ? { ...l, hasActiveLocationShareLink: false } : l)),
      error: (err) => this.locationError.set(err.error?.message ?? 'Could not revoke share link.')
    });
  }
```

- [ ] **Step 3: Fix Leaflet's default marker icon paths**

Leaflet's default marker icon URLs are relative and break under Angular's bundler. In `ui/src/app/shared/land-location-picker/land-location-picker.component.ts`, add near the top of the file (after imports, before the `@Component` decorator):

```typescript
// Leaflet's default icon URLs are relative paths that don't resolve through Angular's
// bundler - point them at jsDelivr's CDN copies of the same marker images instead of
// vendoring image assets for three tiny icons.
L.Icon.Default.mergeOptions({
  iconRetinaUrl: 'https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/images/marker-icon-2x.png',
  iconUrl: 'https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/images/marker-icon.png',
  shadowUrl: 'https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/images/marker-shadow.png'
});
```

- [ ] **Step 4: Verify in the browser**

Start the dev server (`preview_start` with the UI's launch config, or `ng serve` if no config exists yet), navigate to a land record's detail panel, click "Set location," confirm the map renders with visible tiles and a marker icon (not a broken image), click the map to drop a pin, click "Use this location," confirm it saves and the panel shows the new lat/lng. Confirm "Open in Google Maps" opens the correct coordinates in a new tab.

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/core/land.service.ts ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts ui/src/app/shared/land-location-picker/land-location-picker.component.ts
git commit -m "feat: wire location picker and share-link controls into land-detail-panel"
```

---

### Task 9: Public `LandLocationLinkService` (Angular)

**Files:**
- Create: `ui/src/app/core/land-location-link.service.ts`

**Interfaces:**
- Consumes: backend routes from Task 5.
- Produces: `LandLocationLinkService.getPreview(token)`, `.setLocation(token, {lat, lng})`. Consumed by Task 10.

- [ ] **Step 1: Create the service**

```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { LandLocation } from './land.service';

export interface LandLocationLinkPreview {
  addressLine: string;
  latitude: number | null;
  longitude: number | null;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

/**
 * Never sends a workspace/land id or auth header - structurally can't, mirrors
 * DocumentRequestLinkService's trust-boundary reasoning for the same public-token pattern.
 */
@Injectable({ providedIn: 'root' })
export class LandLocationLinkService {
  constructor(private http: HttpClient) {}

  private base(token: string): string {
    return `${environment.apiBaseUrl}/land-location-links/${token}`;
  }

  getPreview(token: string): Observable<LandLocationLinkPreview> {
    return this.http.get<ApiResponse<LandLocationLinkPreview>>(this.base(token)).pipe(map(res => res.data));
  }

  setLocation(token: string, location: LandLocation): Observable<void> {
    return this.http.put<void>(this.base(token), { latitude: location.lat, longitude: location.lng });
  }
}
```

- [ ] **Step 2: Build**

Run: `cd ui && npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/core/land-location-link.service.ts
git commit -m "feat: add public LandLocationLinkService"
```

---

### Task 10: Public `PublicSetLocationComponent` + route

**Files:**
- Create: `ui/src/app/pages/set-location/public-set-location.component.ts`
- Modify: `ui/src/app/app.routes.ts`

**Interfaces:**
- Consumes: `LandLocationLinkService` (Task 9), `LandLocationPickerComponent` (Task 7).
- Produces: route `/set-location/:token`.

- [ ] **Step 1: Create the component**

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { LandLocationLinkPreview, LandLocationLinkService } from '../../core/land-location-link.service';
import { LandLocationPickerComponent } from '../../shared/land-location-picker/land-location-picker.component';

/**
 * Standalone public page, reached by people with no account - no app shell,
 * no auth guard. Mirrors PublicDocumentUploadComponent's structure.
 */
@Component({
  selector: 'app-public-set-location',
  standalone: true,
  imports: [CommonModule, LandLocationPickerComponent],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-neutral-50 p-lg">
      <div class="max-w-lg w-full bg-white rounded-md shadow p-lg">
        @if (loading()) {
          <p class="text-sm text-neutral-500">Loading…</p>
        } @else if (error()) {
          <p class="text-sm text-neutral-600">{{ error() }}</p>
        } @else if (saved()) {
          <p class="text-sm text-neutral-900">Location saved — you can close this page.</p>
          <button type="button" class="text-sm text-primary-600 mt-sm" (click)="saved.set(false)">
            Adjust the pin
          </button>
        } @else {
          <h1 class="text-lg font-semibold text-neutral-900 mb-xs">Set land location</h1>
          <p class="text-sm text-neutral-500 mb-md">{{ preview()!.addressLine }}</p>
          <app-land-location-picker
            [initialLat]="preview()!.latitude"
            [initialLng]="preview()!.longitude"
            (locationChosen)="onLocationChosen($event)"
          />
          @if (saveError()) {
            <p class="text-xs text-primary-500 mt-sm">{{ saveError() }}</p>
          }
        }
      </div>
    </div>
  `
})
export class PublicSetLocationComponent implements OnInit {
  loading = signal(true);
  error = signal('');
  preview = signal<LandLocationLinkPreview | null>(null);
  saved = signal(false);
  saveError = signal('');

  private token = '';

  constructor(private route: ActivatedRoute, private linkService: LandLocationLinkService) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    this.linkService.getPreview(this.token).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('This link is no longer valid.');
        this.loading.set(false);
      }
    });
  }

  onLocationChosen(location: { lat: number; lng: number }): void {
    this.saveError.set('');
    this.linkService.setLocation(this.token, location).subscribe({
      next: () => {
        this.saved.set(true);
        this.preview.update(p => (p ? { ...p, latitude: location.lat, longitude: location.lng } : p));
      },
      error: (err) => this.saveError.set(err.error?.message ?? 'Could not save location.')
    });
  }
}
```

- [ ] **Step 2: Register the route**

In `ui/src/app/app.routes.ts`, add the import:
```typescript
import { PublicSetLocationComponent } from './pages/set-location/public-set-location.component';
```

Add the route entry after `{ path: 'document-upload/:token', component: PublicDocumentUploadComponent },`:
```typescript
  { path: 'set-location/:token', component: PublicSetLocationComponent },
```

- [ ] **Step 3: Verify in the browser**

As Admin, generate a share link from `land-detail-panel` ("Copy share link"). Open the copied URL directly (paste into the browser). Confirm the page loads outside the app shell, shows the address, renders the map, allows placing a pin, and shows "Location saved" after submit. Reload and confirm the pin persists (re-fetch shows the saved point). Then in the authenticated land-detail-panel, confirm the same lat/lng now shows there too.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/set-location/ ui/src/app/app.routes.ts
git commit -m "feat: add public set-location page for client link"
```

---

### Task 11: Collapsed row summary — owner + location badge

**Files:**
- Modify: `ui/src/app/pages/land/land-list.component.ts`
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `Land.ownerName`, `Land.latitude`, `Land.longitude` (Task 8's `Land` interface extension).

- [ ] **Step 1: Update `land-list.component.ts`'s row template**

In `ui/src/app/pages/land/land-list.component.ts`, change the Address `<td>` (currently `{{ addressLine(row.land) }}`) to:

```html
                  <td class="px-lg py-sm text-neutral-900">
                    <div class="flex items-center gap-sm">
                      <span>{{ addressLine(row.land) }}</span>
                      @if (row.land.latitude !== null) {
                        <a
                          class="text-primary-600"
                          [href]="'https://www.google.com/maps?q=' + row.land.latitude + ',' + row.land.longitude"
                          target="_blank"
                          rel="noopener"
                          title="Open in Google Maps"
                          (click)="$event.stopPropagation()"
                        >📍</a>
                      } @else {
                        <span class="text-neutral-300" title="Location not set">📍</span>
                      }
                    </div>
                    @if (row.land.ownerName) {
                      <span class="text-xs text-neutral-500 block">{{ row.land.ownerName }}</span>
                    }
                  </td>
```

- [ ] **Step 2: Update `job-detail.component.ts`'s land row**

In `ui/src/app/pages/job/job-detail.component.ts`, change the land row's inner `<div>` (currently containing `{{ addressLine(l) }}` and the size `@if`) to:

```html
                    <div>
                      <span class="text-sm text-neutral-900">{{ addressLine(l) }}</span>
                      @if (l.latitude !== null) {
                        <a
                          class="text-xs text-primary-600 ml-xs"
                          [href]="'https://www.google.com/maps?q=' + l.latitude + ',' + l.longitude"
                          target="_blank"
                          rel="noopener"
                          title="Open in Google Maps"
                          (click)="$event.stopPropagation()"
                        >📍</a>
                      } @else {
                        <span class="text-xs text-neutral-300 ml-xs" title="Location not set">📍</span>
                      }
                      @if (l.ownerName) {
                        <span class="text-xs text-neutral-500 block">{{ l.ownerName }}</span>
                      }
                      @if (l.size) {
                        <span class="text-xs text-neutral-500 block">{{ l.size }} {{ l.sizeUnit }}</span>
                      }
                    </div>
```

- [ ] **Step 3: Verify in the browser**

Navigate to the land list page and a job's detail page with at least one land record that has a location set and one that doesn't. Confirm the filled pin (📍, colored, clickable, opens Google Maps in a new tab, doesn't toggle/navigate the row) versus the muted pin (not clickable) render correctly, and owner name shows under the address when present.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/land/land-list.component.ts ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat: show owner and location indicator on collapsed land rows"
```

---

## Self-Review Notes

- **Spec coverage:** structured lat/lng (Task 1), Leaflet+OSM picker (Task 7), Google Maps deep link (Task 8/11), client share link generate/regenerate/revoke (Tasks 3/4/8), public unauthenticated set-location page (Tasks 5/9/10), collapsed row owner + location indicator (Task 11) — all covered.
- **Out-of-scope items honored:** no reverse geocoding, no in-app directions, no self-hosted tiles, no per-recipient/expiring links, no new Casbin permission (reused `land.edit`).
- **Type consistency check:** `LandLocationRequest{Latitude,Longitude}` (Task 2) is what Task 3's methods and Task 4/5's controllers consume; Angular's `LandLocation{lat,lng}` (Task 8) is deliberately lowercase/short-named for frontend ergonomics and is translated to `{latitude, longitude}` at the HTTP call site in `LandService.setLocation` — confirmed consistent at each translation point.
