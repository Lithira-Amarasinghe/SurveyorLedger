import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobService, JobParticipant } from '../../core/job.service';

/// <summary>Shared by invoice-list and quotation-list - only the send() call site
/// differs, so this owns picking recipients and lets the parent handle the actual
/// send request + its own success/error handling.</summary>
@Component({
  selector: 'app-send-document-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">Send {{ documentLabel }} {{ documentNumber }}</h2>
        <p class="text-xs text-neutral-500 mt-xs">Sends a link plus a PDF to each selected recipient.</p>

        @if (!jobId) {
          <p class="text-sm text-neutral-500 mt-lg">Recipient selection isn't available yet for workspace-level documents.</p>
        } @else if (loading()) {
          <p class="text-sm text-neutral-500 mt-lg">Loading job participants…</p>
        } @else if (participants().length === 0) {
          <p class="text-sm text-neutral-500 mt-lg">No Client or Finance participant on this job yet.</p>
        } @else {
          <div class="mt-lg space-y-sm">
            @for (p of participants(); track p.personId) {
              <label class="flex items-center gap-sm text-sm text-neutral-900">
                <input type="checkbox" [checked]="selected.has(p.personId)" (change)="toggle(p.personId)" />
                {{ p.firstName }} {{ p.lastName }}
                <span class="text-xs text-neutral-500">({{ p.role }}{{ p.email ? ', ' + p.email : '' }})</span>
              </label>
            }
          </div>
        }

        @if (error()) {
          <p class="text-sm text-primary-500 mt-md">{{ error() }}</p>
        }

        <div class="flex justify-end gap-sm pt-lg">
          <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
          <button type="button" class="btn-primary" [disabled]="sending() || selected.size === 0" (click)="submit()">
            {{ sending() ? 'Sending…' : 'Send' }}
          </button>
        </div>
      </div>
    </div>
  `
})
export class SendDocumentModalComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() jobId: string | null = null;
  @Input() documentLabel = '';
  @Input() documentNumber = '';
  @Output() cancel = new EventEmitter<void>();
  @Output() send = new EventEmitter<string[]>();

  participants = signal<JobParticipant[]>([]);
  selected = new Set<string>();
  loading = signal(false);
  sending = signal(false);
  error = signal('');

  constructor(private jobService: JobService) {}

  ngOnInit(): void {
    if (!this.jobId) return;
    this.loading.set(true);
    this.jobService.getParticipants(this.workspaceId, this.jobId).subscribe({
      next: all => {
        const eligible = all.filter(p => p.role === 'Client' || p.role === 'Finance');
        this.participants.set(eligible);
        eligible.forEach(p => this.selected.add(p.personId));
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  toggle(personId: string): void {
    if (this.selected.has(personId)) this.selected.delete(personId);
    else this.selected.add(personId);
  }

  submit(): void {
    if (this.selected.size === 0) return;
    this.send.emit([...this.selected]);
  }
}
