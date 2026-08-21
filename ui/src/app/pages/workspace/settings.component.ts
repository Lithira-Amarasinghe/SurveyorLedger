import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Letterhead, WorkspaceService } from '../../core/workspace.service';
import { CurrentWorkspaceService } from '../../core/current-workspace.service';

@Component({
  selector: 'app-workspace-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="p-lg max-w-2xl mx-auto space-y-lg">
      <h1 class="text-lg font-semibold text-neutral-900">Settings</h1>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else {
        <div class="card">
          <h2 class="text-sm font-semibold text-neutral-900 mb-xs">Letterhead</h2>
          <p class="text-xs text-neutral-500 mb-md">Shown on every invoice and quotation PDF issued from this workspace.</p>

          <div class="mb-md">
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Logo</label>
            @if (logoUrl(); as url) {
              <div class="flex items-center gap-md">
                <img [src]="url" alt="Company logo" class="h-16 w-16 object-contain rounded border border-neutral-200 bg-white" />
                <button type="button" class="text-xs text-primary-500 hover:text-primary-600" [disabled]="savingLogo()" (click)="removeLogo()">Remove logo</button>
              </div>
            } @else {
              <p class="text-xs text-neutral-500 mb-xs">No logo uploaded.</p>
            }
            <input class="mt-xs" type="file" accept=".png,.jpg,.jpeg" [disabled]="savingLogo()" (change)="onLogoSelected($event)" />
            @if (savingLogo()) {
              <p class="text-xs text-neutral-500 mt-xs">Uploading…</p>
            }
          </div>

          <form class="space-y-md" (ngSubmit)="save()">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Company name</label>
              <input class="input-field" type="text" name="companyName" [(ngModel)]="companyName" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Address</label>
              <textarea class="input-field" rows="2" name="address" [(ngModel)]="address"></textarea>
            </div>
            <div class="grid grid-cols-2 gap-sm">
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Phone</label>
                <input class="input-field" type="text" name="phone" [(ngModel)]="phone" />
              </div>
              <div>
                <label class="block text-xs font-medium text-neutral-700 mb-xs">Email</label>
                <input class="input-field" type="email" name="email" [(ngModel)]="email" />
              </div>
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Registration / tax number</label>
              <input class="input-field" type="text" name="registrationNumber" [(ngModel)]="registrationNumber" />
            </div>

            @if (error()) {
              <p class="text-sm text-primary-500">{{ error() }}</p>
            }
            @if (savedMessage()) {
              <p class="text-sm text-green-700">{{ savedMessage() }}</p>
            }

            <div class="flex justify-end pt-sm">
              <button type="submit" class="btn-primary" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save' }}</button>
            </div>
          </form>
        </div>
      }
    </div>
  `
})
export class WorkspaceSettingsComponent implements OnInit {
  workspaceId = '';

  companyName = '';
  address = '';
  phone = '';
  email = '';
  registrationNumber = '';
  logoUrl = signal<string | null>(null);

  loading = signal(true);
  saving = signal(false);
  savingLogo = signal(false);
  error = signal('');
  savedMessage = signal('');

  constructor(
    private workspaceService: WorkspaceService,
    private currentWorkspace: CurrentWorkspaceService
  ) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.workspaceService.getLetterhead(this.workspaceId).subscribe({
      next: letterhead => {
        this.applyLetterhead(letterhead);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load settings.');
        this.loading.set(false);
      }
    });
  }

  private applyLetterhead(letterhead: Letterhead): void {
    this.companyName = letterhead.companyName ?? '';
    this.address = letterhead.address ?? '';
    this.phone = letterhead.phone ?? '';
    this.email = letterhead.email ?? '';
    this.registrationNumber = letterhead.registrationNumber ?? '';
    this.refreshLogoPreview(letterhead.hasLogo);
  }

  private refreshLogoPreview(hasLogo: boolean): void {
    if (!hasLogo) {
      this.logoUrl.set(null);
      return;
    }
    this.workspaceService.getLetterheadLogoBlob(this.workspaceId).subscribe({
      next: blob => this.logoUrl.set(URL.createObjectURL(blob)),
      error: () => this.logoUrl.set(null)
    });
  }

  onLogoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.savingLogo.set(true);
    this.error.set('');
    this.workspaceService.uploadLetterheadLogo(this.workspaceId, file).subscribe({
      next: letterhead => {
        this.refreshLogoPreview(letterhead.hasLogo);
        this.savingLogo.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not upload logo.');
        this.savingLogo.set(false);
      }
    });
    input.value = '';
  }

  removeLogo(): void {
    this.savingLogo.set(true);
    this.workspaceService.deleteLetterheadLogo(this.workspaceId).subscribe({
      next: letterhead => {
        this.refreshLogoPreview(letterhead.hasLogo);
        this.savingLogo.set(false);
      },
      error: () => this.savingLogo.set(false)
    });
  }

  save(): void {
    this.saving.set(true);
    this.error.set('');
    this.savedMessage.set('');
    this.workspaceService
      .updateLetterhead(this.workspaceId, {
        companyName: this.companyName || undefined,
        address: this.address || undefined,
        phone: this.phone || undefined,
        email: this.email || undefined,
        registrationNumber: this.registrationNumber || undefined
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.savedMessage.set('Settings saved.');
        },
        error: err => {
          this.saving.set(false);
          this.error.set(err.error?.message ?? 'Could not save settings.');
        }
      });
  }
}
