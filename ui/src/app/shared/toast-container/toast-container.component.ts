import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '../../core/toast.service';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="fixed top-lg right-lg z-[100] space-y-sm w-72">
      @for (t of toastService.toasts(); track t.id) {
        <div
          class="card py-sm px-md text-sm shadow-lg flex items-start justify-between gap-sm"
          [class.border-primary-300]="t.kind === 'error'"
          [class.text-primary-600]="t.kind === 'error'"
          [class.text-neutral-700]="t.kind === 'info'"
        >
          <span>{{ t.message }}</span>
          <button type="button" class="text-neutral-400 hover:text-neutral-600" (click)="toastService.dismiss(t.id)">✕</button>
        </div>
      }
    </div>
  `
})
export class ToastContainerComponent {
  constructor(public toastService: ToastService) {}
}
