import { AfterViewInit, Component, ElementRef, Input, OnChanges, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as QRCode from 'qrcode';

/** Local, offline QR generation (no external QR-image API) - encodes the same Google Maps deep link the "Open in Google Maps" button already uses. */
@Component({
  selector: 'app-land-location-qr',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="flex flex-col items-center gap-xs">
      <canvas #canvasEl [width]="sizePx" [height]="sizePx"></canvas>
      <button type="button" class="text-xs text-primary-600 hover:text-primary-700" (click)="download()">
        Download PNG
      </button>
    </div>
  `
})
export class LandLocationQrComponent implements AfterViewInit, OnChanges {
  @Input() lat!: number;
  @Input() lng!: number;
  @Input() sizePx = 160;

  @ViewChild('canvasEl') canvasEl!: ElementRef<HTMLCanvasElement>;

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
}
