import { Component, OnInit, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable, Subject, forkJoin } from 'rxjs';
import { Job, JobParticipant, JobService } from '../../core/job.service';
import { Land, addressLine } from '../../core/land.service';
import { Person } from '../../core/person.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { AddPersonWidgetComponent } from './add-person-widget/add-person-widget.component';
import { AddLandWidgetComponent } from './add-land-widget/add-land-widget.component';
import { LandDetailPanelComponent } from '../land/land-detail-panel/land-detail-panel.component';
import { HasUnsavedChanges } from '../../core/unsaved-changes.guard';

const STATUSES = ['Draft', 'Scheduled', 'InProgress', 'Completed', 'Cancelled'];

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, AddPersonWidgetComponent, AddLandWidgetComponent, LandDetailPanelComponent],
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
          <input class="input-field mt-sm text-base font-semibold" [(ngModel)]="titleDraft" />
          <textarea
            class="input-field mt-sm text-sm"
            rows="2"
            placeholder="Description (optional)"
            [(ngModel)]="descriptionDraft"
          ></textarea>

          @if (headerDirty()) {
            <div class="flex items-center justify-end gap-sm mt-sm">
              @if (headerError()) {
                <span class="text-xs text-primary-500 mr-auto">{{ headerError() }}</span>
              } @else {
                <span class="text-xs text-amber-600 mr-auto">Unsaved changes</span>
              }
              <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700" [disabled]="savingHeader()" (click)="discardHeader()">
                Discard
              </button>
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600 font-medium" [disabled]="savingHeader()" (click)="saveHeader()">
                {{ savingHeader() ? 'Saving…' : 'Save changes' }}
              </button>
            </div>
          }
        </div>

        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-md">People</h2>
          @if (participants().length > 0) {
            <div class="space-y-xs mb-md">
              @for (p of participants(); track p.userId) {
                <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
                  <div>
                    <span class="text-sm text-neutral-900">{{ p.firstName }} {{ p.lastName }}</span>
                    <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600 ml-sm">{{ p.role }}</span>
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
                <div class="rounded bg-neutral-50">
                  <div class="flex items-center justify-between px-md py-sm cursor-pointer" (click)="toggleLand(l.landId)">
                    <div>
                      <span class="text-sm text-neutral-900">{{ addressLine(l) }}</span>
                      @if (l.size) {
                        <span class="text-xs text-neutral-500 block">{{ l.size }} {{ l.sizeUnit }}</span>
                      }
                    </div>
                    <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeLand(l); $event.stopPropagation()">
                      Remove
                    </button>
                  </div>
                  @if (expandedLandId() === l.landId) {
                    <div class="px-md pb-md pt-sm border-t border-neutral-200">
                      <app-land-detail-panel [workspaceId]="workspaceId" [landId]="l.landId" (deleted)="onLandDeleted(l.landId)" />
                    </div>
                  }
                </div>
              }
            </div>
          }
          <app-add-land-widget [workspaceId]="workspaceId" (added)="onLandAdded($event)" />
        </div>
      </div>
    }

    @if (confirmingLeave()) {
      <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg">
        <div class="card w-full max-w-sm">
          <h2 class="text-base font-semibold text-neutral-900">Unsaved changes</h2>
          <p class="text-sm text-neutral-600 mt-xs">
            You've edited the job title or description but haven't saved. What would you like to do?
          </p>
          <div class="flex flex-col gap-sm mt-lg">
            <button type="button" class="btn-primary" (click)="saveAndLeave()">Save and leave</button>
            <button type="button" class="btn-secondary" (click)="discardAndLeave()">Discard changes</button>
            <button type="button" class="btn-secondary" (click)="stayOnPage()">Keep editing</button>
          </div>
        </div>
      </div>
    }
  `
})
export class JobDetailComponent implements OnInit, HasUnsavedChanges {
  @ViewChild('personWidget') personWidget?: AddPersonWidgetComponent;

  workspaceId = '';
  jobId = '';
  job = signal<Job | null>(null);
  participants = signal<JobParticipant[]>([]);
  lands = signal<Land[]>([]);
  loading = signal(true);
  savingHeader = signal(false);
  headerError = signal('');
  error = signal('');
  statuses = STATUSES;
  titleDraft = '';
  descriptionDraft = '';

  addressLine = addressLine;
  expandedLandId = signal<string | null>(null);
  confirmingLeave = signal(false);
  private leaveDecision: Subject<boolean> | null = null;

  toggleLand(landId: string): void {
    this.expandedLandId.update(current => (current === landId ? null : landId));
  }

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

  headerDirty(): boolean {
    const current = this.job();
    if (!current) return false;
    return this.titleDraft.trim() !== current.title || (this.descriptionDraft.trim() || null) !== current.description;
  }

  discardHeader(): void {
    const current = this.job();
    if (!current) return;
    this.titleDraft = current.title;
    this.descriptionDraft = current.description ?? '';
    this.headerError.set('');
  }

  saveHeader(onSaved?: () => void): void {
    const current = this.job();
    if (!current || !this.headerDirty()) return;
    if (!this.titleDraft.trim()) {
      this.headerError.set('Title is required.');
      return;
    }

    this.headerError.set('');
    this.savingHeader.set(true);
    this.jobService
      .update(this.workspaceId, this.jobId, { title: this.titleDraft.trim(), description: this.descriptionDraft.trim() || null })
      .subscribe({
        next: (job) => {
          this.job.set(job);
          this.titleDraft = job.title;
          this.descriptionDraft = job.description ?? '';
          this.savingHeader.set(false);
          onSaved?.();
        },
        error: (err) => {
          this.savingHeader.set(false);
          this.headerError.set(err.error?.message ?? 'Could not save changes.');
        }
      });
  }

  /** Router guard hook - pauses navigation until the user picks Save/Discard/Stay. */
  canDeactivate(): boolean | Observable<boolean> {
    if (!this.headerDirty()) return true;

    this.confirmingLeave.set(true);
    this.leaveDecision = new Subject<boolean>();
    return this.leaveDecision.asObservable();
  }

  saveAndLeave(): void {
    this.saveHeader(() => this.resolveLeave(true));
  }

  discardAndLeave(): void {
    this.discardHeader();
    this.resolveLeave(true);
  }

  stayOnPage(): void {
    this.resolveLeave(false);
  }

  private resolveLeave(allow: boolean): void {
    this.confirmingLeave.set(false);
    this.leaveDecision?.next(allow);
    this.leaveDecision?.complete();
    this.leaveDecision = null;
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

  onPersonAdded(person: Person): void {
    this.jobService.addParticipant(this.workspaceId, this.jobId, person.userId).subscribe({
      next: () => {
        this.personWidget?.markAdded();
        this.jobService.getParticipants(this.workspaceId, this.jobId).subscribe(participants => this.participants.set(participants));
      },
      error: (err) => this.personWidget?.markFailed(err.error?.message ?? 'Could not add person.')
    });
  }

  removeParticipant(p: JobParticipant): void {
    this.jobService.removeParticipant(this.workspaceId, this.jobId, p.userId).subscribe({
      next: () => this.participants.update(list => list.filter(x => x.userId !== p.userId)),
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

  /** The land record itself was deleted (not just unlinked) - drop it locally, no separate unlink call needed. */
  onLandDeleted(landId: string): void {
    this.lands.update(list => list.filter(l => l.landId !== landId));
    this.expandedLandId.set(null);
  }
}
