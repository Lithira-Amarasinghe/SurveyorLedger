import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface DocumentRequestFormValue {
  title: string;
  description: string | null;
  category: string;
  targetRole: string | null;
  targetUserId: string | null;
}

/** The "+ Request document" form - one place for Job (role-or-person targeting) and Land (role-only, allowPersonTarget=false hides the person branch). No HTTP inside - caller calls its own request service's create(). */
@Component({
  selector: 'app-document-request-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="rounded bg-neutral-50 p-md space-y-sm">
      <input class="input-field text-sm" placeholder="What do you need? (e.g. Legal Deed)" [(ngModel)]="title" />
      <textarea class="input-field text-sm" rows="2" placeholder="Description (optional)" [(ngModel)]="description"></textarea>
      <select class="input-field text-sm" [(ngModel)]="category">
        <option value="SurveyPlan">SurveyPlan</option>
        <option value="LegalDocument">LegalDocument</option>
        <option value="Photo">Photo</option>
        <option value="Other">Other</option>
      </select>
      <select class="input-field text-sm" [(ngModel)]="targetKind">
        <option value="anyone">Anyone</option>
        <option value="role">By role</option>
        @if (allowPersonTarget) {
          <option value="person">Specific person</option>
        }
      </select>
      @if (targetKind === 'role') {
        <select class="input-field text-sm" [(ngModel)]="targetRole">
          <option value="Admin">Admin</option>
          <option value="Surveyor">Surveyor</option>
          <option value="Client">Client</option>
        </select>
      } @else if (targetKind === 'person') {
        <select class="input-field text-sm" [(ngModel)]="targetUserId">
          <option value="" disabled>Select a person</option>
          @for (p of personOptions; track p.id) {
            <option [value]="p.id">{{ p.name }}</option>
          }
        </select>
      }
      <div class="flex items-center justify-end gap-sm">
        <button type="button" class="btn-secondary text-xs" (click)="cancelled.emit()">Cancel</button>
        <button type="button" class="btn-primary text-xs" [disabled]="!title.trim()" (click)="submit()">Request</button>
      </div>
    </div>
  `
})
export class DocumentRequestFormComponent {
  @Input() allowPersonTarget = false;
  @Input() personOptions: { id: string; name: string }[] = [];
  @Output() submitted = new EventEmitter<DocumentRequestFormValue>();
  @Output() cancelled = new EventEmitter<void>();

  title = '';
  description = '';
  category = 'Other';
  targetKind: 'anyone' | 'role' | 'person' = 'anyone';
  targetRole = 'Client';
  targetUserId = '';

  submit(): void {
    if (!this.title.trim()) return;
    this.submitted.emit({
      title: this.title.trim(),
      description: this.description.trim() || null,
      category: this.category,
      targetRole: this.targetKind === 'role' ? this.targetRole : null,
      targetUserId: this.targetKind === 'person' ? this.targetUserId || null : null
    });
  }
}
