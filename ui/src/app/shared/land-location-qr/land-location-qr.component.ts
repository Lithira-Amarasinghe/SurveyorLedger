import { AfterViewInit, Component, ElementRef, Input, OnChanges, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as QRCode from 'qrcode';
import { IconComponent } from '../icon/icon.component';

/** Local, offline QR generation (no external QR-image API) - encodes the same Google Maps deep link the "Open in Google Maps" button already uses. */
@Component({
  selector: 'app-land-location-qr',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div class="flex flex-col items-center gap-xs">
      <canvas #canvasEl [width]="sizePx" [height]="sizePx"></canvas>
      <div class="flex items-center gap-sm">
        <button type="button" class="icon-btn" title="Download PNG" (click)="download()">
          <app-icon name="download" />
        </button>
        <button type="button" class="icon-btn" [title]="copied() ? 'Copied!' : 'Copy image'" (click)="copyImage()">
          <app-icon name="copy" />
        </button>
      </div>
    </div>
  `,
  styles: [`.icon-btn { display: flex; align-items: center; justify-content: center; width: 1.75rem; height: 1.75rem; border-radius: 0.25rem; color: var(--color-neutral-500, #737373); } .icon-btn:hover { background: var(--color-neutral-100, #f5f5f5); color: var(--color-primary-600, #0284c7); }`]
})
export class LandLocationQrComponent implements AfterViewInit, OnChanges {
  @Input() lat!: number;
  @Input() lng!: number;
  @Input() sizePx = 160;

  @ViewChild('canvasEl') canvasEl!: ElementRef<HTMLCanvasElement>;

  copied = signal(false);

  ngAfterViewInit(): void {
    this.render();
  }

  ngOnChanges(): void {
    if (this.canvasEl) this.render();
  }

  private render(): void {
    const url = `https://www.google.com/maps?q=${this.lat},${this.lng}`;
    QRCode.toCanvas(this.canvasEl.nativeElement, url, { width: this.sizePx });
  }

  download(): void {
    const link = document.createElement('a');
    link.download = 'land-location-qr.png';
    link.href = this.canvasEl.nativeElement.toDataURL('image/png');
    link.click();
  }

  copyImage(): void {
    this.canvasEl.nativeElement.toBlob(blob => {
      if (!blob) return;
      navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]).then(() => {
        this.copied.set(true);
        setTimeout(() => this.copied.set(false), 2000);
      });
    }, 'image/png');
  }
}
