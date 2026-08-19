# Land Area: Acres/Roods/Perches (A-R-P) — Design Spec

Date: 2026-08-18

## Purpose

Replace the generic, unenforced `Size`/`SizeUnit` free-text pair on `Land` with a proper Sri Lankan land measurement system: Acres, Roods, Perches (1 Acre = 4 Roods = 160 Perches, 1 Rood = 40 Perches). This is the standard land-registry unit for this domain — `SizeUnit` was never actually a real multi-unit system, just an unenforced label that every existing usage assumed meant "acres."

## Scope

- `Size`/`SizeUnit` removed outright from `Land` — one field group, one validation path, no "which one is authoritative" ambiguity between a decimal `Size` and a text `SizeUnit`.
- No backfill of existing `Size` values — dropped, land records start with area unset (per explicit decision; there's no seed data and no verified real usage to preserve).
- No new Casbin permission — every land field, including area, is already gated by the existing `land.edit`/`land.view` checks (`EnsureLandAccessAsync`). This is a field swap, not a new capability.
- Frontend/backend call sites affected (from exploration): `Land.cs`, `LandConfiguration.cs`, `LandRequest.cs`, `LandResponse.cs`, `LandService.cs` (Create/Update), `land.service.ts` (`Land`/`LandRequest` interfaces + `land.service.spec.ts` fixtures), `land-detail-panel.component.ts` (form + create + edit), `land-list.component.ts` (display), `land-print.component.ts` (display), `add-land-widget.component.ts` (job's quick-create form + display), `job-detail.component.ts` (land row display).

## Data Model

### `LandArea` (new owned type, mirrors `Address`)

```csharp
namespace SurveyorLedger.Data.Entities;

/// <summary>
/// EF Core owned type - no separate table, columns embedded on Land. All three null means
/// "not set." If any one is provided, the others default to 0 (both client- and
/// server-side) - "15 perches" alone is a valid, complete area, not a validation error.
/// </summary>
public class LandArea
{
    public int? Acres { get; set; }
    public int? Roods { get; set; }
    public decimal? Perches { get; set; }
}
```

### `Land` changes

Remove `Size`/`SizeUnit`. Add:

```csharp
public LandArea Area { get; set; } = new();
```

### EF configuration (`LandConfiguration`)

```csharp
builder.OwnsOne(x => x.Area, a =>
{
    a.Property(p => p.Acres).HasColumnName("AreaAcres");
    a.Property(p => p.Roods).HasColumnName("AreaRoods");
    a.Property(p => p.Perches).HasColumnName("AreaPerches").HasColumnType("decimal(5,2)");
});
```

Remove the existing `builder.Property(x => x.Size).HasColumnType("decimal(18,2)")` and `builder.Property(x => x.SizeUnit).HasMaxLength(20)` lines.

### Migration

`dotnet ef migrations add ReplaceSizeWithLandArea` — drops `Size`/`SizeUnit` columns, adds `AreaAcres` (int, nullable), `AreaRoods` (int, nullable), `AreaPerches` (decimal(5,2), nullable). No data migration step (explicit decision — start blank).

## Backend Validation

### `AreaDto` (new, alongside `AddressDto` in `LandRequest`/`LandResponse`)

```csharp
public class AreaDto
{
    [Range(0, 100000, ErrorMessage = "Acres must be zero or greater.")]
    public int? Acres { get; set; }

    [Range(0, 3, ErrorMessage = "Roods must be between 0 and 3.")]
    public int? Roods { get; set; }

    [Range(0, 39.99, ErrorMessage = "Perches must be between 0 and 39.99.")]
    public decimal? Perches { get; set; }
}
```

`LandRequest` gets `public AreaDto? Area { get; set; }` (replacing `Size`/`SizeUnit`). `LandResponse` gets `public AreaDto Area { get; set; } = new();` (replacing the same, matching how `Address`/`AddressDto` already round-trip).

### Cross-field coercion (`LandService`)

A private `ToArea(AreaDto? dto)` helper (mirrors the existing `ToAddress(AddressDto? dto)` helper): if `dto` is null or all three fields are null, returns `new LandArea()` (fully unset). Otherwise, any null field is coerced to `0` before constructing the `LandArea` — this is the "15 perches alone is valid" rule, enforced once, server-side, regardless of what the client sent. No `ValidationException` path is needed for "partial" input; `[Range]` attributes alone reject truly out-of-bounds values (negative acres, Roods=4, Perches=45).

## UI

### `LandAreaInputComponent` (new, `ui/src/app/shared/land-area-input/`)

Three inputs in a row:
- **Acres**: `<input type="number" min="0">`.
- **Roods**: `<select>` with exactly four options (0, 1, 2, 3) — not a free number field. A rood is never anything else; a dropdown makes "Roods: 5" structurally unrepresentable instead of catching it after the fact via a validation message. This matters more here than a typical numeric field because the audience explicitly includes people unfamiliar with software.
- **Perches**: `<input type="number" min="0" max="39.99" step="0.01">`.

Below the three inputs, a read-only computed line, updated live as any field changes (no HTTP, pure client-side arithmetic):

```
≈ 2.42 acres · 9,800 m² · 0.98 ha
```

Conversion constants (`ui/src/app/core/land.service.ts`, exported alongside the new `formatArea` helper):
- 1 Perch = 25.29285264 m² (1 acre = 4046.8564224 m² ÷ 160)
- Total perches = `acres * 160 + roods * 40 + perches`
- Decimal acres = total perches ÷ 160
- Square meters = total perches × 25.29285264
- Hectares = square meters ÷ 10000

`@Input() value: LandAreaValue`, `@Output() valueChange: EventEmitter<LandAreaValue>` — controlled component, same shape as every other picker/input already built for this feature set (`LandLocationPickerComponent`, `OwnerPickerComponent`). No HTTP inside the component.

### `land.service.ts` changes

`Land`/`LandRequest` interfaces: replace `size`/`sizeUnit` with:

```typescript
export interface LandAreaValue {
  acres: number | null;
  roods: number | null;
  perches: number | null;
}
```
`area: LandAreaValue` on both interfaces.

New exported helpers (same file, same "single source of truth" pattern as `addressLine`):
- `formatArea(area: LandAreaValue): string` → compact display, e.g. `"1A 2R 15.5P"`, or `"—"` when fully unset. Only non-zero/non-null components that matter are shown (e.g. `"15.5P"` alone when Acres/Roods are 0).
- `areaToAcres`, `areaToSquareMeters`, `areaToHectares` — the conversion functions above, used by both `LandAreaInputComponent`'s live computed line and anywhere else that might want the equivalent (print page).

### Call-site updates

- `land-detail-panel.component.ts`: form state becomes three fields (`areaAcres`, `areaRoods`, `areaPerches`) bound to `<app-land-area-input>`, in both create-mode and edit-mode sections (same component, same "Details" block it already lives in). Submit payload sends `area: { acres, roods, perches }` instead of `size`/`sizeUnit`.
- `land-list.component.ts`, `job-detail.component.ts`: display swaps `{{ land.size }} {{ land.sizeUnit }}` for `{{ formatArea(land.area) }}`.
- `land-print.component.ts`: same swap, printed page.
- `add-land-widget.component.ts` (job's quick-create): same `<app-land-area-input>` swap in its form, same `formatArea` swap in its results-list display.
- `land.service.spec.ts`: fixtures updated from `size`/`sizeUnit` to `area: { acres, roods, perches }`.

## Error Handling

- Out-of-range `Roods`/`Perches`/`Acres` from a non-browser client (e.g. a direct API call bypassing the dropdown) → `400` via the existing `[Range]`-attribute → `ValidationException` pipeline, same as every other `LandRequest` field.
- Partial input (only one or two of the three set) is not an error — coerced server-side per the cross-field rule above, matching what the UI already does live so the two never disagree.

## Testing

- Backend: extend the existing `LandServiceTests`-equivalent coverage (or add a focused `LandAreaTests` if none exists for `CreateAsync`/`UpdateAsync` today) — creating with only `Perches` set persists `Acres=0, Roods=0`; `Roods=4` and `Perches=45` both rejected with `ValidationException`; area fully omitted persists as fully null (not zeroed).
- Frontend: no new automated tests planned beyond updating the existing `land.service.spec.ts` fixtures to compile against the new shape — `formatArea`/`areaToAcres`/etc. are simple pure functions, verified by manual build + in-browser check per this repo's established "build + targeted check" verification approach, not a full suite run.

## Out of Scope (v1)

- No backfill/conversion of existing `Size` data (explicit decision).
- No search/filter by area range — `LandService.SearchAsync`'s existing text search (address/deed/survey) is untouched; area isn't a searchable field in v1.
- No persisted "total perches" or "total m²" column for sorting — the computed equivalents are display-only, derived client-side; if sorting/filtering by area becomes a real need later, that's a separate follow-up.
