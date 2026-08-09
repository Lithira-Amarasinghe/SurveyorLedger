# Job Feature UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the sidebar's dead "Jobs" placeholder with a working Job list + Job detail UI covering the phone-call workflow: create a job (title only), attach a client (search-or-create, unified with staff search), attach land (search-or-create), change status.

**Architecture:** Standalone Angular components + signals, following this repo's existing pattern exactly (see `MembersComponent`, `DashboardComponent`, `create-modal.component.ts`). Three new services (`JobService`, `LandService`, `PersonService`) wrap `HttpClient` and unwrap the backend's `ApiResponse<T>` envelope, same as `WorkspaceService`. No new UI framework, no state management library — signals + services is what's already here.

**Tech Stack:** Angular 21 standalone components, RxJS, Tailwind (existing `btn-primary`/`btn-secondary`/`card`/`input-field` utility classes and `xs`/`sm`/`md`/`lg` spacing tokens — do not invent new ones), Vitest + `@angular/core/testing` + `HttpClientTestingModule` for service tests.

## Global Constraints

- Follow `docs/superpowers/specs/2026-08-09-job-ui-design.md` exactly — Job list + Job detail only, no Land/Client dedicated management screens this pass.
- Backend API base path: `${environment.apiBaseUrl}/workspace/{workspaceId}/...` — every new service call is workspace-scoped, `workspaceId` comes from `CurrentWorkspaceService.current()`.
- `ApiResponse<T>` envelope (`{success, data, message?}`) — every HTTP call unwraps via `.pipe(map(res => res.data))`, matching `WorkspaceService`.
- No separate `ClientService` — `PersonService` is the only frontend-facing service for people (see spec).
- People are differentiated by **actual role** (`Admin`/`Manager`/`Surveyor`/`Client`), never a generic staff/client flag.
- Reuse existing Tailwind classes (`btn-primary`, `btn-secondary`, `card`, `input-field`) — no new CSS.
- No `any` casts. No child component's internal signals poked directly from a parent template/class — child components expose a small public method surface for parents to call.

---

## File Structure

**New files:**
- `ui/src/app/core/job.service.ts` — Job CRUD, participants, land links (includes `getLands`)
- `ui/src/app/core/job.service.spec.ts`
- `ui/src/app/core/land.service.ts` — Land search/create, `addressLine()` helper
- `ui/src/app/core/land.service.spec.ts`
- `ui/src/app/core/person.service.ts` — unified people search/create
- `ui/src/app/core/person.service.spec.ts`
- `ui/src/app/pages/job/job-list.component.ts` — Job table + create modal trigger
- `ui/src/app/pages/job/create-job-modal/create-job-modal.component.ts`
- `ui/src/app/pages/job/job-detail.component.ts` — header + People + Land sections
- `ui/src/app/pages/job/add-person-widget/add-person-widget.component.ts` — reusable search-or-create-person widget
- `ui/src/app/pages/job/add-land-widget/add-land-widget.component.ts` — reusable search-or-create-land widget

**Modified files:**
- `ui/src/app/app.routes.ts` — replace `ComingSoonComponent` at `jobs` with `JobListComponent`, add `jobs/:jobId`
- `UI_IMPLEMENTATION_GUIDE.md` — drop "Jobs" from the not-in-scope list (last task)

---

### Task 1: JobService

**Files:**
- Create: `ui/src/app/core/job.service.ts`
- Test: `ui/src/app/core/job.service.spec.ts`

**Interfaces:**
- Consumes: `Land` type from `ui/src/app/core/land.service.ts` (Task 2 — see note below on task ordering).
- Produces: `Job` interface (`{jobId, jobNumber, title, description: string | null, status, createdBy, createdAt, updatedAt}`), `JobParticipant` interface (`{id, userId, firstName, lastName, email: string | null, participantType, addedAt}`), `JobService` class with methods `list`, `create`, `getById`, `update`, `updateStatus`, `getParticipants`, `addParticipant`, `removeParticipant`, `getLands`, `addLand`, `removeLand`.

**Task ordering note:** `JobService.getLands` returns `Land[]`, and `Land` is defined in Task 2's `land.service.ts`. Implement Task 2 (`LandService`) immediately before finishing this task's Step 3 — write this task's tests first (Step 1-2 below), then do Task 2 in full, then come back and finish Task 1's Step 3 implementation with the `Land` import available. This avoids a half-finished `JobService` being patched later.

- [ ] **Step 1: Write the failing tests**

```typescript
// ui/src/app/core/job.service.spec.ts
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { JobService } from './job.service';
import { environment } from '../../environments/environment';

describe('JobService', () => {
  let service: JobService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';
  const base = `${environment.apiBaseUrl}/workspace/${workspaceId}/job`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [JobService]
    });
    service = TestBed.inject(JobService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list() unwraps ApiResponse and hits the correct URL', () => {
    const jobs = [{ jobId: 'j1', jobNumber: 'JOB-0001', title: 'Test', description: null, status: 'Draft', createdBy: 'u1', createdAt: '2026-01-01', updatedAt: '2026-01-01' }];
    service.list(workspaceId).subscribe(result => expect(result).toEqual(jobs));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: jobs });
  });

  it('create() posts title only', () => {
    const job = { jobId: 'j1', jobNumber: 'JOB-0001', title: 'New job', description: null, status: 'Draft', createdBy: 'u1', createdAt: '2026-01-01', updatedAt: '2026-01-01' };
    service.create(workspaceId, 'New job').subscribe(result => expect(result).toEqual(job));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ title: 'New job' });
    req.flush({ success: true, data: job });
  });

  it('updateStatus() puts to the /status sub-route', () => {
    service.updateStatus(workspaceId, 'j1', 'Scheduled').subscribe();
    const req = httpMock.expectOne(`${base}/j1/status`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ status: 'Scheduled' });
    req.flush({ success: true, data: {} });
  });

  it('addParticipant() posts participantType in the body', () => {
    service.addParticipant(workspaceId, 'j1', 'u2', 'Client').subscribe();
    const req = httpMock.expectOne(`${base}/j1/participants/u2`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ participantType: 'Client' });
    req.flush({ success: true, data: {} });
  });

  it('removeParticipant() deletes with no body', () => {
    service.removeParticipant(workspaceId, 'j1', 'u2').subscribe();
    const req = httpMock.expectOne(`${base}/j1/participants/u2`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('getLands() gets the /lands sub-route', () => {
    const lands = [{ landId: 'l1', address: { street: 'Main St', city: null, district: null, postalCode: null, country: null }, size: null, sizeUnit: null, gpsCoordinates: null, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' }];
    service.getLands(workspaceId, 'j1').subscribe(result => expect(result).toEqual(lands));
    const req = httpMock.expectOne(`${base}/j1/lands`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: lands });
  });

  it('addLand() posts with no body', () => {
    service.addLand(workspaceId, 'j1', 'land1').subscribe();
    const req = httpMock.expectOne(`${base}/j1/lands/land1`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('removeLand() deletes', () => {
    service.removeLand(workspaceId, 'j1', 'land1').subscribe();
    const req = httpMock.expectOne(`${base}/j1/lands/land1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ui && npx vitest run src/app/core/job.service.spec.ts`
Expected: FAIL — `job.service.ts` does not exist.

- [ ] **Step 3: Complete Task 2 (LandService) first, then write this implementation**

```typescript
// ui/src/app/core/job.service.ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { Land } from './land.service';

export interface Job {
  jobId: string;
  jobNumber: string;
  title: string;
  description: string | null;
  status: string;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
}

export interface JobParticipant {
  id: string;
  userId: string;
  firstName: string;
  lastName: string;
  email: string | null;
  participantType: string;
  addedAt: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class JobService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/job`;
  }

  list(workspaceId: string): Observable<Job[]> {
    return this.http.get<ApiResponse<Job[]>>(this.base(workspaceId)).pipe(map(res => res.data));
  }

  create(workspaceId: string, title: string): Observable<Job> {
    return this.http.post<ApiResponse<Job>>(this.base(workspaceId), { title }).pipe(map(res => res.data));
  }

  getById(workspaceId: string, jobId: string): Observable<Job> {
    return this.http.get<ApiResponse<Job>>(`${this.base(workspaceId)}/${jobId}`).pipe(map(res => res.data));
  }

  update(workspaceId: string, jobId: string, request: { title: string; description: string | null }): Observable<Job> {
    return this.http.put<ApiResponse<Job>>(`${this.base(workspaceId)}/${jobId}`, request).pipe(map(res => res.data));
  }

  updateStatus(workspaceId: string, jobId: string, status: string): Observable<Job> {
    return this.http.put<ApiResponse<Job>>(`${this.base(workspaceId)}/${jobId}/status`, { status }).pipe(map(res => res.data));
  }

  getParticipants(workspaceId: string, jobId: string): Observable<JobParticipant[]> {
    return this.http.get<ApiResponse<JobParticipant[]>>(`${this.base(workspaceId)}/${jobId}/participants`).pipe(map(res => res.data));
  }

  addParticipant(workspaceId: string, jobId: string, userId: string, participantType: string): Observable<JobParticipant> {
    return this.http
      .post<ApiResponse<JobParticipant>>(`${this.base(workspaceId)}/${jobId}/participants/${userId}`, { participantType })
      .pipe(map(res => res.data));
  }

  removeParticipant(workspaceId: string, jobId: string, userId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${jobId}/participants/${userId}`);
  }

  getLands(workspaceId: string, jobId: string): Observable<Land[]> {
    return this.http.get<ApiResponse<Land[]>>(`${this.base(workspaceId)}/${jobId}/lands`).pipe(map(res => res.data));
  }

  addLand(workspaceId: string, jobId: string, landId: string): Observable<void> {
    return this.http.post<void>(`${this.base(workspaceId)}/${jobId}/lands/${landId}`, {});
  }

  removeLand(workspaceId: string, jobId: string, landId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${jobId}/lands/${landId}`);
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd ui && npx vitest run src/app/core/job.service.spec.ts`
Expected: PASS, all 8 tests green.

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/core/job.service.ts ui/src/app/core/job.service.spec.ts
git commit -m "feat: add JobService for job CRUD, participants, land links"
```

---

### Task 2: LandService

**Files:**
- Create: `ui/src/app/core/land.service.ts`
- Test: `ui/src/app/core/land.service.spec.ts`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: `Address` interface (`{street: string | null, city: string | null, district: string | null, postalCode: string | null, country: string | null}`), `Land` interface (`{landId, address: Address, size: number | null, sizeUnit: string | null, gpsCoordinates: string | null, notes: string | null, createdAt, updatedAt}`), `LandService` with `search(workspaceId, query?)` and `create(workspaceId, request)`, and an exported `addressLine(land: Land): string` helper — the single source of truth for formatting a Land's address line, used by both `AddLandWidgetComponent` (Task 7) and `JobDetailComponent` (Task 8) instead of each reimplementing it.

- [ ] **Step 1: Write the failing tests**

```typescript
// ui/src/app/core/land.service.spec.ts
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { LandService, addressLine, Land } from './land.service';
import { environment } from '../../environments/environment';

describe('LandService', () => {
  let service: LandService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';
  const base = `${environment.apiBaseUrl}/workspace/${workspaceId}/land`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [LandService]
    });
    service = TestBed.inject(LandService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('search() with a query appends ?query=', () => {
    service.search(workspaceId, 'main st').subscribe();
    const req = httpMock.expectOne(`${base}?query=main%20st`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [] });
  });

  it('search() with no query hits the base URL', () => {
    service.search(workspaceId).subscribe();
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [] });
  });

  it('create() posts the land request', () => {
    const request = { address: { street: '123 Main St', city: 'Colombo', district: null, postalCode: null, country: null }, size: 10, sizeUnit: 'acres' };
    const land = { landId: 'l1', address: request.address, size: 10, sizeUnit: 'acres', gpsCoordinates: null, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
    service.create(workspaceId, request).subscribe(result => expect(result).toEqual(land));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: land });
  });
});

describe('addressLine', () => {
  const baseLand: Land = {
    landId: 'l1',
    address: { street: null, city: null, district: null, postalCode: null, country: null },
    size: null,
    sizeUnit: null,
    gpsCoordinates: null,
    notes: null,
    createdAt: '2026-01-01',
    updatedAt: '2026-01-01'
  };

  it('joins street and city with a comma', () => {
    expect(addressLine({ ...baseLand, address: { ...baseLand.address, street: '123 Main St', city: 'Colombo' } })).toBe('123 Main St, Colombo');
  });

  it('falls back to a placeholder when both are empty', () => {
    expect(addressLine(baseLand)).toBe('Unnamed land record');
  });

  it('uses just street when city is missing', () => {
    expect(addressLine({ ...baseLand, address: { ...baseLand.address, street: '123 Main St' } })).toBe('123 Main St');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ui && npx vitest run src/app/core/land.service.spec.ts`
Expected: FAIL — `land.service.ts` does not exist.

- [ ] **Step 3: Write the implementation**

```typescript
// ui/src/app/core/land.service.ts
import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface Address {
  street: string | null;
  city: string | null;
  district: string | null;
  postalCode: string | null;
  country: string | null;
}

export interface Land {
  landId: string;
  address: Address;
  size: number | null;
  sizeUnit: string | null;
  gpsCoordinates: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface LandRequest {
  address?: Address;
  size?: number;
  sizeUnit?: string;
  gpsCoordinates?: string;
  notes?: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

/** Single source of truth for formatting a Land's address into a display line. */
export function addressLine(land: Land): string {
  return [land.address.street, land.address.city].filter(Boolean).join(', ') || 'Unnamed land record';
}

@Injectable({ providedIn: 'root' })
export class LandService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/land`;
  }

  search(workspaceId: string, query?: string): Observable<Land[]> {
    const params = query ? new HttpParams().set('query', query) : undefined;
    return this.http.get<ApiResponse<Land[]>>(this.base(workspaceId), { params }).pipe(map(res => res.data));
  }

  create(workspaceId: string, request: LandRequest): Observable<Land> {
    return this.http.post<ApiResponse<Land>>(this.base(workspaceId), request).pipe(map(res => res.data));
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd ui && npx vitest run src/app/core/land.service.spec.ts`
Expected: PASS, all 6 tests green (3 service + 3 `addressLine`).

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/core/land.service.ts ui/src/app/core/land.service.spec.ts
git commit -m "feat: add LandService with search/create and shared addressLine helper"
```

Return to Task 1 Step 3 now that `Land` exists, finish that implementation, then continue to Task 3.

---

### Task 3: PersonService

**Files:**
- Create: `ui/src/app/core/person.service.ts`
- Test: `ui/src/app/core/person.service.spec.ts`

**Interfaces:**
- Consumes: `WorkspaceService.getMembers(workspaceId): Observable<Member[]>` (existing, `ui/src/app/core/workspace.service.ts` — `Member = {userId, email, firstName, lastName, role, assignedAt, isOwner}`).
- Produces: `Person` interface (`{userId, name, roleLabel}`), `PersonService` with `searchPeople(workspaceId, query)` and `createClient(workspaceId, request)`.

This is the unified layer the UI talks to for the People widget — merges workspace members (`roleLabel` = their real role) with bare clients returned by `GET /workspace/{id}/client?query=` (`roleLabel` = `"Client"`), de-duplicated by `userId`, filtered by `query` on name/email for the members half (the client endpoint already filters server-side).

- [ ] **Step 1: Write the failing tests**

```typescript
// ui/src/app/core/person.service.spec.ts
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { PersonService } from './person.service';
import { environment } from '../../environments/environment';

describe('PersonService', () => {
  let service: PersonService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [PersonService]
    });
    service = TestBed.inject(PersonService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('searchPeople() merges members and clients, labels each by real role', () => {
    let result: any;
    service.searchPeople(workspaceId, 'sam').subscribe(r => (result = r));

    const membersReq = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/members`);
    membersReq.flush({
      success: true,
      data: [
        { userId: 'u1', email: 'sam@x.com', firstName: 'Sam', lastName: 'Surveyor', role: 'Surveyor', assignedAt: '2026-01-01', isOwner: false },
        { userId: 'u2', email: 'ann@x.com', firstName: 'Ann', lastName: 'Admin', role: 'Admin', assignedAt: '2026-01-01', isOwner: true }
      ]
    });

    const clientsReq = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/client?query=sam`);
    clientsReq.flush({
      success: true,
      data: [{ userId: 'u3', firstName: 'Samantha', lastName: 'Client', phone: '077', email: null, hasLogin: false, createdAt: '2026-01-01' }]
    });

    expect(result).toEqual([
      { userId: 'u1', name: 'Sam Surveyor', roleLabel: 'Surveyor' },
      { userId: 'u3', name: 'Samantha Client', roleLabel: 'Client' }
    ]);
  });

  it('searchPeople() de-duplicates by userId, preferring the member entry (real role over generic Client label)', () => {
    let result: any;
    service.searchPeople(workspaceId, 'sam').subscribe(r => (result = r));

    const membersReq = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/members`);
    membersReq.flush({
      success: true,
      data: [{ userId: 'u1', email: 'sam@x.com', firstName: 'Sam', lastName: 'Both', role: 'Surveyor', assignedAt: '2026-01-01', isOwner: false }]
    });

    const clientsReq = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/client?query=sam`);
    clientsReq.flush({
      success: true,
      data: [{ userId: 'u1', firstName: 'Sam', lastName: 'Both', phone: null, email: 'sam@x.com', hasLogin: true, createdAt: '2026-01-01' }]
    });

    expect(result).toEqual([{ userId: 'u1', name: 'Sam Both', roleLabel: 'Surveyor' }]);
  });

  it('createClient() posts to /client and returns a Person labeled Client', () => {
    let result: any;
    service.createClient(workspaceId, { firstName: 'New', lastName: 'Client', phone: '0771234567' }).subscribe(r => (result = r));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/client`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ firstName: 'New', lastName: 'Client', phone: '0771234567' });
    req.flush({ success: true, data: { userId: 'u9', firstName: 'New', lastName: 'Client', phone: '0771234567', email: null, hasLogin: false, createdAt: '2026-01-01' } });

    expect(result).toEqual({ userId: 'u9', name: 'New Client', roleLabel: 'Client' });
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ui && npx vitest run src/app/core/person.service.spec.ts`
Expected: FAIL — `person.service.ts` does not exist.

- [ ] **Step 3: Write the implementation**

```typescript
// ui/src/app/core/person.service.ts
import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, forkJoin } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { WorkspaceService, Member } from './workspace.service';

export interface Person {
  userId: string;
  name: string;
  roleLabel: string;
}

interface ClientResponse {
  userId: string;
  firstName: string;
  lastName: string;
  phone: string | null;
  email: string | null;
  hasLogin: boolean;
  createdAt: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class PersonService {
  constructor(private http: HttpClient, private workspaceService: WorkspaceService) {}

  private clientBase(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/client`;
  }

  /**
   * Merges workspace members (real role: Admin/Manager/Surveyor/Client) with bare
   * clients (roleLabel "Client") into one search result, de-duplicated by userId -
   * a person who is both a workspace member AND has been used as a client keeps
   * their member entry (real role wins over the generic "Client" label).
   */
  searchPeople(workspaceId: string, query: string): Observable<Person[]> {
    const term = query.trim().toLowerCase();

    const members$ = this.workspaceService.getMembers(workspaceId).pipe(
      map(members =>
        members
          .filter(m => !term || `${m.firstName} ${m.lastName}`.toLowerCase().includes(term) || m.email.toLowerCase().includes(term))
          .map(m => this.toPersonFromMember(m))
      )
    );

    const params = new HttpParams().set('query', query);
    const clients$ = this.http
      .get<ApiResponse<ClientResponse[]>>(this.clientBase(workspaceId), { params })
      .pipe(map(res => res.data.map(c => this.toPersonFromClient(c))));

    return forkJoin({ members: members$, clients: clients$ }).pipe(
      map(({ members, clients }) => {
        const seen = new Set(members.map(m => m.userId));
        const uniqueClients = clients.filter(c => !seen.has(c.userId));
        return [...members, ...uniqueClients];
      })
    );
  }

  createClient(workspaceId: string, request: { firstName: string; lastName: string; phone?: string }): Observable<Person> {
    return this.http
      .post<ApiResponse<ClientResponse>>(this.clientBase(workspaceId), request)
      .pipe(map(res => this.toPersonFromClient(res.data)));
  }

  private toPersonFromMember(m: Member): Person {
    return { userId: m.userId, name: `${m.firstName} ${m.lastName}`, roleLabel: m.role };
  }

  private toPersonFromClient(c: ClientResponse): Person {
    return { userId: c.userId, name: `${c.firstName} ${c.lastName}`, roleLabel: 'Client' };
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd ui && npx vitest run src/app/core/person.service.spec.ts`
Expected: PASS, all 3 tests green.

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/core/person.service.ts ui/src/app/core/person.service.spec.ts
git commit -m "feat: add PersonService unifying staff and client search by real role"
```

---

### Task 4: CreateJobModalComponent

**Files:**
- Create: `ui/src/app/pages/job/create-job-modal/create-job-modal.component.ts`

**Interfaces:**
- Consumes: `JobService.create(workspaceId, title): Observable<Job>` (Task 1), `Job` type (Task 1).
- Produces: `CreateJobModalComponent` with `@Input() workspaceId: string`, `@Output() cancel: EventEmitter<void>`, `@Output() created: EventEmitter<Job>`.

Mirrors `ui/src/app/pages/workspace/create-modal/create-modal.component.ts` exactly (same backdrop/card/form structure), single Title field instead of Name/Description/Tier.

- [ ] **Step 1: Write the component**

```typescript
// ui/src/app/pages/job/create-job-modal/create-job-modal.component.ts
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Job, JobService } from '../../../core/job.service';

@Component({
  selector: 'app-create-job-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">New job</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Title</label>
            <input class="input-field" type="text" name="title" [(ngModel)]="title" required autofocus />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !title.trim()">
              {{ loading() ? 'Creating…' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class CreateJobModalComponent {
  @Input() workspaceId = '';
  @Output() cancel = new EventEmitter<void>();
  @Output() created = new EventEmitter<Job>();

  title = '';
  loading = signal(false);
  error = signal('');

  constructor(private jobService: JobService) {}

  submit(): void {
    if (!this.title.trim()) return;
    this.error.set('');
    this.loading.set(true);
    this.jobService.create(this.workspaceId, this.title.trim()).subscribe({
      next: (job) => {
        this.loading.set(false);
        this.created.emit(job);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not create job.');
      }
    });
  }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors referencing `create-job-modal.component.ts`.

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/pages/job/create-job-modal/create-job-modal.component.ts
git commit -m "feat: add CreateJobModalComponent (title-only job creation)"
```

---

### Task 5: JobListComponent + routing

**Files:**
- Create: `ui/src/app/pages/job/job-list.component.ts`
- Modify: `ui/src/app/app.routes.ts` — remove the `ComingSoonComponent` import/usage for `jobs` (check first whether anything else in the file still uses `ComingSoonComponent` before deleting its import — grep the file), import `JobListComponent` and `JobDetailComponent` instead.

**Interfaces:**
- Consumes: `JobService.list(workspaceId): Observable<Job[]>`, `Job` type (Task 1), `CreateJobModalComponent` (Task 4), `CurrentWorkspaceService.current()` (existing).
- Produces: `JobListComponent`, routed at `app/workspace/:id/jobs`.

**Note:** this task's route change references `JobDetailComponent`, built in Task 8. That import stays unresolved until Task 8 lands — expected, both tasks are part of the same plan, not two separate deliveries.

- [ ] **Step 1: Write the component**

```typescript
// ui/src/app/pages/job/job-list.component.ts
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Job, JobService } from '../../core/job.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { CreateJobModalComponent } from './create-job-modal/create-job-modal.component';

const STATUS_STYLES: Record<string, string> = {
  Draft: 'bg-neutral-100 text-neutral-600',
  Scheduled: 'bg-blue-100 text-blue-700',
  InProgress: 'bg-amber-100 text-amber-700',
  Completed: 'bg-green-100 text-green-700',
  Cancelled: 'bg-neutral-200 text-neutral-500'
};

@Component({
  selector: 'app-job-list',
  standalone: true,
  imports: [CommonModule, CreateJobModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Jobs</h1>
        <button class="btn-primary" (click)="modalOpen.set(true)">New job</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (jobs().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No jobs yet. Create one to get started.</div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Job #</th>
                <th class="text-left px-lg py-sm font-medium">Title</th>
                <th class="text-left px-lg py-sm font-medium">Status</th>
                <th class="text-left px-lg py-sm font-medium">Created</th>
              </tr>
            </thead>
            <tbody>
              @for (job of jobs(); track job.jobId) {
                <tr class="border-t border-neutral-200 cursor-pointer hover:bg-neutral-50" (click)="open(job)">
                  <td class="px-lg py-sm text-neutral-900 font-mono text-xs">{{ job.jobNumber }}</td>
                  <td class="px-lg py-sm text-neutral-900">{{ job.title }}</td>
                  <td class="px-lg py-sm">
                    <span class="text-xs px-sm py-xs rounded" [class]="statusClass(job.status)">{{ job.status }}</span>
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ job.createdAt | date: 'mediumDate' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-create-job-modal [workspaceId]="workspaceId" (cancel)="modalOpen.set(false)" (created)="onCreated($event)" />
    }
  `
})
export class JobListComponent implements OnInit {
  workspaceId = '';
  jobs = signal<Job[]>([]);
  loading = signal(true);
  error = signal('');
  modalOpen = signal(false);

  constructor(
    private jobService: JobService,
    private currentWorkspace: CurrentWorkspaceService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.jobService.list(this.workspaceId).subscribe({
      next: (jobs) => {
        this.jobs.set(jobs);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load jobs.');
        this.loading.set(false);
      }
    });
  }

  statusClass(status: string): string {
    return STATUS_STYLES[status] ?? STATUS_STYLES['Draft'];
  }

  open(job: Job): void {
    this.router.navigate(['/app/workspace', this.workspaceId, 'jobs', job.jobId]);
  }

  onCreated(job: Job): void {
    this.modalOpen.set(false);
    this.router.navigate(['/app/workspace', this.workspaceId, 'jobs', job.jobId]);
  }
}
```

`CommonModule` already exports `DatePipe`, used above via `| date` — no extra import needed.

- [ ] **Step 2: Wire the route**

Edit `ui/src/app/app.routes.ts`:
- Run `grep -n "ComingSoonComponent" ui/src/app/app.routes.ts` first — confirm the only usage is the `jobs` route line before removing the import.
- Remove the `ComingSoonComponent` import and its `jobs` route usage.
- Add `import { JobListComponent } from './pages/job/job-list.component';`
- Add `import { JobDetailComponent } from './pages/job/job-detail.component';` (built in Task 8).
- Replace `{ path: 'jobs', component: ComingSoonComponent, data: { title: 'Jobs' } },` with:
```typescript
{ path: 'jobs', component: JobListComponent },
{ path: 'jobs/:jobId', component: JobDetailComponent },
```

- [ ] **Step 3: Verify it compiles**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: error only about `JobDetailComponent`/`job-detail.component` not existing yet (expected until Task 8) — no other errors.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/job/job-list.component.ts ui/src/app/app.routes.ts
git commit -m "feat: add JobListComponent, wire jobs route"
```

---

### Task 6: AddPersonWidget

**Files:**
- Create: `ui/src/app/pages/job/add-person-widget/add-person-widget.component.ts`

**Interfaces:**
- Consumes: `PersonService.searchPeople(workspaceId, query): Observable<Person[]>`, `PersonService.createClient(...)`, `Person` type (Task 3).
- Produces: `AddPersonWidgetComponent` with `@Input() workspaceId: string`, `@Output() added: EventEmitter<{person: Person, participantType: string}>`, and two **public methods for the parent to call after handling the `added` event** — `markAdded(): void` (resets the widget to its initial search state, call on success) and `markFailed(message: string): void` (shows an error and re-enables the Add button, call on failure). The parent never touches this widget's internal signals directly.

Internal state is a single `mode` signal: `'search' | 'create' | 'confirm'` — replaces the earlier draft's sentinel-object hack for tracking phase.

- [ ] **Step 1: Write the component**

```typescript
// ui/src/app/pages/job/add-person-widget/add-person-widget.component.ts
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { Person, PersonService } from '../../../core/person.service';

type Mode = 'search' | 'create' | 'confirm';

@Component({
  selector: 'app-add-person-widget',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="border border-neutral-200 rounded-md p-md">
      @if (mode() === 'search') {
        <input
          class="input-field"
          type="text"
          placeholder="Search by name or email…"
          [(ngModel)]="query"
          (ngModelChange)="onQueryChange($event)"
        />

        @if (searching()) {
          <p class="text-xs text-neutral-500 mt-sm">Searching…</p>
        } @else if (query.trim().length > 0) {
          <div class="mt-sm space-y-xs">
            @for (person of results(); track person.userId) {
              <button
                type="button"
                class="w-full text-left px-md py-sm rounded hover:bg-neutral-100 flex items-center justify-between"
                (click)="choose(person)"
              >
                <span class="text-sm text-neutral-900">{{ person.name }}</span>
                <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ person.roleLabel }}</span>
              </button>
            }
            @if (results().length === 0) {
              <button
                type="button"
                class="w-full text-left px-md py-sm rounded hover:bg-neutral-100 text-sm text-primary-600"
                (click)="startCreate()"
              >
                + Create "{{ query.trim() }}" as new client
              </button>
            }
          </div>
        }
      } @else if (mode() === 'create') {
        <div class="space-y-sm">
          <p class="text-sm font-medium text-neutral-900">New client</p>
          <input class="input-field" type="text" placeholder="First name" [(ngModel)]="newFirstName" />
          <input class="input-field" type="text" placeholder="Last name" [(ngModel)]="newLastName" />
          <input class="input-field" type="text" placeholder="Phone (optional)" [(ngModel)]="newPhone" />
          @if (error()) {
            <p class="text-xs text-primary-500">{{ error() }}</p>
          }
          <div class="flex justify-end gap-sm">
            <button type="button" class="btn-secondary" (click)="reset()">Cancel</button>
            <button
              type="button"
              class="btn-primary"
              [disabled]="!newFirstName.trim() || !newLastName.trim() || creatingClient()"
              (click)="createAndContinue()"
            >
              {{ creatingClient() ? 'Creating…' : 'Create & continue' }}
            </button>
          </div>
        </div>
      } @else {
        <div class="space-y-sm">
          <p class="text-sm text-neutral-900">
            Add <strong>{{ selected()!.name }}</strong> as:
          </p>
          <select class="input-field" [(ngModel)]="participantType">
            <option value="Client">Client</option>
            <option value="Surveyor">Surveyor</option>
            <option value="Assistant">Assistant</option>
            <option value="Other">Other</option>
          </select>
          @if (error()) {
            <p class="text-xs text-primary-500">{{ error() }}</p>
          }
          <div class="flex justify-end gap-sm">
            <button type="button" class="btn-secondary" (click)="reset()">Cancel</button>
            <button type="button" class="btn-primary" [disabled]="adding()" (click)="confirmAdd()">
              {{ adding() ? 'Adding…' : 'Add' }}
            </button>
          </div>
        </div>
      }
    </div>
  `
})
export class AddPersonWidgetComponent {
  @Input() workspaceId = '';
  @Output() added = new EventEmitter<{ person: Person; participantType: string }>();

  mode = signal<Mode>('search');
  query = '';
  results = signal<Person[]>([]);
  searching = signal(false);
  selected = signal<Person | null>(null);
  participantType = 'Client';
  newFirstName = '';
  newLastName = '';
  newPhone = '';
  creatingClient = signal(false);
  adding = signal(false);
  error = signal('');

  private queryChanged = new Subject<string>();

  constructor(private personService: PersonService) {
    this.queryChanged
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((q) => {
          if (!q.trim()) {
            this.searching.set(false);
            return [];
          }
          this.searching.set(true);
          return this.personService.searchPeople(this.workspaceId, q.trim());
        })
      )
      .subscribe({
        next: (people) => {
          this.results.set(people);
          this.searching.set(false);
        },
        error: () => this.searching.set(false)
      });
  }

  onQueryChange(value: string): void {
    this.queryChanged.next(value);
  }

  choose(person: Person): void {
    this.selected.set(person);
    this.mode.set('confirm');
  }

  startCreate(): void {
    const parts = this.query.trim().split(/\s+/);
    this.newFirstName = parts[0] ?? '';
    this.newLastName = parts.slice(1).join(' ');
    this.mode.set('create');
  }

  createAndContinue(): void {
    if (!this.newFirstName.trim() || !this.newLastName.trim()) return;
    this.error.set('');
    this.creatingClient.set(true);
    this.personService
      .createClient(this.workspaceId, {
        firstName: this.newFirstName.trim(),
        lastName: this.newLastName.trim(),
        phone: this.newPhone.trim() || undefined
      })
      .subscribe({
        next: (person) => {
          this.creatingClient.set(false);
          this.selected.set(person);
          this.mode.set('confirm');
        },
        error: (err) => {
          this.creatingClient.set(false);
          this.error.set(err.error?.message ?? 'Could not create client.');
        }
      });
  }

  confirmAdd(): void {
    const person = this.selected();
    if (!person) return;
    this.error.set('');
    this.adding.set(true);
    this.added.emit({ person, participantType: this.participantType });
  }

  /** Call after successfully handling the `added` event - resets to the search state. */
  markAdded(): void {
    this.reset();
  }

  /** Call if handling the `added` event failed - shows the error, re-enables Add. */
  markFailed(message: string): void {
    this.adding.set(false);
    this.error.set(message);
  }

  reset(): void {
    this.mode.set('search');
    this.query = '';
    this.results.set([]);
    this.selected.set(null);
    this.newFirstName = '';
    this.newLastName = '';
    this.newPhone = '';
    this.participantType = 'Client';
    this.error.set('');
    this.adding.set(false);
  }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no new errors from this file.

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/pages/job/add-person-widget/add-person-widget.component.ts
git commit -m "feat: add AddPersonWidget - unified search-or-create for job participants"
```

---

### Task 7: AddLandWidget

**Files:**
- Create: `ui/src/app/pages/job/add-land-widget/add-land-widget.component.ts`

**Interfaces:**
- Consumes: `LandService.search(workspaceId, query): Observable<Land[]>`, `LandService.create(...)`, `Land`/`Address` types, `addressLine()` helper (all Task 2).
- Produces: `AddLandWidgetComponent` with `@Input() workspaceId: string`, `@Output() added: EventEmitter<Land>`.

Same search-then-create shape as `AddPersonWidget`, simpler since there's no role/type step — selecting or creating a Land emits immediately, no confirm mode needed.

- [ ] **Step 1: Write the component**

```typescript
// ui/src/app/pages/job/add-land-widget/add-land-widget.component.ts
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { Address, Land, LandService, addressLine } from '../../../core/land.service';

@Component({
  selector: 'app-add-land-widget',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="border border-neutral-200 rounded-md p-md">
      @if (!creatingNew()) {
        <input
          class="input-field"
          type="text"
          placeholder="Search by address, deed, or survey plan number…"
          [(ngModel)]="query"
          (ngModelChange)="onQueryChange($event)"
        />

        @if (searching()) {
          <p class="text-xs text-neutral-500 mt-sm">Searching…</p>
        } @else if (query.trim().length > 0) {
          <div class="mt-sm space-y-xs">
            @for (land of results(); track land.landId) {
              <button
                type="button"
                class="w-full text-left px-md py-sm rounded hover:bg-neutral-100"
                (click)="choose(land)"
              >
                <span class="text-sm text-neutral-900">{{ addressLine(land) }}</span>
                @if (land.size) {
                  <span class="text-xs text-neutral-500 block">{{ land.size }} {{ land.sizeUnit }}</span>
                }
              </button>
            }
            @if (results().length === 0) {
              <button
                type="button"
                class="w-full text-left px-md py-sm rounded hover:bg-neutral-100 text-sm text-primary-600"
                (click)="startCreate()"
              >
                + Create new land record
              </button>
            }
          </div>
        }
      } @else {
        <div class="space-y-sm">
          <p class="text-sm font-medium text-neutral-900">New land</p>
          <input class="input-field" type="text" placeholder="Street" [(ngModel)]="street" />
          <input class="input-field" type="text" placeholder="City" [(ngModel)]="city" />
          <input class="input-field" type="text" placeholder="District (optional)" [(ngModel)]="district" />
          <div class="flex gap-sm">
            <input class="input-field" type="number" placeholder="Size" [(ngModel)]="size" />
            <input class="input-field" type="text" placeholder="Unit (e.g. acres)" [(ngModel)]="sizeUnit" />
          </div>
          @if (error()) {
            <p class="text-xs text-primary-500">{{ error() }}</p>
          }
          <div class="flex justify-end gap-sm">
            <button type="button" class="btn-secondary" (click)="reset()">Cancel</button>
            <button type="button" class="btn-primary" [disabled]="!street.trim() || creating()" (click)="createAndAdd()">
              {{ creating() ? 'Creating…' : 'Create & attach' }}
            </button>
          </div>
        </div>
      }
    </div>
  `
})
export class AddLandWidgetComponent {
  @Input() workspaceId = '';
  @Output() added = new EventEmitter<Land>();

  query = '';
  results = signal<Land[]>([]);
  searching = signal(false);
  creatingNew = signal(false);
  street = '';
  city = '';
  district = '';
  size: number | null = null;
  sizeUnit = '';
  creating = signal(false);
  error = signal('');

  addressLine = addressLine;

  private queryChanged = new Subject<string>();

  constructor(private landService: LandService) {
    this.queryChanged
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((q) => {
          if (!q.trim()) {
            this.searching.set(false);
            return [];
          }
          this.searching.set(true);
          return this.landService.search(this.workspaceId, q.trim());
        })
      )
      .subscribe({
        next: (lands) => {
          this.results.set(lands);
          this.searching.set(false);
        },
        error: () => this.searching.set(false)
      });
  }

  onQueryChange(value: string): void {
    this.queryChanged.next(value);
  }

  choose(land: Land): void {
    this.added.emit(land);
  }

  startCreate(): void {
    this.street = this.query.trim();
    this.creatingNew.set(true);
  }

  createAndAdd(): void {
    if (!this.street.trim()) return;
    this.error.set('');
    this.creating.set(true);

    const address: Address = {
      street: this.street.trim(),
      city: this.city.trim() || null,
      district: this.district.trim() || null,
      postalCode: null,
      country: null
    };

    this.landService
      .create(this.workspaceId, {
        address,
        size: this.size ?? undefined,
        sizeUnit: this.sizeUnit.trim() || undefined
      })
      .subscribe({
        next: (land) => {
          this.creating.set(false);
          this.added.emit(land);
          this.reset();
        },
        error: (err) => {
          this.creating.set(false);
          this.error.set(err.error?.message ?? 'Could not create land record.');
        }
      });
  }

  reset(): void {
    this.query = '';
    this.results.set([]);
    this.creatingNew.set(false);
    this.street = '';
    this.city = '';
    this.district = '';
    this.size = null;
    this.sizeUnit = '';
    this.error.set('');
  }
}
```

`addressLine` is assigned as an instance property (`addressLine = addressLine;`) so the template can call it as a method — a plain imported function isn't directly callable from an Angular template expression.

- [ ] **Step 2: Verify it compiles**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no new errors from this file.

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/pages/job/add-land-widget/add-land-widget.component.ts
git commit -m "feat: add AddLandWidget - search-or-create for job land links"
```

---

### Task 8: JobDetailComponent

**Files:**
- Create: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `JobService` (Task 1: `getById`, `update`, `updateStatus`, `getParticipants`, `addParticipant`, `removeParticipant`, `getLands`, `addLand`, `removeLand`), `Land`/`addressLine` (Task 2), `Person` (Task 3), `AddPersonWidgetComponent` with its `markAdded`/`markFailed` public methods (Task 6), `AddLandWidgetComponent` (Task 7), `Job`/`JobParticipant` types (Task 1).

- [ ] **Step 1: Write the component**

```typescript
// ui/src/app/pages/job/job-detail.component.ts
import { Component, OnInit, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { Job, JobParticipant, JobService } from '../../core/job.service';
import { Land, addressLine } from '../../core/land.service';
import { Person } from '../../core/person.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { AddPersonWidgetComponent } from './add-person-widget/add-person-widget.component';
import { AddLandWidgetComponent } from './add-land-widget/add-land-widget.component';

const STATUSES = ['Draft', 'Scheduled', 'InProgress', 'Completed', 'Cancelled'];

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, AddPersonWidgetComponent, AddLandWidgetComponent],
  template: `
    @if (loading()) {
      <p class="p-lg text-sm text-neutral-500">Loading…</p>
    } @else if (error()) {
      <div class="p-lg">
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      </div>
    } @else if (job(); as j) {
      <div class="p-lg max-w-3xl mx-auto space-y-lg">
        <div class="card">
          <div class="flex items-center justify-between">
            <span class="font-mono text-xs text-neutral-500">{{ j.jobNumber }}</span>
            <select class="input-field w-40 py-xs" [ngModel]="j.status" (ngModelChange)="onStatusChange($event)">
              @for (s of statuses; track s) {
                <option [value]="s">{{ s }}</option>
              }
            </select>
          </div>
          <input class="input-field mt-sm text-base font-semibold" [(ngModel)]="titleDraft" (blur)="saveHeader()" />
          <textarea
            class="input-field mt-sm text-sm"
            rows="2"
            placeholder="Description (optional)"
            [(ngModel)]="descriptionDraft"
            (blur)="saveHeader()"
          ></textarea>
        </div>

        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-md">People</h2>
          @if (participants().length > 0) {
            <div class="space-y-xs mb-md">
              @for (p of participants(); track p.id) {
                <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
                  <div>
                    <span class="text-sm text-neutral-900">{{ p.firstName }} {{ p.lastName }}</span>
                    <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600 ml-sm">{{ p.participantType }}</span>
                  </div>
                  <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeParticipant(p)">
                    Remove
                  </button>
                </div>
              }
            </div>
          }
          <app-add-person-widget #personWidget [workspaceId]="workspaceId" (added)="onPersonAdded($event)" />
        </div>

        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-md">Land</h2>
          @if (lands().length > 0) {
            <div class="space-y-xs mb-md">
              @for (l of lands(); track l.landId) {
                <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
                  <div>
                    <span class="text-sm text-neutral-900">{{ addressLine(l) }}</span>
                    @if (l.size) {
                      <span class="text-xs text-neutral-500 block">{{ l.size }} {{ l.sizeUnit }}</span>
                    }
                  </div>
                  <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeLand(l)">
                    Remove
                  </button>
                </div>
              }
            </div>
          }
          <app-add-land-widget [workspaceId]="workspaceId" (added)="onLandAdded($event)" />
        </div>
      </div>
    }
  `
})
export class JobDetailComponent implements OnInit {
  @ViewChild('personWidget') personWidget?: AddPersonWidgetComponent;

  workspaceId = '';
  jobId = '';
  job = signal<Job | null>(null);
  participants = signal<JobParticipant[]>([]);
  lands = signal<Land[]>([]);
  loading = signal(true);
  error = signal('');
  statuses = STATUSES;
  titleDraft = '';
  descriptionDraft = '';

  addressLine = addressLine;

  constructor(
    private jobService: JobService,
    private currentWorkspace: CurrentWorkspaceService,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.jobId = this.route.snapshot.paramMap.get('jobId') ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    forkJoin({
      job: this.jobService.getById(this.workspaceId, this.jobId),
      participants: this.jobService.getParticipants(this.workspaceId, this.jobId),
      lands: this.jobService.getLands(this.workspaceId, this.jobId)
    }).subscribe({
      next: ({ job, participants, lands }) => {
        this.job.set(job);
        this.titleDraft = job.title;
        this.descriptionDraft = job.description ?? '';
        this.participants.set(participants);
        this.lands.set(lands);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message ?? 'Could not load job.');
        this.loading.set(false);
      }
    });
  }

  saveHeader(): void {
    const current = this.job();
    if (!current) return;
    if (this.titleDraft.trim() === current.title && (this.descriptionDraft.trim() || null) === current.description) return;
    if (!this.titleDraft.trim()) {
      this.titleDraft = current.title;
      return;
    }

    this.jobService
      .update(this.workspaceId, this.jobId, { title: this.titleDraft.trim(), description: this.descriptionDraft.trim() || null })
      .subscribe({
        next: (job) => this.job.set(job),
        error: (err) => {
          this.error.set(err.error?.message ?? 'Could not save changes.');
          this.titleDraft = current.title;
          this.descriptionDraft = current.description ?? '';
        }
      });
  }

  onStatusChange(status: string): void {
    const current = this.job();
    if (!current || current.status === status) return;
    const previous = current.status;
    this.job.set({ ...current, status });

    this.jobService.updateStatus(this.workspaceId, this.jobId, status).subscribe({
      error: (err) => {
        this.job.set({ ...current, status: previous });
        this.error.set(err.error?.message ?? 'Could not change status.');
      }
    });
  }

  onPersonAdded(event: { person: Person; participantType: string }): void {
    this.jobService.addParticipant(this.workspaceId, this.jobId, event.person.userId, event.participantType).subscribe({
      next: () => {
        this.personWidget?.markAdded();
        this.jobService.getParticipants(this.workspaceId, this.jobId).subscribe(participants => this.participants.set(participants));
      },
      error: (err) => this.personWidget?.markFailed(err.error?.message ?? 'Could not add person.')
    });
  }

  removeParticipant(p: JobParticipant): void {
    this.jobService.removeParticipant(this.workspaceId, this.jobId, p.userId).subscribe({
      next: () => this.participants.update(list => list.filter(x => x.id !== p.id)),
      error: (err) => this.error.set(err.error?.message ?? 'Could not remove participant.')
    });
  }

  onLandAdded(land: Land): void {
    this.jobService.addLand(this.workspaceId, this.jobId, land.landId).subscribe({
      next: () => this.lands.update(list => (list.some(l => l.landId === land.landId) ? list : [...list, land])),
      error: (err) => this.error.set(err.error?.message ?? 'Could not attach land.')
    });
  }

  removeLand(land: Land): void {
    this.jobService.removeLand(this.workspaceId, this.jobId, land.landId).subscribe({
      next: () => this.lands.update(list => list.filter(l => l.landId !== land.landId)),
      error: (err) => this.error.set(err.error?.message ?? 'Could not remove land.')
    });
  }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: clean — this also resolves the `JobDetailComponent` import added to `app.routes.ts` in Task 5, so the whole project should compile with zero errors now.

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat: add JobDetailComponent - header, people, and land sections"
```

---

### Task 9: Manual verification + docs update

**Files:**
- Modify: `UI_IMPLEMENTATION_GUIDE.md` — find the "NOT in scope" line listing "Jobs, Surveys" (`grep -n "NOT in scope" UI_IMPLEMENTATION_GUIDE.md`) and remove "Jobs" from it, matching the update already made to `.claude/rules.md` for the backend work.

- [ ] **Step 1: Run the full build**

Run: `cd ui && ng build`
Expected: clean build, no errors.

- [ ] **Step 2: Run all new unit tests together**

Run: `cd ui && npx vitest run src/app/core/job.service.spec.ts src/app/core/land.service.spec.ts src/app/core/person.service.spec.ts`
Expected: all PASS (17 tests total: 8 Job + 6 Land + 3 Person).

- [ ] **Step 3: Manual smoke test**

Start the API (`cd api && dotnet run --project src/SurveyorLedger.API`) and UI (`cd ui && ng serve`), log in as a workspace Admin, then:
1. Navigate to a workspace → Jobs (sidebar link) → confirm the list loads (empty state if no jobs yet).
2. Click "New job", enter a title, submit → confirm navigation to the new job's detail page.
3. On detail: edit the title, click away → confirm it saves (reload the page, title persists).
4. In People: search for a name with no existing match → confirm "Create as new client" appears → create one → confirm the role-picker step appears → pick "Client" → Add → confirm it appears in the People list.
5. Search for an existing workspace member by name → confirm they show up with their real role label (not "Client") → add them as "Surveyor" → confirm both people now show in the list with distinct role badges.
6. In Land: search a term with no match → "Create new land record" → fill address → Create & attach → confirm it appears in the Land list.
7. Change Status via the dropdown → confirm it persists on reload.
8. Remove a participant and remove the land link → confirm both disappear and reload confirms persistence.

- [ ] **Step 4: Update docs**

Edit `UI_IMPLEMENTATION_GUIDE.md`: remove "Jobs" from the not-in-scope list.

- [ ] **Step 5: Commit**

```bash
git add UI_IMPLEMENTATION_GUIDE.md
git commit -m "docs: drop Jobs from UI not-in-scope list, feature is built"
```
