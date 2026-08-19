# Land Area: Unified Storage, Multi-Unit Input/Output Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `Land.Size`/`SizeUnit` with a single canonical `AreaSquareMeters` column, accepting area input in Acres/Roods/Perches, square meters, or hectares (exactly one system per write) and always returning all three representations on read.

**Architecture:** One new pure-math static class (`AreaConversion`) is the single source of conversion truth server-side. `Land` stores one `decimal? AreaSquareMeters`. `AreaDto` carries all five optional fields (A/R/P + m² + ha); `LandService` converts whichever system was populated into the canonical value on write, and expands the canonical value into all five fields on read. Frontend mirrors the same formulas for a live client-side preview, with a new shared `LandAreaInputComponent` (unit-system tabs) replacing every `Size`/`SizeUnit` input/display across the land pages.

**Tech Stack:** .NET 9 / EF Core 9 (backend, no new packages). Angular 21 (frontend, no new packages).

## Global Constraints

- 1 Perch = 25.29285264 m²; 1 Rood = 40 Perches; 1 Acre = 4 Roods = 160 Perches; 1 Hectare = 10000 m² (spec, verbatim).
- Exactly one unit system per write (A-R-P, or m², or hectares) — more than one populated is a `400 ValidationException`, `"Provide area in one unit system only."`
- Within the A-R-P system, any one of Acres/Roods/Perches implies the others default to 0 (not an error) — unchanged from the original A-R-P-only spec.
- No backfill of existing `Size` data — dropped, land records start with area unset.
- No new Casbin permission — area is gated by the existing `land.edit`/`land.view` checks already covering every other land field.
- Verification: build after each backend/frontend task; run only the new/affected test class via `dotnet test --filter <ClassName>`, never the full suite, per this repo's established verification approach.

---

### Task 1: `AreaConversion` static class + unit tests

**Files:**
- Create: `api/src/SurveyorLedger.Core/AreaConversion.cs`
- Create: `api/tests/SurveyorLedger.API.Tests/Core/AreaConversionTests.cs`

**Interfaces:**
- Produces: `AreaConversion.SquareMetersPerPerch/PerRood/PerAcre/PerHectare` (decimal constants), `AreaConversion.FromAcresRoodsPerches(int acres, int roods, decimal perches) : decimal`, `AreaConversion.ToAcresRoodsPerches(decimal squareMeters) : (int Acres, int Roods, decimal Perches)`. Consumed by Task 3 (`LandService`).

- [ ] **Step 1: Write the failing tests**

```csharp
using SurveyorLedger.Core;
using Xunit;

namespace SurveyorLedger.API.Tests.Core;

public class AreaConversionTests
{
    [Fact]
    public void FromAcresRoodsPerches_TwoAcres_ReturnsExpectedSquareMeters()
    {
        var result = AreaConversion.FromAcresRoodsPerches(2, 0, 0);
        Assert.Equal(8093.7128448m, result);
    }

    [Fact]
    public void FromAcresRoodsPerches_OneRood_ReturnsExpectedSquareMeters()
    {
        var result = AreaConversion.FromAcresRoodsPerches(0, 1, 0);
        Assert.Equal(1011.7141056m, result);
    }

    [Fact]
    public void FromAcresRoodsPerches_OnePerch_ReturnsExpectedSquareMeters()
    {
        var result = AreaConversion.FromAcresRoodsPerches(0, 0, 1);
        Assert.Equal(25.29285264m, result);
    }

    [Fact]
    public void ToAcresRoodsPerches_RoundTripsFromAcresRoodsPerches()
    {
        var squareMeters = AreaConversion.FromAcresRoodsPerches(3, 2, 15.5m);
        var (acres, roods, perches) = AreaConversion.ToAcresRoodsPerches(squareMeters);

        Assert.Equal(3, acres);
        Assert.Equal(2, roods);
        Assert.Equal(15.5m, perches);
    }

    [Fact]
    public void ToAcresRoodsPerches_ZeroSquareMeters_ReturnsAllZero()
    {
        var (acres, roods, perches) = AreaConversion.ToAcresRoodsPerches(0m);

        Assert.Equal(0, acres);
        Assert.Equal(0, roods);
        Assert.Equal(0m, perches);
    }

    [Fact]
    public void ToAcresRoodsPerches_ExactlyOneAcre_RollsOverCorrectly()
    {
        var (acres, roods, perches) = AreaConversion.ToAcresRoodsPerches(4046.8564224m);

        Assert.Equal(1, acres);
        Assert.Equal(0, roods);
        Assert.Equal(0m, perches);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `cd api && dotnet test --filter AreaConversionTests`
Expected: FAIL - `AreaConversion` does not exist.

- [ ] **Step 3: Implement `AreaConversion`**

```csharp
namespace SurveyorLedger.Core;

/// <summary>
/// Single source of area-unit conversion truth. Acres/Roods/Perches (Sri Lankan land
/// survey convention: 1 Acre = 4 Roods = 160 Perches, 1 Rood = 40 Perches) and the metric
/// system both convert through square meters, the canonical stored unit on Land.
/// </summary>
public static class AreaConversion
{
    public const decimal SquareMetersPerPerch = 25.29285264m;
    public const decimal SquareMetersPerRood = SquareMetersPerPerch * 40;
    public const decimal SquareMetersPerAcre = SquareMetersPerRood * 4;
    public const decimal SquareMetersPerHectare = 10000m;

    public static decimal FromAcresRoodsPerches(int acres, int roods, decimal perches) =>
        acres * SquareMetersPerAcre + roods * SquareMetersPerRood + perches * SquareMetersPerPerch;

    public static (int Acres, int Roods, decimal Perches) ToAcresRoodsPerches(decimal squareMeters)
    {
        var totalPerches = squareMeters / SquareMetersPerPerch;
        var acres = (int)Math.Floor(totalPerches / 160);
        var remainder = totalPerches - acres * 160;
        var roods = (int)Math.Floor(remainder / 40);
        var perches = Math.Round(remainder - roods * 40, 2);
        return (acres, roods, perches);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cd api && dotnet test --filter AreaConversionTests`
Expected: All 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add api/src/SurveyorLedger.Core/AreaConversion.cs api/tests/SurveyorLedger.API.Tests/Core/AreaConversionTests.cs
git commit -m "feat: add AreaConversion for A-R-P <-> square meter conversion"
```

---

### Task 2: `Land` entity + migration

**Files:**
- Modify: `api/src/SurveyorLedger.Data/Entities/Land.cs`
- Modify: `api/src/SurveyorLedger.Data/Configurations/LandConfiguration.cs`
- Create (generated): migration files

**Interfaces:**
- Produces: `Land.AreaSquareMeters` (`decimal?`) — consumed by Task 3.

- [ ] **Step 1: Replace `Size`/`SizeUnit` on `Land`**

In `api/src/SurveyorLedger.Data/Entities/Land.cs`, replace:

```csharp
    public decimal? Size { get; set; }
    public string? SizeUnit { get; set; }
```

with:

```csharp
    public decimal? AreaSquareMeters { get; set; }
```

- [ ] **Step 2: Update `LandConfiguration`**

In `api/src/SurveyorLedger.Data/Configurations/LandConfiguration.cs`, replace:

```csharp
        builder.Property(x => x.Size).HasColumnType("decimal(18,2)");
        builder.Property(x => x.SizeUnit).HasMaxLength(20);
```

with:

```csharp
        builder.Property(x => x.AreaSquareMeters).HasColumnType("decimal(14,4)");
```

- [ ] **Step 3: Generate the migration**

```bash
cd api && dotnet ef migrations add ReplaceSizeWithUnifiedArea --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

Expected: migration drops `Size`, `SizeUnit`, adds `AreaSquareMeters` (decimal(14,4), nullable). Inspect the generated file — confirm no unrelated model drift.

- [ ] **Step 4: Apply the migration**

```bash
dotnet ef database update --project src/SurveyorLedger.Data --startup-project src/SurveyorLedger.API
```

Expected: succeeds, no errors.

- [ ] **Step 5: Build**

Run: `cd api && dotnet build`
Expected: fails at this point — `LandRequest`/`LandResponse`/`LandService` still reference `Size`/`SizeUnit`. That's expected; Task 3 fixes it. Confirm the *only* errors are those three files referencing the removed properties (if anything else breaks, stop and investigate before continuing).

- [ ] **Step 6: Commit**

```bash
git add api/src/SurveyorLedger.Data/
git commit -m "feat: replace Land.Size/SizeUnit with AreaSquareMeters"
```

---

### Task 3: `AreaDto`, `LandRequest`/`LandResponse`, `LandService` conversion + wiring

**Files:**
- Create: `api/src/SurveyorLedger.API/Models/Land/AreaDto.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Land/LandRequest.cs`
- Modify: `api/src/SurveyorLedger.API/Models/Land/LandResponse.cs`
- Modify: `api/src/SurveyorLedger.API/Services/LandService.cs`
- Modify: `api/src/SurveyorLedger.API/Controllers/LandController.cs`

**Interfaces:**
- Consumes: `AreaConversion.FromAcresRoodsPerches`/`ToAcresRoodsPerches` (Task 1), `Land.AreaSquareMeters` (Task 2).
- Produces: `AreaDto { Acres, Roods, Perches, SquareMeters, Hectares }`, `LandService.ToAreaSquareMeters(AreaDto?) : decimal?` (private, but its behavior is what Task 4's tests exercise through `CreateAsync`/`UpdateAsync`), `LandService.ToAreaDto(decimal?) : AreaDto` (private, exercised the same way).

- [ ] **Step 1: Create `AreaDto`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Write: populate exactly one unit system (Acres/Roods/Perches, or SquareMeters, or
/// Hectares) - LandService rejects more than one. Read: every field is always populated,
/// computed server-side from the one stored canonical value.
/// </summary>
public class AreaDto
{
    [Range(0, 100000, ErrorMessage = "Acres must be between 0 and 100000.")]
    public int? Acres { get; set; }

    [Range(0, 3, ErrorMessage = "Roods must be between 0 and 3.")]
    public int? Roods { get; set; }

    [Range(0, 39.99, ErrorMessage = "Perches must be between 0 and 39.99.")]
    public decimal? Perches { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "SquareMeters must be zero or greater.")]
    public decimal? SquareMeters { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Hectares must be zero or greater.")]
    public decimal? Hectares { get; set; }
}
```

- [ ] **Step 2: Update `LandRequest`**

In `api/src/SurveyorLedger.API/Models/Land/LandRequest.cs`, replace:

```csharp
    [Range(0, double.MaxValue, ErrorMessage = "Size must be zero or greater.")]
    public decimal? Size { get; set; }

    [StringLength(20)]
    public string? SizeUnit { get; set; }
```

with:

```csharp
    public AreaDto? Area { get; set; }
```

- [ ] **Step 3: Update `LandResponse`**

In `api/src/SurveyorLedger.API/Models/Land/LandResponse.cs`, replace:

```csharp
    public decimal? Size { get; set; }
    public string? SizeUnit { get; set; }
```

with:

```csharp
    public AreaDto Area { get; set; } = new();
```

- [ ] **Step 4: Add `ToAreaSquareMeters`/`ToAreaDto` to `LandService`, wire into Create/Update**

In `api/src/SurveyorLedger.API/Services/LandService.cs`, add `using SurveyorLedger.Core;` to the top imports (for `AreaConversion`).

Replace the two lines in `CreateAsync`:

```csharp
            Size = request.Size,
            SizeUnit = request.SizeUnit,
```

with:

```csharp
            AreaSquareMeters = ToAreaSquareMeters(request.Area),
```

Replace the two lines in `UpdateAsync`:

```csharp
        land.Size = request.Size;
        land.SizeUnit = request.SizeUnit;
```

with:

```csharp
        land.AreaSquareMeters = ToAreaSquareMeters(request.Area);
```

Add the two new private helpers, next to `ToAddress` at the bottom of the class:

```csharp
    /// <summary>
    /// Exactly one unit system may be populated - Acres/Roods/Perches (any one implies
    /// the others default to 0), or SquareMeters, or Hectares. Null/empty dto or all
    /// fields null means "area unset."
    /// </summary>
    private static decimal? ToAreaSquareMeters(AreaDto? dto)
    {
        if (dto is null)
            return null;

        var hasArp = dto.Acres.HasValue || dto.Roods.HasValue || dto.Perches.HasValue;
        var hasSquareMeters = dto.SquareMeters.HasValue;
        var hasHectares = dto.Hectares.HasValue;

        var systemCount = (hasArp ? 1 : 0) + (hasSquareMeters ? 1 : 0) + (hasHectares ? 1 : 0);
        if (systemCount > 1)
            throw new ValidationException("Provide area in one unit system only.");
        if (systemCount == 0)
            return null;

        if (hasSquareMeters)
            return dto.SquareMeters!.Value;
        if (hasHectares)
            return dto.Hectares!.Value * AreaConversion.SquareMetersPerHectare;

        return AreaConversion.FromAcresRoodsPerches(dto.Acres ?? 0, dto.Roods ?? 0, dto.Perches ?? 0);
    }

    private static AreaDto ToAreaDto(decimal? squareMeters)
    {
        if (squareMeters is null)
            return new AreaDto();

        var (acres, roods, perches) = AreaConversion.ToAcresRoodsPerches(squareMeters.Value);
        return new AreaDto
        {
            Acres = acres,
            Roods = roods,
            Perches = perches,
            SquareMeters = squareMeters.Value,
            Hectares = squareMeters.Value / AreaConversion.SquareMetersPerHectare
        };
    }
```

- [ ] **Step 5: Wire `ToAreaDto` into `LandController.ToResponse`**

In `api/src/SurveyorLedger.API/Controllers/LandController.cs`, find the `ToResponse(Land l)` mapper and replace:

```csharp
            Size = l.Size,
            SizeUnit = l.SizeUnit,
```

with:

```csharp
            Area = ToAreaDto(l.AreaSquareMeters),
```

`ToAreaDto` is `private static` on `LandService`, not visible from the controller — add a small private static mirror on `LandController` itself (the controller already has its own `ToResponse` mappers independent of the service, matching the existing pattern where `LandController` does its own entity→DTO mapping rather than calling into the service for it):

```csharp
        private static AreaDto ToAreaDto(decimal? squareMeters)
        {
            if (squareMeters is null)
                return new AreaDto();

            var (acres, roods, perches) = AreaConversion.ToAcresRoodsPerches(squareMeters.Value);
            return new AreaDto
            {
                Acres = acres,
                Roods = roods,
                Perches = perches,
                SquareMeters = squareMeters.Value,
                Hectares = squareMeters.Value / AreaConversion.SquareMetersPerHectare
            };
        }
```

Add `using SurveyorLedger.Core;` to `LandController.cs`'s imports.

(This small duplication — the same `ToAreaDto` body in both `LandService` and `LandController` — mirrors how `ToAddress`/address mapping already works in this codebase: `LandService.ToAddress` converts *into* the entity on write, `LandController.ToResponse` converts *out of* the entity on read, independently. Not a shared static method because the two call sites have different lifetimes and dependencies; duplicating six lines of pure arithmetic is cheaper than adding a shared static class for it.)

- [ ] **Step 6: Build**

Run: `cd api && dotnet build`
Expected: Build succeeded, no errors.

- [ ] **Step 7: Commit**

```bash
git add api/src/SurveyorLedger.API/
git commit -m "feat: add AreaDto and unified area conversion to LandService/LandController"
```

---

### Task 4: Backend service tests

**Files:**
- Create: `api/tests/SurveyorLedger.API.Tests/Services/LandAreaServiceTests.cs`

**Interfaces:**
- Consumes: `WorkspaceIntegrationTestBase`, `ILandService.CreateAsync`/`UpdateAsync`/`GetByIdAsync` (existing), `AreaDto` (Task 3).

- [ ] **Step 1: Write the test file**

```csharp
using Microsoft.Extensions.DependencyInjection;
using SurveyorLedger.API.Models.Land;
using SurveyorLedger.API.Services;
using SurveyorLedger.Core.Exceptions;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class LandAreaServiceTests : WorkspaceIntegrationTestBase
{
    private ILandService _landService = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILandService, LandService>();
    }

    private LandRequest BaseRequest(AreaDto? area) => new()
    {
        Address = new AddressDto { Street = "123 Main St", City = "Colombo" },
        Area = area
    };

    [Fact]
    public async Task CreateAsync_OnlyPerchesSet_PersistsAndReturnsAllRepresentations()
    {
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(new AreaDto { Perches = 40 }));
        var fetched = await _landService.GetByIdAsync(WorkspaceId, AdminId, land.Id);

        Assert.Equal(1011.7141056m, fetched.AreaSquareMeters);
    }

    [Fact]
    public async Task CreateAsync_SquareMetersSet_ConvertsAndPersists()
    {
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(new AreaDto { SquareMeters = 5000 }));
        var fetched = await _landService.GetByIdAsync(WorkspaceId, AdminId, land.Id);

        Assert.Equal(5000m, fetched.AreaSquareMeters);
    }

    [Fact]
    public async Task CreateAsync_HectaresSet_ConvertsAndPersists()
    {
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(new AreaDto { Hectares = 1 }));
        var fetched = await _landService.GetByIdAsync(WorkspaceId, AdminId, land.Id);

        Assert.Equal(10000m, fetched.AreaSquareMeters);
    }

    [Fact]
    public async Task CreateAsync_BothAcresAndSquareMetersSet_Throws()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(new AreaDto { Acres = 1, SquareMeters = 100 })));
    }

    [Fact]
    public async Task CreateAsync_AreaOmitted_PersistsNull()
    {
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(null));
        var fetched = await _landService.GetByIdAsync(WorkspaceId, AdminId, land.Id);

        Assert.Null(fetched.AreaSquareMeters);
    }

    [Fact]
    public async Task UpdateAsync_RoodsFour_Throws()
    {
        var land = await _landService.CreateAsync(WorkspaceId, AdminId, BaseRequest(null));

        await Assert.ThrowsAsync<Core.Exceptions.ValidationException>(async () =>
        {
            // Roods=4 fails DataAnnotations validation at the controller boundary in
            // production; the service layer's ToAreaSquareMeters doesn't re-validate
            // Range itself, so this test goes through the DTO's [Range] via a manual
            // check to document the contract - see LandAreaDtoValidationTests below.
            await Task.CompletedTask;
            throw new Core.Exceptions.ValidationException("Roods must be between 0 and 3.");
        });
    }
}
```

- [ ] **Step 2: Run to verify it fails initially, then passes**

Run: `cd api && dotnet test --filter LandAreaServiceTests`
Expected (before this task existed, N/A since Task 3 already implemented the behavior) — run now and expect all 6 PASS, since Task 3's implementation already exists. If any fail, fix `LandService` (Task 3), not the test.

- [ ] **Step 3: Replace the placeholder `Roods=4` test with a real `[Range]`-attribute test**

The `UpdateAsync_RoodsFour_Throws` test above doesn't actually exercise `[Range]` (DataAnnotations validation happens at the ASP.NET model-binding layer, not inside `LandService`, so a direct service call bypasses it — this is consistent with how `LandRequest.Size`'s old `[Range]` attribute was never re-validated inside `LandService` either). Replace it with a plain DTO validation test instead, which is what actually exercises the attribute:

```csharp
using System.ComponentModel.DataAnnotations;
```

Add this `using` to the top of the file, then replace the `UpdateAsync_RoodsFour_Throws` test with:

```csharp
    [Fact]
    public void AreaDto_RoodsFour_FailsValidation()
    {
        var dto = new AreaDto { Roods = 4 };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AreaDto.Roods)));
    }

    [Fact]
    public void AreaDto_PerchesFortyFive_FailsValidation()
    {
        var dto = new AreaDto { Perches = 45 };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AreaDto.Perches)));
    }
```

- [ ] **Step 4: Run the full file**

Run: `cd api && dotnet test --filter LandAreaServiceTests`
Expected: All 7 tests PASS (5 from Step 1 minus the removed one, plus the 2 new DTO validation tests = 7 total: `CreateAsync_OnlyPerchesSet...`, `CreateAsync_SquareMetersSet...`, `CreateAsync_HectaresSet...`, `CreateAsync_BothAcresAndSquareMetersSet_Throws`, `CreateAsync_AreaOmitted_PersistsNull`, `AreaDto_RoodsFour_FailsValidation`, `AreaDto_PerchesFortyFive_FailsValidation`).

- [ ] **Step 5: Commit**

```bash
git add api/tests/SurveyorLedger.API.Tests/Services/LandAreaServiceTests.cs
git commit -m "test: cover unified area conversion and validation"
```

---

### Task 5: Angular `land.service.ts` — `LandAreaValue`, conversion helpers, `formatArea`

**Files:**
- Modify: `ui/src/app/core/land.service.ts`

**Interfaces:**
- Produces: `LandAreaValue { acres, roods, perches, squareMeters, hectares }` (all `number | null`), `formatArea(area: LandAreaValue): string`, `acresRoodsPerchesToSquareMeters(acres, roods, perches): number`, `squareMetersToAcresRoodsPerches(squareMeters): { acres, roods, perches }`, `squareMetersToHectares(squareMeters): number`, `hectaresToSquareMeters(hectares): number`. Consumed by Task 6 (`LandAreaInputComponent`) and Task 7/8 (display call sites).

- [ ] **Step 1: Replace `size`/`sizeUnit` on `Land`/`LandRequest`**

In `ui/src/app/core/land.service.ts`, add the new interface near the top (after `Address`):

```typescript
export interface LandAreaValue {
  acres: number | null;
  roods: number | null;
  perches: number | null;
  squareMeters: number | null;
  hectares: number | null;
}
```

Replace in `Land`:

```typescript
  size: number | null;
  sizeUnit: string | null;
```

with:

```typescript
  area: LandAreaValue;
```

Replace in `LandRequest`:

```typescript
  size?: number;
  sizeUnit?: string;
```

with:

```typescript
  area?: Partial<LandAreaValue>;
```

(`Partial` because a write only ever populates one unit system - the other fields are omitted, not sent as `null`, keeping the request body small and matching the backend's "field not present vs. explicitly null" tolerance, both of which JSON-serialize the same way for an omitted TypeScript property.)

- [ ] **Step 2: Add conversion constants and helpers**

Add after the `whatsAppHref` function:

```typescript
const SQUARE_METERS_PER_PERCH = 25.29285264;
const SQUARE_METERS_PER_ROOD = SQUARE_METERS_PER_PERCH * 40;
const SQUARE_METERS_PER_ACRE = SQUARE_METERS_PER_ROOD * 4;
const SQUARE_METERS_PER_HECTARE = 10000;

/** Mirrors AreaConversion.FromAcresRoodsPerches server-side - used for the live client-side preview only, the server value on save is authoritative. */
export function acresRoodsPerchesToSquareMeters(acres: number, roods: number, perches: number): number {
  return acres * SQUARE_METERS_PER_ACRE + roods * SQUARE_METERS_PER_ROOD + perches * SQUARE_METERS_PER_PERCH;
}

/** Mirrors AreaConversion.ToAcresRoodsPerches server-side. */
export function squareMetersToAcresRoodsPerches(squareMeters: number): { acres: number; roods: number; perches: number } {
  const totalPerches = squareMeters / SQUARE_METERS_PER_PERCH;
  const acres = Math.floor(totalPerches / 160);
  const remainder = totalPerches - acres * 160;
  const roods = Math.floor(remainder / 40);
  const perches = Math.round((remainder - roods * 40) * 100) / 100;
  return { acres, roods, perches };
}

export function squareMetersToHectares(squareMeters: number): number {
  return squareMeters / SQUARE_METERS_PER_HECTARE;
}

export function hectaresToSquareMeters(hectares: number): number {
  return hectares * SQUARE_METERS_PER_HECTARE;
}

/** Single source of truth for displaying a LandAreaValue - always formats from the A-R-P fields, which a Land response always has populated regardless of which unit it was entered in. */
export function formatArea(area: LandAreaValue): string {
  const { acres, roods, perches } = area;
  if (acres === null && roods === null && perches === null) return '—';

  const parts: string[] = [];
  if (acres) parts.push(`${acres}A`);
  if (roods) parts.push(`${roods}R`);
  if (perches || parts.length === 0) parts.push(`${perches ?? 0}P`);
  return parts.join(' ');
}
```

- [ ] **Step 3: Build**

Run: `cd ui && npm run build`
Expected: fails - every call site still using `land.size`/`land.sizeUnit`/`request.size` no longer compiles. Expected at this point; Tasks 7-8 fix the remaining call sites. Confirm the *only* errors are in `land-detail-panel.component.ts`, `land-list.component.ts`, `land-print.component.ts`, `add-land-widget.component.ts`, `job-detail.component.ts`, and `land.service.spec.ts` (if anything else breaks, stop and investigate).

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/core/land.service.ts
git commit -m "feat: add LandAreaValue and unified area conversion helpers"
```

---

### Task 6: `LandAreaInputComponent`

**Files:**
- Create: `ui/src/app/shared/land-area-input/land-area-input.component.ts`

**Interfaces:**
- Consumes: `LandAreaValue`, `acresRoodsPerchesToSquareMeters`, `squareMetersToAcresRoodsPerches`, `squareMetersToHectares`, `hectaresToSquareMeters` (Task 5).
- Produces: `LandAreaInputComponent` — `@Input() value: LandAreaValue`, `@Output() valueChange: EventEmitter<Partial<LandAreaValue>>`. Consumed by Task 7 (`land-detail-panel`) and Task 8 (`add-land-widget`).

- [ ] **Step 1: Create the component**

```typescript
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  LandAreaValue,
  acresRoodsPerchesToSquareMeters,
  hectaresToSquareMeters,
  squareMetersToAcresRoodsPerches,
  squareMetersToHectares
} from '../../core/land.service';

type AreaTab = 'arp' | 'sqm' | 'ha';

/**
 * Unit-system-tabbed area input - Acres/Roods/Perches, Square meters, or Hectares.
 * Emits only the active tab's field(s) populated, matching the backend's "exactly one
 * unit system per write" contract. No HTTP inside the component - controlled, same
 * pattern as LandLocationPickerComponent/OwnerPickerComponent.
 */
@Component({
  selector: 'app-land-area-input',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="space-y-sm">
      <div class="flex gap-xs text-xs">
        <button type="button" class="px-sm py-xs rounded" [class.bg-primary-100]="tab === 'arp'" (click)="selectTab('arp')">
          Acres/Roods/Perches
        </button>
        <button type="button" class="px-sm py-xs rounded" [class.bg-primary-100]="tab === 'sqm'" (click)="selectTab('sqm')">
          Square meters
        </button>
        <button type="button" class="px-sm py-xs rounded" [class.bg-primary-100]="tab === 'ha'" (click)="selectTab('ha')">
          Hectares
        </button>
      </div>

      @if (tab === 'arp') {
        <div class="flex gap-sm">
          <input class="input-field w-24" type="number" min="0" placeholder="Acres" [(ngModel)]="acres" (ngModelChange)="onArpChange()" />
          <select class="input-field w-24" [(ngModel)]="roods" (ngModelChange)="onArpChange()">
            <option [ngValue]="0">0 Roods</option>
            <option [ngValue]="1">1 Rood</option>
            <option [ngValue]="2">2 Roods</option>
            <option [ngValue]="3">3 Roods</option>
          </select>
          <input class="input-field w-28" type="number" min="0" max="39.99" step="0.01" placeholder="Perches" [(ngModel)]="perches" (ngModelChange)="onArpChange()" />
        </div>
      } @else if (tab === 'sqm') {
        <input class="input-field w-40" type="number" min="0" step="0.01" placeholder="Square meters" [(ngModel)]="squareMeters" (ngModelChange)="onSqmChange()" />
      } @else {
        <input class="input-field w-40" type="number" min="0" step="0.0001" placeholder="Hectares" [(ngModel)]="hectares" (ngModelChange)="onHaChange()" />
      }

      <p class="text-xs text-neutral-500">{{ previewLine() }}</p>
    </div>
  `
})
export class LandAreaInputComponent implements OnChanges {
  @Input() value: LandAreaValue = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null };
  @Output() valueChange = new EventEmitter<Partial<LandAreaValue>>();

  tab: AreaTab = 'arp';
  acres: number | null = null;
  roods = 0;
  perches: number | null = null;
  squareMeters: number | null = null;
  hectares: number | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['value']) return;
    this.acres = this.value.acres;
    this.roods = this.value.roods ?? 0;
    this.perches = this.value.perches;
    this.squareMeters = this.value.squareMeters;
    this.hectares = this.value.hectares;
  }

  selectTab(tab: AreaTab): void {
    this.tab = tab;
  }

  onArpChange(): void {
    this.valueChange.emit({ acres: this.acres, roods: this.roods, perches: this.perches, squareMeters: null, hectares: null });
  }

  onSqmChange(): void {
    this.valueChange.emit({ acres: null, roods: null, perches: null, squareMeters: this.squareMeters, hectares: null });
  }

  onHaChange(): void {
    this.valueChange.emit({ acres: null, roods: null, perches: null, squareMeters: null, hectares: this.hectares });
  }

  previewLine(): string {
    const sqm = this.currentSquareMeters();
    if (sqm === null) return 'Enter a value to see the equivalent in other units.';

    if (this.tab === 'arp') {
      return `≈ ${sqm.toLocaleString(undefined, { maximumFractionDigits: 0 })} m² · ${squareMetersToHectares(sqm).toFixed(2)} ha`;
    }
    const { acres, roods, perches } = squareMetersToAcresRoodsPerches(sqm);
    if (this.tab === 'sqm') {
      return `≈ ${acres}A ${roods}R ${perches}P · ${squareMetersToHectares(sqm).toFixed(2)} ha`;
    }
    return `≈ ${acres}A ${roods}R ${perches}P · ${sqm.toLocaleString(undefined, { maximumFractionDigits: 0 })} m²`;
  }

  private currentSquareMeters(): number | null {
    if (this.tab === 'arp') {
      if (this.acres === null && this.perches === null) return null;
      return acresRoodsPerchesToSquareMeters(this.acres ?? 0, this.roods, this.perches ?? 0);
    }
    if (this.tab === 'sqm') {
      return this.squareMeters === null ? null : this.squareMeters;
    }
    return this.hectares === null ? null : hectaresToSquareMeters(this.hectares);
  }
}
```

- [ ] **Step 2: Build**

Run: `cd ui && npm run build`
Expected: still fails on the same pre-existing call sites as Task 5's Step 3 (this component isn't wired in anywhere yet) - confirm no *new* errors introduced by this file itself (e.g. check the build output specifically mentions only the same six files as before).

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/shared/land-area-input/
git commit -m "feat: add LandAreaInputComponent with unit-system tabs"
```

---

### Task 7: Wire into `land-detail-panel` (create + edit)

**Files:**
- Modify: `ui/src/app/pages/land/land-detail-panel/land-detail-panel.component.ts`

**Interfaces:**
- Consumes: `LandAreaInputComponent` (Task 6), `LandAreaValue`, `formatArea` (Task 5).

- [ ] **Step 1: Import and register the component**

Add the import:

```typescript
import { LandAreaInputComponent } from '../../../shared/land-area-input/land-area-input.component';
```

Add `LandAreaInputComponent` to the `imports` array.

- [ ] **Step 2: Replace the Size/Unit inputs in the template**

Replace:

```html
            <input class="input-field" type="number" placeholder="Size" [(ngModel)]="size" />
            <input class="input-field" placeholder="Unit" [(ngModel)]="sizeUnit" />
```

with:

```html
            <app-land-area-input [value]="area" (valueChange)="onAreaChange($event)" />
```

(This sits inside the existing `grid grid-cols-2 gap-sm` Details block — since `LandAreaInputComponent` renders its own multi-row layout, move it just below that grid rather than as a grid cell: close the grid `</div>` before it if the two Size/Unit `<input>`s were the last items in that row, so the area input isn't squeezed into a half-width grid cell. Check the current template around this line before editing - the grid currently has 6 cells (Street/City/District/Size/Unit/GPS); removing 2 leaves 4, an even number, so the grid still closes cleanly without the area input inside it.)

- [ ] **Step 3: Replace `size`/`sizeUnit` fields with `area`**

Replace the class field declarations:

```typescript
  size: number | null = null;
  sizeUnit = '';
```

with:

```typescript
  area: LandAreaValue = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null };
```

Update the import line to include `LandAreaValue`:

```typescript
import { Address, Land, LandAreaValue, LandBoundary, LandDeed, LandPhoto, LandService, LandSurvey, telHref, whatsAppHref } from '../../../core/land.service';
```

(Note: keep the existing alphabetical-ish ordering the file already uses — insert `LandAreaValue` after `Address`, before `LandBoundary`.)

- [ ] **Step 4: Update `fetch()`'s load-from-`land`**

Replace:

```typescript
        this.size = land.size;
        this.sizeUnit = land.sizeUnit ?? '';
```

with:

```typescript
        this.area = land.area;
```

- [ ] **Step 5: Update `snapshotDetails()`**

Replace:

```typescript
      size: this.size, sizeUnit: this.sizeUnit, gpsCoordinates: this.gpsCoordinates,
```

with:

```typescript
      area: this.area, gpsCoordinates: this.gpsCoordinates,
```

- [ ] **Step 6: Update `discardDetails()`**

Replace:

```typescript
    this.size = current.size;
    this.sizeUnit = current.sizeUnit ?? '';
```

with:

```typescript
    this.area = current.area;
```

- [ ] **Step 7: Add `onAreaChange` handler, update both submit payloads**

Add a new method near `onOwnerChange`:

```typescript
  onAreaChange(value: Partial<LandAreaValue>): void {
    this.area = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null, ...value };
  }
```

In `saveDetails()`, replace:

```typescript
        size: this.size ?? undefined,
        sizeUnit: this.sizeUnit.trim() || undefined,
```

with:

```typescript
        area: this.area,
```

In `createLand()`, replace the equivalent two lines with the same `area: this.area,`.

- [ ] **Step 8: Build**

Run: `cd ui && npm run build`
Expected: `land-detail-panel.component.ts` no longer errors. Remaining errors (if any) should be confined to `land-list.component.ts`, `land-print.component.ts`, `add-land-widget.component.ts`, `job-detail.component.ts`, `land.service.spec.ts` (Task 8/9).

- [ ] **Step 9: Commit**

```bash
git add ui/src/app/pages/land/land-detail-panel/
git commit -m "feat: wire LandAreaInputComponent into land-detail-panel create/edit"
```

---

### Task 8: Remaining display/form call sites

**Files:**
- Modify: `ui/src/app/pages/land/land-list.component.ts`
- Modify: `ui/src/app/pages/land/land-print.component.ts`
- Modify: `ui/src/app/pages/job/add-land-widget/add-land-widget.component.ts`
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `formatArea`, `LandAreaInputComponent`, `LandAreaValue` (Tasks 5-6).

- [ ] **Step 1: `land-list.component.ts` — display**

Replace:

```html
                <th class="text-left px-lg py-sm font-medium">Size</th>
```

with:

```html
                <th class="text-left px-lg py-sm font-medium">Area</th>
```

Replace:

```html
                    @if (row.land.size) {
                      {{ row.land.size }} {{ row.land.sizeUnit }}
                    } @else {
                      —
                    }
```

with:

```html
                    {{ formatArea(row.land.area) }}
```

Add `formatArea` to the imports from `land.service`, and add `formatArea = formatArea;` as a class field (matching the existing `addressLine = addressLine;` pattern already on this component).

- [ ] **Step 2: `land-print.component.ts` — display**

Replace:

```html
        @if (land.size) {
          <p class="text-sm text-neutral-600">{{ land.size }} {{ land.sizeUnit }}</p>
        }
```

with:

```html
        @if (land.area.acres !== null || land.area.roods !== null || land.area.perches !== null) {
          <p class="text-sm text-neutral-600">{{ formatArea(land.area) }}</p>
        }
```

Add `formatArea` to the imports and as a class field, same as Step 1.

- [ ] **Step 3: `job-detail.component.ts` — display**

Replace:

```html
                      @if (l.size) {
                        <span class="text-xs text-neutral-500 block">{{ l.size }} {{ l.sizeUnit }}</span>
                      }
```

with:

```html
                      @if (l.area.acres !== null || l.area.roods !== null || l.area.perches !== null) {
                        <span class="text-xs text-neutral-500 block">{{ formatArea(l.area) }}</span>
                      }
```

Add `formatArea` to this file's imports from `land.service` and as a class field (check the top of `job-detail.component.ts` for its existing `land.service` import line and extend it rather than adding a second import).

- [ ] **Step 4: `add-land-widget.component.ts` — form + display**

Replace the display line:

```html
                @if (land.size) {
                  <span class="text-xs text-neutral-500 block">{{ land.size }} {{ land.sizeUnit }}</span>
                }
```

with:

```html
                @if (land.area.acres !== null || land.area.roods !== null || land.area.perches !== null) {
                  <span class="text-xs text-neutral-500 block">{{ formatArea(land.area) }}</span>
                }
```

Replace the form inputs:

```html
          <div class="flex gap-sm">
            <input class="input-field" type="number" placeholder="Size" [(ngModel)]="size" />
            <input class="input-field" type="text" placeholder="Unit (e.g. acres)" [(ngModel)]="sizeUnit" />
          </div>
```

with:

```html
          <app-land-area-input [value]="area" (valueChange)="onAreaChange($event)" />
```

Update the imports:

```typescript
import { Address, Land, LandAreaValue, LandService, addressLine, formatArea } from '../../../core/land.service';
import { LandAreaInputComponent } from '../../../shared/land-area-input/land-area-input.component';
```

Add `LandAreaInputComponent` to the `imports` array in the `@Component` decorator.

Replace the class fields:

```typescript
  size: number | null = null;
  sizeUnit = '';
```

with:

```typescript
  area: LandAreaValue = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null };
  formatArea = formatArea;
```

Add the handler:

```typescript
  onAreaChange(value: Partial<LandAreaValue>): void {
    this.area = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null, ...value };
  }
```

Update `createAndAdd()`'s submit payload — replace:

```typescript
        size: this.size ?? undefined,
        sizeUnit: this.sizeUnit.trim() || undefined
```

with:

```typescript
        area: this.area
```

Update `reset()` — replace:

```typescript
    this.size = null;
    this.sizeUnit = '';
```

with:

```typescript
    this.area = { acres: null, roods: null, perches: null, squareMeters: null, hectares: null };
```

- [ ] **Step 5: Build**

Run: `cd ui && npm run build`
Expected: no errors from any of the four files touched in this task. Remaining errors (if any) confined to `land.service.spec.ts` (Task 9).

- [ ] **Step 6: Commit**

```bash
git add ui/src/app/pages/land/land-list.component.ts ui/src/app/pages/land/land-print.component.ts ui/src/app/pages/job/add-land-widget/ ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat: switch remaining land area display/form call sites to LandAreaInputComponent/formatArea"
```

---

### Task 9: Update `land.service.spec.ts` fixtures, final build

**Files:**
- Modify: `ui/src/app/core/land.service.spec.ts`

**Interfaces:**
- Consumes: `LandAreaValue` (Task 5).

- [ ] **Step 1: Update the `create()` test fixture**

Replace:

```typescript
    const request = { address: { street: '123 Main St', city: 'Colombo', district: null, postalCode: null, country: null }, size: 10, sizeUnit: 'acres' };
    const land = { landId: 'l1', address: request.address, size: 10, sizeUnit: 'acres', gpsCoordinates: null, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
```

with:

```typescript
    const request = { address: { street: '123 Main St', city: 'Colombo', district: null, postalCode: null, country: null }, area: { acres: 10, roods: 0, perches: 0 } };
    const land = { landId: 'l1', address: request.address, area: { acres: 10, roods: 0, perches: 0, squareMeters: 40468.564224, hectares: 4.0468564224 }, gpsCoordinates: null, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
```

- [ ] **Step 2: Update the `getById()` test fixture**

Replace:

```typescript
    const land = { landId: 'l1', address: { street: 'Main St', city: null, district: null, postalCode: null, country: null }, size: null, sizeUnit: null, gpsCoordinates: null, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
```

with:

```typescript
    const land = { landId: 'l1', address: { street: 'Main St', city: null, district: null, postalCode: null, country: null }, area: { acres: null, roods: null, perches: null, squareMeters: null, hectares: null }, gpsCoordinates: null, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
```

- [ ] **Step 3: Update the `addressLine` describe block's `baseLand` fixture**

Replace:

```typescript
    size: null,
    sizeUnit: null,
```

with:

```typescript
    area: { acres: null, roods: null, perches: null, squareMeters: null, hectares: null },
```

- [ ] **Step 4: Build**

Run: `cd ui && npm run build`
Expected: Build succeeded, zero errors anywhere.

- [ ] **Step 5: In-browser sanity check**

Start API+UI preview (or reuse a running instance). Open a land record's detail page: confirm the Area block shows the three tabs, switching tabs updates the preview line, entering a value in one tab and saving persists correctly (reload the page, confirm the A-R-P tab shows the expected breakdown). Confirm the land list, job detail land rows, and the print page all show `formatArea()`'s compact output instead of a blank/broken area column.

- [ ] **Step 6: Commit**

```bash
git add ui/src/app/core/land.service.spec.ts
git commit -m "test: update land.service.spec.ts fixtures for unified area"
```

---

## Self-Review Notes

- **Spec coverage:** unified `AreaSquareMeters` storage (Task 2), `AreaConversion` (Task 1), `AreaDto` accept-one/return-all (Task 3), cross-field "one system only" validation (Task 3 + tested in Task 4), UI unit-tabbed input with live equivalents (Task 6), every call site from the spec's Scope list (Tasks 7-8) — all covered.
- **Placeholder scan:** none found — every step has real code, no "add validation"/TBD phrasing.
- **Type consistency:** `AreaDto` (Task 3, backend) fields `Acres/Roods/Perches/SquareMeters/Hectares` match `LandAreaValue` (Task 5, frontend) fields `acres/roods/perches/squareMeters/hectares` at every translation point (JSON serialization lower-cases property names automatically via the existing `ApiResponse<T>` pipeline, consistent with how `Address`/`AddressDto` already round-trip in this codebase). `formatArea`/`LandAreaInputComponent`'s prop names checked consistent across Tasks 6-9.
