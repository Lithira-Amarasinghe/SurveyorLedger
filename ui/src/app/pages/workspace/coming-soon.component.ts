import { Component, input } from '@angular/core';

@Component({
  selector: 'app-coming-soon',
  standalone: true,
  template: `
    <div class="p-lg max-w-2xl mx-auto">
      <div class="card text-center text-sm text-neutral-500">{{ title() }} is coming soon.</div>
    </div>
  `
})
export class ComingSoonComponent {
  title = input.required<string>();
}
