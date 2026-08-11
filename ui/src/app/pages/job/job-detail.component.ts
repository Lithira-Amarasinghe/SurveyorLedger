import { Component, OnInit, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { Observable, Subject, forkJoin } from 'rxjs';
import { Job, JobParticipant, JobService } from '../../core/job.service';
import { Land, addressLine } from '../../core/land.service';
import { Person } from '../../core/person.service';
import { Milestone, MilestoneService } from '../../core/milestone.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { AddPersonWidgetComponent } from './add-person-widget/add-person-widget.component';
import { AddLandWidgetComponent } from './add-land-widget/add-land-widget.component';
import { LandDetailPanelComponent } from '../land/land-detail-panel/land-detail-panel.component';
import { HasUnsavedChanges } from '../../core/unsaved-changes.guard';

const STATUSES = ['Draft', 'Scheduled', 'InProgress', 'Completed', 'Cancelled'];
const MILESTONE_STATUSES = ['Pending', 'InProgress', 'Completed'];

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, DragDropModule, AddPersonWidgetComponent, AddLandWidgetComponent, LandDetailPanelComponent],
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

        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-md">Milestones</h2>
          @if (milestones().length > 0) {
            <div cdkDropList class="space-y-xs mb-md" (cdkDropListDropped)="onMilestoneDropped($event)">
              @for (m of milestones(); track m.milestoneId) {
                <div cdkDrag [cdkDragDisabled]="isClient()" class="flex items-center justify-between gap-sm px-md py-sm rounded bg-neutral-50">
                  <div class="flex items-center gap-sm min-w-0">
                    @if (!isClient()) {
                      <span cdkDragHandle class="cursor-grab text-neutral-400 select-none flex-shrink-0">⠿</span>
                    }
                    <span class="flex-shrink-0">{{ milestoneStatusIcon(m.status) }}</span>
                    <div class="min-w-0">
                      <span class="text-sm text-neutral-900 truncate block">{{ m.title }}</span>
                      @if (m.description) {
                        <span class="text-xs text-neutral-500 truncate block">{{ m.description }}</span>
                      }
                    </div>
                  </div>
                  <div class="flex items-center gap-sm flex-shrink-0 whitespace-nowrap">
                    @if (m.status === 'Completed') {
                      <span class="text-xs text-neutral-500">Completed {{ m.completedAt | date: 'mediumDate' }}</span>
                    } @else if (m.dueDate) {
                      <span class="text-xs text-neutral-500">Due: {{ m.dueDate | date: 'mediumDate' }}</span>
                    }
                    @if (isClient()) {
                      <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-600">{{ m.status }}</span>
                    } @else {
                      <select class="input-field w-32 py-xs text-xs" [ngModel]="m.status" (ngModelChange)="onMilestoneStatusChange(m, $event)">
                        @for (s of milestoneStatuses; track s) {
                          <option [value]="s">{{ s }}</option>
                        }
                      </select>
                      <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="removeMilestone(m)">
                        Remove
                      </button>
                    }
                  </div>
                </div>
              }
            </div>
          }
          @if (!isClient()) {
            @if (addingMilestone()) {
              <div class="rounded bg-neutral-50 p-md space-y-sm">
                <input class="input-field text-sm" placeholder="Title" [(ngModel)]="milestoneTitleDraft" />
                <textarea class="input-field text-sm" rows="2" placeholder="Description (optional)" [(ngModel)]="milestoneDescriptionDraft"></textarea>
                <input class="input-field text-sm" type="date" [(ngModel)]="milestoneDueDateDraft" />
                @if (milestoneError()) {
                  <p class="text-xs text-primary-500">{{ milestoneError() }}</p>
                }
                <div class="flex items-center justify-end gap-sm">
                  <button type="button" class="btn-secondary text-xs" (click)="cancelAddMilestone()">Cancel</button>
                  <button type="button" class="btn-primary text-xs" (click)="submitMilestone()">Add</button>
                </div>
              </div>
            } @else {
              <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="addingMilestone.set(true)">
                + Add milestone
              </button>
            }
          }
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
  milestones = signal<Milestone[]>([]);
  milestoneStatuses = MILESTONE_STATUSES;
  addingMilestone = signal(false);
  milestoneTitleDraft = '';
  milestoneDescriptionDraft = '';
  milestoneDueDateDraft = '';
  milestoneError = signal('');
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
    private milestoneService: MilestoneService,
    private currentWorkspace: CurrentWorkspaceService,
    private route: ActivatedRoute
  ) {}

  isClient(): boolean {
    return this.currentWorkspace.current()?.role === 'Client';
  }

  milestoneStatusIcon(status: string): string {
    return status === 'Completed' ? '✓' : status === 'InProgress' ? '◐' : '○';
  }

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
      lands: this.jobService.getLands(this.workspaceId, this.jobId),
      milestones: this.milestoneService.list(this.workspaceId, this.jobId)
    }).subscribe({
      next: ({ job, participants, lands, milestones }) => {
        this.job.set(job);
        this.titleDraft = job.title;
        this.descriptionDraft = job.description ?? '';
        this.participants.set(participants);
        this.lands.set(lands);
        this.milestones.set(milestones);
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

  cancelAddMilestone(): void {
    this.addingMilestone.set(false);
    this.milestoneTitleDraft = '';
    this.milestoneDescriptionDraft = '';
    this.milestoneDueDateDraft = '';
    this.milestoneError.set('');
  }

  submitMilestone(): void {
    if (!this.milestoneTitleDraft.trim()) {
      this.milestoneError.set('Title is required.');
      return;
    }
    this.milestoneService
      .create(this.workspaceId, this.jobId, {
        title: this.milestoneTitleDraft.trim(),
        description: this.milestoneDescriptionDraft.trim() || null,
        dueDate: this.milestoneDueDateDraft || null
      })
      .subscribe({
        next: (milestone) => {
          this.milestones.update(list => [...list, milestone]);
          this.cancelAddMilestone();
        },
        error: (err) => this.milestoneError.set(err.error?.message ?? 'Could not add milestone.')
      });
  }

  onMilestoneStatusChange(milestone: Milestone, status: string): void {
    if (milestone.status === status) return;
    const previous = milestone.status;
    this.milestones.update(list => list.map(m => (m.milestoneId === milestone.milestoneId ? { ...m, status } : m)));

    this.milestoneService.updateStatus(this.workspaceId, this.jobId, milestone.milestoneId, status).subscribe({
      next: (updated) => this.milestones.update(list => list.map(m => (m.milestoneId === updated.milestoneId ? updated : m))),
      error: (err) => {
        this.milestones.update(list => list.map(m => (m.milestoneId === milestone.milestoneId ? { ...m, status: previous } : m)));
        this.error.set(err.error?.message ?? 'Could not change milestone status.');
      }
    });
  }

  removeMilestone(milestone: Milestone): void {
    this.milestoneService.delete(this.workspaceId, this.jobId, milestone.milestoneId).subscribe({
      next: () => this.milestones.update(list => list.filter(m => m.milestoneId !== milestone.milestoneId)),
      error: (err) => this.error.set(err.error?.message ?? 'Could not remove milestone.')
    });
  }

  onMilestoneDropped(event: CdkDragDrop<Milestone[]>): void {
    if (event.previousIndex === event.currentIndex) return;
    const previous = this.milestones();
    const reordered = [...previous];
    moveItemInArray(reordered, event.previousIndex, event.currentIndex);
    this.milestones.set(reordered);

    this.milestoneService.reorder(this.workspaceId, this.jobId, reordered.map(m => m.milestoneId)).subscribe({
      next: (updated) => this.milestones.set(updated),
      error: (err) => {
        this.milestones.set(previous);
        this.error.set(err.error?.message ?? 'Could not reorder milestones.');
      }
    });
  }
}
