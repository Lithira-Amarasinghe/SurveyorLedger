# Land GPS Location Map + Client Set-Location Link + Collapsed Summary — Design Spec

Date: 2026-08-12

## Purpose

Surveyors currently navigate to land using free-text address/GPS notes, causing avoidable back-and-forth ("which road, left or right at the junction?"). Let anyone with edit access pin the exact location on a map, share that pin as a Google Maps link, and let the client set/correct the pin themselves via a link requiring no account. Also improve the collapsed land row so it carries useful signal without expanding.

## Scope

- Structured `Latitude`/`Longitude` on `Land`, alongside the existing free-text `GpsCoordinates` (kept for manual notes, e.g. "ask for Silva's shop").
- Map picker: Leaflet + OpenStreetMap tiles (no API key, no billing). Address search via Nominatim (OSM's free geocoder).
- "Copy Google Maps link" / "Open in Google Maps" — plain deep link (`https://www.google.com/maps?q={lat},{lng}`), no Google SDK involved.
- Reusable share-link on `Land` (same shape as `DocumentRequest.ShareToken`, no new table) so a client can open a public page and set/update the pin without login.
- Collapsed land row (land-list, job-detail land block) shows owner name and a location-set indicator.

## Data Model

### `Land` additions

```csharp
public decimal? Latitude { get; set; }
public decimal? Longitude { get; set; }
public string? LocationShareToken { get; set; }
```

`Latitude`/`Longitude` are the map-picked point. `GpsCoordinates` (existing string) is untouched — still free text for notes that aren't a pin (e.g. "3rd gate past the temple").

### EF configuration (`LandConfiguration`)

```csharp
builder.Property(x => x.Latitude).HasColumnType("decimal(9,6)");
builder.Property(x => x.Longitude).HasColumnType("decimal(9,6)");
builder.Property(x => x.LocationShareToken).HasMaxLength(64);
builder.HasIndex(x => x.LocationShareToken).IsUnique().HasFilter("[LocationShareToken] IS NOT NULL");
```

`decimal(9,6)` gives ~11cm precision at the equator, plenty for a land parcel pin and standard for lat/lng storage. Filtered unique index matches the `DocumentRequest.ShareToken` precedent — enforced only when a token exists.

### Migration

`dotnet ef migrations add AddLandLocation` — three columns + one filtered unique index on `Land`. No FK, no other schema impact.

## Backend

### `LandService` additions

- `SetLocationAsync(workspaceId, callerUserId, landId, lat, lng)` — same permission gate as other land field edits (`LandPermission.Edit` via Casbin, matches `UpdateAsync`). Validates `-90 <= lat <= 90`, `-180 <= lng <= 180`.
- `GenerateLocationShareLinkAsync(workspaceId, callerUserId, landId)` — same edit gate. Sets `LocationShareToken = Guid.NewGuid("N")` if absent; if one already exists, returns it unchanged (reusable-until-revoked, not overwritten on every call — regenerate is a distinct explicit action, see next).
- `RegenerateLocationShareLinkAsync(...)` — same gate, always issues a fresh token (old one stops resolving immediately). Separate method from generate so "get or create" (idempotent, used when just opening the panel) and "I want a new link" (explicit user action) aren't the same call with different meanings depending on state.
- `RevokeLocationShareLinkAsync(...)` — same gate, sets `LocationShareToken = null`.
- `GetByLocationShareTokenAsync(token)` — no workspace/job scoping (unauthenticated caller). Looks up by token, throws `NotFoundException` if absent/revoked. Returns the land's address + current lat/lng only — not owner PII, not deeds/surveys/boundaries, not workspace internals.
- `SetLocationViaShareTokenAsync(token, lat, lng)` — same token lookup, same lat/lng validation, updates `Land.Latitude/Longitude`. No auth — the token is the auth, matching the `DocumentRequest` upload-via-token precedent.

### API surface

New `LandLocationLinkController` (unauthenticated, mirrors `DocumentRequestLinkController`'s trust-boundary-visible split):

```
PUT    /api/workspace/{workspaceId}/land/{landId}/location              [Authorize], land.edit — set lat/lng directly
POST   /api/workspace/{workspaceId}/land/{landId}/location-share-link   [Authorize], land.edit — get-or-create token
POST   /api/workspace/{workspaceId}/land/{landId}/location-share-link/regenerate [Authorize], land.edit
DELETE /api/workspace/{workspaceId}/land/{landId}/location-share-link   [Authorize], land.edit — revoke
GET    /api/land-location-links/{token}                                 public
PUT    /api/land-location-links/{token}                                 public — set lat/lng
```

The authenticated set/generate/revoke actions live on the existing `LandController`. Only the two public routes live in the new controller.

`[EnableRateLimiting("auth")]` on `LandLocationLinkController` — reuses the existing per-IP policy, same reasoning as the document-request link controller: unauthenticated write surface.

### Response shapes

`LandResponse` gains `Latitude`, `Longitude`, `HasActiveLocationShareLink` (bool, same "leak existence not secret" pattern as `DocumentRequestResponse.HasActiveShareLink`). The raw token is returned only from the generate/regenerate endpoint responses.

`LandLocationLinkPreviewResponse`: `AddressLine (string, pre-formatted), Latitude, Longitude` (nullable — null means "not set yet"). No land id, no owner, no workspace name — the client already knows which land this is about from context (the person who sent them the link told them), and nothing here is useful to someone who intercepts the URL beyond "set a pin."

## UI

### Shared `LandLocationPickerComponent` (standalone)

Inputs: `initialLat`, `initialLng` (nullable). Output: `locationChosen: {lat, lng}`.

- Leaflet map (`leaflet` npm package + its default OSM tile layer, CSS imported once in `styles.scss` or the component). Centers on `initialLat/Lng` if set, else a workspace-country-ish fallback (or geolocation prompt if available — optional, degrades silently if denied).
- Search input debounced to Nominatim (`https://nominatim.openstreetmap.org/search?format=json&q=...`), results list, clicking a result centers + drops the pin there.
- Click-to-place and drag-to-adjust on the pin (Leaflet marker, `draggable: true`).
- "Use this location" button emits `locationChosen`; parent owns the save call and closes the modal.
- No API key, no billing setup, works offline-tile-wise only if self-hosted (out of scope — uses the public OSM tile server, subject to their fair-use policy, acceptable at this app's scale).

### `land-detail-panel` — new "Location" block (next to Details)

- Shows `lat, lng` (6 decimals) or "Not set."
- **Set/update location** — opens `LandLocationPickerComponent` in a modal, on `locationChosen` calls `LandService.setLocation(...)`, updates local state.
- **Open in Google Maps** — `<a target="_blank" href="https://www.google.com/maps?q={lat},{lng}">`, disabled/hidden until a pin exists.
- **Copy Google Maps link** — clipboard-writes the same URL, brief "Copied" confirmation (reuses the existing inline-feedback pattern from the document-request share link work — transient text swap, no new toast system).
- **Client share link** — "Copy share link" (get-or-create + copy `${origin}/set-location/{token}`) when no active link; "Regenerate" / "Revoke" once one exists, sourced from `land.hasActiveLocationShareLink`.

### Public `PublicSetLocationComponent` — route `/set-location/:token`

- Registered outside the auth guard in `app.routes.ts`, no app shell (same pattern as `/document-upload/:token`).
- Fetches the preview on load. Invalid/revoked token → plain "This link is no longer valid" state, no form.
- Otherwise: shows the address line for context, embeds `LandLocationPickerComponent` pre-centered on the existing pin if any, a Submit button calling `PUT /api/land-location-links/{token}`. On success, shows a plain "Location saved — you can close this page" confirmation (stays reusable, so also lets them immediately re-open the picker to adjust if they made a mistake).
- New `LandLocationLinkService` (Angular): `getPreview(token)`, `setLocation(token, lat, lng)`. Never sends workspace/land id or auth header — same trust-boundary reasoning as `DocumentRequestLinkService`.

### Collapsed row summary (`land-list.component.ts`, `job-detail.component.ts` land block)

Both currently show address + size. Add:
- Owner name (`land.ownerName`, already on the response) — muted text, e.g. `Silva, W.M. · 2 acres`.
- A location badge: filled pin icon (clickable → opens Google Maps directly, `stopPropagation` so it doesn't also toggle the row) when `latitude/longitude` set; outline/muted pin when not set (not clickable, just a signal that it's missing). Small enough to sit inline with the address line, no layout change to the row's height.

## Error Handling

- Invalid/unknown/revoked token → `NotFoundException` (404) on both link endpoints, same "don't reveal existence" stance as the document-request link work.
- Out-of-range lat/lng (should be unreachable from the picker, but validated server-side regardless — defense in depth, same reasoning as file-upload validation elsewhere) → `ValidationException` (400).
- Nominatim unreachable/rate-limited → search box shows inline "Search unavailable — click the map to place the pin," picker remains fully usable without it.
- Geolocation denied/unsupported → silent no-op, picker just doesn't auto-center on the user; no error shown, this is optional convenience only.
- Leaflet tile load failure (offline) → browser shows Leaflet's default blank/broken-tile state; out of scope to build a custom offline fallback for a v1.

## Testing

- Service tests: `SetLocationAsync` persists and enforces `land.edit` permission (Client forbidden, same as other land edits); lat/lng range validation rejects out-of-bounds values; `GenerateLocationShareLinkAsync` is idempotent (second call returns same token); `RegenerateLocationShareLinkAsync` always issues a new token and the old one no longer resolves via `GetByLocationShareTokenAsync`; `RevokeLocationShareLinkAsync` clears the token and it no longer resolves; `SetLocationViaShareTokenAsync` succeeds on a valid token and updates the same `Land` row the authenticated path would.
- Manual: generate a link as Surveyor, open in incognito, confirm preview renders with no land/workspace id leaked in the payload, set a pin, confirm it round-trips back to the authenticated land-detail-panel; confirm "Open in Google Maps" opens the correct coordinates; confirm collapsed row pin badge flips from outline to filled after a location is set; confirm revoke kills the old link immediately.

## Out of Scope (v1)

- Reverse geocoding the pin back into `Address` fields (street/city/district) — the address stays independently editable text; the pin is purely a navigation aid.
- Distance/directions rendering inside the app (turn-by-turn) — "Open in Google Maps" hands that off to a purpose-built app instead of reinventing it.
- Self-hosted tile server or offline map caching.
- Per-recipient or expiring client links (mirrors the "reusable until revoked" decision — no analytics, no multiple simultaneous links).
