import { Component, OnInit, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, Subject } from 'rxjs';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';
import { LandDetailPanelComponent } from './land-detail-panel/land-detail-panel.component';
import { HasUnsavedChanges } from '../../core/unsaved-changes.guard';

@Component({
  selector: 'app-land-detail',
  standalone: true,
  imports: [CommonModule, LandDetailPanelComponent],
  template: `
    <div class="p-lg max-w-3xl mx-auto">
      <div class="card">
        <app-land-detail-panel #panel [workspaceId]="workspaceId" [landId]="landId" (deleted)="onDeleted()" />
      </div>
    </div>

    @if (confirmingLeave()) {
      <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg">
        <div class="card w-full max-w-sm">
          <h2 class="text-base font-semibold text-neutral-900">Unsaved changes</h2>
          <p class="text-sm text-neutral-600 mt-xs">
            You've edited this land record but haven't saved. What would you like to do?
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
export class LandDetailComponent implements OnInit, HasUnsavedChanges {
  @ViewChild('panel') panel?: LandDetailPanelComponent;

  workspaceId = '';
  landId = '';

  constructor(
    private currentWorkspace: CurrentWorkspaceService,
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.landId = this.route.snapshot.paramMap.get('landId') ?? '';
  }

  onDeleted(): void {
    this.router.navigate(['/app/workspace', this.workspaceId, 'lands']);
  }

  confirmingLeave = signal(false);
  private leaveDecision: Subject<boolean> | null = null;

  /** Delegates to the panel's own dirty state - it owns the Details fields being edited. */
  canDeactivate(): boolean | Observable<boolean> {
    if (!this.panel?.detailsDirty()) return true;

    this.confirmingLeave.set(true);
    this.leaveDecision = new Subject<boolean>();
    return this.leaveDecision.asObservable();
  }

  saveAndLeave(): void {
    this.panel?.saveDetails(() => this.resolveLeave(true));
  }

  discardAndLeave(): void {
    this.panel?.discardDetails();
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
}
