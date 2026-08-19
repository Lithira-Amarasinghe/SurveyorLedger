import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ICONS, IconName } from '../icons';

/** Single render path for every icon in ICONS - callers pass a name, never inline an <svg> themselves (DRY: one place owns the wrapper markup/sizing). */
@Component({
  selector: 'app-icon',
  standalone: true,
  imports: [CommonModule],
  template: `<svg class="w-4 h-4" fill="none" stroke="currentColor" stroke-width="2" viewBox="0 0 24 24" [innerHTML]="svg()"></svg>`
})
export class IconComponent {
  @Input({ required: true }) name!: IconName;

  constructor(private sanitizer: DomSanitizer) {}

  svg(): SafeHtml {
    return this.sanitizer.bypassSecurityTrustHtml(ICONS[this.name]);
  }
}
