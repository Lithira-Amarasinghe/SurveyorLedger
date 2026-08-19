# Land Area: Unified Storage, Multi-Unit Input/Output — Design Spec

Date: 2026-08-18 (revised)

## Purpose

Replace the generic, unenforced `Size`/`SizeUnit` free-text pair on `Land` with a proper land measurement system supporting both the Sri Lankan survey convention (Acres, Roods, Perches — 1 Acre = 4 Roods = 160 Perches, 1 Rood = 40 Perches) and the metric system (square meters, hectares). Whichever unit a value is entered in, it's converted and stored in one canonical unit — every read can then return the value in any of the supported units without ambiguity about which one is "authoritative."

## Revision note

The original version of this spec stored Acres/Roods/Perches as three separate columns (an owned type mirroring `Address`). This revision replaces that with a **single canonical decimal column** (total square meters) plus conversion helpers on both read and write — the actual requirement is "store unified, accept/return many units," which a three-column A-R-P-only storage shape doesn't satisfy (it has no unambiguous place to put a metric input without first converting it anyway, so the conversion logic has to exist regardless — better to have exactly one canonical value and derive everything, including A-R-P, from it).

## Scope

- `Size`/`SizeUnit` removed outright from `Land`, replaced by a single `AreaSquareMeters` (decimal, nullable) column — the canonical value.
- No backfill of existing `Size` values — dropped, land records start with area unset (unchanged decision from the original spec).
- No new Casbin permission — area is gated by the same existing `land.edit`/`land.view` checks as every other land field.
- `LandRequest`/`LandResponse` gain a shared `AreaDto`, accepting **exactly one** unit system per write and always returning **all** unit systems on read (see below — this is what satisfies "provide endpoints for retrieving the area in required ways" without inventing per-unit query parameters).
- Same call sites as the original spec: `Land.cs`, `LandConfiguration.cs`, `LandRequest.cs`, `LandResponse.cs`, `LandService.cs`, `land.service.ts`, `land-detail-panel.component.ts`, `land-list.component.ts`, `land-print.component.ts`, `add-land-widget.component.ts`, `job-detail.component.ts`, `land.service.spec.ts`.

## Data Model

### `Land` changes

Remove `Size`/`SizeUnit`. Add:

```csharp
public decimal? AreaSquareMeters { get; set; }
```

No owned type needed — a single scalar doesn't warrant one (unlike `Address`/the old `LandArea`, which grouped multiple always-together fields).

### EF configuration (`LandConfiguration`)

```csharp
builder.Property(x => x.AreaSquareMeters).HasColumnType("decimal(14,4)");
```

`decimal(14,4)` — four decimal places on square meters is sub-millimeter precision, comfortably more than a hand-entered A-R-P or hectare value will ever carry after conversion; 14 total digits covers land sizes far beyond any realistic parcel.

Remove the existing `Size`/`SizeUnit` property configuration lines.

### Migration

`dotnet ef migrations add ReplaceSizeWithUnifiedArea` — drops `Size`/`SizeUnit`, adds `AreaSquareMeters` (decimal(14,4), nullable). No data migration step.

## Conversion Constants (shared reasoning, implemented twice — see below)

- 1 Perch = 25.29285264 m²
- 1 Rood = 40 Perches = 1011.7141056 m²
- 1 Acre = 4 Roods = 4046.8564224 m²
- 1 Hectare = 10000 m²

`SquareMeters = Acres × 4046.8564224 + Roods × 1011.7141056 + Perches × 25.29285264`

Reverse (m² → A-R-P): `totalPerches = SquareMeters ÷ 25.29285264`; `Acres = floor(totalPerches ÷ 160)`; `remainder = totalPerches − Acres×160`; `Roods = floor(remainder ÷ 40)`; `Perches = round(remainder − Roods×40, 2)`.

These constants are implemented once server-side (`SurveyorLedger.Core/AreaConversion.cs`, the authoritative source — what actually gets persisted) and once client-side (`land.service.ts`, for the live preview while typing, before the value round-trips through a save). This duplication is deliberate, not an oversight: the client-side copy only ever drives a *preview* the user sees before submitting; the server-side copy is what's actually stored. A live preview one keystroke ahead of a save doesn't need to share a compiled module with the backend to be trustworthy — it needs to roughly agree, and using the same formula (not the same file) achieves that without a cross-language shared-constants build step that would be disproportionate to three numbers.

## Backend

### `AreaDto` (new, alongside `AddressDto`)

```csharp
public class AreaDto
{
    // A-R-P system
    [Range(0, 100000)] public int? Acres { get; set; }
    [Range(0, 3)] public int? Roods { get; set; }
    [Range(0, 39.99)] public decimal? Perches { get; set; }

    // Metric system
    [Range(0, double.MaxValue)] public decimal? SquareMeters { get; set; }
    [Range(0, double.MaxValue)] public decimal? Hectares { get; set; }
}
```

`LandRequest.Area` (write): the caller populates **one** system — either `{Acres, Roods, Perches}` or `SquareMeters` or `Hectares` — and leaves the other system's fields null. `LandResponse.Area` (read): **all** fields are always populated, computed from the one stored `AreaSquareMeters` value, so a single GET satisfies "retrieve in whatever unit you need" without a `?unit=` query parameter or multiple endpoints.

### `AreaConversion` (new, `SurveyorLedger.Core/AreaConversion.cs`)

Static class, the authoritative constants and both conversion directions:

```csharp
namespace SurveyorLedger.Core;

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

### `LandService` — `ToAreaSquareMeters` / `ToAreaDto`

`ToAreaSquareMeters(AreaDto? dto)` (write path, mirrors the existing `ToAddress` helper):
- `dto` null, or all five fields null → returns `null` (area unset).
- Exactly one system populated → converts via `AreaConversion`, returns the resulting `decimal`.
- More than one system populated (e.g. both `Acres` and `SquareMeters` set) → throws `ValidationException("Provide area in one unit system only.")`. This is the one genuinely new cross-field rule this revision adds — everything else is per-field `[Range]` validation.
- Within the A-R-P system specifically, the original spec's "any one of the three implies the others default to 0" rule still applies (e.g. `Perches` alone, `Acres`/`Roods` null, is valid — coerced to 0 before calling `FromAcresRoodsPerches`).

`ToAreaDto(decimal? squareMeters)` (read path): if `null`, returns an `AreaDto` with everything null. Otherwise populates `SquareMeters` directly, `Hectares = squareMeters / 10000`, and `(Acres, Roods, Perches)` via `AreaConversion.ToAcresRoodsPerches`.

Used identically in `CreateAsync`, `UpdateAsync`, and wherever `LandResponse` is built (the existing `ToResponse` mapper in `LandController`).

## UI

### `LandAreaInputComponent` (new, `ui/src/app/shared/land-area-input/`)

A small unit-system selector (three tabs/segmented buttons: **A-R-P**, **Square meters**, **Hectares**) — whichever is active determines which input(s) show and which fields the component emits:

- **A-R-P** tab: Acres number input, Roods 0/1/2/3 dropdown (unchanged reasoning from the original spec — a dropdown makes an invalid Roods value structurally impossible), Perches decimal input (step 0.01, max 39.99).
- **Square meters** tab: one decimal input.
- **Hectares** tab: one decimal input.

Below the active tab's input(s), a read-only line converts live (client-side, using the same formulas as `AreaConversion`) into the *other* two representations, e.g. while on the A-R-P tab: `≈ 9,800 m² · 0.98 ha`; while on the Square meters tab: `≈ 2A 2R 0P · 0.98 ha`. This is the accessibility piece from the original spec, now generalized to whichever system the user isn't currently typing in.

`@Output() valueChange` emits only the active tab's field(s) populated (matching the backend's "exactly one system" contract) — e.g. `{ acres: 2, roods: 2, perches: 0, squareMeters: null, hectares: null }` while on the A-R-P tab.

On load (edit mode), the component receives the full `AreaDto` from `LandResponse` (all fields populated) and defaults to the A-R-P tab, pre-filled from the `Acres`/`Roods`/`Perches` the server already computed — switching tabs re-derives the other systems' starting values from the same response, so no client-side conversion is needed just to populate the initial view.

### `land.service.ts` changes

```typescript
export interface LandAreaValue {
  acres: number | null;
  roods: number | null;
  perches: number | null;
  squareMeters: number | null;
  hectares: number | null;
}
```

`area: LandAreaValue` on both `Land` and `LandRequest`.

New exported helpers:
- `formatArea(area: LandAreaValue): string` — compact A-R-P display for read-only contexts (land list, job rows, print page), e.g. `"1A 2R 15.5P"`, or `"—"` when unset. Always formats from the A-R-P fields (which are always populated in a `Land` response, per the backend's `ToAreaDto`), regardless of which unit the record was originally entered in — one consistent read format across the app, matching how the rest of this feature set already standardized on A-R-P as the primary domain convention for *display*.
- `acresRoodsPerchesToSquareMeters`, `squareMetersToAcresRoodsPerches`, `squareMetersToHectares`, `hectaresToSquareMeters` — mirror `AreaConversion`'s two directions plus the trivial hectare ones, used by `LandAreaInputComponent`'s live preview.

### Call-site updates

Same as the original spec's list (`land-detail-panel.component.ts`, `land-list.component.ts`, `land-print.component.ts`, `add-land-widget.component.ts`, `job-detail.component.ts`, `land.service.spec.ts`) — display sites all use `formatArea()` unchanged from the original plan; only the underlying `LandAreaValue` shape and the input component gained the two extra metric fields and the tab selector.

## Error Handling

- More than one unit system populated on write → `400 ValidationException`, `"Provide area in one unit system only."`
- Out-of-range value within a system (negative acres, Roods=4, negative m²) → `400` via `[Range]`, same pipeline as every other field.
- Partial A-R-P input (e.g. `Perches` alone) is not an error — coerced to 0 siblings before conversion, unchanged from the original spec.

## Testing

- Backend: `AreaConversion` round-trip tests (`FromAcresRoodsPerches` → `ToAcresRoodsPerches` recovers the original triple, within perch rounding) — pure unit tests, no DB. Service-level tests (extending/adding to the land test suite): create with only `Perches` set persists correctly; create with `SquareMeters` set converts and round-trips through a subsequent GET as the equivalent A-R-P; create with both `Acres` and `Hectares` set is rejected; `Roods=4` rejected.
- Frontend: `land.service.spec.ts` fixtures updated to the new `LandAreaValue` shape; conversion helpers are simple pure functions, verified by manual build + in-browser check, not a full suite run (unchanged verification approach from the original spec).

## Out of Scope (v1)

- No backfill/conversion of existing `Size` data (unchanged).
- No search/filter by area range (unchanged).
- No unit-preference setting (e.g. "this workspace always shows metric") — the A-R-P tab is always the default on load; out of scope until there's a real request for it.
